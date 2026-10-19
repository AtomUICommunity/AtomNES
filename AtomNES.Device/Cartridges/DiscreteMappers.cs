namespace AtomNES.Device.Cartridges;

/// <summary>Mapper 2 (UxROM)：$8000 16KB 可切换，$C000 固定末页。</summary>
public sealed class Uxrom(RomImage rom) : Mapper(rom)
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
        SetPrg16K(0, value & bus);
    }
}

/// <summary>Mapper 180 (UNROM 7408 / Crazy Climber)：$8000 固定首页，$C000 可切换。</summary>
public sealed class Unrom180(RomImage rom) : Mapper(rom)
{
    public override void Reset()
    {
        SetPrg16K(0, 0);
        SetPrg16K(1, 0);
        SetChr8K(0);
    }

    protected override void WriteRegister(ushort address, byte value) => SetPrg16K(1, value & 7);
}

/// <summary>Mapper 7 (AxROM)：32KB PRG 切换 + 单屏镜像选择。</summary>
public sealed class Axrom(RomImage rom) : Mapper(rom)
{
    public override void Reset()
    {
        SetPrg32K(0);
        SetChr8K(0);
        SetMirroring(Mirroring.SingleScreenLow);
    }

    protected override void WriteRegister(ushort address, byte value)
    {
        SetPrg32K(value & 0x0F);
        SetMirroring((value & 0x10) != 0 ? Mirroring.SingleScreenHigh : Mirroring.SingleScreenLow);
    }
}

/// <summary>Mapper 11 (Color Dreams)：低 2 位 PRG 32KB，高 4 位 CHR 8KB。</summary>
public sealed class ColorDreams(RomImage rom) : Mapper(rom)
{
    public override void Reset()
    {
        SetPrg32K(0);
        SetChr8K(0);
    }

    protected override void WriteRegister(ushort address, byte value)
    {
        SetPrg32K(value & 3);
        SetChr8K(value >> 4);
    }
}

/// <summary>Mapper 66 (GxROM)：位 4-5 PRG 32KB，位 0-1 CHR 8KB。</summary>
public sealed class Gxrom(RomImage rom) : Mapper(rom)
{
    public override void Reset()
    {
        SetPrg32K(0);
        SetChr8K(0);
    }

    protected override void WriteRegister(ushort address, byte value)
    {
        SetPrg32K((value >> 4) & 3);
        SetChr8K(value & 3);
    }
}

/// <summary>
/// Mapper 34：CHR 不超过 8KB 时为 BNROM（$8000-$FFFF 写 32KB PRG），
/// 否则为 NINA-001（$7FFD-$7FFF 寄存器）。
/// </summary>
public sealed class Mapper34(RomImage rom) : Mapper(rom)
{
    private readonly bool _nina = rom.Chr.Length > 0x2000;

    public override void Reset()
    {
        SetPrg32K(0);
        SetChr8K(0);
    }

    protected override void WriteRegister(ushort address, byte value)
    {
        if (_nina) return;
        TryCpuRead(address, out var bus);
        SetPrg32K(value & bus);
    }

    protected override bool WriteLowRegister(ushort address, byte value)
    {
        if (!_nina) return false;
        switch (address)
        {
            case 0x7FFD: SetPrg32K(value & 1); break;
            case 0x7FFE: SetChr4K(0, value & 0x0F); break;
            case 0x7FFF: SetChr4K(1, value & 0x0F); break;
        }

        return false;
    }
}
