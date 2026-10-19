using AtomNES.Device.Cartridges;
using System.Globalization;
using AtomNES.Device.Bus;
using AtomNES.Device.Cpu;

namespace AtomNES.Device.Tests;

/// <summary>
/// 使用 nestest.nes 自动模式（PC=$C000）逐指令比对 nestest.log 中的寄存器与周期数。
/// </summary>
public class NestestTests
{
    private static string RomPath(string name) => Path.Combine(AppContext.BaseDirectory, "TestRoms", name);

    [Fact]
    public void Nestest_MatchesGoldenLog()
    {
        var bus = new NesBus();
        bus.InsertCartridge(MapperFactory.Create(RomImage.Load(RomPath("nestest.nes"))));
        var cpu = bus.Cpu;
        cpu.PC = 0xC000;

        var lines = File.ReadAllLines(RomPath("nestest.log"));
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var expectedPc = ushort.Parse(line.AsSpan(0, 4), NumberStyles.HexNumber);
            var expected = (
                PC: expectedPc,
                A: Hex(line, "A:"),
                X: Hex(line, "X:"),
                Y: Hex(line, "Y:"),
                P: Hex(line, "P:"),
                SP: Hex(line, "SP:"),
                Cyc: long.Parse(line[(line.IndexOf("CYC:", StringComparison.Ordinal) + 4)..]));
            var actual = (PC: cpu.PC, cpu.A, cpu.X, cpu.Y, cpu.P, cpu.SP, Cyc: cpu.TotalCycles);

            Assert.True(expected == actual,
                $"Line {i + 1}: {line}\nExpected {expected}\nActual   {actual}\n" +
                $"Disasm   {Disassembler.DisassembleOne(bus, cpu.PC).Text}");

            bus.Step();
        }

        // 官方/非官方测试结果码：$02/$03 均为 0 表示全部通过
        Assert.Equal(0, bus.Peek(0x0002));
        Assert.Equal(0, bus.Peek(0x0003));
    }

    private static byte Hex(string line, string key)
    {
        var idx = line.IndexOf(key, 48, StringComparison.Ordinal) + key.Length;
        return byte.Parse(line.AsSpan(idx, 2), NumberStyles.HexNumber);
    }
}

