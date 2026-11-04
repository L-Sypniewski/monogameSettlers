using Homestead.Client.Terrain;
using Microsoft.Xna.Framework;

namespace Homestead.Client.Tests;

public class TerrainAtlasScaleTests
{
    [Theory]
    [InlineData(0, 123, 77, true)]  // continuous water, five cells
    [InlineData(1, 17, 23, false)]  // continuous sand, five cells
    [InlineData(16, 5, 31, true)]   // the crest image, two cells
    [InlineData(5, 0, 0, false)]    // single tile
    [InlineData(6, 41, 19, true)]
    public void A_doubled_atlas_samples_the_same_normalized_point(int image, int x, int y, bool up)
    {
        var one = new Vector2[3];
        var two = new Vector2[3];
        try
        {
            TerrainAtlas.Scale = 1;
            TerrainMesh.ContinuousUv(image, x, y, up, one);
            TerrainAtlas.Scale = 2;
            TerrainMesh.ContinuousUv(image, x, y, up, two);
        }
        finally
        {
            TerrainAtlas.Scale = 1;
        }

        // Every value of the texel math scales by exactly two, so the UVs stay bit for bit the same.
        Assert.Equal(one, two);
    }
}
