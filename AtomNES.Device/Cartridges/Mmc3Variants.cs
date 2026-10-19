namespace AtomNES.Device.Cartridges;

/// <summary>Mapper 206 (Namco 108 / DxROM)：MMC3 前身，无 IRQ、无镜像控制、PRG/CHR 模式固定。</summary>
public class Namco108(RomImage rom) : Mmc3(rom)
{
    protected override void WriteRegister(ushort address, byte value)
    {
        if (address >= 0xA000) return;
        if ((address & 1) == 0) value &= 0x3F;
        base.WriteRegister(address, value);
    }
}

/// <summary>Mapper 88：Namco 108，CHR 左半区使用低 64KB，右半区使用高 64KB。</summary>
public class Namco108Chr64(RomImage rom) : Namco108(rom)
{
    protected override void UpdateChrBanks()
    {
        Registers[0] &= 0x3F;
        Registers[1] &= 0x3F;
        for (var i = 2; i < 6; i++) Registers[i] |= 0x40;
        base.UpdateChrBanks();
    }
}

/// <summary>Mapper 154：Mapper 88 + 单屏镜像位。</summary>
public sealed class Namco108Mirror(RomImage rom) : Namco108Chr64(rom)
{
    protected override void WriteRegister(ushort address, byte value)
    {
        SetMirroring((value & 0x40) != 0 ? Mirroring.SingleScreenHigh : Mirroring.SingleScreenLow);
        base.WriteRegister(address, value);
    }
}

/// <summary>Mapper 95 (Dragon Buster)：CHR 寄存器 bit5 控制名称表。</summary>
public sealed class Namco108Nt(RomImage rom) : Namco108(rom)
{
    protected override void WriteRegister(ushort address, byte value)
    {
        base.WriteRegister(address, value);
        if ((address & 1) == 0 || address >= 0xA000) return;
        var nt1 = (Registers[0] >> 5) & 1;
        var nt2 = (Registers[1] >> 5) & 1;
        SetNametable(0, nt1);
        SetNametable(1, nt1);
        SetNametable(2, nt2);
        SetNametable(3, nt2);
    }
}

/// <summary>Mapper 76 (Namco 3446)：4 个 2KB CHR 页（寄存器 2-5）。</summary>
public sealed class Namco3446(RomImage rom) : Namco108(rom)
{
    protected override void UpdateChrBanks()
    {
        for (var i = 0; i < 4; i++)
        {
            SetChr1K(i * 2, Registers[i + 2] * 2);
            SetChr1K(i * 2 + 1, Registers[i + 2] * 2 + 1);
        }
    }
}

/// <summary>Mapper 118 (TxSROM)：CHR 寄存器 bit7 直接选择 CIRAM 页作为名称表。</summary>
public sealed class TxSrom(RomImage rom) : Mmc3(rom)
{
    protected override void WriteMirroring(byte value)
    {
    }

    protected override void UpdateChrBanks()
    {
        base.UpdateChrBanks();
        if ((BankSelect & 0x80) == 0)
        {
            SetNametable(0, Registers[0] >> 7);
            SetNametable(1, Registers[0] >> 7);
            SetNametable(2, Registers[1] >> 7);
            SetNametable(3, Registers[1] >> 7);
        }
        else
        {
            for (var i = 0; i < 4; i++) SetNametable(i, Registers[2 + i] >> 7);
        }
    }
}

/// <summary>Mapper 119 (TQROM)：CHR 页 bit6 选择 8KB CHR RAM。</summary>
public sealed class Tqrom(RomImage rom) : Mmc3(rom, 0x2000)
{
    protected override void SetChrBank(int slot, int bank)
    {
        if ((bank & 0x40) != 0) SetChrRam1K(slot, bank & 7);
        else SetChr1K(slot, bank & 0x3F);
    }
}

/// <summary>Mapper 189 (TXC / 街霸)：$4120-$7FFF 写 32KB PRG，覆盖 MMC3 的 PRG 映射。</summary>
public sealed class Mapper189(RomImage rom) : Mmc3(rom)
{
    private int _prg;

    public override void Reset()
    {
        _prg = 0;
        base.Reset();
    }

    protected override bool WriteLowRegister(ushort address, byte value)
    {
        if (address < 0x4120) return false;
        _prg = (value | (value >> 4)) & 0x07;
        UpdateBanks();
        return address < 0x6000;
    }

    protected override void UpdatePrgBanks() => SetPrg32K(_prg);
}
