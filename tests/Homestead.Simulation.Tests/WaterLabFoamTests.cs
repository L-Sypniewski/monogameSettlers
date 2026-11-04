using Homestead.Client.Terrain;

namespace Homestead.Simulation.Tests;

/// <summary>The water lab's foam band: where it lives along the shore, its pulse, and the dither that breaks the ring.</summary>
public class WaterLabFoamTests
{
    [Fact]
    public void BandIsZeroAtTheBeachAndOnTheOpenSea()
    {
        Assert.Equal(0, WaterLabFoam.Band(0));
        Assert.Equal(0, WaterLabFoam.Band(-0.1f));
        Assert.Equal(0, WaterLabFoam.Band(0.2f));
        Assert.Equal(0, WaterLabFoam.Band(1));
    }

    [Fact]
    public void BandPlateausShortlyBeforeTheShore()
    {
        Assert.Equal(1, WaterLabFoam.Band(0.1f), 5);
        // Both edges are smooth: values between the ends and the plateau lie strictly inside.
        foreach (float strength in new[] { 0.02f, 0.05f, 0.15f, 0.19f })
            Assert.InRange(WaterLabFoam.Band(strength), 0.001f, 0.999f);
    }

    [Fact]
    public void HashIsDeterministicAndSpread()
    {
        var seen = new HashSet<float>();
        for (int x = -20; x < 20; x++)
        for (int y = -20; y < 20; y++)
        {
            float a = WaterLabFoam.Hash01(x, y), b = WaterLabFoam.Hash01(x, y);
            Assert.Equal(a, b);
            Assert.InRange(a, 0, 1);
            seen.Add(a);
        }
        Assert.True(seen.Count > 1000, $"the hash spreads over the plane, not a few values: {seen.Count}");
    }

    [Fact]
    public void AlphaFollowsTheBandAndThePulse()
    {
        // On the beach and on the open sea there is never foam, whatever the swell does.
        for (float phase = 0; phase < 6.3f; phase += 0.3f)
        {
            Assert.Equal(0, WaterLabFoam.Alpha(0, phase, 3, 4));
            Assert.Equal(0, WaterLabFoam.Alpha(1, phase, 3, 4));
        }
        // In the band the swell washes it in and out: some phase has none, the crest phase reaches the peak.
        float peak = 0, still = 0;
        for (int step = 0; step < 100; step++)
        {
            float alpha = WaterLabFoam.Alpha(0.1f, step * 0.063f, 3, 4);
            peak = Math.Max(peak, alpha);
            still += alpha < 0.001f ? 1 : 0;
        }
        Assert.InRange(peak, WaterLabFoam.FoamAlpha * 0.75f, WaterLabFoam.FoamAlpha * 1.25f);
        Assert.True(still > 0, "there is a phase in which the foam has washed out");
    }

    [Fact]
    public void DitherBreaksThePerfectRing()
    {
        float phase = MathF.PI / 2; // pulse at its peak: without dither every point would be equal
        var alphas = new HashSet<float>();
        for (int x = 0; x < 40; x++)
            alphas.Add(WaterLabFoam.Alpha(0.1f, phase, x, 5));
        Assert.True(alphas.Count > 10, $"neighboring points differ, the ring is not a perfect one: {alphas.Count}");
    }

    [Fact]
    public void AlphaStaysWithinZeroAndOne()
    {
        for (int x = -5; x < 5; x++)
        for (int y = -5; y < 5; y++)
        for (int step = 0; step < 10; step++)
        {
            float alpha = WaterLabFoam.Alpha(0.12f, step * 0.7f, x, y);
            Assert.InRange(alpha, 0, 1);
        }
    }
}
