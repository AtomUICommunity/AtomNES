using AtomNES.Device.Bus;
using AtomNES.Device.Cpu;

namespace AtomNES.Device.Tests;

internal sealed class FlatBus : ICpuBus
{
    public readonly byte[] Memory = new byte[0x10000];

    public bool Nmi;
    public bool Irq;

    public byte Read(ushort address) => Memory[address];

    public void Write(ushort address, byte value) => Memory[address] = value;

    public byte Peek(ushort address) => Memory[address];

    public bool NmiLine => Nmi;

    public bool IrqLine => Irq;

    public bool OamDmaPending => false;

    public byte TakeOamDma() => 0;

    public Cpu6502 Load(ushort origin, params byte[] program)
    {
        program.CopyTo(Memory, origin);
        Memory[0xFFFC] = (byte)origin;
        Memory[0xFFFD] = (byte)(origin >> 8);
        var cpu = new Cpu6502(this);
        cpu.PowerOn();
        return cpu;
    }
}

public class Cpu6502Tests
{
    [Fact]
    public void PowerOn_LoadsResetVector_AndInitialState()
    {
        var bus = new FlatBus();
        var cpu = bus.Load(0x8000, 0xEA);
        Assert.Equal(0x8000, cpu.PC);
        Assert.Equal(0xFD, cpu.SP);
        Assert.Equal(0x24, cpu.P);
        Assert.Equal(7, cpu.TotalCycles);
    }

    [Fact]
    public void Adc_SetsCarryAndOverflow()
    {
        var bus = new FlatBus();
        var cpu = bus.Load(0x8000, 0xA9, 0x50, 0x69, 0x50);
        cpu.Step();
        cpu.Step();
        Assert.Equal(0xA0, cpu.A);
        Assert.True(cpu.Flags.HasFlag(CpuFlags.Overflow));
        Assert.True(cpu.Flags.HasFlag(CpuFlags.Negative));
        Assert.False(cpu.Flags.HasFlag(CpuFlags.Carry));
    }

    [Fact]
    public void JsrRts_RoundTrip()
    {
        var bus = new FlatBus();
        var cpu = bus.Load(0x8000, 0x20, 0x10, 0x80, 0xEA);
        bus.Memory[0x8010] = 0xA2; bus.Memory[0x8011] = 0x42; bus.Memory[0x8012] = 0x60;
        Assert.Equal(6, cpu.Step());
        Assert.Equal(0x8010, cpu.PC);
        cpu.Step();
        Assert.Equal(6, cpu.Step());
        Assert.Equal(0x8003, cpu.PC);
        Assert.Equal(0x42, cpu.X);
        Assert.Equal(0xFD, cpu.SP);
    }

    [Fact]
    public void JmpIndirect_PageWrapBug()
    {
        var bus = new FlatBus();
        var cpu = bus.Load(0x8000, 0x6C, 0xFF, 0x02);
        bus.Memory[0x02FF] = 0x34;
        bus.Memory[0x0200] = 0x12;
        bus.Memory[0x0300] = 0x99;
        cpu.Step();
        Assert.Equal(0x1234, cpu.PC);
    }

    [Fact]
    public void Branch_CyclePenalties()
    {
        var bus = new FlatBus();
        var cpu = bus.Load(0x80FB, 0xA9, 0x01, 0xD0, 0x01);
        cpu.Step();
        Assert.Equal(4, cpu.Step());
        Assert.Equal(0x8100, cpu.PC);

        cpu = bus.Load(0x8000, 0xA9, 0x00, 0xD0, 0x05, 0xF0, 0x00);
        cpu.Step();
        Assert.Equal(2, cpu.Step());
        Assert.Equal(3, cpu.Step());
    }

    [Fact]
    public void AbsoluteX_ReadPagePenalty_WriteFixed()
    {
        var bus = new FlatBus();
        var cpu = bus.Load(0x9000, 0xA2, 0x01, 0xBD, 0xFF, 0x80, 0x9D, 0xFF, 0x80, 0xBD, 0x00, 0x80);
        cpu.Step();
        Assert.Equal(5, cpu.Step());
        Assert.Equal(5, cpu.Step());
        Assert.Equal(4, cpu.Step());
    }

    [Fact]
    public void ReadModifyWrite_PerformsDummyWrite()
    {
        var writes = new List<(ushort, byte)>();
        var bus = new RecordingBus(writes);
        bus.Memory[0x10] = 0x41;
        var cpu = bus.Load(0x8000, 0xE6, 0x10); // INC $10
        Assert.Equal(5, cpu.Step());
        Assert.Equal([(0x10, 0x41), (0x10, 0x42)], writes);
    }

    [Fact]
    public void Nmi_IsTakenAfterCurrentInstruction()
    {
        var bus = new FlatBus();
        var cpu = bus.Load(0x8000, 0xEA, 0xEA);
        bus.Memory[0xFFFA] = 0x00; bus.Memory[0xFFFB] = 0x90;
        bus.Nmi = true;
        Assert.Equal(2 + 7, cpu.Step());
        Assert.Equal(0x9000, cpu.PC);
        Assert.Equal(0x24, bus.Memory[0x01FB]);
        Assert.Equal(0x01, bus.Memory[0x01FC]);
        Assert.Equal(0x80, bus.Memory[0x01FD]);
    }

    [Fact]
    public void Nmi_IsEdgeTriggered()
    {
        var bus = new FlatBus();
        var cpu = bus.Load(0x8000, 0xEA, 0xEA, 0xEA);
        bus.Memory[0x9000] = 0xEA;
        bus.Memory[0xFFFA] = 0x00; bus.Memory[0xFFFB] = 0x90;
        bus.Nmi = true;
        cpu.Step();
        Assert.Equal(0x9000, cpu.PC);
        Assert.Equal(2, cpu.Step());
        Assert.Equal(0x9001, cpu.PC);
    }

    [Fact]
    public void Cli_DelaysIrqByOneInstruction()
    {
        var bus = new FlatBus();
        var cpu = bus.Load(0x8000, 0x58, 0xEA, 0xEA);
        bus.Memory[0xFFFE] = 0x00; bus.Memory[0xFFFF] = 0xA0;
        bus.Irq = true;
        cpu.Step(); // CLI
        Assert.Equal(0x8001, cpu.PC);
        cpu.Step(); // NOP，随后响应 IRQ
        Assert.Equal(0xA000, cpu.PC);
    }

    [Fact]
    public void Unofficial_LaxAndDcp()
    {
        var bus = new FlatBus();
        bus.Memory[0x10] = 0x05;
        var cpu = bus.Load(0x8000, 0xA7, 0x10, 0xC7, 0x10);
        cpu.Step();
        Assert.Equal(5, cpu.A);
        Assert.Equal(5, cpu.X);
        Assert.Equal(5, cpu.Step());
        Assert.Equal(4, bus.Memory[0x10]);
        Assert.True(cpu.Flags.HasFlag(CpuFlags.Carry));
    }

    [Fact]
    public void Disassembler_FormatsOperands()
    {
        var bus = new FlatBus();
        bus.Load(0x8000, 0xBD, 0x34, 0x12, 0xD0, 0xFE, 0x0A);
        var lines = Disassembler.Disassemble(bus, 0x8000, 0x8005);
        Assert.Equal(["LDA $1234,X", "BNE $8003", "ASL A"], lines.Select(l => l.Text));
    }

    [Fact]
    public void OpcodeTable_ClassifiesOfficial()
    {
        Assert.Equal(151, Enumerable.Range(0, 256).Count(i => OpcodeTable.Get((byte)i).Official));
    }

    private sealed class RecordingBus(List<(ushort, byte)> writes) : ICpuBus
    {
        public readonly byte[] Memory = new byte[0x10000];
        public byte Read(ushort address) => Memory[address];
        public void Write(ushort address, byte value) { writes.Add((address, value)); Memory[address] = value; }
        public byte Peek(ushort address) => Memory[address];
        public bool NmiLine => false;
        public bool IrqLine => false;
        public bool OamDmaPending => false;
        public byte TakeOamDma() => 0;

        public Cpu6502 Load(ushort origin, params byte[] program)
        {
            program.CopyTo(Memory, origin);
            Memory[0xFFFC] = (byte)origin;
            Memory[0xFFFD] = (byte)(origin >> 8);
            var cpu = new Cpu6502(this);
            cpu.PowerOn();
            return cpu;
        }
    }
}
