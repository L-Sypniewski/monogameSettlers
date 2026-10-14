namespace Homestead.Assets.Maps;

/// <summary>Landscape overview of a map: one pixel per grid point, colored by landscape and shaded by relief.</summary>
public static class MapOverview
{
    /// <summary>Pixels (0xAABBGGRR) row by row, <see cref="MapArea.Size"/> × <see cref="MapArea.Size"/>.</summary>
    public static uint[] Pixels(MapArea area)
    {
        var pixels = new uint[area.Size * area.Size];
        for (int y = 0; y < area.Size; y++)
        for (int x = 0; x < area.Size; x++)
        {
            int i = area.Index(x, y);
            uint color = BaseColor(area.Landscape[i]);
            // Simple relief shading from the height difference to the point above.
            int dh = y > 0 ? area.Height[i] - area.Height[area.Index(x, y - 1)] : 0;
            float light = Math.Clamp(1f + dh * 0.12f, 0.5f, 1.4f);
            pixels[i] = Shade(color, light);
        }
        return pixels;
    }

    private static uint BaseColor(byte landscape) => (landscape & 0xf0) switch
    {
        0x00 => Rgb(30, 80, 200),    // water
        0x10 => Rgb(40, 120, 30),    // grass and borders
        0x20 => Rgb(110, 105, 100),  // mountain
        0x30 => Rgb(200, 180, 90),   // sand / beach
        0x40 => Rgb(210, 170, 80),   // desert
        0x50 => Rgb(30, 60, 40),     // moor
        0x60 => Rgb(60, 120, 220),   // river
        0x80 => Rgb(235, 240, 245),  // snow
        0x90 => Rgb(90, 60, 40),     // mud
        _ => Rgb(255, 0, 255),       // unknown
    };

    private static uint Rgb(uint r, uint g, uint b) => 0xff000000 | (b << 16) | (g << 8) | r;

    private static uint Shade(uint c, float f)
    {
        uint Ch(int shift) => (uint)Math.Clamp((int)(((c >> shift) & 0xff) * f), 0, 255) << shift;
        return 0xff000000 | Ch(16) | Ch(8) | Ch(0);
    }
}
