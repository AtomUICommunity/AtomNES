using System.Runtime.CompilerServices;
using AtomNES.Device.Cpu;
using AtomNES.Device.Input;

namespace AtomNES.Device.Bus;

/// <summary>
/// NES 系统总线：连接 CPU / PPU / APU / 卡带 / 手柄，并负责主时钟调度。
/// <para>
/// 采用主时钟（master clock，NTSC 21.477272 MHz）模型：CPU 每周期 12 个主时钟、PPU 每点 4 个主时钟。
/// 一次 CPU 总线访问被拆为“周期前半”与“周期后半”，读访问在第 5 个主时钟、写访问在第 7 个主时钟发生，
/// PPU 在访问前后分别追赶到对应主时钟。这样 $2002/$2007 等寄存器读写相对 PPU 点的相位与真机一致，
/// VBlank/NMI 竞争、精灵 0 命中等时序测试可以精确到单个 PPU 点。
/// </para>
/// <code>
/// $0000-$07FF  2KB 内部 RAM（$0800-$1FFF 镜像）
/// $2000-$2007  PPU 寄存器（$2008-$3FFF 镜像）
/// $4000-$4017  APU / I/O
/// $4018-$401F  CPU 测试模式（未使用）
/// $4020-$FFFF  卡带空间
/// </code>
/// </summary>
public sealed class NesBus : ICpuBus
{
    private const int CpuDivider = 12;
    private const int PpuDivider = 4;
    private const int StartClocks = 6;
    private const int EndClocks = 6;

    // PPU 相对 CPU 的主时钟偏移（真机上电时随机，1 为最常见的对齐）
    private const int PpuOffset = 1;

    private readonly byte[] _ram = new byte[0x800];
    private byte _openBus;

    private long _masterClock;
    private long _ppuMasterClock;

    private bool _oamDmaPending;
    private byte _oamDmaPage;

    public NesBus()
    {
        Cpu = new Cpu6502(this);
    }

    public Cpu6502 Cpu { get; }

    public IPpuDevice? Ppu { get; private set; }

    public IApuDevice? Apu { get; private set; }

    public ICartridgeDevice? Cartridge { get; private set; }

    public StandardController Controller1 { get; } = new();

    public StandardController Controller2 { get; } = new();

    /// <summary>内部 RAM 视图（调试器使用）。</summary>
    public ReadOnlySpan<byte> Ram => _ram;

    public long MasterClock => _masterClock;

    public void ConnectPpu(IPpuDevice? ppu)
    {
        Ppu = ppu;
        ppu?.ConnectCartridge(Cartridge);
    }

    public void ConnectApu(IApuDevice? apu) => Apu = apu;

    public void InsertCartridge(ICartridgeDevice cartridge)
    {
        Cartridge = cartridge;
        Ppu?.ConnectCartridge(cartridge);
        PowerOn();
    }

    public void RemoveCartridge()
    {
        Cartridge = null;
        Ppu?.ConnectCartridge(null);
    }

    /// <summary>冷启动：清空 RAM 并复位全部设备。</summary>
    public void PowerOn()
    {
        Array.Clear(_ram);
        _openBus = 0;
        _masterClock = 0;
        _ppuMasterClock = 0;
        _oamDmaPending = false;
        Controller1.Reset();
        Controller2.Reset();
        Cartridge?.Reset();
        Ppu?.PowerOn();
        Apu?.Reset();
        Cpu.PowerOn();
    }

    /// <summary>热复位（RESET 按键）：RAM、调色板、OAM 保留。</summary>
    public void Reset()
    {
        _oamDmaPending = false;
        Cartridge?.Reset();
        Ppu?.Reset();
        Apu?.Reset();
        Cpu.Reset();
    }

    #region 调度

    /// <summary>执行一条 CPU 指令，返回消耗的 CPU 周期。</summary>
    public int Step() => Cpu.Step();

    /// <summary>运行直到 PPU 完成一帧（进入 VBlank）。</summary>
    public void RunFrame()
    {
        if (Cartridge is null) return;

        var ppu = Ppu;
        if (ppu is null)
        {
            var target = Cpu.TotalCycles + 29781;
            while (Cpu.TotalCycles < target && !Cpu.Jammed) Cpu.Step();
            return;
        }

        ppu.FrameComplete = false;
        while (!ppu.FrameComplete && !Cpu.Jammed)
        {
            Cpu.Step();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void StartCycle(bool forRead)
    {
        _masterClock += forRead ? StartClocks - 1 : StartClocks + 1;
        RunPpu();
        Apu?.Clock();
        Cartridge?.CpuClock();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EndCycle(bool forRead)
    {
        _masterClock += forRead ? EndClocks + 1 : EndClocks - 1;
        RunPpu();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RunPpu()
    {
        var ppu = Ppu;
        if (ppu is null)
        {
            return;
        }

        var runTo = _masterClock - PpuOffset;
        while (_ppuMasterClock + PpuDivider <= runTo)
        {
            ppu.Clock();
            _ppuMasterClock += PpuDivider;
        }
    }

    #endregion

    #region ICpuBus

    public bool NmiLine => Ppu?.NmiLine ?? false;

    public bool IrqLine => (Apu?.IrqActive ?? false) || (Cartridge?.IrqActive ?? false);

    public bool OamDmaPending => _oamDmaPending;

    public byte TakeOamDma()
    {
        _oamDmaPending = false;
        return _oamDmaPage;
    }

    public byte Read(ushort address)
    {
        StartCycle(true);
        var value = address < 0x2000 ? _ram[address & 0x07FF] : ReadSlow(address);
        _openBus = value;
        EndCycle(true);
        return value;
    }

    public void Write(ushort address, byte value)
    {
        StartCycle(false);
        _openBus = value;
        if (address < 0x2000)
        {
            _ram[address & 0x07FF] = value;
        }
        else
        {
            WriteSlow(address, value);
        }

        EndCycle(false);
    }

    private byte ReadSlow(ushort address)
    {
        if (address < 0x4000)
        {
            return Ppu?.ReadRegister(address & 0x0007) ?? _openBus;
        }

        if (address < 0x4020)
        {
            switch (address)
            {
                case 0x4015:
                    // $4015 读取不会驱动数据总线的 bit5
                    return (byte)((Apu?.ReadStatus() ?? 0) & 0xDF | (_openBus & 0x20));
                case 0x4016:
                    return (byte)((_openBus & 0xE0) | Controller1.Read());
                case 0x4017:
                    return (byte)((_openBus & 0xE0) | Controller2.Read());
                default:
                    return _openBus;
            }
        }

        if (Cartridge is not null && Cartridge.TryCpuRead(address, out var value))
        {
            return value;
        }

        return _openBus;
    }

    private void WriteSlow(ushort address, byte value)
    {
        if (address < 0x4000)
        {
            Ppu?.WriteRegister(address & 0x0007, value);
            return;
        }

        if (address < 0x4020)
        {
            switch (address)
            {
                case 0x4014:
                    _oamDmaPage = value;
                    _oamDmaPending = true;
                    return;
                case 0x4016:
                    Controller1.WriteStrobe(value);
                    Controller2.WriteStrobe(value);
                    return;
                default:
                    if (address <= 0x4017)
                    {
                        Apu?.WriteRegister(address, value);
                    }

                    return;
            }
        }

        Cartridge?.TryCpuWrite(address, value);
    }

    public byte Peek(ushort address)
    {
        if (address < 0x2000) return _ram[address & 0x07FF];
        if (address < 0x4000) return Ppu?.PeekRegister(address & 0x0007) ?? 0;
        if (address < 0x4020)
        {
            return address switch
            {
                0x4016 => Controller1.Peek(),
                0x4017 => Controller2.Peek(),
                _ => 0
            };
        }

        return Cartridge is not null && Cartridge.TryCpuRead(address, out var v) ? v : (byte)0;
    }

    #endregion
}
