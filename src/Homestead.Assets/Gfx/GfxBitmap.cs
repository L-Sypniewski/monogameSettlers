namespace Homestead.Assets.Gfx;

/// <summary>
/// A decoded image from a GFX file. Pixels are packed RGBA8 with R in the lowest byte
/// (same layout as MonoGame's <c>Color.PackedValue</c>), row-major, top to bottom.
/// </summary>
public sealed class GfxBitmap
{
    public GfxBitmap(int width, int height, int offsetX, int offsetY, uint[] pixels)
    {
        if (pixels.Length != width * height)
            throw new ArgumentException("Pixel buffer does not match image size.", nameof(pixels));
        Width = width;
        Height = height;
        OffsetX = offsetX;
        OffsetY = offsetY;
        Pixels = pixels;
    }

    public int Width { get; }
    public int Height { get; }

    /// <summary>Drawing offset relative to the anchor point (e.g. the settler's foot position).</summary>
    public int OffsetX { get; }

    public int OffsetY { get; }

    public uint[] Pixels { get; }

    public static GfxBitmap Empty { get; } = new(0, 0, 0, 0, []);
}
