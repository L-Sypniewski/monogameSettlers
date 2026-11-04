using Homestead.Assets.Gfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Homestead.Client.Terrain;

/// <summary>
/// The water lab's normals proposal: a tileable normal map derived from each continuous water image's luminance
/// (Sobel over the wrapping edges, so the 32x32 tile repeats), and N pre-lit frames per image with a directional
/// light rotating around them - all synthesized from the game's own loaded pixels, no new assets. Pure frame math
/// here (headless-testable); <see cref="BuildStrip"/> packs the frames of every water image into one texture.
/// </summary>
public static class WaterLabNormals
{
    /// <summary>How many lit frames per image: the light turns by 1/N of a full circle between them.</summary>
    public const int Frames = 8;

    /// <summary>Frames per second of the frame sequence the water samples.</summary>
    public const float FramesPerSecond = 8;

    /// <summary>Ambient share of the lighting; the rest is the rotating directional light.</summary>
    public const float Ambient = 0.8f;

    /// <summary>How strongly the luminance gradient bends the normal (Sobel output is small on these textures).</summary>
    public const float SobelStrength = 6;

    /// <summary>All continuous water images (Water1..Water8), in strip row order.</summary>
    public static readonly int[] WaterImages = [TerrainKind.Water1.ImageNumber(), TerrainKind.Water2.ImageNumber(),
        TerrainKind.Water3.ImageNumber(), TerrainKind.Water4.ImageNumber(), TerrainKind.Water5.ImageNumber(),
        TerrainKind.Water6.ImageNumber(), TerrainKind.Water7.ImageNumber(), TerrainKind.Water8.ImageNumber()];

    /// <summary>Side length of the water images (they all tile at 32x32 texels).</summary>
    public const int Tile = 32;

    /// <summary>The strip texture's side (8 frames next to each other, 8 image rows below each other).</summary>
    public const int StripSize = Frames * Tile;

    /// <summary>Strip row of an atlas image, -1 if it is no continuous water image.</summary>
    public static int RowOf(int image) => Array.IndexOf(WaterImages, image);

    /// <summary>Luminance of a packed RGBA8 pixel (R in the lowest byte), 0 to 1.</summary>
    public static float Luminance(uint pixel) =>
        (((pixel & 0xff) + ((pixel >> 8) & 0xff) + ((pixel >> 16) & 0xff)) / 765f);

    /// <summary>
    /// A tileable normal map from an image's luminance: central differences over the wrapping edges, so the
    /// map repeats with the image; flat parts face the viewer (0, 0, 1).
    /// </summary>
    public static Vector3[] NormalMap(uint[] pixels, int width, int height)
    {
        var normals = new Vector3[width * height];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            float right = Luminance(pixels[y * width + (x + 1) % width]);
            float left = Luminance(pixels[y * width + (x - 1 + width) % width]);
            float down = Luminance(pixels[((y + 1) % height) * width + x]);
            float up = Luminance(pixels[((y - 1 + height) % height) * width + x]);
            normals[y * width + x] = Vector3.Normalize(new Vector3(
                -(right - left) * SobelStrength, -(down - up) * SobelStrength, 1));
        }
        return normals;
    }

    /// <summary>The light direction of a frame: a turn around the image plane, from above.</summary>
    public static Vector3 LightOf(int frame)
    {
        float angle = frame / (float)Frames * MathHelper.TwoPi;
        return Vector3.Normalize(new Vector3(MathF.Cos(angle), MathF.Sin(angle), 0.55f));
    }

    /// <summary>One lit frame of a base pixel: its color under the frame's directional light on the normal map.</summary>
    public static uint LitPixel(uint pixel, Vector3 normal, int frame)
    {
        float light = Ambient + (1 - Ambient) * Math.Max(0, Vector3.Dot(normal, LightOf(frame)));
        int r = (int)MathF.Round((pixel & 0xff) * light);
        int g = (int)MathF.Round((pixel >> 8 & 0xff) * light);
        int b = (int)MathF.Round((pixel >> 16 & 0xff) * light);
        return (uint)(Math.Clamp(r, 0, 255) | Math.Clamp(g, 0, 255) << 8 | Math.Clamp(b, 0, 255) << 16) | 0xff000000;
    }

    /// <summary>
    /// The frames of one image as a horizontal strip (Frames tiles of Tile x Tile), each the base image lit by its
    /// frame's light over the image's own normal map. Row 0 of <see cref="BuildStrip"/>.
    /// </summary>
    public static uint[] FrameStrip(uint[] pixels, int width, int height)
    {
        var normals = NormalMap(pixels, width, height);
        var strip = new uint[Frames * Tile * Tile];
        for (int frame = 0; frame < Frames; frame++)
        for (int y = 0; y < Math.Min(height, Tile); y++)
        for (int x = 0; x < Math.Min(width, Tile); x++)
            strip[y * Frames * Tile + frame * Tile + x] = LitPixel(pixels[y * width + x], normals[y * width + x], frame);
        return strip;
    }

    /// <summary>Builds the strip texture: every continuous water image's lit frames, one image per row.</summary>
    public static Texture2D BuildStrip(GraphicsDevice device, GfxFile landscapeFile)
    {
        var data = new uint[StripSize * StripSize];
        for (int row = 0; row < WaterImages.Length; row++)
        {
            var bitmap = landscapeFile.ReadLandscape(WaterImages[row]);
            if (bitmap.Width <= 0 || bitmap.Height <= 0)
                continue;
            var strip = FrameStrip(bitmap.Pixels, bitmap.Width, bitmap.Height);
            int dst = row * Tile * StripSize;
            for (int y = 0; y < Tile; y++)
            for (int x = 0; x < Frames * Tile; x++)
                data[dst + y * StripSize + x] = strip[y * Frames * Tile + x];
        }
        var texture = new Texture2D(device, StripSize, StripSize);
        texture.SetData(data);
        return texture;
    }
}
