using System;
using System.Diagnostics;
using System.Threading;
using AtomNES.Device.Audio;
using AtomNES.Device.Bus;
using AtomNES.Device.Cartridges;
using AtomNES.Device.Display;
using AtomNES.Device.Ppu;

namespace AtomNES.Services;

/// <summary>
/// 模拟器宿主：独占一个后台线程按 NTSC 帧率（60.0988 Hz）驱动 <see cref="NesBus"/>。
/// 所有对总线状态的访问（包括调试器读取）都通过 <see cref="SyncRoot"/> 串行化。
/// </summary>
public sealed class EmulatorHost : IDisposable
{
    public const double NtscFrameRate = 60.0988;

    private readonly Thread _thread;
    private readonly AutoResetEvent _wake = new(false);
    private volatile bool _disposed;
    private volatile bool _paused;
    private double _speed = 1.0;

    private long _fpsFrames;
    private long _fpsStamp = Stopwatch.GetTimestamp();

    public EmulatorHost()
    {
        Ppu = new Ppu2C02(Screen);
        Bus.ConnectPpu(Ppu);
        Apu = new Apu2A03(AudioOutput.SampleRate);
        Bus.ConnectApu(Apu);
        Audio = AudioOutput.TryCreate(out var audioError);
        AudioError = audioError;
        _thread = new Thread(Run) { IsBackground = true, Name = "NES Emulation", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    public object SyncRoot { get; } = new();

    public FrameBuffer Screen { get; } = new();

    public NesBus Bus { get; } = new();

    public Ppu2C02 Ppu { get; }

    public Apu2A03 Apu { get; }

    /// <summary>音频设备；初始化失败时为 null（模拟照常运行，仅无声）。</summary>
    public AudioOutput? Audio { get; }

    public string? AudioError { get; }

    public bool Muted
    {
        get => Audio?.Muted ?? true;
        set
        {
            if (Audio is not null) Audio.Muted = value;
        }
    }

    public float Volume
    {
        get => Apu.Synth.Volume;
        set => Apu.Synth.Volume = Math.Clamp(value, 0f, 1f);
    }

    public RomImage? Rom { get; private set; }

    public string? RomPath { get; private set; }

    public bool IsLoaded => Rom is not null;

    public bool IsPaused => _paused;

    /// <summary>运行/暂停状态变化（可能在后台线程触发）。</summary>
    public event Action? StateChanged;

    /// <summary>模拟线程异常（后台线程触发）。</summary>
    public event Action<Exception>? Faulted;

    public double Speed
    {
        get => Volatile.Read(ref _speed);
        set => Volatile.Write(ref _speed, Math.Clamp(value, 0.25, 8));
    }

    /// <summary>最近一秒实际运行帧率。</summary>
    public double MeasuredFps { get; private set; }

    public void Load(string path)
    {
        var rom = RomImage.Load(path);
        var mapper = MapperFactory.Create(rom);
        lock (SyncRoot)
        {
            Bus.InsertCartridge(mapper);
            Screen.Clear();
            Rom = rom;
            RomPath = path;
        }

        Resume();
    }

    public void Unload()
    {
        lock (SyncRoot)
        {
            Bus.RemoveCartridge();
            Rom = null;
            RomPath = null;
            Screen.Clear();
        }

        _paused = false;
        StateChanged?.Invoke();
    }

    public void Reset()
    {
        lock (SyncRoot) Bus.Reset();
    }

    public void PowerCycle()
    {
        lock (SyncRoot)
        {
            Bus.PowerOn();
            Screen.Clear();
        }
    }

    public void Pause()
    {
        if (_paused) return;
        _paused = true;
        StateChanged?.Invoke();
    }

    public void Resume()
    {
        _paused = false;
        _wake.Set();
        StateChanged?.Invoke();
    }

    /// <summary>暂停并执行一条 CPU 指令。</summary>
    public void StepInstruction()
    {
        if (!IsLoaded) return;
        Pause();
        lock (SyncRoot) Bus.Step();
    }

    /// <summary>暂停并运行到下一条扫描线。</summary>
    public void StepScanline()
    {
        if (!IsLoaded) return;
        Pause();
        lock (SyncRoot)
        {
            var line = Ppu.Scanline;
            while (Ppu.Scanline == line && !Bus.Cpu.Jammed) Bus.Step();
        }
    }

    /// <summary>暂停并运行一整帧（到下一次 VBlank）。</summary>
    public void StepFrame()
    {
        if (!IsLoaded) return;
        Pause();
        lock (SyncRoot) Bus.RunFrame();
    }

    private void Run()
    {
        var freq = (double)Stopwatch.Frequency;
        var next = Stopwatch.GetTimestamp();
        while (!_disposed)
        {
            if (_paused || !IsLoaded)
            {
                MeasuredFps = 0;
                _wake.WaitOne(100);
                next = Stopwatch.GetTimestamp();
                _fpsFrames = 0;
                _fpsStamp = next;
                continue;
            }

            try
            {
                lock (SyncRoot)
                {
                    Bus.RunFrame();
                    if (Bus.Cpu.Jammed) Pause();
                    // 非 1x 速度时丢弃音频，避免变调与积压
                    var discard = Math.Abs(Speed - 1.0) > 0.001;
                    Audio?.Submit(Apu.Synth, discard);
                }
            }
            catch (Exception ex)
            {
                Pause();
                Faulted?.Invoke(ex);
                continue;
            }

            UpdateFps();

            next += (long)(freq / (NtscFrameRate * Speed));
            var now = Stopwatch.GetTimestamp();
            if (next - now < -freq * 0.1)
            {
                // 落后太多（卡顿/调试）时重新对齐，避免疯狂追帧
                next = now;
                continue;
            }

            // 粗睡眠 + 末段让出/自旋，兼顾 CPU 占用与帧间隔精度
            long remain;
            while ((remain = next - Stopwatch.GetTimestamp()) > 0)
            {
                var ms = remain * 1000.0 / freq;
                if (ms > 2) Thread.Sleep(1);
                else if (ms > 0.3) Thread.Yield();
                else Thread.SpinWait(50);
            }
        }
    }

    private void UpdateFps()
    {
        _fpsFrames++;
        var now = Stopwatch.GetTimestamp();
        var elapsed = (now - _fpsStamp) / (double)Stopwatch.Frequency;
        if (elapsed < 1) return;
        MeasuredFps = _fpsFrames / elapsed;
        _fpsFrames = 0;
        _fpsStamp = now;
    }

    public void Dispose()
    {
        _disposed = true;
        _wake.Set();
        _thread.Join(500);
        Audio?.Dispose();
    }
}
