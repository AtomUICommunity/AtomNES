using AtomNES.Device.Bus;
using AtomNES.Device.Cartridges;
using AtomNES.Device.Display;
using AtomNES.Device.Input;
using AtomNES.Device.Ppu;

namespace AtomNES.Device.Tests;

public class NesBusTests
{
    private static NesBus Create(params byte[] program)
    {
        var prg = new byte[0x8000];
        program.CopyTo(prg, 0);
        prg[0x7FFC] = 0x00;
        prg[0x7FFD] = 0x80;
        var header = new byte[16];
        "NES\u001a"u8.CopyTo(header);
        header[4] = 2;
        header[5] = 1;
        var image = header.Concat(prg).Concat(new byte[0x2000]).ToArray();
        var bus = new NesBus();
        bus.ConnectPpu(new Ppu2C02(new FrameBuffer()));
        bus.InsertCartridge(MapperFactory.Create(RomImage.Parse(image)));
        return bus;
    }

    [Fact]
    public void Ram_IsMirroredEvery2K()
    {
        var bus = Create();
        bus.Write(0x0001, 0xAB);
        Assert.Equal(0xAB, bus.Peek(0x0801));
        Assert.Equal(0xAB, bus.Peek(0x1801));
    }

    [Fact]
    public void PpuRunsThreeDotsPerCpuCycle()
    {
        var bus = Create(0xEA);
        var ppu = (Ppu2C02)bus.Ppu!;
        var before = ppu.Scanline * 341 + ppu.Dot;
        bus.Step();
        Assert.Equal(6, ppu.Scanline * 341 + ppu.Dot - before);
    }

    [Fact]
    public void OamDma_CopiesPage_AndTakes513Or514Cycles()
    {
        // LDA #$02; STA $4014; NOP
        var bus = Create(0xA9, 0x02, 0x8D, 0x14, 0x40, 0xEA);
        for (var i = 0; i < 256; i++) bus.Write((ushort)(0x0200 + i), (byte)i);
        bus.Step();
        bus.Step();
        var cycles = bus.Step();
        Assert.True(cycles - 2 is 513 or 514, $"cycles={cycles}");
        Assert.Equal(0xFF, ((Ppu2C02)bus.Ppu!).Oam[0xFF]);
    }

    [Fact]
    public void Controller_ShiftsButtonsInOrder()
    {
        var bus = Create();
        bus.Controller1.Buttons = NesButtons.A | NesButtons.Start | NesButtons.Right;
        bus.Write(0x4016, 1);
        bus.Write(0x4016, 0);
        var bits = Enumerable.Range(0, 9).Select(_ => bus.Read(0x4016) & 1).ToArray();
        Assert.Equal([1, 0, 0, 1, 0, 0, 0, 1, 1], bits);
    }
}
