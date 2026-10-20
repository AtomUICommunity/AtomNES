using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using AtomNES.Device.Cpu;
using AtomNES.Device.Display;
using AtomNES.Localization;
using AtomNES.Services;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AtomNES.ViewModels;

public sealed partial class DisasmLine : ObservableObject
{
    public required ushort Address { get; init; }

    public required string Bytes { get; init; }

    public required string Text { get; init; }

    public string AddressText => $"${Address:X4}";

    [ObservableProperty] private bool _isCurrent;
}

public sealed record FlagItem(string Name, bool IsSet);

public sealed record RegisterItem(string Name, string Value);

public partial class DebuggerViewModel : ViewModelBase, IDisposable
{
    private const int LinesBefore = 8;
    private const int LinesAfter = 24;

    private readonly EmulatorHost _host;
    private readonly DispatcherTimer _timer;
    private readonly uint[] _patternBuffer = new uint[128 * 128];
    private readonly uint[] _nameTableBuffer = new uint[512 * 480];

    // 已执行过的指令地址缓存，用于向前回看反汇编（6502 无法可靠地反向反汇编）
    private readonly SortedSet<ushort> _knownStarts = [];

    public DebuggerViewModel(EmulatorHost host)
    {
        _host = host;
        _host.StateChanged += OnHostStateChanged;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(200), DispatcherPriority.Background, (_, _) =>
        {
            if (AutoRefresh && !_host.IsPaused) Refresh();
        });
        _timer.Start();
        Refresh();
    }

    public ObservableCollection<DisasmLine> Disassembly { get; } = [];

    [ObservableProperty] private DisasmLine? _currentLine;
    [ObservableProperty] private IReadOnlyList<RegisterItem> _cpuRegisters = [];
    [ObservableProperty] private IReadOnlyList<FlagItem> _cpuFlags = [];
    [ObservableProperty] private IReadOnlyList<RegisterItem> _ppuRegisters = [];
    [ObservableProperty] private IReadOnlyList<uint> _paletteColors = [];
    [ObservableProperty] private bool _isPaused;
    [ObservableProperty] private bool _autoRefresh = true;
    [ObservableProperty] private int _selectedPalette;
    [ObservableProperty] private WriteableBitmap _patternTable0 = CreateBitmap(128, 128);
    [ObservableProperty] private WriteableBitmap _patternTable1 = CreateBitmap(128, 128);
    [ObservableProperty] private WriteableBitmap _nameTables = CreateBitmap(512, 480);

    public IReadOnlyList<Avalonia.Media.IBrush> PaletteBrushes { get; private set; } = [];

    public string RunButtonText => Lang.Get(IsPaused ? AppLangResourceKind.DbgContinue : AppLangResourceKind.DbgPause);

    partial void OnIsPausedChanged(bool value) => OnPropertyChanged(nameof(RunButtonText));

    partial void OnSelectedPaletteChanged(int value) => Refresh();

    private void OnHostStateChanged() => Dispatcher.UIThread.Post(Refresh);

    [RelayCommand]
    private void ToggleRun()
    {
        if (!_host.IsLoaded) return;
        if (_host.IsPaused) _host.Resume();
        else _host.Pause();
    }

    [RelayCommand]
    private void StepInstruction()
    {
        _host.StepInstruction();
        Refresh();
    }

    [RelayCommand]
    private void StepScanline()
    {
        _host.StepScanline();
        Refresh();
    }

    [RelayCommand]
    private void StepFrame()
    {
        _host.StepFrame();
        Refresh();
    }

    [RelayCommand]
    private void Reset()
    {
        _host.Reset();
        Refresh();
    }

    [RelayCommand]
    public void Refresh()
    {
        IsPaused = _host.IsPaused;
        if (!_host.IsLoaded)
        {
            Disassembly.Clear();
            return;
        }

        List<DisassembledLine> lines;
        lock (_host.SyncRoot)
        {
            var cpu = _host.Bus.Cpu;
            var ppu = _host.Ppu;
            CpuRegisters =
            [
                new("A", $"${cpu.A:X2}"),
                new("X", $"${cpu.X:X2}"),
                new("Y", $"${cpu.Y:X2}"),
                new("SP", $"${cpu.SP:X2}"),
                new("PC", $"${cpu.PC:X4}"),
                new("CYC", cpu.TotalCycles.ToString()),
            ];
            var p = cpu.P;
            CpuFlags =
            [
                new("N", (p & 0x80) != 0), new("V", (p & 0x40) != 0), new("U", (p & 0x20) != 0),
                new("B", (p & 0x10) != 0), new("D", (p & 0x08) != 0), new("I", (p & 0x04) != 0),
                new("Z", (p & 0x02) != 0), new("C", (p & 0x01) != 0),
            ];
            PpuRegisters =
            [
                new(Lang.Get(AppLangResourceKind.DbgScanline), ppu.Scanline.ToString()),
                new(Lang.Get(AppLangResourceKind.DbgDot), ppu.Dot.ToString()),
                new(Lang.Get(AppLangResourceKind.DbgFrame), ppu.FrameNumber.ToString()),
                new("CTRL", $"${ppu.Ctrl:X2}"),
                new("MASK", $"${ppu.Mask:X2}"),
                new("V", $"${ppu.V:X4}"),
                new("T", $"${ppu.T:X4}"),
                new("Fine X", ppu.FineX.ToString()),
                new("VBlank", ppu.VBlank ? "1" : "0"),
            ];

            lines = BuildDisassembly(cpu.PC);

            ppu.RenderPatternTable(0, SelectedPalette, _patternBuffer);
            PatternTable0 = Replace(PatternTable0, _patternBuffer, 128, 128);
            ppu.RenderPatternTable(1, SelectedPalette, _patternBuffer);
            PatternTable1 = Replace(PatternTable1, _patternBuffer, 128, 128);
            ppu.RenderNameTables(_nameTableBuffer);
            NameTables = Replace(NameTables, _nameTableBuffer, 512, 480);

            var brushes = new Avalonia.Media.IBrush[32];
            var pal = ppu.PaletteRam;
            for (var i = 0; i < 32; i++)
            {
                var c = NesPalette.Table[pal[i] & 0x3F];
                brushes[i] = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromUInt32(c | 0xFF000000));
            }

            PaletteBrushes = brushes;
        }

        OnPropertyChanged(nameof(PaletteBrushes));
        UpdateDisassembly(lines);
    }

    private List<DisassembledLine> BuildDisassembly(ushort pc)
    {
        var bus = _host.Bus;
        var result = new List<DisassembledLine>(LinesBefore + LinesAfter);

        // 回看：从已知指令起点中取 PC 之前的若干条，并验证它们能连续衔接到 PC
        var before = new List<DisassembledLine>();
        foreach (var start in _knownStarts.GetViewBetween((ushort)Math.Max(0, pc - 64), pc).Reverse())
        {
            if (start >= pc) continue;
            var line = Disassembler.DisassembleOne(bus, start);
            var expectedNext = before.Count == 0 ? pc : before[^1].Address;
            if (start + line.Length != expectedNext) continue;
            before.Add(line);
            if (before.Count >= LinesBefore) break;
        }

        before.Reverse();
        result.AddRange(before);

        int addr = pc;
        for (var i = 0; i < LinesAfter && addr <= 0xFFFF; i++)
        {
            var line = Disassembler.DisassembleOne(bus, (ushort)addr);
            result.Add(line);
            _knownStarts.Add(line.Address);
            addr += line.Length;
        }

        if (_knownStarts.Count > 65536) _knownStarts.Clear();
        return result;
    }

    private void UpdateDisassembly(List<DisassembledLine> lines)
    {
        Disassembly.Clear();
        var pc = _host.Bus.Cpu.PC;
        DisasmLine? current = null;
        foreach (var l in lines)
        {
            var bytes = new System.Text.StringBuilder();
            for (var i = 0; i < l.Length; i++)
            {
                if (i > 0) bytes.Append(' ');
                bytes.Append(_host.Bus.Peek((ushort)(l.Address + i)).ToString("X2"));
            }

            var item = new DisasmLine { Address = l.Address, Bytes = bytes.ToString(), Text = l.Text, IsCurrent = l.Address == pc };
            if (item.IsCurrent) current = item;
            Disassembly.Add(item);
        }

        CurrentLine = current;
    }

    private static WriteableBitmap CreateBitmap(int w, int h) =>
        new(new PixelSize(w, h), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);

    private static unsafe void Blit(WriteableBitmap bmp, uint[] src, int w, int h)
    {
        using var fb = bmp.Lock();
        fixed (uint* s = src)
        {
            for (var y = 0; y < h; y++)
            {
                Buffer.MemoryCopy(s + y * w, (byte*)fb.Address + y * fb.RowBytes, fb.RowBytes, w * 4);
            }
        }
    }

    // Image 绑定同一实例时不会重绘，因此每次生成新位图并释放旧的
    private static WriteableBitmap Replace(WriteableBitmap old, uint[] src, int w, int h)
    {
        var bmp = CreateBitmap(w, h);
        Blit(bmp, src, w, h);
        Dispatcher.UIThread.Post(old.Dispose, DispatcherPriority.Background);
        return bmp;
    }
    public void Dispose()
    {
        _timer.Stop();
        _host.StateChanged -= OnHostStateChanged;
    }
}
