namespace AtomNES.Device.Cartridges;

/// <summary>
/// Mapper 192/194/195（外星/南晶 MMC3 + 板载 CHR RAM）：CHR 页号落在 [first, last] 区间时映射到 CHR RAM。
/// </summary>
public sealed class Mmc3ChrRam(RomImage rom, int firstRamBank, int lastRamBank, int ramKb)
    : Mmc3(rom, rom.HasChrRam ? 0 : ramKb * 0x400)
{
    protected override void SetChrBank(int slot, int bank)
    {
        if (!Rom.HasChrRam && bank >= firstRamBank && bank <= lastRamBank) SetChrRam1K(slot, bank - firstRamBank);
        else SetChr1K(slot, bank);
    }
}

/// <summary>Mapper 199（外星 MMC3 扩展）：额外 2 个 PRG 寄存器与 2 个 CHR 寄存器，页号 &lt; 8 使用 8KB CHR RAM。</summary>
public sealed class Mapper199(RomImage rom) : Mmc3(rom, 0x2000)
{
    private readonly int[] _ex = new int[4];

    public override void Reset()
    {
        _ex[0] = 0xFE;
        _ex[1] = 0xFF;
        _ex[2] = 1;
        _ex[3] = 3;
        base.Reset();
    }

    protected override void WriteRegister(ushort address, byte value)
    {
        if ((address & 0xE001) == 0x8001 && (BankSelect & 0x08) != 0)
        {
            _ex[BankSelect & 3] = value;
            UpdateBanks();
            return;
        }

        base.WriteRegister(address, value);
    }

    protected override void WriteMirroring(byte value) => SetMirroring((value & 3) switch
    {
        0 => Mirroring.Vertical,
        1 => Mirroring.Horizontal,
        2 => Mirroring.SingleScreenLow,
        _ => Mirroring.SingleScreenHigh
    });

    protected override void UpdatePrgBanks()
    {
        base.UpdatePrgBanks();
        SetPrgBank(2, _ex[0]);
        SetPrgBank(3, _ex[1]);
    }

    protected override void UpdateChrBanks()
    {
        base.UpdateChrBanks();
        SetChrBank(0, Registers[0]);
        SetChrBank(1, _ex[2]);
        SetChrBank(2, Registers[1]);
        SetChrBank(3, _ex[3]);
    }

    protected override void SetChrBank(int slot, int bank)
    {
        if (bank < 8) SetChrRam1K(slot, bank);
        else SetChr1K(slot, bank);
    }
}

/// <summary>Mapper 162（外星）：$5000-$5300 四个寄存器组合 32KB PRG 页。</summary>
public sealed class Waixing162(RomImage rom) : Mapper(rom)
{
    private readonly int[] _regs = new int[4];

    public override void Reset()
    {
        _regs[0] = 3;
        _regs[1] = 0;
        _regs[2] = 0;
        _regs[3] = 7;
        SetChr8K(0);
        Update();
    }

    protected override bool WriteLowRegister(ushort address, byte value)
    {
        if (address is < 0x5000 or > 0x5FFF) return false;
        _regs[(address >> 8) & 3] = value;
        Update();
        return true;
    }

    private void Update()
    {
        var high = (_regs[2] & 0x0F) << 4;
        var bank = (_regs[3] & 5) switch
        {
            0 => (_regs[0] & 0x0C) | (_regs[1] & 0x02) | high,
            1 => (_regs[0] & 0x0C) | high,
            4 => (_regs[0] & 0x0E) | ((_regs[1] >> 1) & 1) | high,
            _ => (_regs[0] & 0x0F) | high
        };
        SetPrg32K(bank);
    }
}

/// <summary>Mapper 164（外星）：$5000/$5100 分别写 32KB PRG 页的低/高 4 位。</summary>
public sealed class Waixing164(RomImage rom) : Mapper(rom)
{
    private int _prg;

    public override void Reset()
    {
        _prg = 0x0F;
        SetPrg32K(_prg);
        SetChr8K(0);
    }

    protected override bool WriteLowRegister(ushort address, byte value)
    {
        if (address is < 0x5000 or > 0x5FFF) return false;
        switch (address & 0x7300)
        {
            case 0x5000: _prg = (_prg & 0xF0) | (value & 0x0F); break;
            case 0x5100: _prg = (_prg & 0x0F) | ((value & 0x0F) << 4); break;
            default: return true;
        }

        SetPrg32K(_prg);
        return true;
    }
}

/// <summary>
/// Mapper 163（南晶）：32KB PRG 页 + 保护寄存器读取；
/// 自动 CHR 切换模式下在第 127/239 行切换 4KB CHR RAM 页，实现整屏不同图块。
/// </summary>
public sealed class Nanjing163(RomImage rom) : Mapper(rom)
{
    private readonly int[] _regs = new int[5];
    private bool _toggle;
    private bool _autoSwitchChr;

    public override void Reset()
    {
        Array.Clear(_regs);
        _autoSwitchChr = false;
        _toggle = true;
        SetPrg32K(0);
        SetChr4K(0, 0);
        SetChr4K(1, 0);
    }

    private void Update()
    {
        _autoSwitchChr = (_regs[0] & 0x80) != 0;
        SetPrg32K((_regs[0] & 0x0F) | ((_regs[2] & 0x0F) << 4));
    }

    public override bool TryCpuRead(ushort address, out byte value)
    {
        if (address is >= 0x5000 and < 0x6000)
        {
            value = (address & 0x7700) switch
            {
                0x5100 => (byte)(_regs[3] | _regs[1] | _regs[0] | (_regs[2] ^ 0xFF)),
                0x5500 => _toggle ? (byte)(_regs[3] | _regs[0]) : (byte)0,
                _ => 4
            };
            return true;
        }

        return base.TryCpuRead(address, out value);
    }

    protected override bool WriteLowRegister(ushort address, byte value)
    {
        if (address is < 0x5000 or > 0x5FFF) return false;
        if (address == 0x5101)
        {
            if (_regs[4] != 0 && value == 0) _toggle = !_toggle;
            _regs[4] = value;
        }
        else if (address == 0x5100 && value == 6)
        {
            SetPrg32K(3);
        }
        else
        {
            switch (address & 0x7300)
            {
                case 0x5000:
                    _regs[0] = value;
                    if ((value & 0x80) == 0 && (Ppu?.Scanline ?? 0) < 128)
                    {
                        SetChr4K(0, 0);
                        SetChr4K(1, 1);
                    }

                    Update();
                    break;
                case 0x5100:
                    _regs[1] = value;
                    if (value == 6) SetPrg32K(3);
                    break;
                case 0x5200:
                    _regs[2] = value;
                    Update();
                    break;
                case 0x5300:
                    _regs[3] = value;
                    break;
            }
        }

        return true;
    }

    protected override void OnPpuAddress(ushort address)
    {
        if (!_autoSwitchChr || Ppu is not { Dot: > 256 } ppu) return;
        if (ppu.Scanline == 239)
        {
            SetChr4K(0, 0);
            SetChr4K(1, 0);
        }
        else if (ppu.Scanline == 127)
        {
            SetChr4K(0, 1);
            SetChr4K(1, 1);
        }
    }
}

/// <summary>Mapper 253（外星 VRC4 变种）：CHR 页 4/5 映射 2KB CHR RAM，CPU 周期驱动的 IRQ。</summary>
public sealed class Waixing253(RomImage rom) : Mapper(rom, 0x800)
{
    private readonly int[] _chrLow = new int[8];
    private readonly int[] _chrHigh = new int[8];
    private bool _forceChrRom;
    private int _irqReload;
    private int _irqCounter;
    private bool _irqEnabled;
    private int _irqScaler;

    public override void Reset()
    {
        Array.Clear(_chrLow);
        Array.Clear(_chrHigh);
        _forceChrRom = false;
        _irqReload = _irqCounter = _irqScaler = 0;
        _irqEnabled = false;
        SetPrg8K(0, 0);
        SetPrg8K(1, 0);
        SetPrg8K(2, -2);
        SetPrg8K(3, -1);
        UpdateChr();
    }

    private void UpdateChr()
    {
        for (var i = 0; i < 8; i++)
        {
            var page = _chrLow[i] | (_chrHigh[i] << 8);
            if (_chrLow[i] is 4 or 5 && !_forceChrRom) SetChrRam1K(i, page & 1);
            else SetChr1K(i, page);
        }
    }

    public override void CpuClock()
    {
        base.CpuClock();
        if (!_irqEnabled || ++_irqScaler < 114) return;
        _irqScaler = 0;
        _irqCounter = (_irqCounter + 1) & 0xFF;
        if (_irqCounter == 0)
        {
            _irqCounter = _irqReload;
            IrqAsserted = true;
        }
    }

    protected override void WriteRegister(ushort address, byte value)
    {
        if (address is >= 0xB000 and <= 0xE00C)
        {
            var slot = ((((address & 0x08) | (address >> 8)) >> 3) + 2) & 7;
            var shift = address & 4;
            var low = ((_chrLow[slot] & (0xF0 >> shift)) | (value << shift)) & 0xFF;
            _chrLow[slot] = low;
            if (slot == 0)
            {
                if (low == 0xC8) _forceChrRom = false;
                else if (low == 0x88) _forceChrRom = true;
            }

            if (shift != 0) _chrHigh[slot] = value >> 4;
            UpdateChr();
            return;
        }

        switch (address)
        {
            case 0x8010: SetPrg8K(0, value); break;
            case 0xA010: SetPrg8K(1, value); break;
            case 0x9400:
                SetMirroring((value & 3) switch
                {
                    0 => Mirroring.Vertical,
                    1 => Mirroring.Horizontal,
                    2 => Mirroring.SingleScreenLow,
                    _ => Mirroring.SingleScreenHigh
                });
                break;
            case 0xF000:
                _irqReload = (_irqReload & 0xF0) | (value & 0x0F);
                IrqAsserted = false;
                break;
            case 0xF004:
                _irqReload = (_irqReload & 0x0F) | ((value << 4) & 0xF0);
                IrqAsserted = false;
                break;
            case 0xF008:
                _irqCounter = _irqReload;
                _irqEnabled = (value & 2) != 0;
                _irqScaler = 0;
                IrqAsserted = false;
                break;
        }
    }
}

/// <summary>
/// Mapper 176（外星 FK23C / BMC）：MMC3 超集，$5xx0-$5xx3 外层寄存器提供 PRG/CHR 基址、
/// 16/32KB PRG 模式、CNROM 模式、扩展 MMC3 寄存器与 WRAM 分页。大量中文 RPG 与合卡使用。
/// </summary>
public sealed class Fk23C(RomImage rom) : Mapper(rom, rom.HasChrRam ? 0 : Math.Max(rom.ChrRamSize, 0x2000))
{
    private readonly int[] _regs = new int[12];
    private int _prgMode;
    private int _outerChrSize;
    private bool _selectChrRam;
    private bool _mmc3ChrMode;
    private bool _cnromChrMode;
    private int _prgBase;
    private int _chrBase;
    private bool _extMmc3;
    private int _wramBank;
    private bool _ramInFirstChrBank;
    private bool _allowSingleScreen;
    private bool _fkRegsEnabled;
    private bool _wramConfig;
    private bool _wramEnabled;
    private bool _wramProtected;
    private bool _invertPrg;
    private bool _invertChr;
    private int _current;
    private int _mirroring;
    private int _cnromReg;
    private int _irqLatch;
    private int _irqCounter;
    private bool _irqReload;
    private bool _irqEnabled;
    private int _irqDelay;
    private A12Watcher _a12;

    protected override bool PrgRamEnabled => _wramConfig || _wramEnabled;

    protected override bool PrgRamWritable => _wramConfig || !_wramProtected;

    public override void Reset()
    {
        _prgMode = _outerChrSize = 0;
        _selectChrRam = false;
        _mmc3ChrMode = true;
        _prgBase = Prg.Length == 1024 * 1024 && ChrRomSize == Prg.Length ? 0x20 : 0;
        _chrBase = 0;
        _extMmc3 = _cnromChrMode = false;
        _wramBank = 0;
        _ramInFirstChrBank = _allowSingleScreen = _wramConfig = _fkRegsEnabled = false;
        _wramEnabled = _wramProtected = false;
        _current = _cnromReg = 0;
        int[] init = [0, 2, 4, 5, 6, 7, 0, 1, 0xFE, 0xFF, 0xFF, 0xFF];
        init.CopyTo(_regs, 0);
        _invertPrg = _invertChr = false;
        _mirroring = 0;
        _irqCounter = _irqLatch = _irqDelay = 0;
        _irqEnabled = _irqReload = false;
        Update();
    }

    private void SelectChr(int slot, int page)
    {
        if (Rom.HasChrRam) SetChr1K(slot, page);
        else if ((_selectChrRam && Chr.Length > ChrRomSize) || (_wramConfig && _ramInFirstChrBank && page <= 7))
            SetChrRam1K(slot, page);
        else SetChr1K(slot, page);
    }

    private void Update()
    {
        SetMirroring((_mirroring & (_allowSingleScreen ? 3 : 1)) switch
        {
            0 => Mirroring.Vertical,
            1 => Mirroring.Horizontal,
            2 => Mirroring.SingleScreenLow,
            _ => Mirroring.SingleScreenHigh
        });
        UpdatePrg();
        UpdateChr();
        var ramPages = Math.Max(1, PrgRam.Length / 0x2000);
        PrgRamOffset = (_wramConfig ? _wramBank % ramPages : 0) * 0x2000;
    }

    private void UpdatePrg()
    {
        var swap = _invertPrg ? 2 : 0;
        switch (_prgMode)
        {
            case 0:
            case 1:
            case 2:
                if (_extMmc3)
                {
                    var outer = _prgBase << 1;
                    SetPrg8K(0 ^ swap, _regs[6] | outer);
                    SetPrg8K(1, _regs[7] | outer);
                    SetPrg8K(2 ^ swap, _regs[8] | outer);
                    SetPrg8K(3, _regs[9] | outer);
                }
                else
                {
                    var mask = 0x3F >> _prgMode;
                    var outer = (_prgBase << 1) & ~mask;
                    SetPrg8K(0 ^ swap, (_regs[6] & mask) | outer);
                    SetPrg8K(1, (_regs[7] & mask) | outer);
                    SetPrg8K(2 ^ swap, (0xFE & mask) | outer);
                    SetPrg8K(3, (0xFF & mask) | outer);
                }

                break;
            case 3:
                SetPrg16K(0, _prgBase);
                SetPrg16K(1, _prgBase);
                break;
            case 4:
                SetPrg32K((_prgBase & 0xFFE) >> 1);
                break;
        }
    }

    private void UpdateChr()
    {
        if (!_mmc3ChrMode)
        {
            var mask = _cnromChrMode ? (_outerChrSize != 0 ? 1 : 3) : 0;
            for (var i = 0; i < 8; i++) SelectChr(i, (((_cnromReg & mask) | _chrBase) << 3) + i);
            return;
        }

        var swap = _invertChr ? 4 : 0;
        if (_extMmc3)
        {
            var outer = _chrBase << 3;
            SelectChr(0 ^ swap, _regs[0] | outer);
            SelectChr(1 ^ swap, _regs[10] | outer);
            SelectChr(2 ^ swap, _regs[1] | outer);
            SelectChr(3 ^ swap, _regs[11] | outer);
            for (var i = 0; i < 4; i++) SelectChr((4 + i) ^ swap, _regs[2 + i] | outer);
        }
        else
        {
            var mask = _outerChrSize != 0 ? 0x7F : 0xFF;
            var outer = (_chrBase << 3) & ~mask;
            SelectChr(0 ^ swap, (_regs[0] & 0xFE & mask) | outer);
            SelectChr(1 ^ swap, ((_regs[0] | 1) & mask) | outer);
            SelectChr(2 ^ swap, (_regs[1] & 0xFE & mask) | outer);
            SelectChr(3 ^ swap, ((_regs[1] | 1) & mask) | outer);
            for (var i = 0; i < 4; i++) SelectChr((4 + i) ^ swap, (_regs[2 + i] & mask) | outer);
        }
    }

    protected override bool WriteLowRegister(ushort address, byte value)
    {
        if (address >= 0x6000 || address < 0x5000) return false;
        if (!_fkRegsEnabled && _wramConfig) return true;
        if ((address & 0x5010) != 0x5010) return true;
        switch (address & 3)
        {
            case 0:
                _prgMode = value & 7;
                _outerChrSize = (value & 0x10) >> 4;
                _selectChrRam = (value & 0x20) != 0;
                _mmc3ChrMode = (value & 0x40) == 0;
                _prgBase = (_prgBase & ~0x180) | ((value & 0x80) << 1) | ((value & 0x08) << 4);
                break;
            case 1:
                _prgBase = (_prgBase & ~0x7F) | (value & 0x7F);
                break;
            case 2:
                _prgBase = (_prgBase & ~0x200) | ((value & 0x40) << 3);
                _chrBase = value;
                _cnromReg = 0;
                break;
            case 3:
                _extMmc3 = (value & 2) != 0;
                _cnromChrMode = (value & 0x44) != 0;
                break;
        }

        Update();
        return true;
    }

    protected override void WriteRegister(ushort address, byte value)
    {
        if (_cnromChrMode && (address <= 0x9FFF || address >= 0xC000))
        {
            _cnromReg = value & 3;
            Update();
        }

        switch (address & 0xE001)
        {
            case 0x8000:
                if (Prg.Length == 16384 * 1024 && value is 0x46 or 0x47) value ^= 1;
                _invertPrg = (value & 0x40) != 0;
                _invertChr = (value & 0x80) != 0;
                _current = value & 0x0F;
                Update();
                break;
            case 0x8001:
                var reg = _current & (_extMmc3 ? 0x0F : 0x07);
                if (reg < 12)
                {
                    _regs[reg] = value;
                    Update();
                }

                break;
            case 0xA000:
                _mirroring = value & 3;
                Update();
                break;
            case 0xA001:
                if ((value & 0x20) == 0) value &= 0xC0;
                _wramBank = value & 3;
                _ramInFirstChrBank = (value & 0x04) != 0;
                _allowSingleScreen = (value & 0x08) != 0;
                _wramConfig = (value & 0x20) != 0;
                _fkRegsEnabled = (value & 0x40) != 0;
                _wramProtected = (value & 0x40) != 0;
                _wramEnabled = (value & 0x80) != 0;
                Update();
                break;
            case 0xC000: _irqLatch = value; break;
            case 0xC001:
                _irqCounter = 0;
                _irqReload = true;
                break;
            case 0xE000:
                _irqEnabled = false;
                IrqAsserted = false;
                break;
            case 0xE001: _irqEnabled = true; break;
        }
    }

    public override void CpuClock()
    {
        base.CpuClock();
        if (_irqDelay > 0 && --_irqDelay == 0) IrqAsserted = true;
    }

    protected override void OnPpuAddress(ushort address)
    {
        if (!_a12.Rise(address, M2Cycles)) return;
        if (_irqCounter == 0 || _irqReload) _irqCounter = _irqLatch;
        else _irqCounter--;
        if (_irqCounter == 0 && _irqEnabled) _irqDelay = 2;
        _irqReload = false;
    }
}

/// <summary>
/// Mapper 90/209/211（晶太 JY Company ASIC）：多种 PRG/CHR 模式、$6000 PRG ROM 窗口、乘法器、
/// 多源 IRQ（CPU 周期 / A12 上升沿 / PPU 读），209 带 MMC2 式 CHR 锁存，209/211 可用 CHR ROM 作名称表。
/// </summary>
public sealed class JyCompany(RomImage rom) : Mapper(rom)
{
    private readonly int[] _prgRegs = new int[4];
    private readonly int[] _chrLow = new int[8];
    private readonly int[] _chrHigh = new int[8];
    private readonly int[] _chrLatch = new int[2];
    private readonly int[] _ntLow = new int[4];
    private readonly int[] _ntHigh = new int[4];
    private int _prgMode;
    private bool _prgAt6000;
    private int _chrMode;
    private bool _chrBlockMode;
    private int _chrBlock;
    private bool _mirrorChr;
    private int _mirroring;
    private bool _advancedNt;
    private bool _disableNtRam;
    private int _ntRamSelect;
    private bool _irqEnabled;
    private int _irqSource;
    private int _irqDirection;
    private bool _irqSmallPrescaler;
    private int _irqPrescaler;
    private int _irqCounter;
    private int _irqXor;
    private int _mul1;
    private int _mul2;
    private int _regRam;
    private ushort _lastPpuAddr;
    private int _prg6000Bank;

    private bool RomNametables => (_advancedNt || Rom.MapperId == 211) && Rom.MapperId != 90;

    public override void Reset()
    {
        _chrLatch[0] = 0;
        _chrLatch[1] = 4;
        Array.Clear(_prgRegs);
        Array.Clear(_chrLow);
        Array.Clear(_chrHigh);
        Array.Clear(_ntLow);
        Array.Clear(_ntHigh);
        _prgMode = _chrMode = _chrBlock = _mirroring = _ntRamSelect = 0;
        _prgAt6000 = _chrBlockMode = _mirrorChr = _advancedNt = _disableNtRam = false;
        _irqEnabled = _irqSmallPrescaler = false;
        _irqSource = _irqDirection = _irqPrescaler = _irqCounter = _irqXor = 0;
        _mul1 = _mul2 = _regRam = 0;
        Update();
    }

    private static int Invert(int v, bool invert) => !invert
        ? v
        : ((v & 1) << 6) | ((v & 2) << 4) | ((v & 4) << 2) | ((v & 0x10) >> 2) | ((v & 0x20) >> 4) | ((v & 0x40) >> 6);

    private void Update()
    {
        UpdatePrg();
        UpdateChr();
        UpdateMirroring();
    }

    private void UpdatePrg()
    {
        var inv = (_prgMode & 3) == 3;
        var r = new int[4];
        for (var i = 0; i < 4; i++) r[i] = Invert(_prgRegs[i], inv);
        var last = (_prgMode & 4) != 0;
        switch (_prgMode & 3)
        {
            case 0:
                var b32 = last ? r[3] : 0x3C;
                for (var i = 0; i < 4; i++) SetPrg8K(i, b32 * 4 + i);
                _prg6000Bank = r[3] * 4 + 3;
                break;
            case 1:
                SetPrg8K(0, r[1] << 1);
                SetPrg8K(1, (r[1] << 1) + 1);
                var b16 = last ? r[3] : 0x3E;
                SetPrg8K(2, b16 * 2);
                SetPrg8K(3, b16 * 2 + 1);
                _prg6000Bank = r[3] * 2 + 1;
                break;
            default:
                SetPrg8K(0, r[0]);
                SetPrg8K(1, r[1]);
                SetPrg8K(2, r[2]);
                SetPrg8K(3, last ? r[3] : 0x3F);
                _prg6000Bank = r[3];
                break;
        }
    }

    private int ChrReg(int index)
    {
        if (_chrMode >= 2 && _mirrorChr && index is 2 or 3) index -= 2;
        if (!_chrBlockMode) return _chrLow[index] | (_chrHigh[index] << 8);
        var (mask, shift) = _chrMode switch
        {
            0 => (0x1F, 5),
            1 => (0x3F, 6),
            2 => (0x7F, 7),
            _ => (0xFF, 8)
        };
        return (_chrLow[index] & mask) | (_chrBlock << shift);
    }

    private void UpdateChr()
    {
        switch (_chrMode)
        {
            case 0:
                SetChr8K(ChrReg(0));
                break;
            case 1:
                SetChr4K(0, ChrReg(_chrLatch[0]));
                SetChr4K(1, ChrReg(_chrLatch[1]));
                break;
            case 2:
                for (var i = 0; i < 4; i++)
                {
                    var b = ChrReg(i * 2) << 1;
                    SetChr1K(i * 2, b);
                    SetChr1K(i * 2 + 1, b + 1);
                }

                break;
            default:
                for (var i = 0; i < 8; i++) SetChr1K(i, ChrReg(i));
                break;
        }
    }

    private void UpdateMirroring()
    {
        if (RomNametables)
        {
            for (var i = 0; i < 4; i++) SetNametable(i, _ntLow[i] & 1);
            return;
        }

        SetMirroring(_mirroring switch
        {
            0 => Mirroring.Vertical,
            1 => Mirroring.Horizontal,
            2 => Mirroring.SingleScreenLow,
            _ => Mirroring.SingleScreenHigh
        });
    }

    public override bool TryCpuRead(ushort address, out byte value)
    {
        if (address is >= 0x5000 and < 0x6000)
        {
            switch (address & 0xF803)
            {
                case 0x5000: value = 0; return true;
                case 0x5800: value = (byte)(_mul1 * _mul2); return true;
                case 0x5801: value = (byte)((_mul1 * _mul2) >> 8); return true;
                case 0x5803: value = (byte)_regRam; return true;
            }

            value = 0;
            return false;
        }

        if (address is >= 0x6000 and < 0x8000)
        {
            if (_prgAt6000)
            {
                value = ReadPrg8K(_prg6000Bank, address);
                return true;
            }

            value = 0;
            return false;
        }

        return base.TryCpuRead(address, out value);
    }

    protected override bool WriteLowRegister(ushort address, byte value)
    {
        if (address is < 0x5000 or >= 0x6000) return false;
        switch (address & 0xF803)
        {
            case 0x5800: _mul1 = value; break;
            case 0x5801: _mul2 = value; break;
            case 0x5803: _regRam = value; break;
        }

        return true;
    }

    protected override void WriteRegister(ushort address, byte value)
    {
        switch (address & 0xF007)
        {
            case >= 0x8000 and <= 0x8007: _prgRegs[address & 3] = value & 0x7F; break;
            case >= 0x9000 and <= 0x9007: _chrLow[address & 7] = value; break;
            case >= 0xA000 and <= 0xA007: _chrHigh[address & 7] = value; break;
            case >= 0xB000 and <= 0xB003: _ntLow[address & 3] = value; break;
            case >= 0xB004 and <= 0xB007: _ntHigh[address & 3] = value; break;
            case 0xC000:
                _irqEnabled = (value & 1) != 0;
                if (!_irqEnabled) IrqAsserted = false;
                break;
            case 0xC001:
                _irqDirection = (value >> 6) & 3;
                _irqSmallPrescaler = (value & 4) != 0;
                _irqSource = value & 3;
                break;
            case 0xC002:
                _irqEnabled = false;
                IrqAsserted = false;
                break;
            case 0xC003: _irqEnabled = true; break;
            case 0xC004: _irqPrescaler = value ^ _irqXor; break;
            case 0xC005: _irqCounter = value ^ _irqXor; break;
            case 0xC006: _irqXor = value; break;
            case 0xD000:
                _prgMode = value & 7;
                _chrMode = (value >> 3) & 3;
                _advancedNt = (value & 0x20) != 0;
                _disableNtRam = (value & 0x40) != 0;
                _prgAt6000 = (value & 0x80) != 0;
                break;
            case 0xD001: _mirroring = value & 3; break;
            case 0xD002: _ntRamSelect = value & 0x80; break;
            case 0xD003:
                _mirrorChr = (value & 0x80) != 0;
                _chrBlockMode = (value & 0x20) == 0;
                _chrBlock = ((value & 0x18) >> 2) | (value & 1);
                break;
        }

        Update();
    }

    public override void CpuClock()
    {
        base.CpuClock();
        if (_irqSource == 0) TickIrq();
    }

    private void TickIrq()
    {
        var mask = _irqSmallPrescaler ? 0x07 : 0xFF;
        var prescaler = _irqPrescaler & mask;
        var clock = false;
        if (_irqDirection == 1)
        {
            prescaler++;
            clock = (prescaler & mask) == 0;
        }
        else if (_irqDirection == 2)
        {
            prescaler = (prescaler - 1) & 0xFF;
            clock = prescaler == 0;
        }

        _irqPrescaler = (_irqPrescaler & ~mask) | (prescaler & mask);
        if (!clock) return;
        if (_irqDirection == 1)
        {
            _irqCounter = (_irqCounter + 1) & 0xFF;
            if (_irqCounter == 0 && _irqEnabled) IrqAsserted = true;
        }
        else
        {
            _irqCounter = (_irqCounter - 1) & 0xFF;
            if (_irqCounter == 0xFF && _irqEnabled) IrqAsserted = true;
        }
    }

    protected override void OnPpuAddress(ushort address)
    {
        if (_irqSource == 1 && (address & 0x1000) != 0 && (_lastPpuAddr & 0x1000) == 0) TickIrq();
        _lastPpuAddr = address;
        if (Rom.MapperId == 209 && (address & 0x2FF8) is 0x0FD8 or 0x0FE8)
        {
            _chrLatch[address >> 12] = (address >> 4) & (((address >> 10) & 4) | 2);
            UpdateChr();
        }
    }

    private bool TryRomNametable(ushort address, out byte value)
    {
        value = 0;
        if (!RomNametables || address is < 0x2000 or >= 0x3F00) return false;
        var nt = (address >> 10) & 3;
        if (!_disableNtRam && (_ntLow[nt] & 0x80) == (_ntRamSelect & 0x80)) return false;
        var offset = (_ntLow[nt] | (_ntHigh[nt] << 8)) * 0x400 + (address & 0x3FF);
        value = offset < ChrRomSize ? Chr[offset] : (byte)0;
        return true;
    }

    public override byte PpuRead(ushort address)
    {
        address &= 0x3FFF;
        if (_irqSource == 2) TickIrq();
        if (TryRomNametable(address, out var value))
        {
            OnPpuAddress(address);
            return value;
        }

        return base.PpuRead(address);
    }

    public override byte PpuPeek(ushort address)
    {
        address &= 0x3FFF;
        return TryRomNametable(address, out var value) ? value : base.PpuPeek(address);
    }
}
