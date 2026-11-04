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
}
