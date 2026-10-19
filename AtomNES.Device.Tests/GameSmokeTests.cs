using AtomNES.Device.Bus;
using AtomNES.Device.Cartridges;
using AtomNES.Device.Display;
using AtomNES.Device.Input;
using AtomNES.Device.Ppu;

namespace AtomNES.Device.Tests;

/// <summary>
/// 用真实游戏 ROM 做冒烟测试：运行若干帧并导出 BMP 截图到测试输出目录（ROM 不存在时跳过）。
/// </summary>
public class GameSmokeTests
{
    private static string? FindRom(string keyword)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Rom")))
        {
            dir = dir.Parent;
        }

        return dir is null
            ? null
            : Directory.GetFiles(Path.Combine(dir.FullName, "Rom"), "*.nes", new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive })
                       .FirstOrDefault(f => Path.GetFileName(f).Contains(keyword));
    }

    [Theory]
    [InlineData("超级马利兄弟", 0)]
    [InlineData("马里奥兄弟", 3)]
    public void Game_BootsAndRenders(string keyword, int expectedMapper)
    {
        var path = FindRom(keyword);
        if (path is null)
        {
            return;
        }

        var rom = RomImage.Load(path);
        Assert.Equal(expectedMapper, rom.MapperId);

        var screen = new FrameBuffer();
        var bus = new NesBus();
        bus.ConnectPpu(new Ppu2C02(screen));
        bus.InsertCartridge(MapperFactory.Create(rom));

        var outDir = Path.Combine(AppContext.BaseDirectory, "Screenshots");
        Directory.CreateDirectory(outDir);

        for (var frame = 1; frame <= 600; frame++)
        {
            // 第 120 帧按下 START 进入游戏，之后按住右键前进
            bus.Controller1.Buttons = frame switch
            {
                >= 120 and < 126 => NesButtons.Start,
                >= 300 => NesButtons.Right,
                _ => NesButtons.None
            };

            bus.RunFrame();
            Assert.False(bus.Cpu.Jammed, $"CPU 在第 {frame} 帧停机，PC={bus.Cpu.PC:X4}");

            if (frame is 40 or 100 or 250 or 400 or 600)
            {
                screen.TryAcquireLatest(out var pixels);
                SaveBmp(Path.Combine(outDir, $"mapper{expectedMapper}_{frame}.bmp"), pixels);
            }
        }

        screen.TryAcquireLatest(out var last);
        Assert.True(last.ToArray().Distinct().Count() > 4, "画面颜色过少，可能没有正确渲染");
    }

    private static void SaveBmp(string path, ReadOnlySpan<uint> pixels)
    {
        const int w = FrameBuffer.Width, h = FrameBuffer.Height;
        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);
        bw.Write((byte)'B'); bw.Write((byte)'M');
        bw.Write(54 + w * h * 4); bw.Write(0); bw.Write(54);
        bw.Write(40); bw.Write(w); bw.Write(-h); bw.Write((short)1); bw.Write((short)32);
        bw.Write(0); bw.Write(w * h * 4); bw.Write(2835); bw.Write(2835); bw.Write(0); bw.Write(0);
        foreach (var p in pixels) bw.Write(p);
    }
}

