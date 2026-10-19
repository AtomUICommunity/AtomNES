namespace AtomNES.Device.Cpu;

public enum AddressingMode : byte
{
    Imp,
    Acc,
    Imm,
    Zp0,
    Zpx,
    Zpy,
    Rel,
    Abs,
    Abx,
    Aby,
    Ind,
    Izx,
    Izy
}

public enum Mnemonic : byte
{
    // 官方指令
    ADC, AND, ASL, BCC, BCS, BEQ, BIT, BMI, BNE, BPL, BRK, BVC, BVS, CLC,
    CLD, CLI, CLV, CMP, CPX, CPY, DEC, DEX, DEY, EOR, INC, INX, INY, JMP,
    JSR, LDA, LDX, LDY, LSR, NOP, ORA, PHA, PHP, PLA, PLP, ROL, ROR, RTI,
    RTS, SBC, SEC, SED, SEI, STA, STX, STY, TAX, TAY, TSX, TXA, TXS, TYA,

    // 非官方指令
    ALR, ANC, ARR, AXS, DCP, ISB, JAM, LAS, LAX, LXA, RLA, RRA, SAX, SHA,
    SHX, SHY, SLO, SRE, TAS, XAA
}

public readonly record struct OpcodeInfo(Mnemonic Mnemonic, AddressingMode Mode, byte Cycles, bool Official)
{
    public int OperandLength => Mode switch
    {
        AddressingMode.Imp or AddressingMode.Acc => 0,
        AddressingMode.Abs or AddressingMode.Abx or AddressingMode.Aby or AddressingMode.Ind => 2,
        _ => 1
    };
}

/// <summary>
/// 2A03 (6502 内核，无 BCD) 的 256 条操作码表，同时供执行与反汇编使用。
/// </summary>
public static class OpcodeTable
{
    private static readonly string[] Rows =
    [
        "BRK IMP|ORA IZX|JAM IMP|SLO IZX|NOP ZP0|ORA ZP0|ASL ZP0|SLO ZP0|PHP IMP|ORA IMM|ASL ACC|ANC IMM|NOP ABS|ORA ABS|ASL ABS|SLO ABS",
        "BPL REL|ORA IZY|JAM IMP|SLO IZY|NOP ZPX|ORA ZPX|ASL ZPX|SLO ZPX|CLC IMP|ORA ABY|NOP IMP|SLO ABY|NOP ABX|ORA ABX|ASL ABX|SLO ABX",
        "JSR ABS|AND IZX|JAM IMP|RLA IZX|BIT ZP0|AND ZP0|ROL ZP0|RLA ZP0|PLP IMP|AND IMM|ROL ACC|ANC IMM|BIT ABS|AND ABS|ROL ABS|RLA ABS",
        "BMI REL|AND IZY|JAM IMP|RLA IZY|NOP ZPX|AND ZPX|ROL ZPX|RLA ZPX|SEC IMP|AND ABY|NOP IMP|RLA ABY|NOP ABX|AND ABX|ROL ABX|RLA ABX",
        "RTI IMP|EOR IZX|JAM IMP|SRE IZX|NOP ZP0|EOR ZP0|LSR ZP0|SRE ZP0|PHA IMP|EOR IMM|LSR ACC|ALR IMM|JMP ABS|EOR ABS|LSR ABS|SRE ABS",
        "BVC REL|EOR IZY|JAM IMP|SRE IZY|NOP ZPX|EOR ZPX|LSR ZPX|SRE ZPX|CLI IMP|EOR ABY|NOP IMP|SRE ABY|NOP ABX|EOR ABX|LSR ABX|SRE ABX",
        "RTS IMP|ADC IZX|JAM IMP|RRA IZX|NOP ZP0|ADC ZP0|ROR ZP0|RRA ZP0|PLA IMP|ADC IMM|ROR ACC|ARR IMM|JMP IND|ADC ABS|ROR ABS|RRA ABS",
        "BVS REL|ADC IZY|JAM IMP|RRA IZY|NOP ZPX|ADC ZPX|ROR ZPX|RRA ZPX|SEI IMP|ADC ABY|NOP IMP|RRA ABY|NOP ABX|ADC ABX|ROR ABX|RRA ABX",
        "NOP IMM|STA IZX|NOP IMM|SAX IZX|STY ZP0|STA ZP0|STX ZP0|SAX ZP0|DEY IMP|NOP IMM|TXA IMP|XAA IMM|STY ABS|STA ABS|STX ABS|SAX ABS",
        "BCC REL|STA IZY|JAM IMP|SHA IZY|STY ZPX|STA ZPX|STX ZPY|SAX ZPY|TYA IMP|STA ABY|TXS IMP|TAS ABY|SHY ABX|STA ABX|SHX ABY|SHA ABY",
        "LDY IMM|LDA IZX|LDX IMM|LAX IZX|LDY ZP0|LDA ZP0|LDX ZP0|LAX ZP0|TAY IMP|LDA IMM|TAX IMP|LXA IMM|LDY ABS|LDA ABS|LDX ABS|LAX ABS",
        "BCS REL|LDA IZY|JAM IMP|LAX IZY|LDY ZPX|LDA ZPX|LDX ZPY|LAX ZPY|CLV IMP|LDA ABY|TSX IMP|LAS ABY|LDY ABX|LDA ABX|LDX ABY|LAX ABY",
        "CPY IMM|CMP IZX|NOP IMM|DCP IZX|CPY ZP0|CMP ZP0|DEC ZP0|DCP ZP0|INY IMP|CMP IMM|DEX IMP|AXS IMM|CPY ABS|CMP ABS|DEC ABS|DCP ABS",
        "BNE REL|CMP IZY|JAM IMP|DCP IZY|NOP ZPX|CMP ZPX|DEC ZPX|DCP ZPX|CLD IMP|CMP ABY|NOP IMP|DCP ABY|NOP ABX|CMP ABX|DEC ABX|DCP ABX",
        "CPX IMM|SBC IZX|NOP IMM|ISB IZX|CPX ZP0|SBC ZP0|INC ZP0|ISB ZP0|INX IMP|SBC IMM|NOP IMP|SBC IMM|CPX ABS|SBC ABS|INC ABS|ISB ABS",
        "BEQ REL|SBC IZY|JAM IMP|ISB IZY|NOP ZPX|SBC ZPX|INC ZPX|ISB ZPX|SED IMP|SBC ABY|NOP IMP|ISB ABY|NOP ABX|SBC ABX|INC ABX|ISB ABX"
    ];

    private static ReadOnlySpan<byte> CycleTable =>
    [
        7, 6, 2, 8, 3, 3, 5, 5, 3, 2, 2, 2, 4, 4, 6, 6,
        2, 5, 2, 8, 4, 4, 6, 6, 2, 4, 2, 7, 4, 4, 7, 7,
        6, 6, 2, 8, 3, 3, 5, 5, 4, 2, 2, 2, 4, 4, 6, 6,
        2, 5, 2, 8, 4, 4, 6, 6, 2, 4, 2, 7, 4, 4, 7, 7,
        6, 6, 2, 8, 3, 3, 5, 5, 3, 2, 2, 2, 3, 4, 6, 6,
        2, 5, 2, 8, 4, 4, 6, 6, 2, 4, 2, 7, 4, 4, 7, 7,
        6, 6, 2, 8, 3, 3, 5, 5, 4, 2, 2, 2, 5, 4, 6, 6,
        2, 5, 2, 8, 4, 4, 6, 6, 2, 4, 2, 7, 4, 4, 7, 7,
        2, 6, 2, 6, 3, 3, 3, 3, 2, 2, 2, 2, 4, 4, 4, 4,
        2, 6, 2, 6, 4, 4, 4, 4, 2, 5, 2, 5, 5, 5, 5, 5,
        2, 6, 2, 6, 3, 3, 3, 3, 2, 2, 2, 2, 4, 4, 4, 4,
        2, 5, 2, 5, 4, 4, 4, 4, 2, 4, 2, 4, 4, 4, 4, 4,
        2, 6, 2, 8, 3, 3, 5, 5, 2, 2, 2, 2, 4, 4, 6, 6,
        2, 5, 2, 8, 4, 4, 6, 6, 2, 4, 2, 7, 4, 4, 7, 7,
        2, 6, 2, 8, 3, 3, 5, 5, 2, 2, 2, 2, 4, 4, 6, 6,
        2, 5, 2, 8, 4, 4, 6, 6, 2, 4, 2, 7, 4, 4, 7, 7
    ];

    private static readonly OpcodeInfo[] Table = Build();

    public static OpcodeInfo Get(byte opcode) => Table[opcode];

    internal static OpcodeInfo[] All => Table;

    private static OpcodeInfo[] Build()
    {
        var table = new OpcodeInfo[256];
        for (var row = 0; row < 16; row++)
        {
            var cells = Rows[row].Split('|');
            for (var col = 0; col < 16; col++)
            {
                var op = row * 16 + col;
                var parts = cells[col].Split(' ');
                var mnemonic = Enum.Parse<Mnemonic>(parts[0]);
                var mode = parts[1] switch
                {
                    "IMP" => AddressingMode.Imp,
                    "ACC" => AddressingMode.Acc,
                    "IMM" => AddressingMode.Imm,
                    "ZP0" => AddressingMode.Zp0,
                    "ZPX" => AddressingMode.Zpx,
                    "ZPY" => AddressingMode.Zpy,
                    "REL" => AddressingMode.Rel,
                    "ABS" => AddressingMode.Abs,
                    "ABX" => AddressingMode.Abx,
                    "ABY" => AddressingMode.Aby,
                    "IND" => AddressingMode.Ind,
                    "IZX" => AddressingMode.Izx,
                    "IZY" => AddressingMode.Izy,
                    _ => throw new InvalidOperationException(parts[1])
                };
                var official = mnemonic <= Mnemonic.TYA &&
                               !(mnemonic == Mnemonic.NOP && op != 0xEA) &&
                               !(mnemonic == Mnemonic.SBC && op == 0xEB);
                table[op] = new OpcodeInfo(mnemonic, mode, CycleTable[op], official);
            }
        }

        return table;
    }
}
