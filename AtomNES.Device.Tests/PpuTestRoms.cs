using System.Text;
using AtomNES.Device.Bus;
using AtomNES.Device.Cartridges;
using AtomNES.Device.Display;
using AtomNES.Device.Ppu;
using Xunit.Abstractions;

namespace AtomNES.Device.Tests;

/// <summary>
/// 运行 blargg 测试 ROM 并读取结果。
/// <list type="bullet">
/// <item>新版（ppu_vbl_nmi、oam_read 等）：$6000 写入状态（$80 运行中），$6001-$6003 为签名 DE B0 61，$6004 起为文本。</item>
/// <item>旧版（2005 年）：结果码在 $F0（blargg_ppu_tests）或 $F8（sprite_hit / sprite_overflow），1 表示通过。</item>
/// </list>
/// </summary>
public class PpuTestRoms(ITestOutputHelper output)
{
    private static string RomPath(string name) => Path.Combine(AppContext.BaseDirectory, "TestRoms", name);

    private static NesBus Boot(string rom)
    {
        var bus = new NesBus();
        bus.ConnectPpu(new Ppu2C02(new FrameBuffer()));
        bus.InsertCartridge(MapperFactory.Create(RomImage.Load(RomPath(rom))));
        return bus;
    }

    private static string ReadText(NesBus bus)
    {
        var sb = new StringBuilder();
        for (ushort a = 0x6004; a < 0x7000; a++)
        {
            var c = bus.Peek(a);
            if (c == 0) break;
            sb.Append((char)c);
        }

        return sb.ToString().Trim();
    }

    private static bool HasSignature(NesBus bus) =>
        bus.Peek(0x6001) == 0xDE && bus.Peek(0x6002) == 0xB0 && bus.Peek(0x6003) == 0x61;

    public static TheoryData<string> NewStyleRoms =>
    [
        "ppu_vbl_nmi/rom_singles/01-vbl_basics.nes",
        "ppu_vbl_nmi/rom_singles/02-vbl_set_time.nes",
        "ppu_vbl_nmi/rom_singles/03-vbl_clear_time.nes",
        "ppu_vbl_nmi/rom_singles/04-nmi_control.nes",
        "ppu_vbl_nmi/rom_singles/05-nmi_timing.nes",
        "ppu_vbl_nmi/rom_singles/06-suppression.nes",
        "ppu_vbl_nmi/rom_singles/07-nmi_on_timing.nes",
        "ppu_vbl_nmi/rom_singles/08-nmi_off_timing.nes",
        "ppu_vbl_nmi/rom_singles/09-even_odd_frames.nes",
        "ppu_vbl_nmi/rom_singles/10-even_odd_timing.nes",
        "ppu_open_bus/ppu_open_bus.nes",
        "ppu_read_buffer/test_ppu_read_buffer.nes",
        "oam_read/oam_read.nes",
        "oam_stress/oam_stress.nes",
        "cpu_dummy_writes/cpu_dummy_writes_ppumem.nes"
    ];

    [Theory]
    [MemberData(nameof(NewStyleRoms))]
    public void NewStyle(string rom)
    {
        var bus = Boot(rom);
        const int maxFrames = 60 * 60;
        var resetAt = -1;
        for (var frame = 0; frame < maxFrames; frame++)
        {
            bus.RunFrame();
            if (!HasSignature(bus)) continue;

            var status = bus.Peek(0x6000);
            if (status == 0x81 && resetAt < 0)
            {
                // 测试请求按下 RESET（需在 100ms 后）
                resetAt = frame + 10;
            }

            if (resetAt >= 0 && frame == resetAt)
            {
                bus.Reset();
                resetAt = int.MaxValue;
            }

            if (status < 0x80)
            {
                var text = ReadText(bus);
                output.WriteLine(text);
                Assert.True(status == 0, $"{rom} 失败（状态 {status}）：\n{text}");
                return;
            }
        }

        Assert.Fail($"{rom} 超时：\n{ReadText(bus)}");
    }

    public static TheoryData<string> OldStyleRoms =>
    [
        "blargg_ppu_tests_2005.09.15b/palette_ram.nes",
        "blargg_ppu_tests_2005.09.15b/power_up_palette.nes",
        "blargg_ppu_tests_2005.09.15b/sprite_ram.nes",
        "blargg_ppu_tests_2005.09.15b/vbl_clear_time.nes",
        "blargg_ppu_tests_2005.09.15b/vram_access.nes",
        "sprite_hit_tests_2005.10.05/01.basics.nes",
        "sprite_hit_tests_2005.10.05/02.alignment.nes",
        "sprite_hit_tests_2005.10.05/03.corners.nes",
        "sprite_hit_tests_2005.10.05/04.flip.nes",
        "sprite_hit_tests_2005.10.05/05.left_clip.nes",
        "sprite_hit_tests_2005.10.05/06.right_edge.nes",
        "sprite_hit_tests_2005.10.05/07.screen_bottom.nes",
        "sprite_hit_tests_2005.10.05/08.double_height.nes",
        "sprite_hit_tests_2005.10.05/09.timing_basics.nes",
        "sprite_hit_tests_2005.10.05/10.timing_order.nes",
        "sprite_hit_tests_2005.10.05/11.edge_timing.nes",
        "sprite_overflow_tests/1.Basics.nes",
        "sprite_overflow_tests/2.Details.nes",
        "sprite_overflow_tests/3.Timing.nes",
        "sprite_overflow_tests/4.Obscure.nes",
        "sprite_overflow_tests/5.Emulator.nes"
    ];

    [Theory]
    [MemberData(nameof(OldStyleRoms))]
    public void OldStyle(string rom)
    {
        var bus = Boot(rom);
        // 2005.09 的 blargg_ppu_tests 结果码在 $F0，2005.10 之后的测试在 $F8
        var resultAddress = (ushort)(rom.StartsWith("blargg_ppu_tests") ? 0xF0 : 0xF8);

        // 结果打印后 ROM 停在 forever 循环：通过检测 PC 长时间停留在极小范围内判断结束
        ushort lastPc = 0;
        var stableFrames = 0;
        for (var frame = 0; frame < 60 * 40; frame++)
        {
            bus.RunFrame();
            var pc = bus.Cpu.PC;
            stableFrames = Math.Abs(pc - lastPc) < 8 && bus.Peek(resultAddress) != 0 ? stableFrames + 1 : 0;
            lastPc = pc;
            if (stableFrames > 120) break;
        }

        var result = bus.Peek(resultAddress);
        output.WriteLine($"{rom}: result={result}");
        Assert.True(result == 1, $"{rom} 失败，结果码 {result}");
    }
}

