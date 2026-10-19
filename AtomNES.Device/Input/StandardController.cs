namespace AtomNES.Device.Input;

[Flags]
public enum NesButtons : byte
{
    None = 0,
    A = 0x80,
    B = 0x40,
    Select = 0x20,
    Start = 0x10,
    Up = 0x08,
    Down = 0x04,
    Left = 0x02,
    Right = 0x01
}

/// <summary>
/// 标准 NES 手柄（4021 移位寄存器）。
/// </summary>
public sealed class StandardController
{
    private volatile byte _buttons;
    private byte _shift;
    private bool _strobe;

    /// <summary>当前按键状态，可由 UI 线程写入。</summary>
    public NesButtons Buttons
    {
        get => (NesButtons)_buttons;
        set => _buttons = (byte)value;
    }

    public void SetButton(NesButtons button, bool pressed)
    {
        Buttons = pressed ? Buttons | button : Buttons & ~button;
    }

    public void WriteStrobe(byte value)
    {
        _strobe = (value & 0x01) != 0;
        if (_strobe)
        {
            _shift = _buttons;
        }
    }

    /// <summary>返回 bit0 为数据位；读完 8 位后官方手柄恒返回 1。</summary>
    public byte Read()
    {
        if (_strobe)
        {
            return (byte)(_buttons >> 7);
        }

        var bit = (byte)(_shift >> 7);
        _shift = (byte)((_shift << 1) | 0x01);
        return bit;
    }

    public byte Peek() => (byte)((_strobe ? _buttons : _shift) >> 7);

    public void Reset()
    {
        _shift = 0;
        _strobe = false;
    }
}
