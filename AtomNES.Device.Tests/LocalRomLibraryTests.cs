using AtomNES.Device.Bus;
using AtomNES.Device.Cartridges;
using AtomNES.Device.Display;
using AtomNES.Device.Input;
using AtomNES.Device.Ppu;
using Xunit.Abstractions;

namespace AtomNES.Device.Tests;

/// <summary>
/// 本地 ROM 库冒烟测试：设置环境变量 ATOMNES_ROM_LIBRARY 指向目录后，
/// 每个已支持的 Mapper 抽取 2 个 ROM 各运行 20 秒，检查 CPU 不停机、画面有内容。
/// </summary>
public class LocalRomLibraryTests(ITestOutputHelper output)
{
    /// <summary>已用 Mesen 确认本身有问题的 ROM（见 AGENTS.md），不参与测试。</summary>
    private static readonly string[] KnownBadRoms = ["[031]", "[032]", "[039]", "[040]", "[042]", "[059]", "[229]"];

    [Fact]
    public void SupportedRoms_RunWithoutJam()
    {
        var dir = Environment.GetEnvironmentVariable("ATOMNES_ROM_LIBRARY");
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

        var failures = new List<string>();
        var count = 0;
        var samples = Directory.EnumerateFiles(dir, "*.nes", SearchOption.AllDirectories)
            .Where(f => !KnownBadRoms.Any(b => Path.GetFileName(f).StartsWith(b)))
            .Select(f => (File: f, Rom: TryLoad(f)))
            .Where(x => x.Rom is not null)
            .GroupBy(x => x.Rom!.MapperId)
            .SelectMany(g => g.Take(2));
        foreach (var (file, rom0) in samples)
        {
            var rom = rom0!;
            var mapper = MapperFactory.Create(rom);

            count++;
            var screen = new FrameBuffer();
            var bus = new NesBus();
            bus.ConnectPpu(new Ppu2C02(screen));
            bus.InsertCartridge(mapper);
            var colors = 0;
            for (var frame = 0; frame < 1200 && !bus.Cpu.Jammed; frame++)
            {
                bus.Controller1.Buttons = frame % 120 is >= 100 and < 106 ? NesButtons.Start : NesButtons.None;
                bus.RunFrame();
                if (frame % 120 == 119 && screen.TryAcquireLatest(out var px))
                {
                    colors = Math.Max(colors, px.ToArray().Distinct().Count());
                }
            }

            var name = $"M{rom.MapperId:D3} {Path.GetFileNameWithoutExtension(file)}";
            if (bus.Cpu.Jammed) failures.Add($"{name}: CPU 停机 PC={bus.Cpu.PC:X4}");
            else if (colors <= 1) failures.Add($"{name}: 黑屏");
        }

        output.WriteLine($"已测试 {count} 个 ROM");
        foreach (var f in failures) output.WriteLine(f);
        Assert.Empty(failures);
    }

    private static RomImage? TryLoad(string file)
    {
        try
        {
            var rom = RomImage.Load(file);
            MapperFactory.Create(rom);
            return rom;
        }
        catch (Exception e) when (e is NotSupportedException or InvalidDataException)
        {
            return null;
        }
    }
}