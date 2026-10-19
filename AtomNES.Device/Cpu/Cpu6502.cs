using System.Runtime.CompilerServices;
using AtomNES.Device.Bus;

namespace AtomNES.Device.Cpu;

[Flags]
public enum CpuFlags : byte
{
    None = 0,
    Carry = 0x01,
    Zero = 0x02,
    InterruptDisable = 0x04,
    Decimal = 0x08,
    Break = 0x10,
    Unused = 0x20,
    Overflow = 0x40,
    Negative = 0x80
}

/// <summary>
/// Ricoh 2A03 CPU 内核（6502，无十进制模式），周期精确。
/// <para>
/// 每一次总线读/写恰好是一个 CPU 周期，包含真实硬件上的全部“哑读/哑写”
/// （跨页修正读、RMW 回写、栈操作空读等），因此 PPU/APU 与 CPU 在周期级同步，
/// 读 $2002/$2007 等有副作用的寄存器时机与真机一致。
/// </para>
/// <para>
/// 中断：NMI 边沿检测、IRQ 电平检测均在每周期末进行，并在指令最后一个周期之前采样
/// （即 “倒数第二周期轮询”），从而自然复现 CLI/SEI/PLP 延迟一条指令、
/// 分支指令延迟中断、BRK/NMI 劫持等真机行为。
/// </para>
/// </summary>
public sealed class Cpu6502
{
    private const ushort StackBase = 0x0100;
    private const ushort NmiVector = 0xFFFA;
    private const ushort ResetVector = 0xFFFC;
    private const ushort IrqVector = 0xFFFE;

    private const byte FlagC = 0x01;
    private const byte FlagZ = 0x02;
    private const byte FlagI = 0x04;
    private const byte FlagD = 0x08;
    private const byte FlagB = 0x10;
    private const byte FlagU = 0x20;
    private const byte FlagV = 0x40;
    private const byte FlagN = 0x80;

    private readonly ICpuBus _bus;

    public byte A;
    public byte X;
    public byte Y;
    public byte SP;
    public ushort PC;
    public byte P;

    private long _cycles;

    // 中断流水线（与真机一致：在每个周期末更新，在指令边界使用“上一周期”的结果）
    private bool _prevNmiLine;
    private bool _needNmi;
    private bool _prevNeedNmi;
    private bool _runIrq;
    private bool _prevRunIrq;

    private bool _inDma;

    public Cpu6502(ICpuBus bus)
    {
        _bus = bus;
    }

    /// <summary>上电以来累计的 CPU 周期数。</summary>
    public long TotalCycles => _cycles;

    /// <summary>执行到 JAM/KIL 指令后 CPU 停机，直到 Reset。</summary>
    public bool Jammed { get; private set; }

    public CpuFlags Flags
    {
        get => (CpuFlags)P;
        set => P = (byte)value;
    }

    public void PowerOn()
    {
        A = X = Y = 0;
        SP = 0x00;
        P = FlagU | FlagI;
        _cycles = 0;
        Reset();
    }

    /// <summary>RESET 序列：7 个周期，其中 3 次压栈被抑制为读。</summary>
    public void Reset()
    {
        _needNmi = _prevNeedNmi = _runIrq = _prevRunIrq = false;
        _prevNmiLine = _bus.NmiLine;
        Jammed = false;

        DummyRead(PC);
        DummyRead(PC);
        DummyRead((ushort)(StackBase | SP--));
        DummyRead((ushort)(StackBase | SP--));
        DummyRead((ushort)(StackBase | SP--));
        P |= FlagI | FlagU;
        var lo = Read(ResetVector);
        var hi = Read(ResetVector + 1);
        PC = (ushort)(lo | (hi << 8));
    }

    /// <summary>执行一条指令（必要时紧接着响应中断），返回消耗的 CPU 周期数。</summary>
    public int Step()
    {
        var start = _cycles;
        if (Jammed)
        {
            DummyRead(0xFFFF);
            return (int)(_cycles - start);
        }

        var opcode = Read(PC++);
        Execute(opcode);

        if (_prevRunIrq || _prevNeedNmi)
        {
            HardwareInterrupt();
        }

        return (int)(_cycles - start);
    }

    #region Bus cycle

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private byte Read(ushort address)
    {
        if (_bus.OamDmaPending && !_inDma)
        {
            RunOamDma(address);
        }

        var value = _bus.Read(address);
        EndCycle();
        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void DummyRead(ushort address) => Read(address);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Write(ushort address, byte value)
    {
        _bus.Write(address, value);
        EndCycle();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EndCycle()
    {
        _cycles++;

        _prevNeedNmi = _needNmi;
        var nmi = _bus.NmiLine;
        if (nmi && !_prevNmiLine)
        {
            _needNmi = true;
        }

        _prevNmiLine = nmi;

        _prevRunIrq = _runIrq;
        _runIrq = _bus.IrqLine && (P & FlagI) == 0;
    }

    /// <summary>
    /// OAM DMA：在 CPU 读周期被挂起。1 个停顿周期 + （奇周期时）1 个对齐周期 + 256 次读/写。
    /// </summary>
    private void RunOamDma(ushort haltAddress)
    {
        _inDma = true;
        var page = _bus.TakeOamDma();

        DummyRead(haltAddress);
        if ((_cycles & 1) != 0)
        {
            DummyRead(haltAddress);
        }

        var baseAddress = (ushort)(page << 8);
        for (var i = 0; i < 256; i++)
        {
            var value = Read((ushort)(baseAddress | i));
            Write(0x2004, value);
        }

        _inDma = false;
    }

    #endregion

    #region Helpers

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Push(byte value) => Write((ushort)(StackBase | SP--), value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private byte Pop() => Read((ushort)(StackBase | ++SP));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetZN(byte value)
    {
        P = (byte)((P & 0x7D) | (value & 0x80) | (value == 0 ? FlagZ : 0));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetFlag(byte flag, bool value)
    {
        if (value) P |= flag;
        else P &= (byte)~flag;
    }

    private void HardwareInterrupt()
    {
        DummyRead(PC);
        DummyRead(PC);
        Push((byte)(PC >> 8));
        Push((byte)PC);

        if (_needNmi)
        {
            _needNmi = false;
            Push((byte)(P | FlagU));
            P |= FlagI;
            PC = ReadVector(NmiVector);
        }
        else
        {
            Push((byte)(P | FlagU));
            P |= FlagI;
            PC = ReadVector(IrqVector);
        }
    }

    private ushort ReadVector(ushort vector)
    {
        var lo = Read(vector);
        var hi = Read((ushort)(vector + 1));
        return (ushort)(lo | (hi << 8));
    }

    private void Brk()
    {
        Read(PC++);
        Push((byte)(PC >> 8));
        Push((byte)PC);
        var flags = (byte)(P | FlagB | FlagU);

        // NMI 劫持：若在 BRK 压栈期间产生 NMI，则跳转到 NMI 向量
        if (_needNmi)
        {
            _needNmi = false;
            Push(flags);
            P |= FlagI;
            PC = ReadVector(NmiVector);
        }
        else
        {
            Push(flags);
            P |= FlagI;
            PC = ReadVector(IrqVector);
        }

        _prevNeedNmi = false;
    }

    #endregion

    #region Addressing

    private ushort FetchAbs()
    {
        var lo = Read(PC++);
        var hi = Read(PC++);
        return (ushort)(lo | (hi << 8));
    }

    /// <summary>
    /// 计算有效地址并产生全部寻址周期。<paramref name="isRead"/> 为 false（写 / 读改写）时，
    /// 索引寻址总会产生一次“未修正高字节”的哑读。
    /// </summary>
    private ushort GetAddress(AddressingMode mode, bool isRead)
    {
        switch (mode)
        {
            case AddressingMode.Imm:
                return PC++;
            case AddressingMode.Zp0:
                return Read(PC++);
            case AddressingMode.Zpx:
            {
                var b = Read(PC++);
                DummyRead(b);
                return (byte)(b + X);
            }
            case AddressingMode.Zpy:
            {
                var b = Read(PC++);
                DummyRead(b);
                return (byte)(b + Y);
            }
            case AddressingMode.Abs:
                return FetchAbs();
            case AddressingMode.Abx:
                return Indexed(FetchAbs(), X, isRead);
            case AddressingMode.Aby:
                return Indexed(FetchAbs(), Y, isRead);
            case AddressingMode.Izx:
            {
                var t = Read(PC++);
                DummyRead(t);
                t += X;
                var lo = Read(t);
                var hi = Read((byte)(t + 1));
                return (ushort)(lo | (hi << 8));
            }
            case AddressingMode.Izy:
            {
                var t = Read(PC++);
                var lo = Read(t);
                var hi = Read((byte)(t + 1));
                return Indexed((ushort)(lo | (hi << 8)), Y, isRead);
            }
            default:
                throw new InvalidOperationException($"Unexpected addressing mode {mode}");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ushort Indexed(ushort baseAddress, byte index, bool isRead)
    {
        var address = (ushort)(baseAddress + index);
        var crossed = ((address ^ baseAddress) & 0xFF00) != 0;
        if (crossed || !isRead)
        {
            DummyRead((ushort)((baseAddress & 0xFF00) | (address & 0x00FF)));
        }

        return address;
    }

    #endregion

    #region ALU

    private void Adc(byte value)
    {
        var sum = A + value + (P & FlagC);
        SetFlag(FlagC, sum > 0xFF);
        SetFlag(FlagV, (~(A ^ value) & (A ^ sum) & 0x80) != 0);
        A = (byte)sum;
        SetZN(A);
    }

    private void Compare(byte register, byte value)
    {
        SetFlag(FlagC, register >= value);
        SetZN((byte)(register - value));
    }

    private byte Asl(byte v)
    {
        SetFlag(FlagC, (v & 0x80) != 0);
        v <<= 1;
        SetZN(v);
        return v;
    }

    private byte Lsr(byte v)
    {
        SetFlag(FlagC, (v & 0x01) != 0);
        v >>= 1;
        SetZN(v);
        return v;
    }

    private byte Rol(byte v)
    {
        var c = P & FlagC;
        SetFlag(FlagC, (v & 0x80) != 0);
        v = (byte)((v << 1) | c);
        SetZN(v);
        return v;
    }

    private byte Ror(byte v)
    {
        var c = (P & FlagC) << 7;
        SetFlag(FlagC, (v & 0x01) != 0);
        v = (byte)((v >> 1) | c);
        SetZN(v);
        return v;
    }

    #endregion

    #region Execute

    private void Branch(bool condition)
    {
        var offset = (sbyte)Read(PC++);
        if (!condition)
        {
            return;
        }

        // 分支成功且未跨页时，真机在此周期不会重新采样 IRQ（中断被延迟一条指令）
        if (_runIrq && !_prevRunIrq)
        {
            _runIrq = false;
        }

        DummyRead(PC);
        var target = (ushort)(PC + offset);
        if (((PC ^ target) & 0xFF00) != 0)
        {
            DummyRead((ushort)((PC & 0xFF00) | (target & 0x00FF)));
        }

        PC = target;
    }

    private void ReadOp(AddressingMode mode, Mnemonic m)
    {
        var v = Read(GetAddress(mode, true));
        switch (m)
        {
            case Mnemonic.LDA: A = v; SetZN(A); break;
            case Mnemonic.LDX: X = v; SetZN(X); break;
            case Mnemonic.LDY: Y = v; SetZN(Y); break;
            case Mnemonic.AND: A &= v; SetZN(A); break;
            case Mnemonic.ORA: A |= v; SetZN(A); break;
            case Mnemonic.EOR: A ^= v; SetZN(A); break;
            case Mnemonic.ADC: Adc(v); break;
            case Mnemonic.SBC: Adc((byte)~v); break;
            case Mnemonic.CMP: Compare(A, v); break;
            case Mnemonic.CPX: Compare(X, v); break;
            case Mnemonic.CPY: Compare(Y, v); break;
            case Mnemonic.BIT:
                P = (byte)((P & 0x3D) | (v & 0xC0) | ((A & v) == 0 ? FlagZ : 0));
                break;
            case Mnemonic.NOP: break;
            case Mnemonic.LAX: A = X = v; SetZN(A); break;
            case Mnemonic.LAS: A = X = SP = (byte)(v & SP); SetZN(A); break;
            case Mnemonic.ANC: A &= v; SetZN(A); SetFlag(FlagC, (A & 0x80) != 0); break;
            case Mnemonic.ALR: A = Lsr((byte)(A & v)); break;
            case Mnemonic.ARR:
                A &= v;
                A = (byte)((A >> 1) | ((P & FlagC) << 7));
                SetZN(A);
                SetFlag(FlagC, (A & 0x40) != 0);
                SetFlag(FlagV, (((A >> 6) ^ (A >> 5)) & 0x01) != 0);
                break;
            case Mnemonic.AXS:
            {
                var ax = (byte)(A & X);
                SetFlag(FlagC, ax >= v);
                X = (byte)(ax - v);
                SetZN(X);
                break;
            }
            case Mnemonic.XAA: A = (byte)((A | 0xEE) & X & v); SetZN(A); break;
            case Mnemonic.LXA: A = X = (byte)((A | 0xEE) & v); SetZN(A); break;
        }
    }

    private void WriteOp(AddressingMode mode, Mnemonic m)
    {
        switch (m)
        {
            case Mnemonic.STA: Write(GetAddress(mode, false), A); return;
            case Mnemonic.STX: Write(GetAddress(mode, false), X); return;
            case Mnemonic.STY: Write(GetAddress(mode, false), Y); return;
            case Mnemonic.SAX: Write(GetAddress(mode, false), (byte)(A & X)); return;
        }

        // SHA/SHX/SHY/TAS：写入值与 (基址高字节 + 1) 相与；跨页时目标高字节被该值替换
        ushort baseAddress;
        byte index;
        if (mode == AddressingMode.Izy)
        {
            var t = Read(PC++);
            var lo = Read(t);
            var hi = Read((byte)(t + 1));
            baseAddress = (ushort)(lo | (hi << 8));
            index = Y;
        }
        else
        {
            baseAddress = FetchAbs();
            index = mode == AddressingMode.Abx ? X : Y;
        }

        var address = (ushort)(baseAddress + index);
        DummyRead((ushort)((baseAddress & 0xFF00) | (address & 0x00FF)));

        var source = m switch
        {
            Mnemonic.SHX => X,
            Mnemonic.SHY => Y,
            Mnemonic.TAS => SP = (byte)(A & X),
            _ => (byte)(A & X)
        };
        var value = (byte)(source & ((baseAddress >> 8) + 1));
        if (((address ^ baseAddress) & 0xFF00) != 0)
        {
            address = (ushort)((address & 0x00FF) | (value << 8));
        }

        Write(address, value);
    }

    private void ReadModifyWriteOp(AddressingMode mode, Mnemonic m)
    {
        if (mode == AddressingMode.Acc)
        {
            DummyRead(PC);
            A = m switch
            {
                Mnemonic.ASL => Asl(A),
                Mnemonic.LSR => Lsr(A),
                Mnemonic.ROL => Rol(A),
                _ => Ror(A)
            };
            return;
        }

        var address = GetAddress(mode, false);
        var v = Read(address);
        Write(address, v);
        switch (m)
        {
            case Mnemonic.ASL: v = Asl(v); break;
            case Mnemonic.LSR: v = Lsr(v); break;
            case Mnemonic.ROL: v = Rol(v); break;
            case Mnemonic.ROR: v = Ror(v); break;
            case Mnemonic.INC: v++; SetZN(v); break;
            case Mnemonic.DEC: v--; SetZN(v); break;
            case Mnemonic.SLO: v = Asl(v); A |= v; SetZN(A); break;
            case Mnemonic.RLA: v = Rol(v); A &= v; SetZN(A); break;
            case Mnemonic.SRE: v = Lsr(v); A ^= v; SetZN(A); break;
            case Mnemonic.RRA: v = Ror(v); Adc(v); break;
            case Mnemonic.DCP: v--; Compare(A, v); break;
            case Mnemonic.ISB: v++; Adc((byte)~v); break;
        }

        Write(address, v);
    }

    private void Execute(byte opcode)
    {
        var info = OpcodeTable.Get(opcode);
        var mode = info.Mode;
        var m = info.Mnemonic;

        switch (m)
        {
            // ---------- 分支 ----------
            case Mnemonic.BCC: Branch((P & FlagC) == 0); return;
            case Mnemonic.BCS: Branch((P & FlagC) != 0); return;
            case Mnemonic.BNE: Branch((P & FlagZ) == 0); return;
            case Mnemonic.BEQ: Branch((P & FlagZ) != 0); return;
            case Mnemonic.BPL: Branch((P & FlagN) == 0); return;
            case Mnemonic.BMI: Branch((P & FlagN) != 0); return;
            case Mnemonic.BVC: Branch((P & FlagV) == 0); return;
            case Mnemonic.BVS: Branch((P & FlagV) != 0); return;

            // ---------- 跳转 / 子程序 ----------
            case Mnemonic.JMP:
                if (mode == AddressingMode.Abs)
                {
                    PC = FetchAbs();
                }
                else
                {
                    // JMP ($xxFF) 页内回绕 bug
                    var ptr = FetchAbs();
                    var lo = Read(ptr);
                    var hi = Read((ushort)((ptr & 0xFF00) | ((ptr + 1) & 0x00FF)));
                    PC = (ushort)(lo | (hi << 8));
                }

                return;
            case Mnemonic.JSR:
            {
                var lo = Read(PC++);
                DummyRead((ushort)(StackBase | SP));
                Push((byte)(PC >> 8));
                Push((byte)PC);
                var hi = Read(PC);
                PC = (ushort)(lo | (hi << 8));
                return;
            }
            case Mnemonic.RTS:
            {
                DummyRead(PC);
                DummyRead((ushort)(StackBase | SP));
                var lo = Pop();
                var hi = Pop();
                PC = (ushort)(lo | (hi << 8));
                DummyRead(PC++);
                return;
            }
            case Mnemonic.RTI:
            {
                DummyRead(PC);
                DummyRead((ushort)(StackBase | SP));
                P = (byte)((Pop() & ~FlagB) | FlagU);
                var lo = Pop();
                var hi = Pop();
                PC = (ushort)(lo | (hi << 8));
                return;
            }
            case Mnemonic.BRK:
                Brk();
                return;

            // ---------- 栈 ----------
            case Mnemonic.PHA: DummyRead(PC); Push(A); return;
            case Mnemonic.PHP: DummyRead(PC); Push((byte)(P | FlagB | FlagU)); return;
            case Mnemonic.PLA:
                DummyRead(PC);
                DummyRead((ushort)(StackBase | SP));
                A = Pop();
                SetZN(A);
                return;
            case Mnemonic.PLP:
                DummyRead(PC);
                DummyRead((ushort)(StackBase | SP));
                P = (byte)((Pop() & ~FlagB) | FlagU);
                return;

            // ---------- 隐含寻址 ----------
            case Mnemonic.TAX: DummyRead(PC); X = A; SetZN(X); return;
            case Mnemonic.TAY: DummyRead(PC); Y = A; SetZN(Y); return;
            case Mnemonic.TXA: DummyRead(PC); A = X; SetZN(A); return;
            case Mnemonic.TYA: DummyRead(PC); A = Y; SetZN(A); return;
            case Mnemonic.TSX: DummyRead(PC); X = SP; SetZN(X); return;
            case Mnemonic.TXS: DummyRead(PC); SP = X; return;
            case Mnemonic.INX: DummyRead(PC); X++; SetZN(X); return;
            case Mnemonic.INY: DummyRead(PC); Y++; SetZN(Y); return;
            case Mnemonic.DEX: DummyRead(PC); X--; SetZN(X); return;
            case Mnemonic.DEY: DummyRead(PC); Y--; SetZN(Y); return;
            case Mnemonic.CLC: DummyRead(PC); P &= unchecked((byte)~FlagC); return;
            case Mnemonic.SEC: DummyRead(PC); P |= FlagC; return;
            case Mnemonic.CLI: DummyRead(PC); P &= unchecked((byte)~FlagI); return;
            case Mnemonic.SEI: DummyRead(PC); P |= FlagI; return;
            case Mnemonic.CLD: DummyRead(PC); P &= unchecked((byte)~FlagD); return;
            case Mnemonic.SED: DummyRead(PC); P |= FlagD; return;
            case Mnemonic.CLV: DummyRead(PC); P &= unchecked((byte)~FlagV); return;

            case Mnemonic.NOP when mode == AddressingMode.Imp:
                DummyRead(PC);
                return;

            case Mnemonic.JAM:
                Jammed = true;
                PC--;
                return;

            // ---------- 写 ----------
            case Mnemonic.STA or Mnemonic.STX or Mnemonic.STY or Mnemonic.SAX
                or Mnemonic.SHA or Mnemonic.SHX or Mnemonic.SHY or Mnemonic.TAS:
                WriteOp(mode, m);
                return;

            // ---------- 读改写 ----------
            case Mnemonic.ASL or Mnemonic.LSR or Mnemonic.ROL or Mnemonic.ROR
                or Mnemonic.INC or Mnemonic.DEC
                or Mnemonic.SLO or Mnemonic.RLA or Mnemonic.SRE or Mnemonic.RRA
                or Mnemonic.DCP or Mnemonic.ISB:
                ReadModifyWriteOp(mode, m);
                return;

            // ---------- 读 ----------
            default:
                ReadOp(mode, m);
                return;
        }
    }

    #endregion
}
