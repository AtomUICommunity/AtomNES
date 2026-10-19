namespace AtomNES.Device.Bus;

/// <summary>
/// CPU 视角下的 16 位地址总线。
/// <para>每一次 <see cref="Read"/> / <see cref="Write"/> 恰好对应一个 CPU 周期，总线在其中推进 PPU/APU。</para>
/// </summary>
public interface ICpuBus
{
    byte Read(ushort address);

    void Write(ushort address, byte value);

    /// <summary>无副作用读取，供调试器/反汇编使用。</summary>
    byte Peek(ushort address);

    /// <summary>NMI 线电平（CPU 自行做边沿检测）。</summary>
    bool NmiLine { get; }

    /// <summary>IRQ 线电平（电平触发）。</summary>
    bool IrqLine { get; }

    /// <summary>是否有待执行的 OAM DMA（$4014 写入后，在下一次 CPU 读周期开始）。</summary>
    bool OamDmaPending { get; }

    /// <summary>取出 OAM DMA 请求，返回源页号。</summary>
    byte TakeOamDma();
}

/// <summary>
/// 挂接在 CPU 总线上的 PPU（$2000-$3FFF，寄存器每 8 字节镜像）。
/// </summary>
public interface IPpuDevice
{
    void PowerOn();

    void Reset();

    void ConnectCartridge(ICartridgeDevice? cartridge);

    /// <summary>推进一个 PPU 点（dot）。</summary>
    void Clock();

    byte ReadRegister(int register);

    byte PeekRegister(int register);

    void WriteRegister(int register, byte value);

    /// <summary>NMI 输出电平 = VBlank 标志 &amp;&amp; PPUCTRL.7。</summary>
    bool NmiLine { get; }

    /// <summary>进入 VBlank 时由 PPU 置位，由宿主清除。</summary>
    bool FrameComplete { get; set; }
}

/// <summary>
/// 挂接在 CPU 总线上的 APU（$4000-$4013、$4015、$4017）。
/// </summary>
public interface IApuDevice
{
    void Reset();

    /// <summary>按 CPU 时钟驱动。</summary>
    void Clock();

    byte ReadStatus();

    void WriteRegister(ushort address, byte value);

    /// <summary>帧计数器 / DMC 的 IRQ 电平。</summary>
    bool IrqActive { get; }
}

/// <summary>
/// 卡带：CPU 侧 $4020-$FFFF，PPU 侧 $0000-$3EFF（CHR + 名称表镜像）。
/// </summary>
public interface ICartridgeDevice
{
    void Reset();

    bool TryCpuRead(ushort address, out byte value);

    bool TryCpuWrite(ushort address, byte value);

    /// <summary>PPU 读取 $0000-$3EFF。Mapper 可在此观察地址线（如 MMC3 的 A12）。</summary>
    byte PpuRead(ushort address);

    void PpuWrite(ushort address, byte value);

    /// <summary>无副作用读取 PPU 地址空间，供调试器使用（不触发 A12 等地址线通知）。</summary>
    byte PpuPeek(ushort address);

    /// <summary>Mapper IRQ 电平（例如 MMC3 扫描线计数器）。</summary>
    bool IrqActive { get; }

    /// <summary>每个 CPU 周期（M2）调用一次。</summary>
    void CpuClock();

    /// <summary>PPU 在非渲染期间把 v 放到地址总线（$2006/$2007），供 A12 监视。</summary>
    void NotifyPpuAddress(ushort address);
}
