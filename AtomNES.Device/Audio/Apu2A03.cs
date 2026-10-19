using AtomNES.Device.Bus;

namespace AtomNES.Device.Audio;

/// <summary>
/// 2A03 APU（NTSC）：2 路脉冲、三角、噪声、DMC 以及帧计数器。
/// <para>
/// 每个 CPU 周期调用一次 <see cref="Clock"/>。混音采用真机非线性公式，
/// 输出电平变化以 CPU 周期时间戳送入 <see cref="BandLimitedSynth"/>，由其完成带限降采样与模拟滤波。
/// </para>
/// </summary>
public sealed class Apu2A03 : IApuDevice
{
    public const double CpuClockRate = 1789772.7272;

    // 帧计数器各步所在的 CPU 周期（NTSC）
    private const int Step1 = 7457;
    private const int Step2 = 14913;
    private const int Step3 = 22371;
    private const int Step4 = 29829;
    private const int Step4Len = 29830;
    private const int Step5 = 37281;
    private const int Step5Len = 37282;

    private const int FlushInterval = 2048;

    private static readonly float[] PulseTable = BuildPulseTable();
    private static readonly float[] TndTable = BuildTndTable();

    private readonly PulseChannel _pulse1 = new(true);
    private readonly PulseChannel _pulse2 = new(false);
    private readonly TriangleChannel _triangle = new();
    private readonly NoiseChannel _noise = new();
    private readonly DmcChannel _dmc = new();

    private long _cycle;
    private int _frameCycle;
    private bool _fiveStep;
    private bool _irqInhibit;
    private bool _frameIrq;
    private int _pendingFrameWrite = -1;
    private int _frameWriteDelay;

    private int _synthClock;
    private float _lastOutput;

    public Apu2A03(int sampleRate = 48000)
    {
        Synth = new BandLimitedSynth(CpuClockRate, sampleRate);
    }

    public BandLimitedSynth Synth { get; }

    public bool IrqActive => _frameIrq || _dmc.Irq;

    private static float[] BuildPulseTable()
    {
        var t = new float[31];
        for (var n = 1; n < t.Length; n++) t[n] = (float)(95.52 / (8128.0 / n + 100));
        return t;
    }

    private static float[] BuildTndTable()
    {
        var t = new float[203];
        for (var n = 1; n < t.Length; n++) t[n] = (float)(163.67 / (24329.0 / n + 100));
        return t;
    }

    public void PowerOn() => ResetCore(false);

    public void Reset() => ResetCore(true);

    private void ResetCore(bool soft)
    {
        _pulse1.Reset(soft);
        _pulse2.Reset(soft);
        _triangle.Reset(soft);
        _noise.Reset(soft);
        _dmc.Reset(soft);
        _frameIrq = false;
        _frameCycle = 0;
        if (!soft) _irqInhibit = _fiveStep = false;

        // 复位等效于向 $4017 写入上次的值（上电时为 $00）；约 9~12 周期后帧计数器开始
        _pendingFrameWrite = (_fiveStep ? 0x80 : 0) | (_irqInhibit ? 0x40 : 0);
        _frameWriteDelay = 3;
    }

    public void Clock()
    {
        _cycle++;

        CommitLengths();
        ClockFrameCounter();

        _triangle.ClockTimer();
        _noise.ClockTimer();
        _dmc.ClockTimer();
        if ((_cycle & 1) == 0)
        {
            _pulse1.ClockTimer();
            _pulse2.ClockTimer();
        }

        Mix();
    }

    private void CommitLengths()
    {
        _pulse1.Length.Commit();
        _pulse2.Length.Commit();
        _triangle.Length.Commit();
        _noise.Length.Commit();
    }

    private void ClockFrameCounter()
    {
        if (_pendingFrameWrite >= 0 && --_frameWriteDelay == 0)
        {
            _fiveStep = (_pendingFrameWrite & 0x80) != 0;
            _pendingFrameWrite = -1;
            _frameCycle = 0;
            if (_fiveStep)
            {
                ClockQuarter();
                ClockHalf();
            }

            return;
        }

        _frameCycle++;
        switch (_frameCycle)
        {
            case Step1:
            case Step3:
                ClockQuarter();
                break;
            case Step2:
                ClockQuarter();
                ClockHalf();
                break;
            case Step4 - 1:
                if (!_fiveStep && !_irqInhibit) _frameIrq = true;
                break;
            case Step4:
                if (!_fiveStep)
                {
                    ClockQuarter();
                    ClockHalf();
                    if (!_irqInhibit) _frameIrq = true;
                }

                break;
            case Step4Len:
                if (!_fiveStep)
                {
                    if (!_irqInhibit) _frameIrq = true;
                    _frameCycle = 0;
                }

                break;
            case Step5:
                ClockQuarter();
                ClockHalf();
                break;
            case Step5Len:
                _frameCycle = 0;
                break;
        }
    }

    private void ClockQuarter()
    {
        _pulse1.ClockQuarter();
        _pulse2.ClockQuarter();
        _triangle.ClockQuarter();
        _noise.ClockQuarter();
    }

    private void ClockHalf()
    {
        _pulse1.ClockHalf();
        _pulse2.ClockHalf();
        _triangle.ClockHalf();
        _noise.ClockHalf();
    }

    private void Mix()
    {
        var output = PulseTable[_pulse1.Output + _pulse2.Output]
                     + TndTable[3 * _triangle.Output + 2 * _noise.Output + _dmc.Output];
        if (output != _lastOutput)
        {
            Synth.AddDelta(_synthClock, output - _lastOutput);
            _lastOutput = output;
        }

        if (++_synthClock >= FlushInterval)
        {
            Synth.EndFrame(_synthClock);
            _synthClock = 0;
        }
    }

    public byte ReadStatus()
    {
        var value = (_pulse1.Length.Counter > 0 ? 0x01 : 0)
                    | (_pulse2.Length.Counter > 0 ? 0x02 : 0)
                    | (_triangle.Length.Counter > 0 ? 0x04 : 0)
                    | (_noise.Length.Counter > 0 ? 0x08 : 0)
                    | (_dmc.BytesRemaining > 0 ? 0x10 : 0)
                    | (_frameIrq ? 0x40 : 0)
                    | (_dmc.Irq ? 0x80 : 0);
        _frameIrq = false;
        return (byte)value;
    }

    public byte PeekStatus() =>
        (byte)((_pulse1.Length.Counter > 0 ? 0x01 : 0)
               | (_pulse2.Length.Counter > 0 ? 0x02 : 0)
               | (_triangle.Length.Counter > 0 ? 0x04 : 0)
               | (_noise.Length.Counter > 0 ? 0x08 : 0)
               | (_dmc.BytesRemaining > 0 ? 0x10 : 0)
               | (_frameIrq ? 0x40 : 0)
               | (_dmc.Irq ? 0x80 : 0));

    public void WriteRegister(ushort address, byte value)
    {
        switch (address)
        {
            case <= 0x4003:
                _pulse1.Write(address & 3, value);
                break;
            case <= 0x4007:
                _pulse2.Write(address & 3, value);
                break;
            case <= 0x400B:
                _triangle.Write(address & 3, value);
                break;
            case <= 0x400F:
                _noise.Write(address & 3, value);
                break;
            case <= 0x4013:
                _dmc.Write(address & 3, value);
                break;
            case 0x4015:
                _pulse1.Length.SetEnabled((value & 0x01) != 0);
                _pulse2.Length.SetEnabled((value & 0x02) != 0);
                _triangle.Length.SetEnabled((value & 0x04) != 0);
                _noise.Length.SetEnabled((value & 0x08) != 0);
                _dmc.Irq = false;
                _dmc.SetEnabled((value & 0x10) != 0);
                break;
            case 0x4017:
                _irqInhibit = (value & 0x40) != 0;
                if (_irqInhibit) _frameIrq = false;
                _pendingFrameWrite = value;
                // 写入发生在 APU 周期内（偶数 CPU 周期）时 3 周期后生效，否则 4 周期后
                _frameWriteDelay = (_cycle & 1) == 0 ? 3 : 4;
                break;
        }
    }

    #region DMC DMA

    public bool DmaRequest => _dmc.NeedsDma;

    public ushort DmaAddress => _dmc.DmaAddress;

    public void CompleteDma(byte value) => _dmc.CompleteDma(value);

    #endregion
}
