namespace AtomNES.Device.Cartridges;

// 阶段 B：正式发行游戏中的中频 Mapper（Bandai、Jaleco、Sunsoft、Konami）。

/// <summary>
/// Mapper 16 / 153 / 159 (Bandai FCG-1/2、LZ93D50)：1KB CHR、16KB PRG、16 位 CPU 周期 IRQ。
/// 153 使用 CHR 寄存器 bit0 选择 256KB 外层 PRG 并带 PRG RAM。EEPROM 存档暂未实现（读回 0）。
/// </summary>
public sealed class BandaiFcg(RomImage rom) : Mapper(rom)
{
    private readonly int[] _chr = new int[8];
    private bool _irqEnabled;
    private int _counter;
    private int _reload;
    private int _prg;
    private int _outer;
    private bool _ramEnabled;

    private bool Lz93D50WithLatch => Rom.MapperId != 16 || Rom.SubMapper != 4;

    private bool OuterPrg => Rom.MapperId == 153 || PrgBankCount8K >= 0x40;

    public override void Reset()
    {
        SetPrg16K(0, 0);
        SetPrg16K(1, 0x0F);
        SetChr8K(0);
    }

    protected override bool PrgRamEnabled => Rom.MapperId == 153 && _ramEnabled;

    public override void CpuClock()
    {
        base.CpuClock();
        if (!_irqEnabled) return;
        if (_counter == 0) IrqAsserted = true;
        _counter = (_counter - 1) & 0xFFFF;
    }

    public override bool TryCpuRead(ushort address, out byte value)
    {
        if (address is >= 0x6000 and < 0x8000 && Rom.MapperId != 153)
        {
            value = 0;
            return true;
        }

        return base.TryCpuRead(address, out value);
    }

    protected override bool WriteLowRegister(ushort address, byte value)
    {
        if (address < 0x6000) return false;
        if (Rom.MapperId != 16 || Rom.SubMapper == 5) return Rom.MapperId == 153;
        Write(address, value);
        return true;
    }

    protected override void WriteRegister(ushort address, byte value)
    {
        if (Rom.MapperId == 16 && Rom.SubMapper == 4) return;
        Write(address, value);
    }

    private void Write(ushort address, byte value)
    {
        switch (address & 0x0F)
        {
            case <= 7:
                _chr[address & 7] = value;
                if (OuterPrg)
                {
                    _outer = 0;
                    foreach (var c in _chr) _outer |= (c & 1) << 4;
                    UpdatePrg();
                }
                else if (!Rom.HasChrRam)
                {
                    SetChr1K(address & 7, value);
                }

                break;
            case 8:
                _prg = value & 0x0F;
                UpdatePrg();
                break;
            case 9:
                SetMirroring((value & 3) switch
                {
                    0 => Mirroring.Vertical,
                    1 => Mirroring.Horizontal,
                    2 => Mirroring.SingleScreenLow,
                    _ => Mirroring.SingleScreenHigh
                });
                break;
            case 0xA:
                _irqEnabled = (value & 1) != 0;
                if (Lz93D50WithLatch) _counter = _reload;
                IrqAsserted = false;
                break;
            case 0xB:
                if (Lz93D50WithLatch) _reload = (_reload & 0xFF00) | value;
                else _counter = (_counter & 0xFF00) | value;
                break;
            case 0xC:
                if (Lz93D50WithLatch) _reload = (_reload & 0xFF) | (value << 8);
                else _counter = (_counter & 0xFF) | (value << 8);
                break;
            case 0xD:
                if (Rom.MapperId == 153) _ramEnabled = (value & 0x20) != 0;
                break;
        }
    }

    private void UpdatePrg()
    {
        SetPrg16K(0, _prg | _outer);
        SetPrg16K(1, 0x0F | _outer);
    }
}

/// <summary>Mapper 18 (Jaleco SS88006)：寄存器按 4 位半字节写入，IRQ 计数器宽度可选 4/8/12/16 位。</summary>
public sealed class JalecoSs88006(RomImage rom) : Mapper(rom)
{
    private static readonly int[] Masks = [0xFFFF, 0x0FFF, 0x00FF, 0x000F];
    private readonly int[] _prg = new int[3];
    private readonly int[] _chr = new int[8];
    private readonly int[] _reload = new int[4];
    private int _counter;
    private int _size;
    private bool _irqEnabled;

    public override void Reset()
    {
        SetPrg8K(3, -1);
    }

    public override void CpuClock()
    {
        base.CpuClock();
        if (!_irqEnabled) return;
        var mask = Masks[_size];
        var c = ((_counter & mask) - 1) & mask;
        if (c == 0) IrqAsserted = true;
        _counter = (_counter & ~mask) | c;
    }

    protected override void WriteRegister(ushort address, byte value)
    {
        var high = (address & 1) != 0;
        value &= 0x0F;
        int Nibble(int old) => high ? (old & 0x0F) | (value << 4) : (old & 0xF0) | value;
        switch (address & 0xF003)
        {
            case 0x8000: case 0x8001: SetPrg8K(0, _prg[0] = Nibble(_prg[0])); break;
            case 0x8002: case 0x8003: SetPrg8K(1, _prg[1] = Nibble(_prg[1])); break;
            case 0x9000: case 0x9001: SetPrg8K(2, _prg[2] = Nibble(_prg[2])); break;
            case >= 0xA000 and <= 0xD003:
                var reg = (((address >> 12) - 0xA) << 1) | ((address >> 1) & 1);
                SetChr1K(reg, _chr[reg] = Nibble(_chr[reg]));
                break;
            case >= 0xE000 and <= 0xE003: _reload[address & 3] = value; break;
            case 0xF000:
                IrqAsserted = false;
                _counter = _reload[0] | (_reload[1] << 4) | (_reload[2] << 8) | (_reload[3] << 12);
                break;
            case 0xF001:
                IrqAsserted = false;
                _irqEnabled = (value & 1) != 0;
                _size = (value & 8) != 0 ? 3 : (value & 4) != 0 ? 2 : (value & 2) != 0 ? 1 : 0;
                break;
            case 0xF002:
                SetMirroring((value & 3) switch
                {
                    0 => Mirroring.Horizontal,
                    1 => Mirroring.Vertical,
                    2 => Mirroring.SingleScreenLow,
                    _ => Mirroring.SingleScreenHigh
                });
                break;
        }
    }
}

/// <summary>Mapper 67 (Sunsoft-3)：2KB CHR、16KB PRG、16 位 CPU 周期 IRQ（双写装载）。</summary>
public sealed class Sunsoft3(RomImage rom) : Mapper(rom)
{
    private bool _latch;
    private bool _irqEnabled;
    private int _counter;

    public override void Reset()
    {
        SetPrg16K(0, 0);
        SetPrg16K(1, -1);
    }

    public override void CpuClock()
    {
        base.CpuClock();
        if (!_irqEnabled) return;
        _counter = (_counter - 1) & 0xFFFF;
        if (_counter != 0xFFFF) return;
        _irqEnabled = false;
        IrqAsserted = true;
    }

    protected override void WriteRegister(ushort address, byte value)
    {
        switch (address & 0xF800)
        {
            case 0x8800: case 0x9800: case 0xA800: case 0xB800:
                var slot = ((address >> 12) - 8) * 2;
                SetChr1K(slot, value * 2);
                SetChr1K(slot + 1, value * 2 + 1);
                break;
            case 0xC800:
                _counter = _latch ? (_counter & 0xFF00) | value : (_counter & 0xFF) | (value << 8);
                _latch = !_latch;
                break;
            case 0xD800:
                _irqEnabled = (value & 0x10) != 0;
                _latch = false;
                IrqAsserted = false;
                break;
            case 0xE800:
                SetMirroring((value & 3) switch
                {
                    0 => Mirroring.Vertical,
                    1 => Mirroring.Horizontal,
                    2 => Mirroring.SingleScreenLow,
                    _ => Mirroring.SingleScreenHigh
                });
                break;
            case 0xF800: SetPrg16K(0, value); break;
        }
    }
}

/// <summary>Mapper 68 (Sunsoft-4)：2KB CHR、16KB PRG，可用 CHR ROM 的 1KB 页作为名称表。</summary>
public sealed class Sunsoft4(RomImage rom) : Mapper(rom)
{
    private readonly int[] _ntRegs = new int[2];
    private readonly int[] _ntChr = new int[4];
    private bool _chrNametables;
    private Mirroring _mirroring = rom.Mirroring;
    private bool _ramEnabled;

    public override void Reset()
    {
        SetPrg16K(0, 0);
        SetPrg16K(1, -1);
    }

    protected override bool PrgRamEnabled => _ramEnabled;

    protected override void WriteRegister(ushort address, byte value)
    {
        switch (address & 0xF000)
        {
            case 0x8000: case 0x9000: case 0xA000: case 0xB000:
                var slot = ((address >> 12) - 8) * 2;
                SetChr1K(slot, value * 2);
                SetChr1K(slot + 1, value * 2 + 1);
                break;
            case 0xC000: _ntRegs[0] = value | 0x80; UpdateNametables(); break;
            case 0xD000: _ntRegs[1] = value | 0x80; UpdateNametables(); break;
            case 0xE000:
                _mirroring = (value & 3) switch
                {
                    0 => Mirroring.Vertical,
                    1 => Mirroring.Horizontal,
                    2 => Mirroring.SingleScreenLow,
                    _ => Mirroring.SingleScreenHigh
                };
                _chrNametables = (value & 0x10) != 0;
                UpdateNametables();
                break;
            case 0xF000:
                SetPrg16K(0, value & 0x0F);
                _ramEnabled = (value & 0x10) != 0;
                break;
        }
    }

    private void UpdateNametables()
    {
        SetMirroring(_mirroring);
        for (var i = 0; i < 4; i++)
        {
            var reg = _mirroring switch
            {
                Mirroring.Vertical => i & 1,
                Mirroring.Horizontal => i >> 1,
                Mirroring.SingleScreenHigh => 1,
                _ => 0
            };
            _ntChr[i] = (_ntRegs[reg] * 0x400) % Math.Max(Chr.Length, 0x400);
        }
    }

    public override byte PpuRead(ushort address)
    {
        address &= 0x3FFF;
        if (_chrNametables && address is >= 0x2000 and < 0x3F00)
        {
            OnPpuAddress(address);
            return Chr[_ntChr[(address >> 10) & 3] + (address & 0x3FF)];
        }

        return base.PpuRead(address);
    }

    public override byte PpuPeek(ushort address)
    {
        address &= 0x3FFF;
        return _chrNametables && address is >= 0x2000 and < 0x3F00
            ? Chr[_ntChr[(address >> 10) & 3] + (address & 0x3FF)]
            : base.PpuPeek(address);
    }

    public override void PpuWrite(ushort address, byte value)
    {
        address &= 0x3FFF;
        if (_chrNametables && address is >= 0x2000 and < 0x3F00) return;
        base.PpuWrite(address, value);
    }
}

/// <summary>Mapper 75 (Konami VRC1)：3 个 8KB PRG、2 个 4KB CHR（高位在 $9000）。</summary>
public sealed class Vrc1(RomImage rom) : Mapper(rom)
{
    private readonly int[] _chr = new int[2];

    public override void Reset()
    {
        SetPrg8K(3, -1);
        Update();
    }

    protected override void WriteRegister(ushort address, byte value)
    {
        switch (address & 0xF000)
        {
            case 0x8000: SetPrg8K(0, value); break;
            case 0x9000:
                SetMirroring((value & 1) != 0 ? Mirroring.Horizontal : Mirroring.Vertical);
                _chr[0] = (_chr[0] & 0x0F) | ((value & 2) << 3);
                _chr[1] = (_chr[1] & 0x0F) | ((value & 4) << 2);
                break;
            case 0xA000: SetPrg8K(1, value); break;
            case 0xC000: SetPrg8K(2, value); break;
            case 0xE000: _chr[0] = (_chr[0] & 0x10) | (value & 0x0F); break;
            case 0xF000: _chr[1] = (_chr[1] & 0x10) | (value & 0x0F); break;
        }

        Update();
    }

    private void Update()
    {
        SetChr4K(0, _chr[0]);
        SetChr4K(1, _chr[1]);
    }
}

/// <summary>Mapper 73 (Konami VRC3)：16KB PRG + 16 位（或 8 位）递增 CPU 周期 IRQ。</summary>
public sealed class Vrc3(RomImage rom) : Mapper(rom)
{
    private bool _enabled;
    private bool _enableOnAck;
    private bool _eightBit;
    private int _reload;
    private int _counter;

    public override void Reset()
    {
        SetPrg16K(0, 0);
        SetPrg16K(1, -1);
        SetChr8K(0);
    }

    public override void CpuClock()
    {
        base.CpuClock();
        if (!_enabled) return;
        if (_eightBit)
        {
            var low = (_counter + 1) & 0xFF;
            if (low == 0)
            {
                low = _reload & 0xFF;
                IrqAsserted = true;
            }

            _counter = (_counter & 0xFF00) | low;
        }
        else
        {
            _counter = (_counter + 1) & 0xFFFF;
            if (_counter != 0) return;
            _counter = _reload;
            IrqAsserted = true;
        }
    }

    protected override void WriteRegister(ushort address, byte value)
    {
        switch (address & 0xF000)
        {
            case 0x8000: _reload = (_reload & 0xFFF0) | (value & 0x0F); break;
            case 0x9000: _reload = (_reload & 0xFF0F) | ((value & 0x0F) << 4); break;
            case 0xA000: _reload = (_reload & 0xF0FF) | ((value & 0x0F) << 8); break;
            case 0xB000: _reload = (_reload & 0x0FFF) | ((value & 0x0F) << 12); break;
            case 0xC000:
                _enabled = (value & 2) != 0;
                if (_enabled) _counter = _reload;
                _eightBit = (value & 4) != 0;
                _enableOnAck = (value & 1) != 0;
                IrqAsserted = false;
                break;
            case 0xD000:
                IrqAsserted = false;
                _enabled = _enableOnAck;
                break;
            case 0xF000: SetPrg16K(0, value & 7); break;
        }
    }
}

/// <summary>
/// Mapper 24 / 26 (Konami VRC6a/b)：16KB + 8KB PRG、1KB CHR、VRC IRQ。
/// 含扩展音频（2 脉冲 + 锯齿）；CHR ROM 名称表模式（$B003 bit4）罕用，暂按 CIRAM 处理。
/// </summary>
public sealed class Vrc6(RomImage rom) : Mapper(rom)
{
    private readonly int[] _chr = new int[8];
    private readonly VrcIrq _irq = new();
    private readonly Audio.Vrc6Audio _audio = new();
    private int _mode;

    public override float AudioOutput => _audio.Output;

    public override bool IrqActive => _irq.Pending;

    protected override bool PrgRamEnabled => (_mode & 0x80) != 0;

    public override void Reset()
    {
        SetPrg8K(3, -1);
        UpdateChr();
    }

    public override void CpuClock()
    {
        base.CpuClock();
        _irq.Clock();
        _audio.Clock();
    }

    protected override void WriteRegister(ushort address, byte value)
    {
        if (Rom.MapperId == 26) address = (ushort)((address & 0xFFFC) | ((address & 1) << 1) | ((address & 2) >> 1));
        _audio.Write(address, value);
        switch (address & 0xF003)
        {
            case >= 0x8000 and <= 0x8003: SetPrg16K(0, value & 0x0F); break;
            case 0xB003:
                _mode = value;
                UpdateChr();
                break;
            case >= 0xC000 and <= 0xC003: SetPrg8K(2, value & 0x1F); break;
            case >= 0xD000 and <= 0xD003:
                _chr[address & 3] = value;
                UpdateChr();
                break;
            case >= 0xE000 and <= 0xE003:
                _chr[4 + (address & 3)] = value;
                UpdateChr();
                break;
            case 0xF000: _irq.SetReload(value); break;
            case 0xF001: _irq.SetControl(value); break;
            case 0xF002: _irq.Acknowledge(); break;
        }
    }

    private void UpdateChr()
    {
        var mask = (_mode & 0x20) != 0 ? 0xFE : 0xFF;
        var or = (_mode & 0x20) != 0 ? 1 : 0;
        switch (_mode & 3)
        {
            case 0:
                for (var i = 0; i < 8; i++) SetChr1K(i, _chr[i]);
                break;
            case 1:
                for (var i = 0; i < 4; i++)
                {
                    SetChr1K(i * 2, _chr[i] & mask);
                    SetChr1K(i * 2 + 1, (_chr[i] & mask) | or);
                }

                break;
            default:
                for (var i = 0; i < 4; i++) SetChr1K(i, _chr[i]);
                SetChr1K(4, _chr[4] & mask);
                SetChr1K(5, (_chr[4] & mask) | or);
                SetChr1K(6, _chr[5] & mask);
                SetChr1K(7, (_chr[5] & mask) | or);
                break;
        }

        switch (_mode & 0x2F)
        {
            case 0x20 or 0x27: SetMirroring(Mirroring.Vertical); break;
            case 0x23 or 0x24: SetMirroring(Mirroring.Horizontal); break;
            case 0x28 or 0x2F: SetMirroring(Mirroring.SingleScreenLow); break;
            case 0x2B or 0x2C: SetMirroring(Mirroring.SingleScreenHigh); break;
            default:
                int[] nt = (_mode & 7) switch
                {
                    0 or 6 or 7 => [_chr[6], _chr[6], _chr[7], _chr[7]],
                    1 or 5 => [_chr[4], _chr[5], _chr[6], _chr[7]],
                    _ => [_chr[6], _chr[7], _chr[6], _chr[7]]
                };
                for (var i = 0; i < 4; i++) SetNametable(i, nt[i] & 1);
                break;
        }
    }
}
