namespace AtomNES.Device.Cartridges;

/// <summary>A12 上升沿检测（过滤背景取图期间的抖动：A12 需保持低电平超过 3 个 M2 周期）。</summary>
internal struct A12Watcher
{
    private bool _a12;
    private long _lowSince;

    public bool Rise(ushort address, long m2Cycles)
    {
        var a12 = (address & 0x1000) != 0;
        var rise = a12 && !_a12 && m2Cycles - _lowSince > 3;
        if (!a12 && _a12) _lowSince = m2Cycles;
        _a12 = a12;
        return rise;
    }
}

/// <summary>Mapper 64 (Tengen RAMBO-1)：MMC3 扩展版，3 个可切换 PRG 页、可选 1KB CHR 模式，IRQ 可由 A12 或 CPU 周期驱动。</summary>
public sealed class Rambo1(RomImage rom) : Mapper(rom)
{
    private readonly int[] _regs = new int[16];
    private int _command;
    private bool _irqEnabled;
    private bool _cycleMode;
    private bool _needReload;
    private int _counter;
    private int _reload;
    private int _cpuPrescaler;
    private int _irqDelay;
    private bool _forceClock;
    private A12Watcher _a12;

    public override void Reset()
    {
        SetPrg8K(3, -1);
        Update();
    }

    public override void CpuClock()
    {
        base.CpuClock();
        if (_irqDelay > 0 && --_irqDelay == 0) IrqAsserted = true;
        if (!_cycleMode && !_forceClock) return;
        _cpuPrescaler = (_cpuPrescaler + 1) & 3;
        if (_cpuPrescaler != 0) return;
        ClockCounter(1);
        _forceClock = false;
    }

    private void ClockCounter(int delay)
    {
        if (_needReload)
        {
            _counter = _reload <= 1 ? _reload + 1 : _reload + 2;
            _needReload = false;
        }
        else if (_counter == 0)
        {
            _counter = _reload + 1;
        }

        _counter = (_counter - 1) & 0xFF;
        if (_counter == 0 && _irqEnabled) _irqDelay = delay;
    }

    protected override void OnPpuAddress(ushort address)
    {
        if (address < 0x2000 && _a12.Rise(address, M2Cycles) && !_cycleMode) ClockCounter(2);
    }

    protected override void WriteRegister(ushort address, byte value)
    {
        switch (address & 0xE001)
        {
            case 0x8000: _command = value; break;
            case 0x8001:
                _regs[_command & 0x0F] = value;
                Update();
                break;
            case 0xA000: SetMirroring((value & 1) != 0 ? Mirroring.Horizontal : Mirroring.Vertical); break;
            case 0xC000: _reload = value; break;
            case 0xC001:
                if (_cycleMode && (value & 1) == 0) _forceClock = true;
                _cycleMode = (value & 1) != 0;
                if (_cycleMode) _cpuPrescaler = 0;
                _needReload = true;
                break;
            case 0xE000:
                _irqEnabled = false;
                IrqAsserted = false;
                break;
            default: _irqEnabled = true; break;
        }
    }

    private void Update()
    {
        if ((_command & 0x40) != 0)
        {
            SetPrg8K(0, _regs[15]);
            SetPrg8K(1, _regs[6]);
            SetPrg8K(2, _regs[7]);
        }
        else
        {
            SetPrg8K(0, _regs[6]);
            SetPrg8K(1, _regs[7]);
            SetPrg8K(2, _regs[15]);
        }

        var inv = (_command & 0x80) != 0 ? 4 : 0;
        SetChr1K(0 ^ inv, _regs[0]);
        SetChr1K(2 ^ inv, _regs[1]);
        SetChr1K(4 ^ inv, _regs[2]);
        SetChr1K(5 ^ inv, _regs[3]);
        SetChr1K(6 ^ inv, _regs[4]);
        SetChr1K(7 ^ inv, _regs[5]);
        var full1K = (_command & 0x20) != 0;
        SetChr1K(1 ^ inv, full1K ? _regs[8] : _regs[0] + 1);
        SetChr1K(3 ^ inv, full1K ? _regs[9] : _regs[1] + 1);
    }
}

/// <summary>Mapper 91 (JY Company 简化版，街头霸王盗版等)：$6000 写 2KB CHR，$7000 写 8KB PRG，MMC3 式扫描线 IRQ。</summary>
public sealed class Mapper91(RomImage rom) : Mapper(rom)
{
    private int _latch;
    private int _counter;
    private bool _reload;
    private bool _enabled;
    private A12Watcher _a12;

    public override void Reset()
    {
        SetPrg8K(2, -2);
        SetPrg8K(3, -1);
    }

    protected override bool WriteLowRegister(ushort address, byte value)
    {
        if (address < 0x6000) return false;
        switch (address & 0x7003)
        {
            case 0x6000: case 0x6001: case 0x6002: case 0x6003:
                var slot = (address & 3) * 2;
                SetChr1K(slot, value * 2);
                SetChr1K(slot + 1, value * 2 + 1);
                break;
            case 0x7000: SetPrg8K(0, value & 0x0F); break;
            case 0x7001: SetPrg8K(1, value & 0x0F); break;
            case 0x7002:
                _enabled = false;
                IrqAsserted = false;
                break;
            case 0x7003:
                _latch = 7;
                _counter = 0;
                _reload = true;
                _enabled = true;
                break;
        }

        return true;
    }

    protected override void OnPpuAddress(ushort address)
    {
        if (address >= 0x2000 || !_a12.Rise(address, M2Cycles)) return;
        if (_counter == 0 || _reload)
        {
            _counter = _latch;
            _reload = false;
        }
        else
        {
            _counter--;
        }

        if (_counter == 0 && _enabled) IrqAsserted = true;
    }
}

/// <summary>Mapper 117 (Future Media)：8KB PRG / 1KB CHR，A12 扫描线 IRQ（计数到 0 后自动关闭）。</summary>
public sealed class Mapper117(RomImage rom) : Mapper(rom)
{
    private int _counter;
    private int _reload;
    private bool _enabled;
    private bool _armed;
    private A12Watcher _a12;

    public override void Reset()
    {
        for (var i = 0; i < 4; i++) SetPrg8K(i, -4 + i);
        SetChr8K(0);
    }

    protected override void WriteRegister(ushort address, byte value)
    {
        switch (address)
        {
            case >= 0x8000 and <= 0x8003: SetPrg8K(address & 3, value); break;
            case >= 0xA000 and <= 0xA007: SetChr1K(address & 7, value); break;
            case 0xC001: _reload = value; break;
            case 0xC002: IrqAsserted = false; break;
            case 0xC003:
                _counter = _reload;
                _armed = true;
                break;
            case 0xD000: SetMirroring((value & 1) != 0 ? Mirroring.Horizontal : Mirroring.Vertical); break;
            case 0xE000:
                _enabled = (value & 1) != 0;
                IrqAsserted = false;
                break;
        }
    }

    protected override void OnPpuAddress(ushort address)
    {
        if (address >= 0x2000 || !_a12.Rise(address, M2Cycles)) return;
        if (!_enabled || !_armed || _counter == 0) return;
        if (--_counter != 0) return;
        IrqAsserted = true;
        _armed = false;
    }
}
