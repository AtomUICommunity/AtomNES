using System.Text;
using AtomNES.Device.Bus;
using AtomNES.Device.Cartridges;
using AtomNES.Device.Display;
using AtomNES.Device.Ppu;
using Xunit.Abstractions;

namespace AtomNES.Device.Tests;

/// <summary>
/// Mapper 测试 ROM：
/// <list type="bullet">
/// <item>Holy Mapperel（pinobatch）：识别 mapper、PRG/CHR/WRAM 大小与详细测试码，结果从名称表文本读取。</item>
/// <item>mmc3_test_2（blargg）：MMC3 IRQ 计数器与 A12 时序。</item>
/// </list>
/// </summary>
public class MapperTestRoms(ITestOutputHelper output)
{
    private static string RomPath(string name) => Path.Combine(AppContext.BaseDirectory, "TestRoms", name);

    private static NesBus Boot(string rom)
    {
        var bus = new NesBus();
        bus.ConnectPpu(new Ppu2C02(new FrameBuffer()));
        bus.InsertCartridge(MapperFactory.Create(RomImage.Load(RomPath(rom))));
        return bus;
    }

    /// <summary>把名称表 $2000 解码为文本（字体按 ASCII &amp; $3F 排列）。</summary>
    private static string ReadNametable(NesBus bus)
    {
        var sb = new StringBuilder();
        for (var row = 0; row < 30; row++)
        {
            var line = new StringBuilder();
            for (var col = 0; col < 32; col++)
            {
                var tile = bus.Cartridge!.PpuPeek((ushort)(0x2000 + row * 32 + col));
                line.Append(tile switch
                {
                    0 => ' ',
                    < 0x20 => (char)(tile + 0x40),
                    < 0x40 => (char)tile,
                    _ => ' '
                });
            }

            var text = line.ToString().TrimEnd();
            if (text.Length > 0) sb.AppendLine(text);
        }

        return sb.ToString();
    }

    public static TheoryData<string, string> HolyMapperelRoms => new()
    {
        { "M0_P32K_C8K_V.nes", "000 " },
        { "M0_P32K_CR32K_V.nes", "000 " },
        { "M0_P32K_CR8K_V.nes", "000 " },
        { "M1_P128K.nes", "001 " },
        { "M1_P128K_C128K.nes", "001 " },
        { "M1_P128K_C128K_S8K.nes", "001 " },
        { "M1_P128K_C128K_W8K.nes", "001 " },
        { "M1_P128K_C32K.nes", "001 " },
        { "M1_P128K_C32K_S8K.nes", "001 " },
        { "M1_P128K_C32K_W8K.nes", "001 " },
        { "M1_P128K_CR8K.nes", "001 " },
        { "M1_P512K_CR8K_S32K.nes", "001 " },
        { "M1_P512K_CR8K_S8K.nes", "001 " },
        { "M1_P512K_S32K.nes", "001 " },
        { "M1_P512K_S8K.nes", "001 " },
        { "M2_P128K_CR8K_V.nes", "002 " },
        { "M2_P128K_V.nes", "002 " },
        { "M3_P32K_C32K_H.nes", "003 " },
        { "M4_P128K.nes", "004 " },
        { "M4_P128K_CR32K.nes", "004 " },
        { "M4_P128K_CR8K.nes", "004 " },
        { "M4_P256K_C256K.nes", "004 " },
        { "M7_P128K.nes", "007 " },
        { "M7_P128K_CR8K.nes", "007 " },
        { "M11_P64K_C64K_V.nes", "011 " },
        { "M11_P64K_CR32K_V.nes", "011 " },
        { "M34_P128K_CR8K_H.nes", "034 " },
        { "M34_P128K_H.nes", "034 " },
        { "M66_P64K_C16K_V.nes", "066 " },
        { "M180_P128K_CR8K_H.nes", "180 " },
        { "M180_P128K_H.nes", "180 " },
        { "M9_P128K_C64K.nes", "009 " },
        { "M10_P128K_C64K_S8K.nes", "010 " },
        { "M10_P128K_C64K_W8K.nes", "010 " },
        { "M28_P512K.nes", "028 " },
        { "M28_P512K_CR32K.nes", "028 " },
        { "M69_P128K_C64K_S8K.nes", "069 " },
        { "M69_P128K_C64K_W8K.nes", "069 " },
        { "M78.3_P128K_C64K.nes", "078 " },
        { "M118_P128K_C64K.nes", "118 " },
    };

    [Theory]
    [MemberData(nameof(HolyMapperelRoms))]
    public void HolyMapperel(string rom, string expectedBoard)
    {
        var bus = Boot("holy_mapperel/" + rom);
        string text = "";
        for (var frame = 0; frame < 60 * 30; frame++)
        {
            bus.RunFrame();
            Assert.False(bus.Cpu.Jammed, $"CPU 停机 PC={bus.Cpu.PC:X4}");
            if (frame % 60 != 59) continue;
            text = ReadNametable(bus);
            if (text.Contains("FLASH ID")) break;
        }

        output.WriteLine(text);
        Assert.Contains("FLASH ID", text);
        Assert.StartsWith(expectedBoard, text.TrimStart());
        Assert.DoesNotContain("PROBLEM", text);
        Assert.DoesNotContain("MISSING", text.Replace("PRG RAM MISSING", ""));
        Assert.Contains("DETAILED TEST RESULT: 0000", text);
    }

    public static TheoryData<string> Mmc3Roms =>
    [
        "mmc3_test_2/1-clocking.nes",
        "mmc3_test_2/2-details.nes",
        "mmc3_test_2/3-A12_clocking.nes",
        "mmc3_test_2/4-scanline_timing.nes",
        "mmc3_test_2/5-MMC3.nes",
    ];

    [Theory]
    [MemberData(nameof(Mmc3Roms))]
    public void Mmc3(string rom)
    {
        var bus = Boot(rom);
        for (var frame = 0; frame < 60 * 30; frame++)
        {
            bus.RunFrame();
            if (bus.Peek(0x6001) != 0xDE || bus.Peek(0x6002) != 0xB0 || bus.Peek(0x6003) != 0x61) continue;
            var status = bus.Peek(0x6000);
            if (status >= 0x80) continue;

            var sb = new StringBuilder();
            for (ushort a = 0x6004; a < 0x7000 && bus.Peek(a) != 0; a++) sb.Append((char)bus.Peek(a));
            output.WriteLine(sb.ToString());
            Assert.True(status == 0, $"{rom} 失败（状态 {status}）：\n{sb}");
            return;
        }

        Assert.Fail($"{rom} 超时");
    }
}
