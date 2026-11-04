using Homestead.Client.Terrain;
using Microsoft.Xna.Framework;

namespace Homestead.Simulation.Tests;

/// <summary>The water lab's normals math: Sobel normal map from luminance, tileable at the wrap, lit frames.</summary>
public class WaterLabNormalsTests
{
    private static uint Pixel(byte r, byte g, byte b) => (uint)(r | g << 8 | b << 16 | 0xff000000);

    [Fact]
    public void LuminanceOfPackedPixels()
    {
        Assert.Equal(0, WaterLabNormals.Luminance(Pixel(0, 0, 0)));
        Assert.Equal(1, WaterLabNormals.Luminance(Pixel(255, 255, 255)));
        Assert.Equal(0.5f, WaterLabNormals.Luminance(Pixel(128, 128, 128)), 2);
    }

    [Fact]
    public void FlatImageFacesTheViewer()
    {
        var flat = new uint[WaterLabNormals.Tile * WaterLabNormals.Tile];
        Array.Fill(flat, Pixel(60, 90, 120));
        foreach (var normal in WaterLabNormals.NormalMap(flat, WaterLabNormals.Tile, WaterLabNormals.Tile))
            Assert.Equal(new Vector3(0, 0, 1), normal);
    }

    [Fact]
    public void NormalMapIsTileable()
    {
        // A bright column and row (not aligned to the edges): the map must not change when the image is shifted.
        int size = WaterLabNormals.Tile;
        var image = new uint[size * size];
        Array.Fill(image, Pixel(40, 40, 40));
        for (int y = 0; y < size; y++)
            image[y * size + 11] = Pixel(200, 200, 200);
        for (int x = 0; x < size; x++)
            image[7 * size + x] = Pixel(200, 200, 200);
        var map = WaterLabNormals.NormalMap(image, size, size);
        var shifted = new uint[image.Length];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
            shifted[y * size + x] = image[y * size + (x + 5) % size];
        var shiftedMap = WaterLabNormals.NormalMap(shifted, size, size);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
            Assert.Equal(map[y * size + (x + 5) % size], shiftedMap[y * size + x]);
    }

    [Fact]
    public void GradientTiltsTheNormalUphill()
    {
        int size = WaterLabNormals.Tile;
        var ramp = new uint[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
            ramp[y * size + x] = Pixel((byte)(20 * x % 256), (byte)(20 * x % 256), (byte)(20 * x % 256));
        var map = WaterLabNormals.NormalMap(ramp, size, size);
        // Brightening to the right: the surface tips left (negative x), staying flat along y.
        Assert.True(map[size + 8].X < -0.01f, $"tilted left, not {map[size + 8]}");
        Assert.Equal(0, map[size + 8].Y, 3);
        Assert.True(map[size + 8].Z > 0.5f, "mostly facing the viewer");
    }

    [Fact]
    public void LitFramesStayInsideTheAmbientDiffuseRange()
    {
        var pixel = Pixel(100, 150, 200);
        for (int frame = 0; frame < WaterLabNormals.Frames; frame++)
        {
            uint lit = WaterLabNormals.LitPixel(pixel, Vector3.UnitZ, frame);
            float least = WaterLabNormals.Ambient * 100;
            float most = 100f;
            float red = lit & 0xff;
            Assert.InRange(red, least - 1, most + 1);
            Assert.Equal(0xff000000, lit & 0xff000000);
        }
        // The light really turns: a tilted normal is lit brighter when the light comes from its side.
        var tilt = Vector3.Normalize(new Vector3(1, 0, 1));
        Assert.True(WaterLabNormals.LitPixel(pixel, tilt, 0) > WaterLabNormals.LitPixel(pixel, tilt, 4));
    }

    [Fact]
    public void FrameStripLaysOutFramesSideBySide()
    {
        int size = WaterLabNormals.Tile;
        var flat = new uint[size * size];
        Array.Fill(flat, Pixel(100, 100, 100));
        var strip = WaterLabNormals.FrameStrip(flat, size, size);
        Assert.Equal(WaterLabNormals.Frames * size * size, strip.Length);
        float flatLight = WaterLabNormals.Ambient + (1 - WaterLabNormals.Ambient)
            * Math.Max(0, Vector3.Dot(Vector3.UnitZ, WaterLabNormals.LightOf(0)));
        Assert.Equal((int)MathF.Round(100 * flatLight), (int)(strip[0] & 0xff));
    }

    [Fact]
    public void StripRowsFollowTheWaterImages()
    {
        Assert.Equal(0, WaterLabNormals.RowOf(TerrainKind.Water1.ImageNumber()));
        Assert.Equal(7, WaterLabNormals.RowOf(TerrainKind.Water8.ImageNumber()));
        Assert.Equal(-1, WaterLabNormals.RowOf(TerrainKind.Grass.ImageNumber()));
        // Distinct images only: no row is claimed twice.
        Assert.Equal(WaterLabNormals.WaterImages.Length, WaterLabNormals.WaterImages.Distinct().Count());
    }
}
