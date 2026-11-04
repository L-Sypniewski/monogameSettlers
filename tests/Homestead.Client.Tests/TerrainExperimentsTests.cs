using Homestead.Client.Terrain;

namespace Homestead.Client.Tests;

public class TerrainExperimentsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Without_entries_everything_stays_off(string? value)
    {
        var parsed = TerrainExperiments.Parse(value);

        Assert.Equal(TerrainExperiments.Off, parsed);
        Assert.False(parsed.IsOn);
        Assert.Empty(parsed.Unknown);
    }

    [Fact]
    public void Every_experiment_switches_on()
    {
        var parsed = TerrainExperiments.Parse("upscale2x,mips,depth,foam");

        Assert.True(parsed.Upscale2x);
        Assert.True(parsed.Mips);
        Assert.True(parsed.Depth);
        Assert.True(parsed.Foam);
        Assert.True(parsed.IsOn);
        Assert.Empty(parsed.Unknown);
    }

    [Fact]
    public void Entries_are_trimmed_and_case_insensitive()
    {
        var parsed = TerrainExperiments.Parse(" Mips , depth ");

        Assert.False(parsed.Upscale2x);
        Assert.True(parsed.Mips);
        Assert.True(parsed.Depth);
        Assert.False(parsed.Foam);
        Assert.Empty(parsed.Unknown);
    }

    [Fact]
    public void Unknown_entries_are_ignored_and_collected()
    {
        var parsed = TerrainExperiments.Parse("depth,raytracing,,4k");

        Assert.True(parsed.Depth);
        Assert.Equal(["raytracing", "4k"], parsed.Unknown);
    }

    [Fact]
    public void Tess2_and_tess4_are_one_knob()
    {
        var tess2 = TerrainExperiments.Parse("tess2");
        Assert.Equal(2, tess2.Tessellation);
        Assert.True(tess2.IsOn);
        Assert.Equal("tess2", tess2.ToString());

        Assert.Equal(4, TerrainExperiments.Parse("tess4").Tessellation);
        // tess4 wins when both are given.
        Assert.Equal(4, TerrainExperiments.Parse("tess2,tess4").Tessellation);
        Assert.Equal(1, TerrainExperiments.Off.Tessellation);
    }

    [Fact]
    public void Softlight_switches_on_and_prints_with_the_rest()
    {
        var parsed = TerrainExperiments.Parse("softlight,depth");

        Assert.True(parsed.SoftLight);
        Assert.True(parsed.IsOn);
        Assert.Equal("depth, softlight", parsed.ToString());
        Assert.Equal("upscale2x, mips, depth, foam, tess4, softlight", TerrainExperiments.All.ToString());
    }
}
