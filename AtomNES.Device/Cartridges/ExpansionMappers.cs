using AtomNES.Device.Audio;

namespace AtomNES.Device.Cartridges;

/// <summary>
/// Mapper 19 (Namco 163) / 210 (Namco 175/340)：8KB PRG ×3、1KB CHR ×8，CHR 页 ≥$E0 时映射 CIRAM；
/// $C000-$D800 选择名称表来源（CIRAM 或 CHR ROM）；15 位 CPU 周期 IRQ；163 带 8 路波表音频。
/// </summary>
public sealed class Namco163(RomImage rom) : Mapper(rom)
{
    private readonly Namco163Audio _audio = new();
    private readonly int[] _slots = new int[12];
    private readonly bool[] _slotCiram = new bool[12];
    private readonly bool _is163 = rom.MapperId == 19;
    private readonly bool _is340 = rom.MapperId == 210 && rom.SubMapper == 2;
    private int _writeProtect;
    private bool _lowNt;
    private bool _highNt;
    private int _irqCounter;

    public override float AudioOutput => _is163 ? _audio.Output : 0f;

    public override void Reset()
    {
        SetPrg8K(3, -1);
        for (var i = 8; i < 12; i++)
        {
            _slotCiram[i] = true;
            _slots[i] = i & 1;
        }
    }

    public override void CpuClock()
    {
        base.CpuClock();
        if ((_irqCounter & 0x8000) != 0 && (_irqCounter & 0x7FFF) != 0x7FFF)
        {
            _irqCounter++;
            if ((_irqCounter & 0x7FFF) == 0x7FFF) IrqAsserted = true;
        }

        if (_is163) _audio.Clock();
    }

    public override bool TryCpuRead(ushort address, out byte value)
    {
        switch (address & 0xF800)
        {
            case 0x4800 when _is163: value = _audio.Read(); return true;
            case 0x5000 when _is163: value = (byte)_irqCounter; return true;
            case 0x5800 when _is163: value = (byte)(_irqCounter >> 8); return true;
        }

        if (address is >= 0x6000 and < 0x8000 && _is340)
        {
            value = 0;
            return false;
        }

        return base.TryCpuRead(address, out value);
    }

    public override bool TryCpuWrite(ushort address, byte value)
    {
        if (address is >= 0x6000 and < 0x8000)
        {
            if (_is340) return false;
            var writable = _is163
                ? (_writeProtect & 0x40) != 0 && (_writeProtect & (1 << ((address >> 11) & 3))) == 0
                : (_writeProtect & 1) != 0;
            if (writable) PrgRam[address & 0x1FFF] = value;
            return true;
        }

        if (address is >= 0x4800 and < 0x6000)
        {
            if (!_is163) return false;
            switch (address & 0xF800)
            {
                case 0x4800: _audio.Write(address, value); break;
                case 0x5000:
                    _irqCounter = (_irqCounter & 0xFF00) | value;
                    IrqAsserted = false;
                    break;
                case 0x5800:
                    _irqCounter = (_irqCounter & 0x00FF) | (value << 8);
                    IrqAsserted = false;
                    break;
            }

            return true;
        }

        return base.TryCpuWrite(address, value);
    }

    protected override void WriteRegister(ushort address, byte value)
    {
        address &= 0xF800;
        switch (address)
        {
            case <= 0x9800:
                SelectChr((address - 0x8000) >> 11, value, !_lowNt);
                break;
            case <= 0xB800:
                SelectChr(((address - 0xA000) >> 11) + 4, value, !_highNt);
                break;
            case <= 0xD800:
                if (_is163)
                {
                    var slot = ((address - 0xC000) >> 11) + 8;
                    _slotCiram[slot] = value >= 0xE0;
                    _slots[slot] = value >= 0xE0 ? value & 1 : value;
                }
                else if (address == 0xC000)
                {
                    _writeProtect = value;
                }

                break;
            case 0xE000:
                SetPrg8K(0, value & 0x3F);
                if (_is340)
                {
                    SetMirroring(((value >> 6) & 3) switch
                    {
                        0 => Mirroring.SingleScreenLow,
                        1 => Mirroring.Vertical,
                        2 => Mirroring.Horizontal,
                        _ => Mirroring.SingleScreenHigh
                    });
                }
                else if (_is163)
                {
                    _audio.Write(address, value);
                }

                break;
            case 0xE800:
                SetPrg8K(1, value & 0x3F);
                _lowNt = (value & 0x40) != 0;
                _highNt = (value & 0x80) != 0;
                break;
            case 0xF000:
                SetPrg8K(2, value & 0x3F);
                break;
            case 0xF800:
                if (_is163)
                {
                    _writeProtect = value;
                    _audio.Write(address, value);
                }

                break;
        }
    }

    private void SelectChr(int slot, byte value, bool allowCiram)
    {
        _slotCiram[slot] = _is163 && allowCiram && value >= 0xE0;
        _slots[slot] = _slotCiram[slot] ? value & 1 : value;
    }

    private ref byte Locate(ushort address)
    {
        address &= 0x3FFF;
        var slot = address < 0x2000 ? address >> 10 : 8 + ((address >> 10) & 3);
        if (!_is163 && slot >= 8) return ref Ciram[NtOffset(address)];
        if (_slotCiram[slot]) return ref Ciram[(_slots[slot] & 1) * 0x400 + (address & 0x3FF)];
        var count = ChrBankCount1K;
        return ref Chr[(_slots[slot] % count) * 0x400 + (address & 0x3FF)];
    }

    public override byte PpuRead(ushort address) => Locate(address);

    public override byte PpuPeek(ushort address) => Locate(address);

    public override void PpuWrite(ushort address, byte value)
    {
        var a = (ushort)(address & 0x3FFF);
        var slot = a < 0x2000 ? a >> 10 : 8 + ((a >> 10) & 3);
        if ((!_is163 && slot >= 8) || _slotCiram[slot] || Rom.HasChrRam) Locate(address) = value;
    }
}

/// <summary>Mapper 85 (Konami VRC7)：8KB PRG ×3、1KB CHR ×8、VRC IRQ、6 声道 FM 音频。</summary>
public sealed class Vrc7(RomImage rom) : Mapper(rom)
{
    private readonly VrcIrq _irq = new();
    private readonly Vrc7Audio _audio = new();
    private int _control;

    public override bool IrqActive => _irq.Pending;

    public override float AudioOutput => _audio.Output;

    protected override bool PrgRamEnabled => (_control & 0x80) != 0;

    public override void Reset()
    {
        _audio.Reset();
        SetPrg8K(3, -1);
        UpdateControl();
    }

    public override void CpuClock()
    {
        base.CpuClock();
        _irq.Clock();
        _audio.Clock();
    }

    private void UpdateControl()
    {
        SetMirroring((_control & 3) switch
        {
            0 => Mirroring.Vertical,
            1 => Mirroring.Horizontal,
            2 => Mirroring.SingleScreenLow,
            _ => Mirroring.SingleScreenHigh
        });
        _audio.Muted = (_control & 0x40) != 0;
    }

    protected override void WriteRegister(ushort address, byte value)
    {
        int a = address;
        if ((a & 0x10) != 0 && (a & 0xF010) != 0x9010) a = (a | 0x08) & ~0x10;
        switch (a & 0xF038)
        {
            case 0x8000: SetPrg8K(0, value & 0x3F); break;
            case 0x8008: SetPrg8K(1, value & 0x3F); break;
            case 0x9000: SetPrg8K(2, value & 0x3F); break;
            case 0x9010 or 0x9030: _audio.Write(a, value); break;
            case 0xA000: SetChr1K(0, value); break;
            case 0xA008: SetChr1K(1, value); break;
            case 0xB000: SetChr1K(2, value); break;
            case 0xB008: SetChr1K(3, value); break;
            case 0xC000: SetChr1K(4, value); break;
            case 0xC008: SetChr1K(5, value); break;
            case 0xD000: SetChr1K(6, value); break;
            case 0xD008: SetChr1K(7, value); break;
            case 0xE000:
                _control = value;
                UpdateControl();
                break;
            case 0xE008: _irq.SetReload(value); break;
            case 0xF000: _irq.SetControl(value); break;
            case 0xF008: _irq.Acknowledge(); break;
        }
    }
}

/// <summary>
/// Mapper 5 (MMC5)：4 种 PRG/CHR 模式、最多 64KB PRG RAM、1KB 扩展 RAM（名称表 / 扩展属性 / 普通 RAM）、
/// 填充名称表、扫描线 IRQ、乘法器以及 2 方波 + PCM 扩展音频。8x16 精灵时背景与精灵使用不同 CHR 寄存器组。
/// 垂直分屏（仅《宇宙警备队 SDF》使用）未实现。
/// </summary>
public sealed class Mmc5(RomImage rom) : Mapper(rom)
{
    private readonly Mmc5Audio _audio = new();
    private readonly byte[] _ram = new byte[Math.Max(rom.PrgRamSize, 0x10000)];
    private readonly byte[] _exRam = new byte[0x400];
    private readonly int[] _prgBanks = new int[5];
    private readonly int[] _chrBanks = new int[12];
    private readonly int[] _chrA = new int[8];
    private readonly int[] _chrB = new int[8];
    private readonly int[] _cpuMap = new int[5];
    private readonly bool[] _cpuIsRam = new bool[5];
    private int _prgMode = 3;
    private int _chrMode;
    private int _ramProtect1;
    private int _ramProtect2;
    private int _exRamMode;
    private int _ntMapping;
    private byte _fillTile;
    private byte _fillAttr;
    private int _chrUpper;
    private bool _lastChrWriteB;
    private int _irqTarget;
    private bool _irqEnabled;
    private bool _irqPending;
    private int _scanline;
    private bool _inFrame;
    private int _idle;
    private int _mul1 = 0xFF;
    private int _mul2 = 0xFF;
    private int _exAttrBank;
    private byte _exAttrPalette;

    public override byte[]? BatteryRam => Rom.HasBattery ? _ram : null;

    public override float AudioOutput => _audio.Output;

    public override bool IrqActive => _irqEnabled && _irqPending;

    public override void Reset()
    {
        _prgBanks[4] = 0xFF;
        _prgMode = 3;
        UpdatePrg();
        UpdateChr();
        SetMirroring(Rom.Mirroring);
    }

    #region CPU

    private void UpdatePrg()
    {
        // _cpuMap[i]：$6000 + i*$2000 窗口的偏移；ROM 偏移以 8KB 页计算
        MapSlot(0, _prgBanks[0], true);
        switch (_prgMode)
        {
            case 0:
                var b32 = _prgBanks[4] & 0x7C;
                for (var i = 0; i < 4; i++) MapRom(1 + i, b32 + i);
                break;
            case 1:
                MapSlot(1, _prgBanks[2] & 0xFE, false);
                MapSlot(2, (_prgBanks[2] & 0xFE) + 1, false);
                MapRom(3, _prgBanks[4] & 0x7E);
                MapRom(4, (_prgBanks[4] & 0x7E) + 1);
                break;
            case 2:
                MapSlot(1, _prgBanks[2] & 0xFE, false);
                MapSlot(2, (_prgBanks[2] & 0xFE) + 1, false);
                MapSlot(3, _prgBanks[3], false);
                MapRom(4, _prgBanks[4] & 0x7F);
                break;
            default:
                MapSlot(1, _prgBanks[1], false);
                MapSlot(2, _prgBanks[2], false);
                MapSlot(3, _prgBanks[3], false);
                MapRom(4, _prgBanks[4] & 0x7F);
                break;
        }
    }

    private void MapSlot(int slot, int value, bool forceRam)
    {
        if (forceRam || (value & 0x80) == 0)
        {
            _cpuIsRam[slot] = true;
            _cpuMap[slot] = ((value & 0x0F) * 0x2000) % _ram.Length;
        }
        else
        {
            MapRom(slot, value & 0x7F);
        }
    }

    private void MapRom(int slot, int bank)
    {
        _cpuIsRam[slot] = false;
        _cpuMap[slot] = (bank % PrgBankCount8K) * 0x2000;
    }

    public override bool TryCpuRead(ushort address, out byte value)
    {
        if (address >= 0x6000)
        {
            var slot = (address - 0x6000) >> 13;
            value = _cpuIsRam[slot] ? _ram[_cpuMap[slot] + (address & 0x1FFF)] : Prg[_cpuMap[slot] + (address & 0x1FFF)];
            if (address is 0xFFFA or 0xFFFB)
            {
                _inFrame = false;
                _scanline = 0;
                _irqPending = false;
            }

            return true;
        }

        if (address is >= 0x5C00 and < 0x6000)
        {
            value = _exRamMode >= 2 ? _exRam[address & 0x3FF] : (byte)0;
            return _exRamMode >= 2;
        }

        switch (address)
        {
            case 0x5015: value = _audio.ReadStatus(); return true;
            case 0x5204:
                value = (byte)((_inFrame ? 0x40 : 0) | (_irqPending ? 0x80 : 0));
                _irqPending = false;
                return true;
            case 0x5205: value = (byte)(_mul1 * _mul2); return true;
            case 0x5206: value = (byte)((_mul1 * _mul2) >> 8); return true;
        }

        value = 0;
        return false;
    }

    public override bool TryCpuWrite(ushort address, byte value)
    {
        if (address >= 0x6000)
        {
            var slot = (address - 0x6000) >> 13;
            if (_cpuIsRam[slot] && _ramProtect1 == 2 && _ramProtect2 == 1)
                _ram[_cpuMap[slot] + (address & 0x1FFF)] = value;
            return true;
        }

        if (address is >= 0x5C00 and < 0x6000)
        {
            if (_exRamMode != 3) _exRam[address & 0x3FF] = _exRamMode <= 1 && !_inFrame ? (byte)0 : value;
            return true;
        }

        if (address is < 0x5000 or > 0x5206) return false;
        switch (address)
        {
            case <= 0x5015: _audio.Write(address, value); break;
            case 0x5100: _prgMode = value & 3; UpdatePrg(); break;
            case 0x5101: _chrMode = value & 3; UpdateChr(); break;
            case 0x5102: _ramProtect1 = value & 3; break;
            case 0x5103: _ramProtect2 = value & 3; break;
            case 0x5104: _exRamMode = value & 3; break;
            case 0x5105: _ntMapping = value; break;
            case 0x5106: _fillTile = value; break;
            case 0x5107: _fillAttr = (byte)((value & 3) * 0x55); break;
            case >= 0x5113 and <= 0x5117:
                _prgBanks[address - 0x5113] = value;
                UpdatePrg();
                break;
            case >= 0x5120 and <= 0x512B:
                _chrBanks[address - 0x5120] = value | (_chrUpper << 8);
                _lastChrWriteB = address >= 0x5128;
                UpdateChr();
                break;
            case 0x5130: _chrUpper = value & 3; break;
            case 0x5203: _irqTarget = value; break;
            case 0x5204: _irqEnabled = (value & 0x80) != 0; break;
            case 0x5205: _mul1 = value; break;
            case 0x5206: _mul2 = value; break;
        }

        return true;
    }

    public override void CpuClock()
    {
        base.CpuClock();
        _audio.Clock();
        if (_idle > 0 && --_idle == 0) _inFrame = false;
    }

    #endregion

    #region PPU

    private void UpdateChr()
    {
        var count = ChrBankCount1K;
        Fill(_chrA, false);
        Fill(_chrB, true);
        return;

        void Fill(int[] map, bool b)
        {
            for (var i = 0; i < 8; i++)
            {
                int bank;
                if (b)
                {
                    var j = i & 3;
                    bank = _chrMode switch
                    {
                        0 => _chrBanks[11] * 8 + i,
                        1 => _chrBanks[11] * 4 + j,
                        2 => _chrBanks[j < 2 ? 9 : 11] * 2 + (i & 1),
                        _ => _chrBanks[8 + j]
                    };
                }
                else
                {
                    bank = _chrMode switch
                    {
                        0 => _chrBanks[7] * 8 + i,
                        1 => _chrBanks[i < 4 ? 3 : 7] * 4 + (i & 3),
                        2 => _chrBanks[(i >> 1) * 2 + 1] * 2 + (i & 1),
                        _ => _chrBanks[i]
                    };
                }

                map[i] = (bank % count) * 0x400;
            }
        }
    }

    /// <summary>PPU 写 $2000/$2001 时由总线转发（MMC5 监听 8x16 精灵模式）。</summary>
    private bool LargeSprites => Ppu is { } p && (p.Ctrl & 0x20) != 0;

    private bool SpriteFetch => Ppu is { Dot: >= 257 and <= 320 };

    private byte ReadChr(ushort address)
    {
        var useA = !LargeSprites || SpriteFetch || (!_inFrame && !_lastChrWriteB);
        var map = useA ? _chrA : _chrB;
        return Chr[map[address >> 10] + (address & 0x3FF)];
    }

    private void DetectScanline(ushort address)
    {
        _idle = 3;
        if (Ppu is not { } p || p.Dot != 339 || address < 0x2000) return;
        if (!_inFrame)
        {
            _inFrame = true;
            _scanline = 0;
            _irqPending = false;
            return;
        }

        _scanline++;
        if (_scanline == _irqTarget && _irqTarget != 0) _irqPending = true;
        if (_scanline >= 240) _inFrame = false;
    }

    public override byte PpuRead(ushort address)
    {
        address &= 0x3FFF;
        DetectScanline(address);
        var exAttr = _exRamMode == 1 && _inFrame && !SpriteFetch;
        if (address < 0x2000)
        {
            if (exAttr) return Chr[((_exAttrBank * 0x1000) + (address & 0xFFF)) % Math.Max(Chr.Length, 1)];
            return ReadChr(address);
        }

        var offset = address & 0x3FF;
        if (exAttr)
        {
            if (offset < 0x3C0)
            {
                var ex = _exRam[offset];
                _exAttrBank = (ex & 0x3F) | (_chrUpper << 6);
                _exAttrPalette = (byte)(((ex >> 6) & 3) * 0x55);
            }
            else
            {
                return _exAttrPalette;
            }
        }

        return ReadNt(address);
    }

    private byte ReadNt(ushort address)
    {
        var offset = address & 0x3FF;
        return ((_ntMapping >> (((address >> 10) & 3) * 2)) & 3) switch
        {
            0 => Ciram[offset],
            1 => Ciram[0x400 + offset],
            2 => _exRamMode <= 1 ? _exRam[offset] : (byte)0,
            _ => offset < 0x3C0 ? _fillTile : _fillAttr
        };
    }

    public override byte PpuPeek(ushort address)
    {
        address &= 0x3FFF;
        return address < 0x2000 ? Chr[_chrA[address >> 10] + (address & 0x3FF)] : ReadNt(address);
    }

    public override void PpuWrite(ushort address, byte value)
    {
        address &= 0x3FFF;
        if (address < 0x2000)
        {
            if (Rom.HasChrRam) Chr[_chrA[address >> 10] + (address & 0x3FF)] = value;
            return;
        }

        var offset = address & 0x3FF;
        switch ((_ntMapping >> (((address >> 10) & 3) * 2)) & 3)
        {
            case 0: Ciram[offset] = value; break;
            case 1: Ciram[0x400 + offset] = value; break;
            case 2:
                if (_exRamMode <= 1) _exRam[offset] = value;
                break;
        }
    }

    #endregion
}
