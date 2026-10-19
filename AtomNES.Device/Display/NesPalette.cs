namespace AtomNES.Device.Display;

/// <summary>
/// 2C02 NTSC 64 色调色板，预先转换为 BGRA32（0xAARRGGBB），
/// 并按 PPUMASK 强调位预生成 8 组，渲染时只需一次查表。
/// </summary>
public static class NesPalette
{
    public const uint Black = 0xFF000000;

    private static ReadOnlySpan<byte> Rgb =>
    [
        84, 84, 84, 0, 30, 116, 8, 16, 144, 48, 0, 136, 68, 0, 100, 92, 0, 48, 84, 4, 0, 60, 24, 0,
        32, 42, 0, 8, 58, 0, 0, 64, 0, 0, 60, 0, 0, 50, 60, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        152, 150, 152, 8, 76, 196, 48, 50, 236, 92, 30, 228, 136, 20, 176, 160, 20, 100, 152, 34, 32, 120, 60, 0,
        84, 90, 32, 40, 114, 56, 8, 124, 76, 0, 118, 92, 0, 102, 120, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        236, 238, 236, 76, 154, 236, 120, 124, 236, 176, 98, 236, 228, 84, 236, 236, 88, 180, 236, 106, 100, 212, 136, 32,
        160, 170, 0, 116, 196, 0, 76, 208, 32, 56, 204, 108, 56, 180, 204, 60, 60, 60, 0, 0, 0, 0, 0, 0,
        236, 238, 236, 168, 204, 236, 188, 188, 236, 212, 178, 236, 236, 174, 236, 236, 174, 212, 236, 180, 176, 228, 196, 144,
        204, 210, 120, 180, 222, 120, 168, 226, 144, 152, 226, 180, 160, 214, 228, 160, 162, 160, 0, 0, 0, 0, 0, 0
    ];

    /// <summary>索引：emphasis(0-7) * 64 + colorIndex。</summary>
    public static readonly uint[] Table = Build();

    public static uint Get(byte colorIndex, int emphasis = 0) => Table[((emphasis & 7) << 6) | (colorIndex & 0x3F)];

    private static uint[] Build()
    {
        var table = new uint[8 * 64];
        for (var e = 0; e < 8; e++)
        {
            // 强调位：bit0=R bit1=G bit2=B，未被强调的通道衰减
            var rf = e != 0 && (e & 1) == 0 ? 0.816 : 1.0;
            var gf = e != 0 && (e & 2) == 0 ? 0.816 : 1.0;
            var bf = e != 0 && (e & 4) == 0 ? 0.816 : 1.0;

            for (var i = 0; i < 64; i++)
            {
                var r = (uint)(Rgb[i * 3] * rf);
                var g = (uint)(Rgb[i * 3 + 1] * gf);
                var b = (uint)(Rgb[i * 3 + 2] * bf);
                table[(e << 6) | i] = 0xFF000000 | (r << 16) | (g << 8) | b;
            }
        }

        return table;
    }
}
