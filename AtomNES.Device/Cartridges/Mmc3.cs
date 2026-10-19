namespace AtomNES.Device.Cartridges;

/// <summary>
/// Mapper 4 (MMC3 / TxROM)。
/// <list type="bullet">
/// <item>IRQ 计数器由 PPU A12 上升沿驱动，A12 需保持低电平至少 3 个 M2 周期才会被计数（滤除背景取图期间的抖动）。</item>
/// <item>采用 Sharp MMC3 行为：计数器为 0 或请求重载时装入锁存值，装入/递减后为 0 且使能则触发 IRQ。</item>
/// </list>
/// </summary>
public class Mmc3(RomImage rom) : Mapper(rom)
{
    protected readonly int[] Registers = new int[8];
    protected int BankSelect;

    private int _irqLatch;
    private int _irqCounter;
    private bool _irqReload;
    private bool _irqEnabled;
    private bool _irqPending;
    private bool _a12;
    private long _a12LowSince;
    private bool _ramEnabled = true;
    private bool _ramWritable = true;

    public override bool IrqActive => _irqPending;

    protected override bool PrgRamEnabled => _ramEnabled;

    protected override bool PrgRamWritable => _ramWritable;

    public override void Reset()
    {
        BankSelect = 0;
        Registers[0] = 0;
        Registers[1] = 2;
        Registers[2] = 4;
        Registers[3] = 5;
        Registers[4] = 6;
        Registers[5] = 7;
        Registers[6] = 0;
        Registers[7] = 1;
        _irqPending = false;
        _irqEnabled = false;
        UpdateBanks();
    }

    protected override void WriteRegister(ushort address, byte value)
    {
        var even = (address & 1) == 0;
        switch (address & 0xE000)
        {
            case 0x8000:
                if (even) BankSelect = value;
                else Registers[BankSelect & 7] = value;
                UpdateBanks();
                break;
            case 0xA000:
                if (even) WriteMirroring(value);
                else
                {
                    _ramEnabled = (value & 0x80) != 0;
                    _ramWritable = (value & 0x40) == 0;
                }

                break;
            case 0xC000:
                if (even) _irqLatch = value;
                else
                {
                    _irqCounter = 0;
                    _irqReload = true;
                }

                break;
            default:
                if (even)
                {
                    _irqEnabled = false;
                    _irqPending = false;
                }
                else
                {
                    _irqEnabled = true;
                }

                break;
        }
    }

    protected virtual void WriteMirroring(byte value) =>
        SetMirroring((value & 1) != 0 ? Mirroring.Horizontal : Mirroring.Vertical);

    protected virtual void UpdateBanks()
    {
        if ((BankSelect & 0x40) == 0)
        {
            SetPrg8K(0, Registers[6]);
            SetPrg8K(2, -2);
        }
        else
        {
            SetPrg8K(0, -2);
            SetPrg8K(2, Registers[6]);
        }

        SetPrg8K(1, Registers[7]);
        SetPrg8K(3, -1);

        var inv = (BankSelect & 0x80) != 0 ? 4 : 0;
        SetChrBank(0 ^ inv, Registers[0] & 0xFE);
        SetChrBank(1 ^ inv, Registers[0] | 1);
        SetChrBank(2 ^ inv, Registers[1] & 0xFE);
        SetChrBank(3 ^ inv, Registers[1] | 1);
        SetChrBank(4 ^ inv, Registers[2]);
        SetChrBank(5 ^ inv, Registers[3]);
        SetChrBank(6 ^ inv, Registers[4]);
        SetChrBank(7 ^ inv, Registers[5]);
    }

    /// <summary>设置 1KB CHR 槽，派生类可改写（例如 Mapper 74 的 CHR RAM 页）。</summary>
    protected virtual void SetChrBank(int slot, int bank) => SetChr1K(slot, bank);

    protected override void OnPpuAddress(ushort address)
    {
        var a12 = (address & 0x1000) != 0;
        if (a12 && !_a12)
        {
            if (M2Cycles - _a12LowSince > 3)
            {
                ClockIrq();
            }
        }
        else if (!a12 && _a12)
        {
            _a12LowSince = M2Cycles;
        }

        _a12 = a12;
    }

    private void ClockIrq()
    {
        if (_irqCounter == 0 || _irqReload)
        {
            _irqCounter = _irqLatch;
            _irqReload = false;
        }
        else
        {
            _irqCounter--;
        }

        if (_irqCounter == 0 && _irqEnabled)
        {
            _irqPending = true;
        }
    }
}
