namespace AtomNES.Device.Cartridges;

/// <summary>Mapper 0 (NROM)：16/32KB PRG，8KB CHR。</summary>
public sealed class Nrom(RomImage rom) : Mapper(rom)
{
    public override void Reset()
    {
        SetPrg16K(0, 0);
        SetPrg16K(1, -1);
        SetChr8K(0);
    }
}

/// <summary>Mapper 3 (CNROM)：8KB CHR 切换。</summary>
public sealed class Cnrom(RomImage rom) : Mapper(rom)
{
    public override void Reset()
    {
        SetPrg16K(0, 0);
        SetPrg16K(1, -1);
        SetChr8K(0);
    }

    protected override void WriteRegister(ushort address, byte value)
    {
        // 总线冲突：写入值与 ROM 当前值相与
        TryCpuRead(address, out var rom);
        SetChr8K(value & rom);
    }
}

public static class MapperFactory
{
    public static Mapper Create(RomImage rom)
    {
        Mapper mapper = rom.MapperId switch
        {
            0 => new Nrom(rom),
            1 => new Mmc1(rom),
            2 => new Uxrom(rom),
            3 => new Cnrom(rom),
            4 => new Mmc3(rom),
            7 => new Axrom(rom),
            11 => new ColorDreams(rom),
            34 => new Mapper34(rom),
            66 => new Gxrom(rom),
            180 => new Unrom180(rom),
            _ => throw new NotSupportedException($"暂不支持 Mapper {rom.MapperId}。")
        };
        mapper.Reset();
        return mapper;
    }
}
