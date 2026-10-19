using System.Runtime.CompilerServices;

namespace AtomNES.Device.Display;

/// <summary>
/// 无锁三缓冲帧缓冲。
/// <para>
/// 模拟线程只写 Back 缓冲，帧完成时调用 <see cref="Present"/> 与“待显示”缓冲交换；
/// UI 线程调用 <see cref="TryAcquireLatest"/> 与“待显示”缓冲交换得到最新的完整帧。
/// 两端互不阻塞，不会撕裂，也不会因 UI 掉帧拖慢模拟。
/// </para>
/// 像素格式为 32 位 BGRA（小端 uint 即 0xAARRGGBB），可直接拷贝进 Bgra8888 位图。
/// </summary>
public sealed class FrameBuffer
{
    public const int Width = 256;
    public const int Height = 240;
    public const int PixelCount = Width * Height;

    private const int DirtyBit = 0x4;
    private const int IndexMask = 0x3;

    private readonly uint[][] _buffers = [new uint[PixelCount], new uint[PixelCount], new uint[PixelCount]];

    private int _back;
    private int _front = 1;

    // 低 2 位：待显示缓冲索引；DirtyBit：是否有未被消费的新帧
    private int _pending = 2;

    private long _frameNumber;

    /// <summary>模拟线程写入用的当前缓冲。</summary>
    public Span<uint> Back => _buffers[_back];

    /// <summary>已提交的帧数。</summary>
    public long FrameNumber => Interlocked.Read(ref _frameNumber);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetPixel(int x, int y, uint bgra)
    {
        _buffers[_back][y * Width + x] = bgra;
    }

    /// <summary>模拟线程：提交当前帧。</summary>
    public void Present()
    {
        var old = Interlocked.Exchange(ref _pending, _back | DirtyBit);
        _back = old & IndexMask;
        Interlocked.Increment(ref _frameNumber);
    }

    /// <summary>UI 线程：若有新帧返回 true。<paramref name="frame"/> 始终指向当前前台帧。</summary>
    public bool TryAcquireLatest(out ReadOnlySpan<uint> frame)
    {
        if ((Volatile.Read(ref _pending) & DirtyBit) == 0)
        {
            frame = _buffers[_front];
            return false;
        }

        var old = Interlocked.Exchange(ref _pending, _front);
        _front = old & IndexMask;
        frame = _buffers[_front];
        return true;
    }

    /// <summary>清屏并提交。</summary>
    public void Clear(uint bgra = NesPalette.Black)
    {
        Back.Fill(bgra);
        Present();
    }
}
