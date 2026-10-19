using System;
using AtomNES.Device.Audio;
using SDL;
using static SDL.SDL3;

namespace AtomNES.Services;

/// <summary>
/// 基于 SDL3 的跨平台音频输出（Windows / macOS / Linux）。
/// <para>
/// 模拟线程每帧把合成好的采样推入 SDL 音频流；根据队列水位用 ±0.5% 的频率比做动态速率控制，
/// 让音频与 60.0988Hz 的视频帧同步，避免缓冲欠载（爆音）或积压（延迟）。
/// </para>
/// </summary>
public sealed unsafe class AudioOutput : IDisposable
{
    public const int SampleRate = 48000;

    // 目标延迟约 50ms
    private const int TargetQueuedBytes = SampleRate * 2 / 20;

    private readonly SDL_AudioStream* _stream;
    private readonly short[] _scratch = new short[4096];

    private AudioOutput(SDL_AudioStream* stream)
    {
        _stream = stream;
    }

    public static AudioOutput? TryCreate(out string? error)
    {
        error = null;
        try
        {
            if (!SDL_InitSubSystem(SDL_InitFlags.SDL_INIT_AUDIO))
            {
                error = SDL_GetError();
                return null;
            }

            var spec = new SDL_AudioSpec { format = SDL_AudioFormat.SDL_AUDIO_S16LE, channels = 1, freq = SampleRate };
            var stream = SDL_OpenAudioDeviceStream(SDL_AUDIO_DEVICE_DEFAULT_PLAYBACK, &spec, null, IntPtr.Zero);
            if (stream == null)
            {
                error = SDL_GetError();
                SDL_QuitSubSystem(SDL_InitFlags.SDL_INIT_AUDIO);
                return null;
            }

            SDL_ResumeAudioStreamDevice(stream);
            return new AudioOutput(stream);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or TypeInitializationException)
        {
            error = ex.Message;
            return null;
        }
    }

    public bool Muted { get; set; }

    /// <summary>从合成器取出全部可用采样并送入设备。</summary>
    public void Submit(BandLimitedSynth synth, bool discard)
    {
        while (synth.Available > 0)
        {
            var n = synth.ReadSamples(_scratch);
            if (discard || Muted) continue;
            fixed (short* p = _scratch)
            {
                SDL_PutAudioStreamData(_stream, (IntPtr)p, n * sizeof(short));
            }
        }

        var queued = SDL_GetAudioStreamQueued(_stream);
        if (queued > TargetQueuedBytes * 4)
        {
            // 积压过多（例如从调试暂停恢复）：直接清空，避免延迟
            SDL_ClearAudioStream(_stream);
            queued = 0;
        }

        // 水位高于目标时略微加快播放，低于目标时略微放慢
        var error = (queued - TargetQueuedBytes) / (double)TargetQueuedBytes;
        var ratio = 1.0 + Math.Clamp(error, -1, 1) * 0.005;
        SDL_SetAudioStreamFrequencyRatio(_stream, (float)ratio);
    }

    public void Clear() => SDL_ClearAudioStream(_stream);

    public void Dispose()
    {
        SDL_DestroyAudioStream(_stream);
        SDL_QuitSubSystem(SDL_InitFlags.SDL_INIT_AUDIO);
    }
}
