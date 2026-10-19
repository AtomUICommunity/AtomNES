namespace AtomNES.Device.Cartridges;

/// <summary>
/// Mapper 1 (MMC1B / SxROM)：5 位串行移位寄存器。
/// <list type="bullet">
/// <item>同一指令连续周期的写入（RMW 双写）只有第一次生效。</item>
/// <item>CHR 寄存器高位在 SNROM/SOROM/SUROM/SXROM 上用作 WRAM 禁用、WRAM 分页和 PRG 256KB 外层分页。</item>
/// </list>
/// </summary>
public sealed class Mmc1(RomImage rom) : Mapper(rom)
{
    private int _shift;
    private int _count;
    private int _control;
    private int _chr0;
    private int _chr1;
    private int _prg;
    private long _lastWriteCycle = -10;
    private bool _ramEnabled = true;

    public override void Reset()
    {
        _shift = 0;
        _count = 0;
        _control |= 0x0C;
        Update();
    }

    protected override bool PrgRamEnabled => _ramEnabled;

    protected override void WriteRegister(ushort address, byte value)
    {
        var consecutive = M2Cycles - _lastWriteCycle <= 1;
        _lastWriteCycle = M2Cycles;
        if (consecutive) return;

        if ((value & 0x80) != 0)
        {
            _shift = 0;
            _count = 0;
            _control |= 0x0C;
            Update();
            return;
        }

        _shift |= (value & 1) << _count;
        if (++_count < 5) return;

        var data = _shift;
        _shift = 0;
        _count = 0;
        switch ((address >> 13) & 3)
        {
            case 0: _control = data; break;
            case 1: _chr0 = data; break;
            case 2: _chr1 = data; break;
            case 3: _prg = data; break;
        }

        Update();
    }

    private void Update()
    {
        SetMirroring((_control & 3) switch
        {
            0 => Mirroring.SingleScreenLow,
            1 => Mirroring.SingleScreenHigh,
            2 => Mirroring.Vertical,
            _ => Mirroring.Horizontal
        });

        if ((_control & 0x10) == 0)
        {
            SetChr4K(0, _chr0 & 0x1E);
            SetChr4K(1, _chr0 | 1);
        }
        else
        {
            SetChr4K(0, _chr0);
            SetChr4K(1, _chr1);
        }

        // 扩展位取自 CHR0（8KB CHR 模式下唯一有效的 CHR 寄存器）
        var ext = _chr0;
        var outer = Prg.Length > 0x40000 ? ext & 0x10 : 0;

        var ramBanks = PrgRam.Length / 0x2000;
        PrgRamOffset = ramBanks switch
        {
            >= 4 => ((ext >> 2) & 3) * 0x2000,
            2 => ((ext >> 3) & 1) * 0x2000,
            _ => 0
        };

        var bank = (_prg & 0x0F) | outer;
        switch ((_control >> 2) & 3)
        {
            case 0:
            case 1:
                SetPrg16K(0, bank & ~1);
                SetPrg16K(1, bank | 1);
                break;
            case 2:
                SetPrg16K(0, outer);
                SetPrg16K(1, bank);
                break;
            default:
                SetPrg16K(0, bank);
                SetPrg16K(1, outer | 0x0F);
                break;
        }

        // SNROM：CHR RAM 且 PRG ≤ 256KB 的板子用 CHR 位 4 作为额外的 WRAM 使能
        var snrom = Rom.HasChrRam && Prg.Length <= 0x40000 && ramBanks == 1;
        _ramEnabled = (_prg & 0x10) == 0 && !(snrom && (ext & 0x10) != 0);
    }
}
