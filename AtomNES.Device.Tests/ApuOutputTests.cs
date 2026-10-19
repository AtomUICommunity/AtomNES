using AtomNES.Device.Audio;
using AtomNES.Device.Bus;
using AtomNES.Device.Cartridges;
using AtomNES.Device.Display;
using AtomNES.Device.Ppu;

namespace AtomNES.Device.Tests;

public class ApuOutputTests
{
    [Fact]
    public void SynthProducesCleanSquareWave()
    {
        // 440Hz 方波：电平跳变经带限合成后无削波、无直流漂移，采样数与时长一致
        var synth = new BandLimitedSynth(Apu2A03.CpuClockRate, 48000);
        var half = (int)(Apu2A03.CpuClockRate / 880);
        var level = 0f;
        long clock = 0;
        var samples = new List<short>();
        var buf = new short[4096];
        for (var frame = 0; frame < 60; frame++)
        {
            var frameClocks = 29830;
            for (var c = 0; c < frameClocks; c++, clock++)
            {
                if (clock % half == 0)
                {
                    var next = level == 0 ? 0.3f : 0f;
                    synth.AddDelta(c, next - level);
                    level = next;
                }
            }

            synth.EndFrame(frameClocks);
            int n;
            while ((n = synth.ReadSamples(buf)) > 0) samples.AddRange(buf.AsSpan(0, n).ToArray());
        }

        Assert.InRange(samples.Count, 47900, 48100);
        var tail = samples.Skip(24000).ToArray();
        Assert.True(Math.Abs(tail.Average(s => (double)s)) < 300, "DC offset");
        Assert.DoesNotContain(tail, s => s is short.MaxValue or short.MinValue);
        Assert.True(tail.Max() > 2000, "too quiet");
    }

    [Fact]
    public void GameProducesAudio()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestRoms", "apu_test", "1-len_ctr.nes");
        var bus = new NesBus();
        var apu = new Apu2A03();
        bus.ConnectPpu(new Ppu2C02(new FrameBuffer()));
        bus.ConnectApu(apu);
        bus.InsertCartridge(MapperFactory.Create(RomImage.Load(path)));
        var buf = new short[4096];
        var total = 0;
        for (var i = 0; i < 120; i++)
        {
            bus.RunFrame();
            int n;
            while ((n = apu.Synth.ReadSamples(buf)) > 0) total += n;
        }

        Assert.InRange(total, 95000, 97000);
    }
}
