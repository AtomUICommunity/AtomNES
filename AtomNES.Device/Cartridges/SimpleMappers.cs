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
            9 => new Mmc2(rom, false),
            10 => new Mmc2(rom, true),
            21 or 22 or 23 or 25 => new Vrc24(rom),
            28 => new Action53(rom),
            32 => new IremG101(rom),
            33 => new TaitoTc0190(rom),
            38 => new Mapper38(rom),
            41 => new Caltron41(rom),
            46 => new ColorDreams46(rom),
            48 => new TaitoTc0690(rom),
            58 => new Mapper58(rom),
            61 => new Mapper61(rom),
            62 => new Mapper62(rom),
            65 => new IremH3001(rom),
            69 => new SunsoftFme7(rom),
            70 => new Bandai74161(rom, false),
            71 => new Camerica71(rom),
            72 => new Jaleco72(rom, false),
            76 => new Namco3446(rom),
            78 => new Jaleco78(rom),
            79 or 146 => new Nina06(rom, false),
            86 => new Jaleco86(rom),
            87 => new Mapper87(rom),
            88 => new Namco108Chr64(rom),
            89 => new Sunsoft89(rom),
            92 => new Jaleco72(rom, true),
            93 => new Sunsoft93(rom),
            94 => new Unrom94(rom),
            95 => new Namco108Nt(rom),
            97 => new Irem97(rom),
            107 => new Mapper107(rom),
            113 => new Nina06(rom, true),
            118 => new TxSrom(rom),
            119 => new Tqrom(rom),
            133 => new Sachen133(rom),
            140 => new Jaleco140(rom),
            150 or 243 => new Sachen74LS374N(rom),
            152 => new Bandai74161(rom, true),
            154 => new Namco108Mirror(rom),
            189 => new Mapper189(rom),
            200 => new Mapper200(rom),
            202 => new Mapper202(rom),
            203 => new Mapper203(rom),
            206 => new Namco108(rom),
            212 => new Mapper212(rom),
            225 => new Mapper225(rom),
            226 => new Mapper226(rom),
            229 => new Mapper229(rom),
            231 => new Mapper231(rom),
            232 => new Camerica232(rom),
            240 => new Mapper240(rom),
            242 => new Mapper242(rom),
            246 => new Mapper246(rom),
            252 => new Waixing252(rom),
            15 => new Mapper15(rom),
            74 => new Mapper74(rom),
            112 => new Mapper112(rom),
            115 => new Mapper115(rom),
            180 => new Unrom180(rom),
            241 => new Mapper241(rom),
            245 => new Mapper245(rom),
            249 => new Mapper249(rom),
            _ => throw new NotSupportedException($"暂不支持 Mapper {rom.MapperId}。")
        };
        mapper.Reset();
        return mapper;
    }
}
