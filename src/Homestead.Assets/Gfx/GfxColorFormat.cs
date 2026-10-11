namespace Homestead.Assets.Gfx;

/// <summary>
/// 16-bit color layout of a GFX file. Each GFX file ships in both variants; the file name
/// suffix encodes the channel masks (<c>.7c003e01f.dat</c> = 555, <c>.f8007e01f.dat</c> = 565).
/// </summary>
public enum GfxColorFormat
{
    Rgb555,
    Rgb565,
}

internal static class GfxColor
{
    public const uint Transparent = 0;

    /// <summary>Semi-transparent black, used for shadow pixels.</summary>
    public const uint Shadow = 0x88000000;

    public static uint ToRgba(ushort c, GfxColorFormat format)
    {
        uint r, g, b;
        if (format == GfxColorFormat.Rgb565)
        {
            r = Expand5(((uint)c >> 11) & 0x1fu);
            g = Expand6(((uint)c >> 5) & 0x3fu);
            b = Expand5(c & 0x1fu);
        }
        else
        {
            r = Expand5(((uint)c >> 10) & 0x1fu);
            g = Expand5(((uint)c >> 5) & 0x1fu);
            b = Expand5(c & 0x1fu);
        }
        return 0xff000000 | (b << 16) | (g << 8) | r;
    }

    /// <summary>Torso pixels are a 5-bit intensity that gets tinted with the player color.</summary>
    public static uint TorsoToRgba(byte value)
    {
        uint v = Expand5(value & 0x1fu);
        return 0xff000000 | (v << 16) | (v << 8) | v;
    }

    private static uint Expand5(uint v) => (v << 3) | (v >> 2);

    private static uint Expand6(uint v) => (v << 2) | (v >> 4);
}
