using Homestead.Assets.Maps;
using Homestead.Client.Terrain;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Homestead.Client.Tests;

public class WaveLayerTessTests
{
    /// <summary>A map with a block of water (landscape 0) in grass (16), land lifted so the light varies.</summary>
    private static WaveLayer WaterLayer(out TerrainMesh mesh, int size = 12, int from = 2, int to = 9)
    {
        var area = new MapArea(size);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            bool water = x >= from && x <= to && y >= from && y <= to;
            area.Landscape[y * size + x] = water ? (byte)0 : (byte)16;
            area.Height[y * size + x] = (byte)(water ? 0 : 2 + ((x + y) & 3));
        }
        mesh = new TerrainMesh(null!, area);
        return new WaveLayer(mesh);
    }

    [Fact]
    public void Bilinear_is_exact_at_grid_points_and_linear_between()
    {
        float[] three = [0, 1, 2, 3, 4, 5, 6, 7, 8]; // value = y * 3 + x

        Assert.Equal(4f, WaveLayer.Bilinear(three, 3, 1, 1));
        Assert.Equal(6f, WaveLayer.Bilinear(three, 3, 0, 2));
        Assert.Equal(2.5f, WaveLayer.Bilinear(three, 3, 1, 0.5f));
        Assert.Equal(4.5f, WaveLayer.Bilinear(three, 3, 1.5f, 1));
        Assert.Equal(6f, WaveLayer.Bilinear(three, 3, 1.5f, 1.5f));
    }

    [Fact]
    public void Bilinear_clamps_outside_the_field()
    {
        float[] three = [0, 1, 2, 3, 4, 5, 6, 7, 8];

        Assert.Equal(1.5f, WaveLayer.Bilinear(three, 3, -5, 0.5f));
        Assert.Equal(6.5f, WaveLayer.Bilinear(three, 3, 99, 1.5f));
    }

    [Fact]
    public void StrengthBilinear_interpolates_the_strength_field()
    {
        var layer = WaterLayer(out _);

        // Linear between neighbors along a row …
        float left = layer.StrengthBilinear(5, 5), right = layer.StrengthBilinear(6, 5);
        Assert.Equal(0.75f * left + 0.25f * right, layer.StrengthBilinear(5.25f, 5), 5);
        Assert.Equal(0.5f * (left + right), layer.StrengthBilinear(5.5f, 5), 5);
        // … open sea is strong, land far from the water is calm.
        Assert.True(layer.StrengthBilinear(5, 5) > 0.05f);
        Assert.Equal(0f, layer.StrengthBilinear(0, 0));
    }

    [Fact]
    public void SubCellCorners_follow_the_terrain_diagonal_pattern()
    {
        // n=1: exactly the field cell's corners, in the terrain's up/down order.
        var up = WaveLayer.SubCellCorners(0, 0, 1, up: true);
        Assert.Equal((0f, 0f), up.A);
        Assert.Equal((0f, 1f), up.B);
        Assert.Equal((1f, 1f), up.C);
        var down = WaveLayer.SubCellCorners(0, 0, 1, up: false);
        Assert.Equal((0f, 0f), down.A);
        Assert.Equal((1f, 1f), down.B);
        Assert.Equal((1f, 0f), down.C);

        // Finer scale: sub-cell (1, 2) of a 4x4 split spans x 0.25…0.5, y 0.5…0.75.
        var quarter = WaveLayer.SubCellCorners(1, 2, 4, up: true);
        Assert.Equal((0.25f, 0.5f), quarter.A);
        Assert.Equal((0.25f, 0.75f), quarter.B);
        Assert.Equal((0.5f, 0.75f), quarter.C);

        // Adjacent sub-cells share their corners.
        Assert.Equal(WaveLayer.SubCellCorners(0, 1, 2, up: true).C, WaveLayer.SubCellCorners(1, 1, 2, up: true).B);
        Assert.Equal(WaveLayer.SubCellCorners(1, 0, 2, up: true).B, WaveLayer.SubCellCorners(1, 1, 2, up: true).A);
    }

    [Fact]
    public void Every_subtriangle_lies_in_one_half_of_its_cell()
    {
        foreach (int tess in new[] { 1, 2, 4 })
        foreach (int sy in Enumerable.Range(0, tess))
        foreach (int sx in Enumerable.Range(0, tess))
        foreach (bool up in new[] { true, false })
        {
            bool parentUp = WaveLayer.ParentIsUp(sx, sy, up);
            var (a, b, c) = WaveLayer.SubCellCorners(sx, sy, tess, up);
            foreach (var (u, v) in new[] { a, b, c })
            {
                var (w0, w1, w2) = WaveLayer.ParentUvWeights(u, v, parentUp);
                Assert.True(w0 >= 0 && w1 >= 0 && w2 >= 0, $"negative weight at ({u}, {v}), tess {tess}, sub ({sx}, {sy})");
                Assert.Equal(1f, w0 + w1 + w2, 5);
            }
        }
    }

    [Fact]
    public void Uv_weights_pick_the_parent_corners_exactly()
    {
        // At the cell corners one weight is exactly 1, so the classic corner UVs come out unchanged.
        Assert.Equal((1f, 0f, 0f), WaveLayer.ParentUvWeights(0, 0, parentUp: true));
        Assert.Equal((0f, 1f, 0f), WaveLayer.ParentUvWeights(0, 1, parentUp: true));
        Assert.Equal((0f, 0f, 1f), WaveLayer.ParentUvWeights(1, 1, parentUp: true));
        Assert.Equal((1f, 0f, 0f), WaveLayer.ParentUvWeights(0, 0, parentUp: false));
        Assert.Equal((0f, 1f, 0f), WaveLayer.ParentUvWeights(1, 1, parentUp: false));
        Assert.Equal((0f, 0f, 1f), WaveLayer.ParentUvWeights(1, 0, parentUp: false));
    }

    /// <summary>The pre-experiment corner loop: what the water pass emitted before tess2/tess4/softlight.</summary>
    private static List<VertexPositionColorTexture> ClassicEmission(
        TerrainMesh mesh, WaveLayer layer, int x0, int x1, int y0, int y1, float seconds)
    {
        var expected = new List<VertexPositionColorTexture>();
        var uv = new Vector2[3];
        for (int y = y0 + 1; y < y1; y++)
        for (int x = x0; x < x1; x++)
        for (int half = 0; half < 2; half++)
        {
            bool up = half == 0;
            (int X, int Y)[] corners = [(x, y), up ? (x, y + 1) : (x + 1, y + 1), up ? (x + 1, y + 1) : (x + 1, y)];
            if (layer.StrengthBilinear(corners[0].X, corners[0].Y) + layer.StrengthBilinear(corners[1].X, corners[1].Y)
                + layer.StrengthBilinear(corners[2].X, corners[2].Y) <= 0)
                continue;
            mesh.TriangleUv(x, y, up, uv);
            for (int c = 0; c < 3; c++)
            {
                var (cx, cy) = corners[c];
                float lift = WaveLayer.MaxLift * layer.StrengthBilinear(cx, cy) * WaveLayer.Wave(cx, cy, seconds);
                float liftAbove = WaveLayer.MaxLift * layer.StrengthBilinear(cx, cy - 1) * WaveLayer.Wave(cx, cy - 1, seconds);
                float height = mesh.HeightAt(cx, cy) + lift;
                float light = TerrainMesh.Light(mesh.HeightAt(cx, cy - 1) + liftAbove, height);
                expected.Add(new VertexPositionColorTexture(
                    new Vector3(cx * ScreenProjection.TileWidth - cy * ScreenProjection.TileWidth / 2f,
                        cy * ScreenProjection.RowHeight - height * ScreenProjection.HeightStep, 0),
                    new Color(light, light, light), uv[c]));
            }
        }
        return expected;
    }

    [Fact]
    public void Water_at_tess_1_is_bit_for_bit_the_classic_corner_emission()
    {
        var layer = WaterLayer(out var mesh);
        const float seconds = 3.7f;
        int x0 = 0, x1 = 11, y0 = 1, y1 = 11;

        layer.Precompute(x0, x1, y0, y1, x1 - x0 + 1, seconds, 1);
        int count = layer.EmitWater(x0, x1, y0, y1, depthShading: false, softLight: false, tess: 1);
        var emitted = layer.WaterBuffer.AsSpan(0, count).ToArray();
        var expected = ClassicEmission(mesh, layer, x0, x1, y0, y1, seconds);

        Assert.NotEmpty(expected);
        Assert.Equal(expected.Count, emitted.Length);
        for (int i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].Position, emitted[i].Position);
            Assert.Equal(expected[i].Color, emitted[i].Color);
            Assert.Equal(expected[i].TextureCoordinate, emitted[i].TextureCoordinate);
        }
    }

    [Fact]
    public void Tessellation_emits_more_triangles_over_the_same_water()
    {
        var layer = WaterLayer(out _);
        const float seconds = 3.7f;
        int x0 = 0, x1 = 11, y0 = 1, y1 = 11;
        int width = x1 - x0 + 1;

        layer.Precompute(x0, x1, y0, y1, width, seconds, 1);
        int coarse = layer.EmitWater(x0, x1, y0, y1, depthShading: false, softLight: false, tess: 1);
        layer.Precompute(x0, x1, y0, y1, width, seconds, 2);
        int twice = layer.EmitWater(x0, x1, y0, y1, depthShading: false, softLight: false, tess: 2);
        layer.Precompute(x0, x1, y0, y1, width, seconds, 4);
        int fourTimes = layer.EmitWater(x0, x1, y0, y1, depthShading: false, softLight: false, tess: 4);

        // 4x/16x the triangles over open water; only the shore fringe, where the finer culling can add
        // sub-cells over corners the coarse culling dropped, keeps it from being exact.
        Assert.InRange(twice, 3.5f * coarse, 4.5f * coarse);
        Assert.InRange(fourTimes, 14f * coarse, 18f * coarse);
    }

    [Fact]
    public void Softlight_keeps_the_geometry_and_brightens_only()
    {
        var layer = WaterLayer(out _);
        const float seconds = 3.7f;
        int x0 = 0, x1 = 11, y0 = 1, y1 = 11;

        layer.Precompute(x0, x1, y0, y1, x1 - x0 + 1, seconds, 1);
        int classic = layer.EmitWater(x0, x1, y0, y1, depthShading: false, softLight: false, tess: 1);
        var before = layer.WaterBuffer.AsSpan(0, classic).ToArray();
        int softened = layer.EmitWater(x0, x1, y0, y1, depthShading: false, softLight: true, tess: 1);
        var after = layer.WaterBuffer.AsSpan(0, softened).ToArray();

        Assert.Equal(classic, softened);
        for (int i = 0; i < classic; i++)
        {
            Assert.Equal(before[i].Position, after[i].Position);
            Assert.Equal(before[i].TextureCoordinate, after[i].TextureCoordinate);
            Assert.True(after[i].Color.R >= before[i].Color.R && after[i].Color.G >= before[i].Color.G
                && after[i].Color.B >= before[i].Color.B);
        }
    }
}
