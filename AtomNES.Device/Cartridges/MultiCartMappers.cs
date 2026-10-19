namespace AtomNES.Device.Cartridges;

// 离散逻辑板与多合一卡带：只做页切换，无 IRQ。

/// <summary>Mapper 28 (Action 53)：$5000 选择寄存器，$8000 写入数据，支持多种外层/内层 PRG 尺寸。</summary>
public sealed class Action53(RomImage rom) : Mapper(rom)
{
    private readonly int[] _regs = new int[4];
    private int _select;
    private int _mirrorBit;

    public override void Reset()
    {
        Array.Clear(_regs);
        _select = _mirrorBit = 0;
        _regs[3] = 0xFF;
        SetPrg16K(1, -1);
        Update();
    }

    protected override bool WriteLowRegister(ushort address, byte value)
    {
        if (address is < 0x5000 or > 0x5FFF) return false;
        _select = ((value & 0x80) >> 6) | (value & 1);
        return true;
    }

    protected override void WriteRegister(ushort address, byte value)
    {
        if (_select <= 1) _mirrorBit = (value >> 4) & 1;
        else if (_select == 2) _mirrorBit = value & 1;
        _regs[_select] = value;
        Update();
    }

    private void Update()
    {
        var mirroring = _regs[2] & 3;
        if ((mirroring & 2) == 0) mirroring = _mirrorBit;
        SetMirroring(mirroring switch
        {
            0 => Mirroring.SingleScreenLow,
            1 => Mirroring.SingleScreenHigh,
            2 => Mirroring.Vertical,
            _ => Mirroring.Horizontal
        });

        var gameSize = (_regs[2] >> 4) & 3;
        var prgSize = (_regs[2] >> 3) & 1;
        var slot = (_regs[2] >> 2) & 1;
        var prg = _regs[1] & 0x0F;
        var outer = _regs[3] << 1;
        int[] outerAnd = [0x1FE, 0x1FC, 0x1F8, 0x1F0];
        int[] innerAnd = [1, 3, 7, 0x0F];
        SetChr8K(_regs[0] & 3);
        if (prgSize != 0)
        {
            SetPrg16K(slot == 0 ? 1 : 0, (outer & outerAnd[gameSize]) | (prg & innerAnd[gameSize]));
            SetPrg16K(slot, (outer & 0x1FE) | slot);
        }
        else
        {
            prg <<= 1;
            SetPrg16K(0, (outer & outerAnd[gameSize]) | (prg & innerAnd[gameSize]));
            SetPrg16K(1, (outer & outerAnd[gameSize]) | ((prg | 1) & innerAnd[gameSize]));
        }
    }
}

/// <summary>Mapper 38 (PCI556)：$7000-$7FFF 写入选择 32KB PRG 与 8KB CHR。</summary>
public sealed class Mapper38(RomImage rom) : Mapper(rom)
{
    public override void Reset()
    {
        SetPrg32K(0);
        SetChr8K(0);
    }

    protected override bool WriteLowRegister(ushort address, byte value)
    {
        if (address < 0x7000) return false;
        SetPrg32K(value & 3);
        SetChr8K((value >> 2) & 3);
        return true;
    }
}

/// <summary>Mapper 41 (Caltron 6-in-1)：$6000-$67FF 地址线作为外层寄存器，$8000 写内层 CHR。</summary>
public sealed class Caltron41(RomImage rom) : Mapper(rom)
{
    private int _prg;
    private int _chr;

    public override void Reset()
    {
        _prg = _chr = 0;
        WriteLowRegister(0x6000, 0);
    }

    protected override bool WriteLowRegister(ushort address, byte value)
    {
        if (address is < 0x6000 or > 0x67FF) return false;
        _prg = address & 7;
        _chr = (_chr & 3) | ((address >> 1) & 0x0C);
        SetPrg32K(_prg);
        SetChr8K(_chr);
        SetMirroring((address & 0x20) != 0 ? Mirroring.Horizontal : Mirroring.Vertical);
        return true;
    }

    protected override void WriteRegister(ushort address, byte value)
    {
        if (_prg < 4) return;
        _chr = (_chr & 0x0C) | (value & 3);
        SetChr8K(_chr);
    }
}

/// <summary>Mapper 46 (Rumble Station)：$6000 外层，$8000 内层。</summary>
public sealed class ColorDreams46(RomImage rom) : Mapper(rom)
{
    private int _outer;
    private int _inner;

    public override void Reset()
    {
        _outer = _inner = 0;
        Update();
    }

    protected override bool WriteLowRegister(ushort address, byte value)
    {
        if (address < 0x6000) return false;
        _outer = value;
        Update();
        return true;
    }

    protected override void WriteRegister(ushort address, byte value)
    {
        _inner = value;
        Update();
    }

    private void Update()
    {
        SetPrg32K(((_outer & 0x0F) << 1) | (_inner & 1));
        SetChr8K(((_outer & 0xF0) >> 1) | ((_inner & 0x70) >> 4));
    }
}

/// <summary>Mapper 70 / 152 (Bandai 74161/7432)：PRG 16KB + CHR 8KB，152 带单屏镜像控制。</summary>
public sealed class Bandai74161(RomImage rom, bool mirroringControl) : Mapper(rom)
{
    private bool _mirroringControl = mirroringControl;

    public override void Reset()
    {
        SetPrg16K(0, 0);
        SetPrg16K(1, -1);
        SetChr8K(0);
        if (!_mirroringControl) SetMirroring(Rom.Mirroring);
    }

    protected override void WriteRegister(ushort address, byte value)
    {
        TryCpuRead(address, out var bus);
        value &= bus;
        if ((value & 0x80) != 0) _mirroringControl = true;
        if (_mirroringControl)
        {
            SetMirroring((value & 0x80) != 0 ? Mirroring.SingleScreenHigh : Mirroring.SingleScreenLow);
        }

        SetPrg16K(0, (value >> 4) & 7);
        SetChr8K(value & 0x0F);
    }
}

/// <summary>Mapper 71 (Camerica BF909x)：$C000 选 16KB PRG；BF9097 在 $9000 控制单屏镜像。</summary>
public sealed class Camerica71(RomImage rom) : Mapper(rom)
{
    private bool _bf9097 = rom.SubMapper == 1;

    public override void Reset()
    {
        SetPrg16K(0, 0);
        SetPrg16K(1, -1);
        SetChr8K(0);
    }

    protected override void WriteRegister(ushort address, byte value)
    {
        if ((address & 0xF000) == 0x9000) _bf9097 = true;
        if (address >= 0xC000 || !_bf9097) SetPrg16K(0, value);
        else SetMirroring((value & 0x10) != 0 ? Mirroring.SingleScreenHigh : Mirroring.SingleScreenLow);
    }
}

/// <summary>Mapper 232 (Camerica BF9096 / Quattro)：外层 64KB 块 + 内层 16KB 页。</summary>
public sealed class Camerica232(RomImage rom) : Mapper(rom)
{
    private int _block;
    private int _page;

    public override void Reset()
    {
        _block = _page = 0;
        Update();
        SetChr8K(0);
    }

    protected override void WriteRegister(ushort address, byte value)
    {
        if (address >= 0xC000) _page = value & 3;
        else _block = Rom.SubMapper == 1 ? ((value >> 4) & 1) | ((value >> 2) & 2) : (value >> 3) & 3;
        Update();
    }

    private void Update()
    {
        SetPrg16K(0, (_block << 2) | _page);
        SetPrg16K(1, (_block << 2) | 3);
    }
}

/// <summary>Mapper 72 / 92 (Jaleco JF-17/JF-19)：上升沿锁存 PRG/CHR 页。</summary>
public sealed class Jaleco72(RomImage rom, bool jf19) : Mapper(rom)
{
    private bool _prgFlag;
    private bool _chrFlag;

    public override void Reset()
    {
        _prgFlag = _chrFlag = false;
        SetPrg16K(0, 0);
        SetPrg16K(1, -1);
        SetChr8K(0);
    }

    protected override void WriteRegister(ushort address, byte value)
    {
        TryCpuRead(address, out var bus);
        value &= bus;
        if (!_prgFlag && (value & 0x80) != 0)
        {
            if (jf19) SetPrg16K(1, value & 0x0F);
            else SetPrg16K(0, value & 7);
        }

        if (!_chrFlag && (value & 0x40) != 0) SetChr8K(value & 0x0F);
        _prgFlag = (value & 0x80) != 0;
        _chrFlag = (value & 0x40) != 0;
    }
}

/// <summary>Mapper 78 (Jaleco JF-16 / Irem Holy Diver)：PRG 16KB + CHR 8KB + 镜像控制。</summary>
public sealed class Jaleco78(RomImage rom) : Mapper(rom)
{
    public override void Reset()
    {
        SetPrg16K(0, 0);
        SetPrg16K(1, -1);
        SetChr8K(0);
    }

    protected override void WriteRegister(ushort address, byte value)
    {
        TryCpuRead(address, out var bus);
        value &= bus;
        SetPrg16K(0, value & 7);
        SetChr8K((value >> 4) & 0x0F);
        var bit = (value & 8) != 0;
        SetMirroring(Rom.SubMapper == 3
            ? bit ? Mirroring.Vertical : Mirroring.Horizontal
            : bit ? Mirroring.SingleScreenHigh : Mirroring.SingleScreenLow);
    }
}

/// <summary>Mapper 79 / 113 / 146 (AVE NINA-03/06)：$4100-$5FFF 选 32KB PRG 与 8KB CHR，113 为多合一扩展。</summary>
public sealed class Nina06(RomImage rom, bool multicart) : Mapper(rom)
{
    public override void Reset()
    {
        SetPrg32K(0);
        SetChr8K(0);
    }

    protected override bool WriteLowRegister(ushort address, byte value)
    {
        if (address >= 0x6000) return false;
        if ((address & 0xE100) != 0x4100) return true;
        if (multicart)
        {
            SetPrg32K((value >> 3) & 7);
            SetChr8K((value & 7) | ((value >> 3) & 8));
            SetMirroring((value & 0x80) != 0 ? Mirroring.Vertical : Mirroring.Horizontal);
        }
        else
        {
            SetPrg32K((value >> 3) & 1);
            SetChr8K(value & 7);
        }

        return true;
    }
}

/// <summary>Mapper 86 (Jaleco JF-13)：$6000 写 PRG/CHR。</summary>
public sealed class Jaleco86(RomImage rom) : Mapper(rom)
{
    public override void Reset()
    {
        SetPrg32K(0);
        SetChr8K(0);
    }

    protected override bool WriteLowRegister(ushort address, byte value)
    {
        if (address < 0x6000) return false;
        if ((address & 0x7000) == 0x6000)
        {
            SetPrg32K((value >> 4) & 3);
            SetChr8K((value & 3) | ((value >> 4) & 4));
        }

        return true;
    }
}

/// <summary>Mapper 87 (Jaleco/Konami)：$6000-$7FFF 写 CHR，低两位交换。</summary>
public sealed class Mapper87(RomImage rom) : Mapper(rom)
{
    public override void Reset()
    {
        SetPrg32K(0);
        SetChr8K(0);
    }

    protected override bool WriteLowRegister(ushort address, byte value)
    {
        if (address < 0x6000) return false;
        SetChr8K(((value & 1) << 1) | ((value >> 1) & 1));
        return true;
    }
}

/// <summary>Mapper 140 (Jaleco JF-11/14)：$6000-$7FFF 写 32KB PRG 与 8KB CHR。</summary>
public sealed class Jaleco140(RomImage rom) : Mapper(rom)
{
    public override void Reset()
    {
        SetPrg32K(0);
        SetChr8K(0);
    }

    protected override bool WriteLowRegister(ushort address, byte value)
    {
        if (address < 0x6000) return false;
        SetPrg32K((value >> 4) & 3);
        SetChr8K(value & 0x0F);
        return true;
    }
}

/// <summary>Mapper 89 (Sunsoft-2 天狗)：PRG 16KB + CHR 8KB + 单屏镜像。</summary>
public sealed class Sunsoft89(RomImage rom) : Mapper(rom)
{
    public override void Reset()
    {
        SetPrg16K(0, 0);
        SetPrg16K(1, -1);
        SetChr8K(0);
    }

    protected override void WriteRegister(ushort address, byte value)
    {
        SetPrg16K(0, (value >> 4) & 7);
        SetChr8K((value & 7) | ((value & 0x80) >> 4));
        SetMirroring((value & 8) != 0 ? Mirroring.SingleScreenHigh : Mirroring.SingleScreenLow);
    }
}

/// <summary>Mapper 93 (Sunsoft-2 / Fantasy Zone)：仅 PRG 16KB 切换。</summary>
public sealed class Sunsoft93(RomImage rom) : Mapper(rom)
{
    public override void Reset()
    {
        SetPrg16K(0, 0);
        SetPrg16K(1, -1);
        SetChr8K(0);
    }

    protected override void WriteRegister(ushort address, byte value) => SetPrg16K(0, (value >> 4) & 7);
}

/// <summary>Mapper 94 (UN1ROM / Senjou no Ookami)：PRG 页号位于 bit2-4。</summary>
public sealed class Unrom94(RomImage rom) : Mapper(rom)
{
    public override void Reset()
    {
        SetPrg16K(0, 0);
        SetPrg16K(1, -1);
        SetChr8K(0);
    }

    protected override void WriteRegister(ushort address, byte value) => SetPrg16K(0, (value >> 2) & 7);
}

/// <summary>Mapper 97 (Irem TAM-S1)：$8000 固定末页，$C000 可切换。</summary>
public sealed class Irem97(RomImage rom) : Mapper(rom)
{
    public override void Reset()
    {
        SetPrg16K(0, -1);
        SetPrg16K(1, -1);
        SetChr8K(0);
    }

    protected override void WriteRegister(ushort address, byte value)
    {
        SetPrg16K(1, value & 0x0F);
        SetMirroring((value >> 6) switch
        {
            0 => Mirroring.SingleScreenLow,
            1 => Mirroring.Horizontal,
            2 => Mirroring.Vertical,
            _ => Mirroring.SingleScreenHigh
        });
    }
}

/// <summary>Mapper 107 (Magic Dragon)：同一写入选择 PRG 与 CHR。</summary>
public sealed class Mapper107(RomImage rom) : Mapper(rom)
{
    public override void Reset()
    {
        SetPrg32K(0);
        SetChr8K(0);
    }

    protected override void WriteRegister(ushort address, byte value)
    {
        SetPrg32K(value >> 1);
        SetChr8K(value);
    }
}

/// <summary>Mapper 133 (Sachen 3009)：$4100 写 PRG/CHR。</summary>
public sealed class Sachen133(RomImage rom) : Mapper(rom)
{
    public override void Reset()
    {
        SetPrg32K(0);
        SetChr8K(0);
    }

    protected override bool WriteLowRegister(ushort address, byte value)
    {
        if ((address & 0x6100) != 0x4100) return address < 0x6000;
        SetPrg32K((value >> 2) & 1);
        SetChr8K(value & 3);
        return true;
    }
}

/// <summary>Mapper 150 / 243 (Sachen 74LS374N)：$4100 索引 + $4101 数据的寄存器组。</summary>
public sealed class Sachen74LS374N(RomImage rom) : Mapper(rom)
{
    private readonly int[] _regs = new int[8];
    private int _current;

    public override void Reset()
    {
        Array.Clear(_regs);
        _current = 0;
        Update();
    }

    public override bool TryCpuRead(ushort address, out byte value)
    {
        if (address is >= 0x4100 and < 0x6000 && (address & 0xC101) == 0x4101)
        {
            value = (byte)(_regs[_current] & 7);
            return true;
        }

        return base.TryCpuRead(address, out value);
    }

    protected override bool WriteLowRegister(ushort address, byte value)
    {
        if (address < 0x4100) return false;
        switch (address & 0xC101)
        {
            case 0x4100: _current = value & 7; break;
            case 0x4101:
                _regs[_current] = value & 7;
                Update();
                break;
        }

        return address < 0x6000;
    }

    private void Update()
    {
        var chr = Rom.MapperId == 150
            ? ((_regs[4] & 1) << 2) | (_regs[6] & 3)
            : (_regs[2] & 1) | ((_regs[4] & 1) << 1) | ((_regs[6] & 3) << 2);
        SetChr8K(chr);
        SetPrg32K(_regs[5] & 3);
        switch ((_regs[7] >> 1) & 3)
        {
            case 0:
                SetNametable(0, 0);
                SetNametable(1, 0);
                SetNametable(2, 0);
                SetNametable(3, 1);
                break;
            case 1: SetMirroring(Mirroring.Horizontal); break;
            case 2: SetMirroring(Mirroring.Vertical); break;
            default: SetMirroring(Mirroring.SingleScreenLow); break;
        }
    }
}

/// <summary>Mapper 240 (C&amp;E)：$4020-$5FFF 写 32KB PRG 与 8KB CHR，带 PRG RAM。</summary>
public sealed class Mapper240(RomImage rom) : Mapper(rom)
{
    public override void Reset()
    {
        SetPrg32K(0);
        SetChr8K(0);
    }

    protected override bool WriteLowRegister(ushort address, byte value)
    {
        if (address >= 0x6000) return false;
        SetPrg32K((value >> 4) & 0x0F);
        SetChr8K(value & 0x0F);
        return true;
    }
}

/// <summary>Mapper 242 (Waixing 32KB)：地址线选择 PRG 与镜像。</summary>
public sealed class Mapper242(RomImage rom) : Mapper(rom)
{
    public override void Reset()
    {
        SetPrg32K(0);
        SetChr8K(0);
        SetMirroring(Mirroring.Vertical);
    }

    protected override void WriteRegister(ushort address, byte value)
    {
        SetMirroring((address & 2) != 0 ? Mirroring.Horizontal : Mirroring.Vertical);
        SetPrg32K((address >> 3) & 0x0F);
    }
}

/// <summary>Mapper 246 (G0151-1 / 封神榜)：$6000-$6007 写 8KB PRG 与 2KB CHR。</summary>
public sealed class Mapper246(RomImage rom) : Mapper(rom)
{
    public override void Reset()
    {
        SetPrg8K(3, 0xFF);
    }

    protected override bool WriteLowRegister(ushort address, byte value)
    {
        if (address is < 0x6000 or > 0x67FF) return false;
        var reg = address & 7;
        if (reg <= 3) SetPrg8K(reg, value);
        else
        {
            SetChr1K((reg & 3) * 2, value * 2);
            SetChr1K((reg & 3) * 2 + 1, value * 2 + 1);
        }

        return true;
    }
}

/// <summary>地址锁存型多合一卡带的公共基类（写入地址本身携带页号）。</summary>
public abstract class AddressLatchMultiCart(RomImage rom) : Mapper(rom)
{
    public override void Reset()
    {
        SetPrg16K(0, 0);
        SetPrg16K(1, 1);
        SetChr8K(0);
    }

    protected void SetPrg(int page, bool nrom128)
    {
        if (nrom128)
        {
            SetPrg16K(0, page);
            SetPrg16K(1, page);
        }
        else
        {
            SetPrg16K(0, page & ~1);
            SetPrg16K(1, (page & ~1) + 1);
        }
    }

    protected void SetMirror(bool horizontal) =>
        SetMirroring(horizontal ? Mirroring.Horizontal : Mirroring.Vertical);
}

/// <summary>Mapper 58 (多合一)。</summary>
public sealed class Mapper58(RomImage rom) : AddressLatchMultiCart(rom)
{
    protected override void WriteRegister(ushort address, byte value)
    {
        SetPrg(address & 7, (address & 0x40) != 0);
        SetChr8K((address >> 3) & 7);
        SetMirror((address & 0x80) != 0);
    }
}

/// <summary>Mapper 61 (多合一)。</summary>
public sealed class Mapper61(RomImage rom) : AddressLatchMultiCart(rom)
{
    protected override void WriteRegister(ushort address, byte value)
    {
        SetPrg(((address & 0x0F) << 1) | ((address >> 5) & 1), (address & 0x10) != 0);
        SetMirror((address & 0x80) != 0);
    }
}

/// <summary>Mapper 62 (Super 700-in-1)。</summary>
public sealed class Mapper62(RomImage rom) : AddressLatchMultiCart(rom)
{
    protected override void WriteRegister(ushort address, byte value)
    {
        SetPrg(((address & 0x3F00) >> 8) | (address & 0x40), (address & 0x20) != 0);
        SetChr8K(((address & 0x1F) << 2) | (value & 3));
        SetMirror((address & 0x80) != 0);
    }
}

/// <summary>Mapper 200 (多合一)。</summary>
public sealed class Mapper200(RomImage rom) : AddressLatchMultiCart(rom)
{
    public override void Reset()
    {
        base.Reset();
        SetPrg(0, true);
    }

    protected override void WriteRegister(ushort address, byte value)
    {
        SetPrg(address & 7, true);
        SetChr8K(address & 7);
        SetMirroring((address & 8) != 0 ? Mirroring.Vertical : Mirroring.Horizontal);
    }
}

/// <summary>Mapper 202 (150-in-1)。</summary>
public sealed class Mapper202(RomImage rom) : AddressLatchMultiCart(rom)
{
    public override void Reset()
    {
        base.Reset();
        SetPrg(0, true);
    }

    protected override void WriteRegister(ushort address, byte value)
    {
        var bank = (address >> 1) & 7;
        SetChr8K(bank);
        SetPrg16K(0, bank);
        SetPrg16K(1, (address & 9) == 9 ? bank + 1 : bank);
        SetMirror((address & 1) != 0);
    }
}

/// <summary>Mapper 203 (35-in-1)。</summary>
public sealed class Mapper203(RomImage rom) : AddressLatchMultiCart(rom)
{
    public override void Reset()
    {
        base.Reset();
        SetPrg(0, true);
    }

    protected override void WriteRegister(ushort address, byte value)
    {
        SetPrg(value >> 2, true);
        SetChr8K(value & 3);
    }
}

/// <summary>Mapper 212 (Super HiK 300-in-1)：$6000 读取 bit7 置位作为保护。</summary>
public sealed class Mapper212(RomImage rom) : AddressLatchMultiCart(rom)
{
    public override void Reset() => WriteRegister(0x8000, 0);

    public override bool TryCpuRead(ushort address, out byte value)
    {
        if (address is >= 0x6000 and < 0x8000)
        {
            var hit = base.TryCpuRead(address, out value);
            if ((address & 0xE010) == 0x6000) value |= 0x80;
            return hit || (address & 0xE010) == 0x6000;
        }

        return base.TryCpuRead(address, out value);
    }

    protected override void WriteRegister(ushort address, byte value)
    {
        if ((address & 0x4000) != 0) SetPrg32K((address & 6) >> 1);
        else SetPrg(address & 7, true);
        SetChr8K(address & 7);
        SetMirror((address & 8) != 0);
    }
}

/// <summary>Mapper 225 (64-in-1 等)。</summary>
public sealed class Mapper225(RomImage rom) : AddressLatchMultiCart(rom)
{
    protected override void WriteRegister(ushort address, byte value)
    {
        var high = (address >> 8) & 0x40;
        SetPrg(((address >> 6) & 0x3F) | high, (address & 0x1000) != 0);
        SetChr8K((address & 0x3F) | high);
        SetMirror((address & 0x2000) != 0);
    }
}

/// <summary>Mapper 226 (76-in-1 等)。</summary>
public sealed class Mapper226(RomImage rom) : AddressLatchMultiCart(rom)
{
    private int _r0;
    private int _r1;

    public override void Reset()
    {
        _r0 = _r1 = 0;
        base.Reset();
    }

    protected override void WriteRegister(ushort address, byte value)
    {
        if ((address & 1) == 0) _r0 = value;
        else _r1 = value;
        SetPrg((_r0 & 0x1F) | ((_r0 & 0x80) >> 2) | ((_r1 & 1) << 6), (_r0 & 0x20) != 0);
        SetMirroring((_r0 & 0x40) != 0 ? Mirroring.Vertical : Mirroring.Horizontal);
    }
}

/// <summary>Mapper 229 (31-in-1)。</summary>
public sealed class Mapper229(RomImage rom) : AddressLatchMultiCart(rom)
{
    public override void Reset() => WriteRegister(0x8000, 0);

    protected override void WriteRegister(ushort address, byte value)
    {
        SetChr8K(address & 0xFF);
        if ((address & 0x1E) == 0) SetPrg32K(0);
        else SetPrg(address & 0x1F, true);
        SetMirror((address & 0x20) != 0);
    }
}

/// <summary>Mapper 231 (20-in-1)。</summary>
public sealed class Mapper231(RomImage rom) : AddressLatchMultiCart(rom)
{
    public override void Reset()
    {
        base.Reset();
        SetPrg(0, true);
    }

    protected override void WriteRegister(ushort address, byte value)
    {
        var bank = ((address >> 5) & 1) | (address & 0x1E);
        SetPrg16K(0, bank & 0x1E);
        SetPrg16K(1, bank);
        SetMirror((address & 0x80) != 0);
    }
}
