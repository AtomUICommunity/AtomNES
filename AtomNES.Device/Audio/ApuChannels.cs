namespace AtomNES.Device.Audio;

/// <summary>长度计数器（脉冲/三角/噪声共用）。重载与 halt 写入延迟到帧计数器时钟之后生效，以复现同周期竞争。</summary>
internal struct LengthCounter
{
    private static readonly byte[] Table =
    [
        10, 254, 20, 2, 40, 4, 80, 6, 160, 8, 60, 10, 14, 12, 26, 14,
        12, 16, 24, 18, 48, 20, 96, 22, 192, 24, 72, 26, 16, 28, 32, 30
    ];

    public int Counter;
    public bool Halt;
    public bool Enabled;
    private bool _newHalt;
    private int _reload;
    private int _previous;

    public void Reset(bool soft)
    {
        if (!soft) Enabled = false;
        Counter = 0;
        Halt = _newHalt = false;
        _reload = _previous = 0;
    }

    public void WriteHalt(bool halt) => _newHalt = halt;

    public void Load(int index)
    {
        if (!Enabled) return;
        _reload = Table[index];
        _previous = Counter;
    }

    public void Clock()
    {
        if (Counter > 0 && !Halt) Counter--;
    }

    /// <summary>提交挂起的写入：若本周期长度刚被时钟递减，则忽略重载（真机行为）。</summary>
    public void Commit()
    {
        if (_reload != 0)
        {
            if (Counter == _previous) Counter = _reload;
            _reload = 0;
        }

        Halt = _newHalt;
    }

    public void SetEnabled(bool enabled)
    {
        Enabled = enabled;
        if (!enabled) Counter = 0;
    }
}

internal struct Envelope
{
    public bool Constant;
    public bool Loop;
    public int Volume;
    public bool Start;
    private int _divider;
    private int _decay;

    public void Write(byte value)
    {
        Loop = (value & 0x20) != 0;
        Constant = (value & 0x10) != 0;
        Volume = value & 0x0F;
    }

    public void Clock()
    {
        if (Start)
        {
            Start = false;
            _decay = 15;
            _divider = Volume;
            return;
        }

        if (_divider > 0)
        {
            _divider--;
            return;
        }

        _divider = Volume;
        if (_decay > 0) _decay--;
        else if (Loop) _decay = 15;
    }

    public readonly int Output => Constant ? Volume : _decay;
}

internal sealed class PulseChannel(bool isPulse1)
{
    private static readonly byte[][] Duty =
    [
        [0, 1, 0, 0, 0, 0, 0, 0],
        [0, 1, 1, 0, 0, 0, 0, 0],
        [0, 1, 1, 1, 1, 0, 0, 0],
        [1, 0, 0, 1, 1, 1, 1, 1]
    ];

    public LengthCounter Length;
    private Envelope _envelope;
    private int _duty;
    private int _step;
    private int _period;
    private int _timer;

    private bool _sweepEnabled;
    private bool _sweepNegate;
    private bool _sweepReload;
    private int _sweepPeriod;
    private int _sweepShift;
    private int _sweepDivider;

    public void Reset(bool soft)
    {
        Length.Reset(soft);
        _envelope = default;
        _duty = _step = _period = _timer = 0;
        _sweepEnabled = _sweepNegate = _sweepReload = false;
        _sweepPeriod = _sweepShift = _sweepDivider = 0;
    }

    public void Write(int reg, byte value)
    {
        switch (reg)
        {
            case 0:
                _duty = value >> 6;
                Length.WriteHalt((value & 0x20) != 0);
                _envelope.Write(value);
                break;
            case 1:
                _sweepEnabled = (value & 0x80) != 0;
                _sweepPeriod = (value >> 4) & 7;
                _sweepNegate = (value & 0x08) != 0;
                _sweepShift = value & 7;
                _sweepReload = true;
                break;
            case 2:
                _period = (_period & 0x700) | value;
                break;
            case 3:
                _period = (_period & 0xFF) | ((value & 7) << 8);
                Length.Load(value >> 3);
                _step = 0;
                _envelope.Start = true;
                break;
        }
    }

    private int TargetPeriod
    {
        get
        {
            var change = _period >> _sweepShift;
            if (!_sweepNegate) return _period + change;
            return _period - change - (isPulse1 ? 1 : 0);
        }
    }

    private bool Muted => _period < 8 || (!_sweepNegate && TargetPeriod > 0x7FF);

    /// <summary>APU 周期（CPU 周期 / 2）驱动。</summary>
    public void ClockTimer()
    {
        if (_timer == 0)
        {
            _timer = _period;
            _step = (_step - 1) & 7;
        }
        else
        {
            _timer--;
        }
    }

    public void ClockQuarter() => _envelope.Clock();

    public void ClockHalf()
    {
        Length.Clock();
        if (_sweepDivider == 0 && _sweepEnabled && _sweepShift > 0 && !Muted)
        {
            var target = TargetPeriod;
            _period = target < 0 ? 0 : target;
        }

        if (_sweepDivider == 0 || _sweepReload)
        {
            _sweepDivider = _sweepPeriod;
            _sweepReload = false;
        }
        else
        {
            _sweepDivider--;
        }
    }

    public int Output => Length.Counter == 0 || Muted || Duty[_duty][_step] == 0 ? 0 : _envelope.Output;
}

internal sealed class TriangleChannel
{
    private static readonly byte[] Sequence =
    [
        15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1, 0,
        0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15
    ];

    public LengthCounter Length;
    private bool _control;
    private int _linearReload;
    private int _linear;
    private bool _linearReloadFlag;
    private int _period;
    private int _timer;
    private int _step;

    public void Reset(bool soft)
    {
        Length.Reset(soft);
        _control = _linearReloadFlag = false;
        _linearReload = _linear = _period = _timer = 0;
        // 复位后三角波序列位置保持（真机不清零），上电时为 0
        if (!soft) _step = 0;
    }

    public void Write(int reg, byte value)
    {
        switch (reg)
        {
            case 0:
                _control = (value & 0x80) != 0;
                Length.WriteHalt(_control);
                _linearReload = value & 0x7F;
                break;
            case 2:
                _period = (_period & 0x700) | value;
                break;
            case 3:
                _period = (_period & 0xFF) | ((value & 7) << 8);
                Length.Load(value >> 3);
                _linearReloadFlag = true;
                break;
        }
    }

    /// <summary>CPU 周期驱动。</summary>
    public void ClockTimer()
    {
        if (_timer == 0)
        {
            _timer = _period;
            // 周期 &lt; 2 为超声频率：真机会输出 7.5 的直流平均值，这里冻结序列避免刺耳的爆音
            if (Length.Counter > 0 && _linear > 0 && _period >= 2) _step = (_step + 1) & 31;
        }
        else
        {
            _timer--;
        }
    }

    public void ClockQuarter()
    {
        if (_linearReloadFlag) _linear = _linearReload;
        else if (_linear > 0) _linear--;
        if (!_control) _linearReloadFlag = false;
    }

    public void ClockHalf() => Length.Clock();

    public int Output => Sequence[_step];
}

internal sealed class NoiseChannel
{
    private static readonly ushort[] Periods = [4, 8, 16, 32, 64, 96, 128, 160, 202, 254, 380, 508, 762, 1016, 2034, 4068];

    public LengthCounter Length;
    private Envelope _envelope;
    private bool _mode;
    private int _period = Periods[0];
    private int _timer;
    private ushort _shift = 1;

    public void Reset(bool soft)
    {
        Length.Reset(soft);
        _envelope = default;
        _mode = false;
        _period = Periods[0];
        _timer = 0;
        _shift = 1;
    }

    public void Write(int reg, byte value)
    {
        switch (reg)
        {
            case 0:
                Length.WriteHalt((value & 0x20) != 0);
                _envelope.Write(value);
                break;
            case 2:
                _mode = (value & 0x80) != 0;
                _period = Periods[value & 0x0F];
                break;
            case 3:
                Length.Load(value >> 3);
                _envelope.Start = true;
                break;
        }
    }

    /// <summary>CPU 周期驱动（周期表即以 CPU 周期为单位）。</summary>
    public void ClockTimer()
    {
        if (--_timer > 0) return;
        _timer = _period;
        var bit = (_shift ^ (_shift >> (_mode ? 6 : 1))) & 1;
        _shift = (ushort)((_shift >> 1) | (bit << 14));
    }

    public void ClockQuarter() => _envelope.Clock();

    public void ClockHalf() => Length.Clock();

    public int Output => Length.Counter == 0 || (_shift & 1) != 0 ? 0 : _envelope.Output;
}
