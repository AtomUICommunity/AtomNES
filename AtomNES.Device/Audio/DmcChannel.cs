namespace AtomNES.Device.Audio;

/// <summary>
/// DMC（增量调制）通道。样本字节通过 DMA 从 CPU 总线读取，由 <see cref="Apu2A03"/> 向 CPU 发出请求。
/// </summary>
internal sealed class DmcChannel
{
    private static readonly ushort[] Rates = [428, 380, 340, 320, 286, 254, 226, 214, 190, 160, 142, 128, 106, 84, 72, 54];

    public bool IrqEnabled;
    public bool Irq;
    public int Output;

    private bool _loop;
    private int _rate = Rates[0];
    private int _timer = Rates[0];
    private ushort _sampleAddress = 0xC000;
    private int _sampleLength = 1;

    private ushort _currentAddress;
    public int BytesRemaining;

    private byte _buffer;
    private bool _bufferEmpty = true;

    private byte _shift;
    private int _bitsRemaining = 8;
    private bool _silence = true;

    public void Reset(bool soft)
    {
        IrqEnabled = Irq = _loop = false;
        _rate = _timer = Rates[0];
        if (!soft) Output = 0;
        _sampleAddress = 0xC000;
        _sampleLength = 1;
        _currentAddress = 0;
        BytesRemaining = 0;
        _bufferEmpty = true;
        _shift = 0;
        _bitsRemaining = 8;
        _silence = true;
    }

    public void Write(int reg, byte value)
    {
        switch (reg)
        {
            case 0:
                IrqEnabled = (value & 0x80) != 0;
                _loop = (value & 0x40) != 0;
                _rate = Rates[value & 0x0F];
                if (!IrqEnabled) Irq = false;
                break;
            case 1:
                Output = value & 0x7F;
                break;
            case 2:
                _sampleAddress = (ushort)(0xC000 | (value << 6));
                break;
            case 3:
                _sampleLength = (value << 4) | 1;
                break;
        }
    }

    public void SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            BytesRemaining = 0;
        }
        else if (BytesRemaining == 0)
        {
            Restart();
        }
    }

    private void Restart()
    {
        _currentAddress = _sampleAddress;
        BytesRemaining = _sampleLength;
    }

    /// <summary>需要 DMA 读取下一个样本字节。</summary>
    public bool NeedsDma => _bufferEmpty && BytesRemaining > 0;

    public ushort DmaAddress => _currentAddress;

    public void CompleteDma(byte value)
    {
        _buffer = value;
        _bufferEmpty = false;
        _currentAddress = (ushort)(_currentAddress == 0xFFFF ? 0x8000 : _currentAddress + 1);
        if (--BytesRemaining == 0)
        {
            if (_loop) Restart();
            else if (IrqEnabled) Irq = true;
        }
    }

    /// <summary>CPU 周期驱动。</summary>
    public void ClockTimer()
    {
        if (--_timer > 0) return;
        _timer = _rate;

        if (!_silence)
        {
            if ((_shift & 1) != 0)
            {
                if (Output <= 125) Output += 2;
            }
            else if (Output >= 2)
            {
                Output -= 2;
            }

            _shift >>= 1;
        }

        if (--_bitsRemaining == 0)
        {
            _bitsRemaining = 8;
            if (_bufferEmpty)
            {
                _silence = true;
            }
            else
            {
                _silence = false;
                _shift = _buffer;
                _bufferEmpty = true;
            }
        }
    }
}
