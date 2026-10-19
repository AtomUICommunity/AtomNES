using System.Text;
using AtomNES.Device.Bus;

namespace AtomNES.Device.Cpu;

public readonly record struct DisassembledLine(ushort Address, byte Opcode, int Length, string Text);

public static class Disassembler
{
    /// <summary>使用 <see cref="ICpuBus.Peek"/> 进行无副作用反汇编。</summary>
    public static List<DisassembledLine> Disassemble(ICpuBus bus, ushort start, ushort end)
    {
        var lines = new List<DisassembledLine>();
        int addr = start;
        while (addr <= end)
        {
            var line = DisassembleOne(bus, (ushort)addr);
            lines.Add(line);
            addr += line.Length;
        }

        return lines;
    }

    public static DisassembledLine DisassembleOne(ICpuBus bus, ushort address)
    {
        var opcode = bus.Peek(address);
        var info = OpcodeTable.Get(opcode);
        var lo = bus.Peek((ushort)(address + 1));
        var hi = bus.Peek((ushort)(address + 2));
        var word = (ushort)(lo | (hi << 8));

        var sb = new StringBuilder(24);
        if (!info.Official) sb.Append('*');
        sb.Append(info.Mnemonic);
        switch (info.Mode)
        {
            case AddressingMode.Acc: sb.Append(" A"); break;
            case AddressingMode.Imm: sb.Append($" #${lo:X2}"); break;
            case AddressingMode.Zp0: sb.Append($" ${lo:X2}"); break;
            case AddressingMode.Zpx: sb.Append($" ${lo:X2},X"); break;
            case AddressingMode.Zpy: sb.Append($" ${lo:X2},Y"); break;
            case AddressingMode.Abs: sb.Append($" ${word:X4}"); break;
            case AddressingMode.Abx: sb.Append($" ${word:X4},X"); break;
            case AddressingMode.Aby: sb.Append($" ${word:X4},Y"); break;
            case AddressingMode.Ind: sb.Append($" (${word:X4})"); break;
            case AddressingMode.Izx: sb.Append($" (${lo:X2},X)"); break;
            case AddressingMode.Izy: sb.Append($" (${lo:X2}),Y"); break;
            case AddressingMode.Rel: sb.Append($" ${(ushort)(address + 2 + (sbyte)lo):X4}"); break;
        }

        return new DisassembledLine(address, opcode, 1 + info.OperandLength, sb.ToString());
    }
}
