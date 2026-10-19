namespace AtomNES.Device.Cartridges;

// 各厂商 ASIC Mapper：MMC2/4、Konami VRC2/4、Irem、Taito、Sunsoft FME-7 等。

/// <summary>Mapper 9 (MMC2) / 10 (MMC4)：读取 $FD/$FE 图块时切换 4KB CHR 锁存。</summary>
public sealed class Mmc2(RomImage rom, bool mmc4) : Mapper(rom)
{
    private readonly int[] _left = new int[2];
    private readonly int[] _right = new int[2];
    private int _leftLatch = 1;
    private int _rightLatch = 1;
    private bool _pending;

    public override void Reset()
    {
        _leftLatch = _rightLatch = 1;
        if (mmc4)
        {
            SetPrg16K(0, 0);
            SetPrg16K(1, -1);
        }
        else
        {
            SetPrg8K(0, 0);
            SetPrg8K(1, -3);
            SetPrg8K(2, -2);
            SetPrg8K(3, -1);
        }

        UpdateChr();
    }

    protected override void WriteRegister(ushort address, byte value)
    {
        switch (address >> 12)
        {
            case 0xA:
                if (mmc4) SetPrg16K(0, value & 0x0F);
                else SetPrg8K(0, value & 0x0F);
                break;
            case 0xB: _left[0] = value & 0x1F; break;
            case 0xC: _left[1] = value & 0x1F; break;
            case 0xD: _right[0] = value & 0x1F; break;
            case 0xE: _right[1] = value & 0x1F; break;
            case 0xF: SetMirroring((value & 1) != 0 ? Mirroring.Horizontal : Mirroring.Vertical); break;
        }

        UpdateChr();
    }

    private void UpdateChr()
    {
        SetChr4K(0, _left[_leftLatch]);
        SetChr4K(1, _right[_rightLatch]);
    }

    protected override void OnPpuAddress(ushort address)
    {
        // 锁存在图块读取完成后才生效，因此在下一次地址变化时再更新页表
        if (_pending)
        {
            UpdateChr();
            _pending = false;
        }

        if (address >= 0x2000) return;
        if (mmc4 ? address is >= 0x0FD8 and <= 0x0FDF : address == 0x0FD8) { _leftLatch = 0; _pending = true; }
        else if (mmc4 ? address is >= 0x0FE8 and <= 0x0FEF : address == 0x0FE8) { _leftLatch = 1; _pending = true; }
        else if (address is >= 0x1FD8 and <= 0x1FDF) { _rightLatch = 0; _pending = true; }
        else if (address is >= 0x1FE8 and <= 0x1FEF) { _rightLatch = 1; _pending = true; }
    }
}

/// <summary>Konami VRC 系列共用的扫描线/周期 IRQ 计数器。</summary>
public sealed class VrcIrq
{
    private int _reload;
    private int _counter;
    private int _prescaler;
    private bool _enabled;
    private bool _enableAfterAck;
    private bool _cycleMode;

    public bool Pending { get; private set; }

    public void SetReloadNibble(byte value, bool high) =>
        _reload = high ? (_reload & 0x0F) | ((value & 0x0F) << 4) : (_reload & 0xF0) | (value & 0x0F);

    public void SetReload(byte value) => _reload = value;

    public void SetControl(byte value)
    {
        _enableAfterAck = (value & 1) != 0;
        _enabled = (value & 2) != 0;
        _cycleMode = (value & 4) != 0;
        if (_enabled)
        {
            _counter = _reload;
            _prescaler = 341;
        }

        Pending = false;
    }

    public void Acknowledge()
    {
        _enabled = _enableAfterAck;
        Pending = false;
    }

    public void Clock()
    {
        if (!_enabled) return;
        _prescaler -= 3;
        if (!_cycleMode && _prescaler > 0) return;
        if (_counter == 0xFF)
        {
            _counter = _reload;
            Pending = true;
        }
        else
        {
            _counter++;
        }

        _prescaler += 341;
    }
}

/// <summary>Mapper 21/22/23/25 (Konami VRC2/VRC4)：地址线按变体映射到寄存器 A0/A1。</summary>
public sealed class Vrc24(RomImage rom) : Mapper(rom)
{
    private readonly int[] _chrLo = new int[8];
    private readonly int[] _chrHi = new int[8];
    private readonly VrcIrq _irq = new();
    private readonly bool _vrc2 = rom.MapperId == 22 || (rom.MapperId == 23 && rom.SubMapper is 0 or 3) || (rom.MapperId == 25 && rom.SubMapper == 3);
    private readonly bool _heuristics = rom.SubMapper == 0 && rom.MapperId != 22;
    private int _prg0;
    private int _prg1;
    private int _prgMode;
    private int _latch;

    public override bool IrqActive => _irq.Pending;

    public override void Reset()
    {
        Update();
    }

    public override void CpuClock()
    {
        base.CpuClock();
        if (_heuristics || !_vrc2) _irq.Clock();
    }

    public override bool TryCpuRead(ushort address, out byte value)
    {
        if (address is >= 0x6000 and < 0x7000 && _vrc2 && !_heuristics && !Rom.IsNes20)
        {
            value = (byte)_latch;
            return true;
        }

        return base.TryCpuRead(address, out value);
    }

    protected override bool WriteLowRegister(ushort address, byte value)
    {
        if (address is < 0x6000 or >= 0x7000) return false;
        _latch = value & 1;
        return true;
    }

    private int Translate(int a)
    {
        int a0, a1;
        if (_heuristics)
        {
            switch (Rom.MapperId)
            {
                case 21:
                    a0 = ((a >> 1) | (a >> 6)) & 1;
                    a1 = ((a >> 2) | (a >> 7)) & 1;
                    break;
                case 25:
                    a0 = ((a >> 1) | (a >> 3)) & 1;
                    a1 = (a | (a >> 2)) & 1;
                    break;
                default:
                    a0 = (a | (a >> 2)) & 1;
                    a1 = ((a >> 1) | (a >> 3)) & 1;
                    break;
            }
        }
        else
        {
            (a0, a1) = (Rom.MapperId, Rom.SubMapper) switch
            {
                (22, _) => ((a >> 1) & 1, a & 1),
                (21, 2) => ((a >> 6) & 1, (a >> 7) & 1),
                (21, _) => ((a >> 1) & 1, (a >> 2) & 1),
                (25, 2) => ((a >> 3) & 1, (a >> 2) & 1),
                (25, _) => ((a >> 1) & 1, a & 1),
                (23, 2) => ((a >> 2) & 1, (a >> 3) & 1),
                _ => (a & 1, (a >> 1) & 1)
            };
        }

        return (a & 0xFF00) | (a1 << 1) | a0;
    }

    protected override void WriteRegister(ushort address, byte value)
    {
        var a = Translate(address) & 0xF00F;
        if (a is >= 0x8000 and <= 0x8006) _prg0 = value & 0x1F;
        else if (a is >= 0x9000 and <= 0x9001 || (_vrc2 && a is >= 0x9002 and <= 0x9003))
        {
            var mask = !_heuristics && _vrc2 ? 1 : 3;
            SetMirroring((value & mask) switch
            {
                0 => Mirroring.Vertical,
                1 => Mirroring.Horizontal,
                2 => Mirroring.SingleScreenLow,
                _ => Mirroring.SingleScreenHigh
            });
        }
        else if (!_vrc2 && a is >= 0x9002 and <= 0x9003) _prgMode = (value >> 1) & 1;
        else if (a is >= 0xA000 and <= 0xA006) _prg1 = value & 0x1F;
        else if (a is >= 0xB000 and <= 0xE006)
        {
            var reg = ((((a >> 12) & 7) - 3) << 1) + ((a >> 1) & 1);
            if ((a & 1) == 0) _chrLo[reg] = value & 0x0F;
            else _chrHi[reg] = value & 0x1F;
        }
        else if (a == 0xF000) _irq.SetReloadNibble(value, false);
        else if (a == 0xF001) _irq.SetReloadNibble(value, true);
        else if (a == 0xF002) _irq.SetControl(value);
        else if (a == 0xF003) _irq.Acknowledge();

        Update();
    }

    private void Update()
    {
        for (var i = 0; i < 8; i++)
        {
            var page = _chrLo[i] | (_chrHi[i] << 4);
            SetChr1K(i, Rom.MapperId == 22 ? page >> 1 : page);
        }

        if (_prgMode == 0)
        {
            SetPrg8K(0, _prg0);
            SetPrg8K(2, -2);
        }
        else
        {
            SetPrg8K(0, -2);
            SetPrg8K(2, _prg0);
        }

        SetPrg8K(1, _prg1);
        SetPrg8K(3, -1);
    }
}

/// <summary>Mapper 32 (Irem G-101)。</summary>
public sealed class IremG101(RomImage rom) : Mapper(rom)
{
    private int _prg0;
    private int _prg1;
    private int _mode;

    public override void Reset()
    {
        if (Rom.SubMapper == 1) SetMirroring(Mirroring.SingleScreenLow);
        Update();
    }

    protected override void WriteRegister(ushort address, byte value)
    {
        switch (address & 0xF000)
        {
            case 0x8000: _prg0 = value & 0x1F; break;
            case 0x9000:
                _mode = Rom.SubMapper == 1 ? 0 : (value >> 1) & 1;
                if (Rom.SubMapper != 1) SetMirroring((value & 1) != 0 ? Mirroring.Horizontal : Mirroring.Vertical);
                break;
            case 0xA000: _prg1 = value & 0x1F; break;
            case 0xB000: SetChr1K(address & 7, value); break;
        }

        Update();
    }

    private void Update()
    {
        SetPrg8K(_mode == 0 ? 0 : 2, _prg0);
        SetPrg8K(_mode == 0 ? 2 : 0, -2);
        SetPrg8K(1, _prg1);
        SetPrg8K(3, -1);
    }
}

/// <summary>Mapper 33 (Taito TC0190)。</summary>
public sealed class TaitoTc0190(RomImage rom) : Mapper(rom)
{
    public override void Reset()
    {
        SetPrg8K(2, -2);
        SetPrg8K(3, -1);
    }

    protected override void WriteRegister(ushort address, byte value)
    {
        switch (address & 0xA003)
        {
            case 0x8000:
                SetPrg8K(0, value & 0x3F);
                SetMirroring((value & 0x40) != 0 ? Mirroring.Horizontal : Mirroring.Vertical);
                break;
            case 0x8001: SetPrg8K(1, value & 0x3F); break;
            case 0x8002:
                SetChr1K(0, value * 2);
                SetChr1K(1, value * 2 + 1);
                break;
            case 0x8003:
                SetChr1K(2, value * 2);
                SetChr1K(3, value * 2 + 1);
                break;
            default: SetChr1K(4 + (address & 3), value); break;
        }
    }
}

/// <summary>Mapper 48 (Taito TC0690)：TC0190 + MMC3 风格扫描线 IRQ（A12 驱动，延迟触发）。</summary>
public sealed class TaitoTc0690(RomImage rom) : Mapper(rom)
{
    private int _latch;
    private int _counter;
    private bool _reload;
    private bool _enabled;
    private int _delay;
    private bool _a12;
    private long _a12LowSince;

    public override void Reset()
    {
        SetPrg8K(2, -2);
        SetPrg8K(3, -1);
    }

    public override void CpuClock()
    {
        base.CpuClock();
        if (_delay > 0 && --_delay == 0) IrqAsserted = true;
    }

    protected override void WriteRegister(ushort address, byte value)
    {
        switch (address & 0xE003)
        {
            case 0x8000: SetPrg8K(0, value & 0x3F); break;
            case 0x8001: SetPrg8K(1, value & 0x3F); break;
            case 0x8002:
                SetChr1K(0, value * 2);
                SetChr1K(1, value * 2 + 1);
                break;
            case 0x8003:
                SetChr1K(2, value * 2);
                SetChr1K(3, value * 2 + 1);
                break;
            case 0xA000: case 0xA001: case 0xA002: case 0xA003:
                SetChr1K(4 + (address & 3), value);
                break;
            case 0xC000:
                IrqAsserted = false;
                _latch = (value ^ 0xFF) + (Rom.SubMapper == 1 ? 1 : 0);
                break;
            case 0xC001:
                IrqAsserted = false;
                _counter = 0;
                _reload = true;
                break;
            case 0xC002: _enabled = true; break;
            case 0xC003:
                _enabled = false;
                IrqAsserted = false;
                break;
            case 0xE000:
                SetMirroring((value & 0x40) != 0 ? Mirroring.Horizontal : Mirroring.Vertical);
                break;
        }
    }

    protected override void OnPpuAddress(ushort address)
    {
        var a12 = (address & 0x1000) != 0;
        if (a12 && !_a12 && M2Cycles - _a12LowSince > 3)
        {
            if (_counter == 0 || _reload)
            {
                _counter = _latch & 0xFF;
                _reload = false;
            }
            else
            {
                _counter--;
            }

            if (_counter == 0 && _enabled) _delay = Rom.SubMapper == 1 ? 6 : 22;
        }
        else if (!a12 && _a12)
        {
            _a12LowSince = M2Cycles;
        }

        _a12 = a12;
    }
}

/// <summary>Mapper 65 (Irem H3001)：16 位 CPU 周期 IRQ 计数器。</summary>
public sealed class IremH3001(RomImage rom) : Mapper(rom)
{
    private bool _irqEnabled;
    private int _counter;
    private int _reload;

    public override void Reset()
    {
        SetPrg8K(0, 0);
        SetPrg8K(1, 1);
        SetPrg8K(2, 0xFE);
        SetPrg8K(3, -1);
    }

    public override void CpuClock()
    {
        base.CpuClock();
        if (!_irqEnabled) return;
        _counter = (_counter - 1) & 0xFFFF;
        if (_counter != 0) return;
        _irqEnabled = false;
        IrqAsserted = true;
    }

    protected override void WriteRegister(ushort address, byte value)
    {
        switch (address)
        {
            case 0x8000: SetPrg8K(0, value); break;
            case 0x9001: SetMirroring((value & 0x80) != 0 ? Mirroring.Horizontal : Mirroring.Vertical); break;
            case 0x9003:
                _irqEnabled = (value & 0x80) != 0;
                IrqAsserted = false;
                break;
            case 0x9004:
                _counter = _reload;
                IrqAsserted = false;
                break;
            case 0x9005: _reload = (_reload & 0xFF) | (value << 8); break;
            case 0x9006: _reload = (_reload & 0xFF00) | value; break;
            case 0xA000: SetPrg8K(1, value); break;
            case 0xC000: SetPrg8K(2, value); break;
            case >= 0xB000 and <= 0xB007: SetChr1K(address & 7, value); break;
        }
    }
}

/// <summary>Mapper 69 (Sunsoft FME-7)：命令/参数寄存器，$6000 可映射 ROM 或 RAM，16 位周期 IRQ（不含 5B 音频）。</summary>
public sealed class SunsoftFme7(RomImage rom) : Mapper(rom)
{
    private int _command;
    private int _ram;
    private bool _irqEnabled;
    private bool _counterEnabled;
    private int _counter;

    public override void Reset()
    {
        SetPrg8K(3, -1);
    }

    public override void CpuClock()
    {
        base.CpuClock();
        if (!_counterEnabled) return;
        _counter = (_counter - 1) & 0xFFFF;
        if (_counter == 0xFFFF && _irqEnabled) IrqAsserted = true;
    }

    protected override bool PrgRamEnabled => (_ram & 0xC0) == 0xC0;

    public override bool TryCpuRead(ushort address, out byte value)
    {
        if (address is >= 0x6000 and < 0x8000 && (_ram & 0x40) == 0)
        {
            value = ReadPrg8K(_ram & 0x3F, address);
            return true;
        }

        return base.TryCpuRead(address, out value);
    }

    protected override void WriteRegister(ushort address, byte value)
    {
        switch (address & 0xE000)
        {
            case 0x8000: _command = value & 0x0F; break;
            case 0xA000:
                switch (_command)
                {
                    case <= 7: SetChr1K(_command, value); break;
                    case 8: _ram = value; break;
                    case <= 0xB: SetPrg8K(_command - 9, value & 0x3F); break;
                    case 0xC:
                        SetMirroring((value & 3) switch
                        {
                            0 => Mirroring.Vertical,
                            1 => Mirroring.Horizontal,
                            2 => Mirroring.SingleScreenLow,
                            _ => Mirroring.SingleScreenHigh
                        });
                        break;
                    case 0xD:
                        _irqEnabled = (value & 1) != 0;
                        _counterEnabled = (value & 0x80) != 0;
                        IrqAsserted = false;
                        break;
                    case 0xE: _counter = (_counter & 0xFF00) | value; break;
                    default: _counter = (_counter & 0xFF) | (value << 8); break;
                }

                break;
        }
    }
}

/// <summary>Mapper 252 (Waixing VRC4 克隆)：CHR 页 6/7 指向 CHR RAM，带 VRC IRQ。</summary>
public sealed class Waixing252(RomImage rom) : Mapper(rom, 0x800)
{
    private readonly int[] _chr = new int[8];
    private readonly VrcIrq _irq = new();

    public override bool IrqActive => _irq.Pending;

    public override void Reset()
    {
        SetPrg8K(2, -2);
        SetPrg8K(3, -1);
        Update();
    }

    public override void CpuClock()
    {
        base.CpuClock();
        _irq.Clock();
    }

    protected override void WriteRegister(ushort address, byte value)
    {
        if (address <= 0x8FFF) SetPrg8K(0, value);
        else if (address is >= 0xA000 and <= 0xAFFF) SetPrg8K(1, value);
        else if (address is >= 0xB000 and <= 0xEFFF)
        {
            var shift = address & 4;
            var bank = ((((address - 0xB000) >> 1) & 0x1800) | ((address << 7) & 0x0400)) / 0x400;
            _chr[bank] = (_chr[bank] & (0xF0 >> shift)) | ((value & 0x0F) << shift);
            Update();
        }
        else
        {
            switch (address & 0xF00C)
            {
                case 0xF000: _irq.SetReloadNibble(value, false); break;
                case 0xF004: _irq.SetReloadNibble(value, true); break;
                case 0xF008: _irq.SetControl(value); break;
                case 0xF00C: _irq.Acknowledge(); break;
            }
        }
    }

    private void Update()
    {
        for (var i = 0; i < 8; i++)
        {
            if (_chr[i] is 6 or 7 && !Rom.HasChrRam) SetChrRam1K(i, _chr[i] & 1);
            else SetChr1K(i, _chr[i]);
        }
    }
}
