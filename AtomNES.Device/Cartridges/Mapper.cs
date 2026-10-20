using System.Runtime.CompilerServices;
using AtomNES.Device.Bus;

namespace AtomNES.Device.Cartridges;

/// <summary>
/// Mapper 基类：以 1KB（CHR）/ 8KB（PRG）为粒度的页表映射，
/// 名称表通过 4 个 1KB 页指向 CIRAM 或卡带 RAM。热路径只做一次数组查表。
/// </summary>
public abstract class Mapper : ICartridgeDevice
{
    protected readonly RomImage Rom;
    protected readonly byte[] Prg;
    protected readonly byte[] Chr;
    protected readonly byte[] PrgRam;
    protected readonly byte[] Ciram;

    private readonly int[] _prgPages = new int[4];
    private readonly int[] _chrPages = new int[8];
    private readonly int[] _ntPages = new int[4];

    protected Mapper(RomImage rom, int extraChrRam = 0)
    {
        Rom = rom;
        Prg = rom.Prg;
        if (extraChrRam > 0)
        {
            // 板载 CHR RAM 附加在 CHR ROM 之后，通过 SetChrRam1K 映射
            ChrRomSize = rom.Chr.Length;
            Chr = new byte[rom.Chr.Length + extraChrRam];
            rom.Chr.CopyTo(Chr, 0);
        }
        else
        {
            ChrRomSize = rom.Chr.Length;
            Chr = rom.Chr;
        }

        PrgRam = new byte[Math.Max(rom.PrgRamSize, 0x2000)];
        Ciram = new byte[rom.Mirroring == Mirroring.FourScreen ? 0x1000 : 0x800];
        Array.Fill(_chrWritable, rom.HasChrRam);
        SetMirroring(rom.Mirroring);
    }

    /// <summary>CHR ROM（或卡带自带 CHR RAM）部分的大小，不含附加 CHR RAM。</summary>
    protected readonly int ChrRomSize;

    private readonly bool[] _chrWritable = new bool[8];

    /// <summary>带电池的卡带返回需要持久化的 PRG RAM，否则为 null。</summary>
    public byte[]? BatteryRam => Rom.HasBattery ? PrgRam : null;

    /// <summary>卡带 IRQ 线电平，供带 CPU 周期计数器的 Mapper 使用。</summary>
    protected bool IrqAsserted;

    public virtual bool IrqActive => IrqAsserted;

    /// <summary>直接读取 PRG ROM 中第 bank 个 8KB 页（用于 $6000 窗口映射 ROM 等场景）。</summary>
    protected byte ReadPrg8K(int bank, int offset)
    {
        var count = PrgBankCount8K;
        bank = ((bank % count) + count) % count;
        return Prg[bank * 0x2000 + (offset & 0x1FFF)];
    }

    public virtual void Reset()
    {
    }

    #region 页映射

    protected int PrgBankCount8K => Math.Max(1, Prg.Length / 0x2000);

    protected int ChrBankCount1K => Math.Max(1, ChrRomSize / 0x400);

    /// <summary>映射 8KB PRG 页到 $8000 + slot*$2000。负数 bank 从末尾倒数。</summary>
    protected void SetPrg8K(int slot, int bank)
    {
        var count = PrgBankCount8K;
        bank = ((bank % count) + count) % count;
        _prgPages[slot] = bank * 0x2000;
    }

    protected void SetPrg16K(int slot, int bank)
    {
        SetPrg8K(slot * 2, bank * 2);
        SetPrg8K(slot * 2 + 1, bank * 2 + 1);
    }

    protected void SetPrg32K(int bank)
    {
        for (var i = 0; i < 4; i++) SetPrg8K(i, bank * 4 + i);
    }

    protected void SetChr1K(int slot, int bank)
    {
        var count = ChrBankCount1K;
        bank = ((bank % count) + count) % count;
        _chrPages[slot] = bank * 0x400;
        _chrWritable[slot] = Rom.HasChrRam;
    }

    /// <summary>映射附加 CHR RAM 的第 page 个 1KB 页到 slot。</summary>
    protected void SetChrRam1K(int slot, int page)
    {
        var count = Math.Max(1, (Chr.Length - ChrRomSize) / 0x400);
        _chrPages[slot] = ChrRomSize + (page % count) * 0x400;
        _chrWritable[slot] = true;
    }

    protected void SetChr4K(int slot, int bank)
    {
        for (var i = 0; i < 4; i++) SetChr1K(slot * 4 + i, bank * 4 + i);
    }

    protected void SetChr8K(int bank)
    {
        for (var i = 0; i < 8; i++) SetChr1K(i, bank * 8 + i);
    }

    protected void SetMirroring(Mirroring mirroring)
    {
        if (Rom.Mirroring == Mirroring.FourScreen)
        {
            mirroring = Mirroring.FourScreen;
        }

        (_ntPages[0], _ntPages[1], _ntPages[2], _ntPages[3]) = mirroring switch
        {
            Mirroring.Horizontal => (0, 0, 0x400, 0x400),
            Mirroring.Vertical => (0, 0x400, 0, 0x400),
            Mirroring.SingleScreenLow => (0, 0, 0, 0),
            Mirroring.SingleScreenHigh => (0x400, 0x400, 0x400, 0x400),
            _ => (0, 0x400, 0x800, 0xC00)
        };
    }

    /// <summary>单独设置某个名称表槽指向 CIRAM 的第 page 页（0/1）。</summary>
    protected void SetNametable(int slot, int page) => _ntPages[slot] = (page & 1) * 0x400;

    #endregion

    #region CPU

    public virtual bool TryCpuRead(ushort address, out byte value)
    {
        if (address >= 0x8000)
        {
            value = Prg[_prgPages[(address >> 13) & 3] + (address & 0x1FFF)];
            return true;
        }

        if (address >= 0x6000 && HasPrgRam && PrgRamEnabled)
        {
            value = PrgRam[(PrgRamOffset + (address & 0x1FFF)) % PrgRam.Length];
            return true;
        }

        value = 0;
        return false;
    }

    public virtual bool TryCpuWrite(ushort address, byte value)
    {
        if (address >= 0x8000)
        {
            WriteRegister(address, value);
            return true;
        }

        if (address >= 0x6000)
        {
            if (HasPrgRam && PrgRamEnabled && PrgRamWritable)
            {
                PrgRam[(PrgRamOffset + (address & 0x1FFF)) % PrgRam.Length] = value;
            }

            return WriteLowRegister(address, value) || (HasPrgRam && PrgRamEnabled);
        }

        if (address >= 0x4020)
        {
            return WriteLowRegister(address, value);
        }

        return false;
    }

    protected virtual bool PrgRamEnabled => true;

    protected virtual bool PrgRamWritable => true;

    /// <summary>NES 2.0 头明确声明无 PRG RAM 时，$6000-$7FFF 为开路总线。</summary>
    protected bool HasPrgRam => !Rom.IsNes20 || Rom.PrgRamSize > 0;

    /// <summary>PRG RAM 当前 8KB 窗口在 <see cref="PrgRam"/> 中的偏移。</summary>
    protected int PrgRamOffset;

    /// <summary>$4020-$7FFF 的寄存器写入（如 NINA-001），返回是否被处理。</summary>
    protected virtual bool WriteLowRegister(ushort address, byte value) => false;

    /// <summary>CPU 周期计数（M2），供需要按周期计时的 Mapper 使用。</summary>
    protected long M2Cycles;

    public virtual void CpuClock() => M2Cycles++;

    public void NotifyPpuAddress(ushort address) => OnPpuAddress((ushort)(address & 0x3FFF));

    protected virtual void WriteRegister(ushort address, byte value)
    {
    }

    #endregion

    #region PPU

    public virtual byte PpuRead(ushort address)
    {
        address &= 0x3FFF;
        if (address < 0x2000)
        {
            OnPpuAddress(address);
            return Chr[_chrPages[address >> 10] + (address & 0x3FF)];
        }

        OnPpuAddress(address);
        return Ciram[_ntPages[(address >> 10) & 3] + (address & 0x3FF)];
    }

    public virtual byte PpuPeek(ushort address)
    {
        address &= 0x3FFF;
        return address < 0x2000
            ? Chr[_chrPages[address >> 10] + (address & 0x3FF)]
            : Ciram[_ntPages[(address >> 10) & 3] + (address & 0x3FF)];
    }

    public virtual void PpuWrite(ushort address, byte value)
    {
        address &= 0x3FFF;
        if (address < 0x2000)
        {
            OnPpuAddress(address);
            if (_chrWritable[address >> 10])
            {
                Chr[_chrPages[address >> 10] + (address & 0x3FF)] = value;
            }

            return;
        }

        OnPpuAddress(address);
        Ciram[_ntPages[(address >> 10) & 3] + (address & 0x3FF)] = value;
    }

    /// <summary>PPU 地址线变化通知（MMC3 的 A12 计数器等）。</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected virtual void OnPpuAddress(ushort address)
    {
    }

    #endregion
}
