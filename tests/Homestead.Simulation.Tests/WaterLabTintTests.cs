using Homestead.Client.Terrain;

namespace Homestead.Simulation.Tests;

/// <summary>The water lab's rings tint: which kinds count as water rings, and the ramp's shape and size.</summary>
public class WaterLabTintTests
{
    [Fact]
    public void RingIndexesWater1ToWater8()
    {
        Assert.Equal(0, WaterLabTint.Ring(TerrainKind.Water1));
        Assert.Equal(7, WaterLabTint.Ring(TerrainKind.Water8));
        for (TerrainKind kind = TerrainKind.Water1; kind <= TerrainKind.Water8; kind++)
            Assert.InRange(WaterLabTint.Ring(kind), 0, 7);
    }

    [Theory]
    [InlineData(TerrainKind.Grass)]
    [InlineData(TerrainKind.Sand)]
    [InlineData(TerrainKind.River4)]
    [InlineData(TerrainKind.Mountain)]
    public void OtherKindsAreNotTinted(TerrainKind kind)
    {
        Assert.Equal(-1, WaterLabTint.Ring(kind));
        Assert.Equal(new Microsoft.Xna.Framework.Vector3(1, 1, 1), WaterLabTint.For(kind));
    }

    [Fact]
    public void ShoreIsUntintedDeepEndReachesTheRampConstant()
    {
        Assert.Equal(new Microsoft.Xna.Framework.Vector3(1, 1, 1), WaterLabTint.For(TerrainKind.Water1));
        Assert.Equal(WaterLabTint.Deep, WaterLabTint.For(TerrainKind.Water8));
    }

    [Fact]
    public void RampIsMonotonicAndSubtle()
    {
        var before = WaterLabTint.For(TerrainKind.Water1);
        for (TerrainKind kind = TerrainKind.Water2; kind <= TerrainKind.Water8; kind++)
        {
            var tint = WaterLabTint.For(kind);
            var deep = WaterLabTint.Deep;
            Assert.True(tint.X <= before.X && tint.Y <= before.Y, $"each ring is at least as deep as the one before: {kind}");
            // Subtle by design: at most ~12% darkening, and cooler (red drops furthest, blue least).
            Assert.InRange(tint.X, deep.X - 0.001f, 1.001f);
            Assert.InRange(tint.Y, deep.Y - 0.001f, 1.001f);
            Assert.InRange(tint.Z, deep.Z - 0.001f, 1.001f);
            Assert.True(tint.X <= tint.Z, $"the tint cools (red below blue): {kind}");
            before = tint;
        }
    }
}
