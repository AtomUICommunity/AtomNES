using System;
using System.Runtime.InteropServices;
using AtomNES.Device.Display;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace AtomNES.Controls;

public enum ScreenScaleMode
{
    /// <summary>保持宽高比铺满可用区域。</summary>
    Fit,

    /// <summary>100%：1 倍。</summary>
    X1,

    /// <summary>200%：2 倍。</summary>
    X2,

    /// <summary>300%：3 倍。</summary>
    X3
}

/// <summary>
/// NES 画面控件。
/// <para>
/// 与旧实现（DispatcherTimer 16ms 轮询 + 每 tick 强制 Measure/Arrange/Visual + 整帧 Marshal.Copy）相比：
/// <list type="bullet">
/// <item>跟随合成器的 <c>RequestAnimationFrame</c>（与显示器垂直同步），只在有新帧时拷贝；</item>
/// <item>与模拟线程通过无锁三缓冲交换，无撕裂、无锁竞争；</item>
/// <item>只触发 InvalidateVisual，不再引发布局；</item>
/// <item>默认 NES 原生 8:7 像素比，提供 100%/200%/300% 固定档位与自适应模式，最近邻采样；</item>
/// <item>按行拷贝，兼容 RowBytes 带填充的平台位图。</item>
/// </list>
/// </para>
/// </summary>
public sealed class NesScreen : Control
{
    public static readonly StyledProperty<FrameBuffer?> FrameBufferProperty =
        AvaloniaProperty.Register<NesScreen, FrameBuffer?>(nameof(FrameBuffer));

    public static readonly StyledProperty<ScreenScaleMode> ScaleModeProperty =
        AvaloniaProperty.Register<NesScreen, ScreenScaleMode>(nameof(ScaleMode), ScreenScaleMode.X2);

    /// <summary>NES 像素宽高比（8:7）。关闭时使用方形像素。</summary>
    public static readonly StyledProperty<bool> UsePixelAspectRatioProperty =
        AvaloniaProperty.Register<NesScreen, bool>(nameof(UsePixelAspectRatio), true);

    /// <summary>裁掉上下各 8 行（大多数电视不可见的过扫描区域）。</summary>
    public static readonly StyledProperty<bool> CropOverscanProperty =
        AvaloniaProperty.Register<NesScreen, bool>(nameof(CropOverscan), true);

    private readonly WriteableBitmap _bitmap = new(
        new PixelSize(FrameBuffer.Width, FrameBuffer.Height),
        new Vector(96, 96),
        PixelFormat.Bgra8888,
        AlphaFormat.Opaque);

    private TopLevel? _topLevel;
    private bool _attached;

    static NesScreen()
    {
        AffectsRender<NesScreen>(ScaleModeProperty, UsePixelAspectRatioProperty, CropOverscanProperty);
        AffectsMeasure<NesScreen>(ScaleModeProperty, UsePixelAspectRatioProperty, CropOverscanProperty);
        ClipToBoundsProperty.OverrideDefaultValue<NesScreen>(true);
    }

    public NesScreen()
    {
        ClearBitmap();
    }

    public FrameBuffer? FrameBuffer
    {
        get => GetValue(FrameBufferProperty);
        set => SetValue(FrameBufferProperty, value);
    }

    public ScreenScaleMode ScaleMode
    {
        get => GetValue(ScaleModeProperty);
        set => SetValue(ScaleModeProperty, value);
    }

    public bool UsePixelAspectRatio
    {
        get => GetValue(UsePixelAspectRatioProperty);
        set => SetValue(UsePixelAspectRatioProperty, value);
    }

    public bool CropOverscan
    {
        get => GetValue(CropOverscanProperty);
        set => SetValue(CropOverscanProperty, value);
    }

    /// <summary>UI 实际显示的帧数（用于 FPS 统计）。</summary>
    public long PresentedFrames { get; private set; }

    private PixelRect SourceRect => CropOverscan
        ? new PixelRect(0, 8, FrameBuffer.Width, FrameBuffer.Height - 16)
        : new PixelRect(0, 0, FrameBuffer.Width, FrameBuffer.Height);

    private double? FixedScale => ScaleMode switch
    {
        ScreenScaleMode.X1 => 1,
        ScreenScaleMode.X2 => 2,
        ScreenScaleMode.X3 => 3,
        _ => null
    };

    private Size LogicalSize
    {
        get
        {
            var src = SourceRect;
            // NES 像素宽高比 8:7
            var w = UsePixelAspectRatio ? src.Width * 8.0 / 7.0 : src.Width;
            return new Size(w, src.Height);
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var size = LogicalSize;
        if (FixedScale is { } fixedScale)
        {
            return size * fixedScale;
        }

        if (double.IsInfinity(availableSize.Width) && double.IsInfinity(availableSize.Height))
        {
            return size;
        }

        var scale = Math.Min(availableSize.Width / size.Width, availableSize.Height / size.Height);
        return double.IsInfinity(scale) ? size : size * scale;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _topLevel = TopLevel.GetTopLevel(this);
        _attached = true;
        RequestNextFrame();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        _topLevel = null;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == FrameBufferProperty && change.NewValue is null)
        {
            ClearBitmap();
            InvalidateVisual();
        }
    }

    private void RequestNextFrame()
    {
        if (_attached)
        {
            _topLevel?.RequestAnimationFrame(OnAnimationFrame);
        }
    }

    private void OnAnimationFrame(TimeSpan _)
    {
        if (FrameBuffer is { } fb && fb.TryAcquireLatest(out var frame))
        {
            CopyToBitmap(frame);
            PresentedFrames++;
            InvalidateVisual();
        }

        RequestNextFrame();
    }

    private void CopyToBitmap(ReadOnlySpan<uint> frame)
    {
        using var locked = _bitmap.Lock();
        var srcBytes = MemoryMarshal.AsBytes(frame);
        const int rowBytes = FrameBuffer.Width * sizeof(uint);
        unsafe
        {
            if (locked.RowBytes == rowBytes)
            {
                srcBytes.CopyTo(new Span<byte>((void*)locked.Address, srcBytes.Length));
                return;
            }

            for (var y = 0; y < FrameBuffer.Height; y++)
            {
                srcBytes.Slice(y * rowBytes, rowBytes)
                        .CopyTo(new Span<byte>((byte*)locked.Address + (long)y * locked.RowBytes, rowBytes));
            }
        }
    }

    private void ClearBitmap()
    {
        Span<uint> black = stackalloc uint[FrameBuffer.Width];
        black.Fill(NesPalette.Black);
        using var locked = _bitmap.Lock();
        unsafe
        {
            for (var y = 0; y < FrameBuffer.Height; y++)
            {
                MemoryMarshal.AsBytes(black)
                             .CopyTo(new Span<byte>((byte*)locked.Address + (long)y * locked.RowBytes,
                                 FrameBuffer.Width * sizeof(uint)));
            }
        }
    }

    public override void Render(DrawingContext context)
    {
        var bounds = Bounds;
        context.FillRectangle(Brushes.Black, new Rect(bounds.Size));

        var logical = LogicalSize;
        var sx = bounds.Width / logical.Width;
        var sy = bounds.Height / logical.Height;
        // 固定档位下若窗口比目标小，则等比缩小以完整显示
        var fit = Math.Min(sx, sy);
        var target = logical * Math.Min(FixedScale ?? fit, fit);

        var dest = new Rect(
            Math.Round((bounds.Width - target.Width) / 2),
            Math.Round((bounds.Height - target.Height) / 2),
            target.Width,
            target.Height);
        var src = SourceRect;

        using (context.PushRenderOptions(new RenderOptions
               {
                   BitmapInterpolationMode = BitmapInterpolationMode.None,
                   EdgeMode = EdgeMode.Aliased
               }))
        {
            context.DrawImage(_bitmap, new Rect(src.X, src.Y, src.Width, src.Height), dest);
        }
    }
}



