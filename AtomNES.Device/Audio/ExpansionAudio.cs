namespace AtomNES.Device.Audio;

/// <summary>
/// 卡带扩展音频芯片。每个 CPU 周期调用 <c>Clock</c>，<c>Output</c> 为与 APU 混音同量纲的电平（约 0~0.3）。
/// 各芯片相对音量参考 Mesen2 的默认混音比例粗略校准。
/// </summary>
internal sealed class Vrc6Audio
{
    private readonly Vrc6Pulse _pulse1 = new();
    private readonly Vrc6Pulse _pulse2 = new();
    private readonly Vrc6Saw _saw = new();
    private bool _halt;

    public float Output => (_pulse1.Volume + _pulse2.Volume + _saw.Volume) * 0.0075f;

    public void Clock()
    {
        if (_halt) return;
        _pulse1.Clock();
        _pulse2.Clock();
        _saw.Clock();
    }

    /// <summary>address 已按 VRC6a 布局规整（$9000-$B002）。</summary>
    public void Write(int address, byte value)
    {
        switch (address & 0xF003)
        {
            case 0x9000 or 0x9001 or 0x9002: _pulse1.Write(address & 3, value); break;
            case 0x9003:
                _halt = (value & 1) != 0;
                var shift = (value & 4) != 0 ? 8 : (value & 2) != 0 ? 4 : 0;
                _pulse1.Shift = _pulse2.Shift = _saw.Shift = shift;
                break;
            case 0xA000 or 0xA001 or 0xA002: _pulse2.Write(address & 3, value); break;
            case 0xB000 or 0xB001 or 0xB002: _saw.Write(address & 3, value); break;
        }
    }

    private sealed class Vrc6Pulse
    {
        private int _volume;
        private int _duty;
        private bool _ignoreDuty;
        private int _frequency = 1;
        private bool _enabled;
        private int _timer = 1;
        private int _step;
        public int Shift;

        public void Write(int reg, byte value)
        {
            switch (reg)
            {
                case 0:
                    _volume = value & 0x0F;
                    _duty = (value >> 4) & 7;
                    _ignoreDuty = (value & 0x80) != 0;
                    break;
                case 1: _frequency = (_frequency & 0xF00) | value; break;
                case 2:
                    _frequency = (_frequency & 0xFF) | ((value & 0x0F) << 8);
                    _enabled = (value & 0x80) != 0;
                    if (!_enabled) _step = 0;
                    break;
            }
        }

        public void Clock()
        {
            if (!_enabled || --_timer != 0) return;
            _step = (_step + 1) & 0x0F;
            _timer = (_frequency >> Shift) + 1;
        }

        public int Volume => !_enabled ? 0 : _ignoreDuty || _step <= _duty ? _volume : 0;
    }

    private sealed class Vrc6Saw
    {
        private int _rate;
        private int _accumulator;
        private int _frequency = 1;
        private bool _enabled;
        private int _timer = 1;
        private int _step;
        public int Shift;

        public void Write(int reg, byte value)
        {
            switch (reg)
            {
                case 0: _rate = value & 0x3F; break;
                case 1: _frequency = (_frequency & 0xF00) | value; break;
                case 2:
                    _frequency = (_frequency & 0xFF) | ((value & 0x0F) << 8);
                    _enabled = (value & 0x80) != 0;
                    if (!_enabled) _accumulator = _step = 0;
                    break;
            }
        }

        public void Clock()
        {
            if (!_enabled || --_timer != 0) return;
            _step = (_step + 1) % 14;
            _timer = (_frequency >> Shift) + 1;
            if (_step == 0) _accumulator = 0;
            else if ((_step & 1) == 0) _accumulator = (_accumulator + _rate) & 0xFF;
        }

        public int Volume => _enabled ? _accumulator >> 3 : 0;
    }
}

/// <summary>Sunsoft 5B（YM2149 子集）：3 路方波，对数音量（包络与噪声未实现，与 Mesen2 一致）。</summary>
internal sealed class Sunsoft5BAudio
{
    private static readonly int[] VolumeLut = BuildLut();
    private readonly byte[] _regs = new byte[16];
    private readonly int[] _timer = new int[3];
    private readonly int[] _step = new int[3];
    private int _current;
    private bool _tick;
    private int _output;

    private static int[] BuildLut()
    {
        var lut = new int[16];
        var v = 1.0;
        for (var i = 1; i < 16; i++)
        {
            v *= 1.1885022274370184 * 1.1885022274370184;
            lut[i] = (int)v;
        }

        return lut;
    }

    public float Output => _output * 0.0006f;

    public void Clock()
    {
        _tick = !_tick;
        if (!_tick) return;
        var sum = 0;
        for (var ch = 0; ch < 3; ch++)
        {
            if (--_timer[ch] <= 0)
            {
                _timer[ch] = _regs[ch * 2] | ((_regs[ch * 2 + 1] & 0x0F) << 8);
                _step[ch] = (_step[ch] + 1) & 0x0F;
            }

            if (((_regs[7] >> ch) & 1) == 0 && _step[ch] < 8) sum += VolumeLut[_regs[8 + ch] & 0x0F];
        }

        _output = sum;
    }

    public void Write(int address, byte value)
    {
        switch (address & 0xE000)
        {
            case 0xC000: _current = value; break;
            case 0xE000:
                if (_current <= 0x0F) _regs[_current] = value;
                break;
        }
    }
}

/// <summary>Namco 163：128 字节内部 RAM 中的最多 8 路 4 位波表，每 15 个 CPU 周期轮流更新一个声道。</summary>
internal sealed class Namco163Audio
{
    public readonly byte[] Ram = new byte[0x80];
    private readonly int[] _channelOut = new int[8];
    private int _position;
    private bool _autoIncrement;
    private int _counter;
    private int _channel = 7;
    private bool _disabled;
    private int _output;

    public float Output => _output * 0.0022f;

    private int Channels => (Ram[0x7F] >> 4) & 7;

    public void Clock()
    {
        if (_disabled || ++_counter < 15) return;
        _counter = 0;
        UpdateChannel(_channel);
        if (--_channel < 7 - Channels) _channel = 7;
    }

    private void UpdateChannel(int ch)
    {
        var b = 0x40 + ch * 8;
        var freq = ((Ram[b + 4] & 3) << 16) | (Ram[b + 2] << 8) | Ram[b];
        var phase = (Ram[b + 5] << 16) | (Ram[b + 3] << 8) | Ram[b + 1];
        var length = 256 - (Ram[b + 4] & 0xFC);
        phase = (phase + freq) % (length << 16);
        var pos = ((phase >> 16) + Ram[b + 6]) & 0xFF;
        var sample = (pos & 1) != 0 ? Ram[pos >> 1] >> 4 : Ram[pos >> 1] & 0x0F;
        _channelOut[ch] = (sample - 8) * (Ram[b + 7] & 0x0F);
        Ram[b + 5] = (byte)(phase >> 16);
        Ram[b + 3] = (byte)(phase >> 8);
        Ram[b + 1] = (byte)phase;

        var sum = 0;
        for (int i = 7, min = 7 - Channels; i >= min; i--) sum += _channelOut[i];
        _output = sum / (Channels + 1);
    }

    public void Write(int address, byte value)
    {
        switch (address & 0xF800)
        {
            case 0x4800:
                Ram[_position] = value;
                if (_autoIncrement) _position = (_position + 1) & 0x7F;
                break;
            case 0xE000: _disabled = (value & 0x40) != 0; break;
            case 0xF800:
                _position = value & 0x7F;
                _autoIncrement = (value & 0x80) != 0;
                break;
        }
    }

    public byte Read()
    {
        var value = Ram[_position];
        if (_autoIncrement) _position = (_position + 1) & 0x7F;
        return value;
    }
}

/// <summary>VRC7：YM2413 衍生 FM 芯片（6 声道），由 emu2413 以 49716Hz 生成样本。</summary>
internal sealed class Vrc7Audio
{
    private const int OpllRate = 49716;
    private const double CyclesPerSample = Apu2A03.CpuClockRate / OpllRate;
    private readonly Opll _opll;
    private int _register;
    private double _timer;
    private int _output;

    public Vrc7Audio()
    {
        _opll = new Opll(OpllRate * 72, OpllRate);
        _opll.SetChipType(1);
        _opll.ResetPatch(1);
    }

    public bool Muted { get; set; }

    public float Output => Muted ? 0f : _output * 0.00006f;

    public void Reset() => _opll.Reset();

    public void Clock()
    {
        _timer -= 1;
        if (_timer > 0) return;
        _timer += CyclesPerSample;
        _output = _opll.Calc();
    }

    public void Write(int address, byte value)
    {
        if (Muted) return;
        switch (address & 0xF030)
        {
            case 0x9010: _register = value; break;
            case 0x9030: _opll.WriteReg(_register, value); break;
        }
    }
}

/// <summary>MMC5 音频：2 路无扫频方波 + 8 位 PCM。包络/长度计数器以约 240Hz 驱动。</summary>
internal sealed class Mmc5Audio
{
    private readonly Mmc5Pulse _pulse1 = new();
    private readonly Mmc5Pulse _pulse2 = new();
    private int _frameCounter;
    private bool _pcmRead;
    private int _pcm;
    private bool _odd;

    public float Output => (_pulse1.Output + _pulse2.Output) * 0.00752f + _pcm * 0.0018f;

    public void Clock()
    {
        _pulse1.Commit();
        _pulse2.Commit();
        _odd = !_odd;
        if (_odd)
        {
            _pulse1.ClockTimer();
            _pulse2.ClockTimer();
        }

        if (++_frameCounter >= 7457)
        {
            _frameCounter = 0;
            _pulse1.ClockFrame();
            _pulse2.ClockFrame();
        }
    }

    public void Write(int address, byte value)
    {
        switch (address)
        {
            case >= 0x5000 and <= 0x5003: _pulse1.Write(address & 3, value); break;
            case >= 0x5004 and <= 0x5007: _pulse2.Write(address & 3, value); break;
            case 0x5010: _pcmRead = (value & 1) != 0; break;
            case 0x5011:
                if (!_pcmRead && value != 0) _pcm = value;
                break;
            case 0x5015:
                _pulse1.Length.SetEnabled((value & 1) != 0);
                _pulse2.Length.SetEnabled((value & 2) != 0);
                break;
        }
    }

    public byte ReadStatus() =>
        (byte)((_pulse1.Length.Counter > 0 ? 1 : 0) | (_pulse2.Length.Counter > 0 ? 2 : 0));

    private sealed class Mmc5Pulse
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

        public void Write(int reg, byte value)
        {
            switch (reg)
            {
                case 0:
                    _duty = value >> 6;
                    Length.WriteHalt((value & 0x20) != 0);
                    _envelope.Write(value);
                    break;
                case 2: _period = (_period & 0x700) | value; break;
                case 3:
                    _period = (_period & 0xFF) | ((value & 7) << 8);
                    Length.Load(value >> 3);
                    _step = 0;
                    _envelope.Start = true;
                    break;
            }
        }

        public void Commit() => Length.Commit();

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

        public void ClockFrame()
        {
            _envelope.Clock();
            Length.Clock();
        }

        public int Output => Length.Counter == 0 || Duty[_duty][_step] == 0 ? 0 : _envelope.Output;
    }
}
