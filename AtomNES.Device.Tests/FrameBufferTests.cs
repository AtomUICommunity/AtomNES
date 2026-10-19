using AtomNES.Device.Display;

namespace AtomNES.Device.Tests;

public class FrameBufferTests
{
    [Fact]
    public void Acquire_ReturnsFalseUntilPresent()
    {
        var fb = new FrameBuffer();
        Assert.False(fb.TryAcquireLatest(out _));
        fb.SetPixel(1, 0, 0xFF112233);
        fb.Present();
        Assert.True(fb.TryAcquireLatest(out var frame));
        Assert.Equal(0xFF112233u, frame[1]);
        Assert.False(fb.TryAcquireLatest(out _));
    }

    [Fact]
    public void Acquire_AlwaysGetsLatestFrame()
    {
        var fb = new FrameBuffer();
        for (uint i = 1; i <= 5; i++)
        {
            fb.Back[0] = i;
            fb.Present();
        }

        Assert.True(fb.TryAcquireLatest(out var frame));
        Assert.Equal(5u, frame[0]);
    }

    [Fact]
    public void Buffers_NeverAlias()
    {
        var fb = new FrameBuffer();
        for (uint i = 0; i < 100; i++)
        {
            fb.Back.Fill(i);
            fb.Present();
            if (i % 3 == 0)
            {
                Assert.True(fb.TryAcquireLatest(out var frame));
                fb.Back.Fill(0xDEAD);
                Assert.Equal(i, frame[0]);
            }
        }
    }

    [Fact]
    public void Palette_IsOpaqueBgra()
    {
        Assert.Equal(0xFF545454u, NesPalette.Get(0x00));
        Assert.Equal(0xFFECEEECu, NesPalette.Get(0x20));
        Assert.All(NesPalette.Table, c => Assert.Equal(0xFFu, c >> 24));
    }
}
