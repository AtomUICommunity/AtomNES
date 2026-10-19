namespace AtomNES.Device.Cartridges;

/// <summary>Mapper 74（外星/汉化 MMC3）：CHR 页 8、9 映射到板载 2KB CHR RAM，用于中文字库。</summary>
public sealed class Mapper74(RomImage rom) : Mmc3(rom, rom.HasChrRam ? 0 : 0x800)
{
    protected override void SetChrBank(int slot, int bank)
    {
        if (!Rom.HasChrRam && (bank & 0xFF) is 8 or 9)
        {
            SetChrRam1K(slot, bank & 1);
        }
        else
        {
            SetChr1K(slot, bank);
        }
    }
}

/// <summary>
/// Mapper 245（外星 MMC3）：CHR 寄存器 0 的 bit1 选择 512KB PRG 外层块；
/// 使用 8KB CHR RAM 时按 CHR 模式整体交换两个 4KB 半区。
/// </summary>
public sealed class Mapper245(RomImage rom) : Mmc3(rom)
{
    protected override void UpdatePrgBanks()
    {
        var outer = (Registers[0] & 0x02) != 0 ? 0x40 : 0;
        var last = PrgBankCount8K >= 0x40 ? 0x3F | outer : PrgBankCount8K - 1;
        var r6 = (Registers[6] & 0x3F) | outer;
        var r7 = (Registers[7] & 0x3F) | outer;
        if ((BankSelect & 0x40) == 0)
        {
            SetPrg8K(0, r6);
            SetPrg8K(2, last - 1);
        }
        else
        {
            SetPrg8K(0, last - 1);
            SetPrg8K(2, r6);
        }

        SetPrg8K(1, r7);
        SetPrg8K(3, last);
    }

    protected override void UpdateChrBanks()
    {
        if (!Rom.HasChrRam)
        {
            base.UpdateChrBanks();
            return;
        }

        var swap = (BankSelect & 0x80) != 0;
        SetChr4K(0, swap ? 1 : 0);
        SetChr4K(1, swap ? 0 : 1);
    }
}

/// <summary>Mapper 249（Waixing 加密 MMC3）：$5000 bit1 开启时对 PRG/CHR 页号做位置换。</summary>
public sealed class Mapper249(RomImage rom) : Mmc3(rom)
{
    private int _exReg;

    public override void Reset()
    {
        _exReg = 0;
        base.Reset();
    }

    protected override bool WriteLowRegister(ushort address, byte value)
    {
        if (address != 0x5000) return false;
        _exReg = value;
        UpdateBanks();
        return true;
    }

    private static int Scramble(int p) =>
        (p & 0x03) | ((p >> 1) & 0x04) | ((p >> 4) & 0x08) | ((p >> 2) & 0x10) | ((p << 3) & 0x20) | ((p << 2) & 0xC0);

    protected override void SetPrgBank(int slot, int bank)
    {
        if (bank < 0) bank += PrgBankCount8K;
        if ((_exReg & 0x02) != 0)
        {
            bank = bank < 0x20
                ? (bank & 0x01) | ((bank >> 3) & 0x02) | ((bank >> 1) & 0x04) | ((bank << 2) & 0x18)
                : Scramble(bank - 0x20);
        }

        SetPrg8K(slot, bank);
    }

    protected override void SetChrBank(int slot, int bank) =>
        SetChr1K(slot, (_exReg & 0x02) != 0 ? Scramble(bank & 0xFF) : bank);
}

/// <summary>Mapper 115（卡圣 MMC3）：$6000 写 PRG 覆盖（16/32KB 模式），$6001 写 CHR 外层位；$5080 为保护寄存器。</summary>
public sealed class Mapper115(RomImage rom) : Mmc3(rom)
{
    private int _prgReg;
    private int _chrReg;
    private byte _protection;

    public override void Reset()
    {
        _prgReg = _chrReg = 0;
        base.Reset();
    }

    public override bool TryCpuRead(ushort address, out byte value)
    {
        if (address is >= 0x5000 and < 0x6000)
        {
            value = _protection;
            return true;
        }

        return base.TryCpuRead(address, out value);
    }

    protected override bool WriteLowRegister(ushort address, byte value)
    {
        if (address < 0x4100) return false;
        if (address == 0x5080)
        {
            _protection = value;
            return true;
        }

        if ((address & 1) != 0) _chrReg = value & 1;
        else _prgReg = value;
        UpdateBanks();
        return true;
    }

    protected override void UpdatePrgBanks()
    {
        base.UpdatePrgBanks();
        if ((_prgReg & 0x80) == 0) return;
        if ((_prgReg & 0x20) != 0)
        {
            SetPrg32K((_prgReg & 0x0F) >> 1);
        }
        else
        {
            SetPrg16K(0, _prgReg & 0x0F);
            SetPrg16K(1, _prgReg & 0x0F);
        }
    }

    protected override void SetChrBank(int slot, int bank) => SetChr1K(slot, bank | (_chrReg << 8));
}

/// <summary>Mapper 112（NTDEC / 亚洲之子）：类 MMC3 寄存器布局但无 IRQ，$C000 为 CHR 外层位。</summary>
public sealed class Mapper112(RomImage rom) : Mapper(rom)
{
    private readonly int[] _regs = new int[8];
    private int _current;
    private int _outer;

    public override void Reset()
    {
        Array.Clear(_regs);
        _current = _outer = 0;
        SetMirroring(Mirroring.Vertical);
        SetPrg8K(2, -2);
        SetPrg8K(3, -1);
        Update();
    }

    protected override bool WriteLowRegister(ushort address, byte value) => address < 0x6000;

    protected override void WriteRegister(ushort address, byte value)
    {
        switch (address & 0xE001)
        {
            case 0x8000: _current = value & 7; break;
            case 0xA000: _regs[_current] = value; break;
            case 0xC000: _outer = value; break;
            case 0xE000: SetMirroring((value & 1) != 0 ? Mirroring.Horizontal : Mirroring.Vertical); break;
        }

        Update();
    }

    private void Update()
    {
        SetPrg8K(0, _regs[0]);
        SetPrg8K(1, _regs[1]);
        SetChr1K(0, _regs[2] & 0xFE);
        SetChr1K(1, _regs[2] | 1);
        SetChr1K(2, _regs[3] & 0xFE);
        SetChr1K(3, _regs[3] | 1);
        SetChr1K(4, _regs[4] | ((_outer & 0x10) << 4));
        SetChr1K(5, _regs[5] | ((_outer & 0x20) << 3));
        SetChr1K(6, _regs[6] | ((_outer & 0x40) << 2));
        SetChr1K(7, _regs[7] | ((_outer & 0x80) << 1));
    }
}

/// <summary>Mapper 241（BxROM 变体，多见于中文 RPG）：$8000-$FFFF 写入选择 32KB PRG，带 8KB PRG RAM。</summary>
public sealed class Mapper241(RomImage rom) : Mapper(rom)
{
    public override void Reset()
    {
        SetPrg32K(0);
        SetChr8K(0);
    }

    protected override void WriteRegister(ushort address, byte value) => SetPrg32K(value);
}

/// <summary>Mapper 15（100-in-1 Contra Function 16）：地址低 2 位选择 4 种 PRG 模式。</summary>
public sealed class Mapper15(RomImage rom) : Mapper(rom)
{
    public override void Reset()
    {
        SetChr8K(0);
        WriteRegister(0x8000, 0);
    }

    protected override void WriteRegister(ushort address, byte value)
    {
        SetMirroring((value & 0x40) != 0 ? Mirroring.Horizontal : Mirroring.Vertical);
        var sub = value >> 7;
        var bank = (value & 0x7F) << 1;
        switch (address & 3)
        {
            case 0:
                for (var i = 0; i < 4; i++) SetPrg8K(i, (bank + i) ^ sub);
                break;
            case 1:
            case 3:
                bank |= sub;
                SetPrg8K(0, bank);
                SetPrg8K(1, bank + 1);
                bank = ((address & 3) == 3 ? bank : bank | 0x0E) | sub;
                SetPrg8K(2, bank);
                SetPrg8K(3, bank + 1);
                break;
            default:
                bank |= sub;
                for (var i = 0; i < 4; i++) SetPrg8K(i, bank);
                break;
        }
    }
}
