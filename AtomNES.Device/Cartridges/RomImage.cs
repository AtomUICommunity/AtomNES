namespace AtomNES.Device.Cartridges;

public enum Mirroring
{
    Horizontal,
    Vertical,
    SingleScreenLow,
    SingleScreenHigh,
    FourScreen
}

/// <summary>iNES / NES 2.0 头解析结果。</summary>
public sealed class RomImage
{
    public required int MapperId { get; init; }
    public required int SubMapper { get; init; }
    public required byte[] Prg { get; init; }
    public required byte[] Chr { get; init; }
    public required bool HasChrRam { get; init; }
    public required int PrgRamSize { get; init; }
    public required int ChrRamSize { get; init; }
    public required Mirroring Mirroring { get; init; }
    public required bool HasBattery { get; init; }
    public required bool IsNes20 { get; init; }

    public static RomImage Load(string path) => Parse(File.ReadAllBytes(path));

    public static RomImage Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 16 || data[0] != 'N' || data[1] != 'E' || data[2] != 'S' || data[3] != 0x1A)
        {
            throw new InvalidDataException("不是有效的 iNES 文件。");
        }

        var flags6 = data[6];
        var flags7 = data[7];
        var nes20 = (flags7 & 0x0C) == 0x08;

        int prgSize, chrSize, mapper, subMapper, prgRam, chrRam;
        if (nes20)
        {
            prgSize = RomSize(data[4], data[9] & 0x0F, 0x4000);
            chrSize = RomSize(data[5], data[9] >> 4, 0x2000);
            mapper = (flags6 >> 4) | (flags7 & 0xF0) | ((data[8] & 0x0F) << 8);
            subMapper = data[8] >> 4;
            var prgRamShift = data[10] & 0x0F;
            var prgNvShift = data[10] >> 4;
            prgRam = (prgRamShift == 0 ? 0 : 64 << prgRamShift) + (prgNvShift == 0 ? 0 : 64 << prgNvShift);
            var chrRamShift = data[11] & 0x0F;
            chrRam = chrRamShift == 0 ? 0 : 64 << chrRamShift;
        }
        else
        {
            prgSize = data[4] * 0x4000;
            chrSize = data[5] * 0x2000;
            // 旧版 dump 工具在 8-15 字节写入垃圾时，只使用低 4 位 mapper 号
            var dirty = data[12] != 0 || data[13] != 0 || data[14] != 0 || data[15] != 0;
            mapper = (flags6 >> 4) | (dirty ? 0 : flags7 & 0xF0);
            subMapper = 0;
            prgRam = 0x2000;
            chrRam = chrSize == 0 ? 0x2000 : 0;
        }

        var offset = 16 + ((flags6 & 0x04) != 0 ? 512 : 0);
        if (data.Length < offset + prgSize)
        {
            throw new InvalidDataException("ROM 文件被截断。");
        }

        var prg = data.Slice(offset, prgSize).ToArray();
        offset += prgSize;
        var chrAvailable = Math.Max(0, Math.Min(chrSize, data.Length - offset));
        var hasChrRam = chrSize == 0;
        var chr = hasChrRam
            ? new byte[Math.Max(chrRam, 0x2000)]
            : data.Slice(offset, chrAvailable).ToArray();

        var mirroring = (flags6 & 0x08) != 0
            ? Mirroring.FourScreen
            : (flags6 & 0x01) != 0 ? Mirroring.Vertical : Mirroring.Horizontal;

        return new RomImage
        {
            MapperId = mapper,
            SubMapper = subMapper,
            Prg = prg,
            Chr = chr,
            HasChrRam = hasChrRam,
            PrgRamSize = prgRam,
            ChrRamSize = chrRam,
            Mirroring = mirroring,
            HasBattery = (flags6 & 0x02) != 0,
            IsNes20 = nes20
        };
    }

    private static int RomSize(int lsb, int msb, int unit)
    {
        if (msb == 0x0F)
        {
            var exponent = lsb >> 2;
            var multiplier = (lsb & 0x03) * 2 + 1;
            return (1 << exponent) * multiplier;
        }

        return ((msb << 8) | lsb) * unit;
    }
}
