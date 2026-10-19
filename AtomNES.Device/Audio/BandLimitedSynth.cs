namespace AtomNES.Device.Audio;

/// <summary>
/// 带限合成器：把以 CPU 周期为时间戳的“电平跳变”转换为指定采样率的 PCM。
/// <para>
/// 每次跳变以加窗 sinc 冲激（64 相位 × 16 抽头）写入差分缓冲，读取时积分还原波形，
/// 从而在 1.79MHz → 48kHz 的降采样中避免混叠（直接点采样是“噪音多、发刺”的主要来源）。
/// 输出端再串联真机 NES 的模拟滤波链：90Hz 高通、440Hz 高通、14kHz 低通。
/// </para>
/// </summary>
public sealed class BandLimitedSynth
{
    private const int Phases = 64;
    private const int Width = 16;
    private const int Half = Width / 2;
    private const int Capacity = 16384;

    private static readonly float[] Kernel = BuildKernel();

    private readonly float[] _buffer = new float[Capacity + 4096];
    private readonly double _samplesPerClock;
    private double _offset;

    private float _integrator;
    private float _hp90Prev, _hp90Out, _hp440Prev, _hp440Out, _lpOut;
    private readonly float _hp90A, _hp440A, _lpB;

    public BandLimitedSynth(double clockRate, int sampleRate)
    {
        SampleRate = sampleRate;
        _samplesPerClock = sampleRate / clockRate;
        var dt = 1.0 / sampleRate;
        _hp90A = HighPass(90, dt);
        _hp440A = HighPass(440, dt);
        var rc = 1.0 / (2 * Math.PI * 14000);
        _lpB = (float)(dt / (rc + dt));
    }

    public int SampleRate { get; }

    /// <summary>已完成、可读取的采样数。</summary>
    public int Available => (int)_offset;

    /// <summary>输出音量（0~1）。</summary>
    public float Volume { get; set; } = 1f;

    private static float HighPass(double hz, double dt)
    {
        var rc = 1.0 / (2 * Math.PI * hz);
        return (float)(rc / (rc + dt));
    }

    private static float[] BuildKernel()
    {
        const double cutoff = 0.45; // 相对采样率，略低于奈奎斯特
        var k = new float[Phases * Width];
        for (var p = 0; p < Phases; p++)
        {
            var frac = (double)p / Phases;
            double sum = 0;
            for (var i = 0; i < Width; i++)
            {
                var x = i - (Half - 1) - frac;
                var n = i - frac + 0.5;
                var window = 0.42 - 0.5 * Math.Cos(2 * Math.PI * n / Width) + 0.08 * Math.Cos(4 * Math.PI * n / Width);
                var sinc = Math.Abs(x) < 1e-9 ? 2 * cutoff : Math.Sin(2 * Math.PI * cutoff * x) / (Math.PI * x);
                var v = sinc * window;
                k[p * Width + i] = (float)v;
                sum += v;
            }

            // 每个相位归一化，保证积分后阶跃幅度精确等于 delta，不产生直流漂移
            for (var i = 0; i < Width; i++) k[p * Width + i] = (float)(k[p * Width + i] / sum);
        }

        return k;
    }

    /// <summary>在本帧第 <paramref name="clock"/> 个 CPU 周期处加入幅度跳变。</summary>
    public void AddDelta(long clock, float delta)
    {
        var t = _offset + clock * _samplesPerClock;
        var index = (int)t;
        if (index + Width >= _buffer.Length) return;
        var phase = (int)((t - index) * Phases);
        var k = phase * Width;
        for (var i = 0; i < Width; i++)
        {
            _buffer[index + i] += delta * Kernel[k + i];
        }
    }

    /// <summary>结束一帧（<paramref name="clocks"/> 个 CPU 周期），使对应采样可读。</summary>
    public void EndFrame(long clocks)
    {
        _offset += clocks * _samplesPerClock;
        if (_offset > Capacity)
        {
            // 长时间未读取（例如无音频设备）：丢弃积压，保留波形连续性
            var drop = (int)_offset - Capacity / 2;
            Discard(drop);
        }
    }

    public int ReadSamples(Span<short> output)
    {
        var count = Math.Min(output.Length, Available);
        var gain = 30000f * Volume;
        for (var i = 0; i < count; i++)
        {
            _integrator += _buffer[i];

            var x = _integrator;
            _hp90Out = _hp90A * (_hp90Out + x - _hp90Prev);
            _hp90Prev = x;
            _hp440Out = _hp440A * (_hp440Out + _hp90Out - _hp440Prev);
            _hp440Prev = _hp90Out;
            _lpOut += _lpB * (_hp440Out - _lpOut);

            output[i] = (short)Math.Clamp(_lpOut * gain, short.MinValue, short.MaxValue);
        }

        Shift(count);
        return count;
    }

    private void Discard(int count)
    {
        for (var i = 0; i < count; i++) _integrator += _buffer[i];
        Shift(count);
    }

    private void Shift(int count)
    {
        var keep = Math.Min((int)_offset - count + Width + 1, _buffer.Length - count);
        Array.Copy(_buffer, count, _buffer, 0, keep);
        Array.Clear(_buffer, keep, _buffer.Length - keep);
        _offset -= count;
    }
}
