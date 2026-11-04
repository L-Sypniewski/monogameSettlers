using Homestead.Client.Terrain;

namespace Homestead.Simulation.Tests;

/// <summary>The water lab's session state: parsing HOMESTEAD_WATER, live get/set, unknown entries.</summary>
public class WaterLabTests
{
    private static void WithState(WaterLabState state, Action act)
    {
        var before = WaterLab.Current;
        WaterLab.Set(state);
        try { act(); }
        finally { WaterLab.Set(before); }
    }

    [Fact]
    public void ParseWithoutVariableIsAllOff()
    {
        foreach (string? empty in new string?[] { null, "", "   " })
        {
            var (state, unknown) = WaterLab.Parse(empty);
            Assert.True(state.None);
            Assert.Empty(unknown);
        }
    }

    [Theory]
    [InlineData("flatwater", nameof(WaterLabState.FlatWater))]
    [InlineData("pixelsurf", nameof(WaterLabState.Pixelsurf))]
    [InlineData("normals", nameof(WaterLabState.Normals))]
    [InlineData("foam", nameof(WaterLabState.Foam))]
    [InlineData("rings", nameof(WaterLabState.Rings))]
    public void ParseSingleFlagIsCaseInsensitive(string entry, string flag)
    {
        foreach (string spelling in new[] { entry, entry.ToUpperInvariant(), " " + entry + " " })
        {
            var (state, unknown) = WaterLab.Parse(spelling);
            Assert.Empty(unknown);
            Assert.True((bool)typeof(WaterLabState).GetProperty(flag)!.GetValue(state)!);
            // Only the one flag is on: the record equals an empty one with just that flag set.
            var expected = new WaterLabState();
            typeof(WaterLabState).GetProperty(flag)!.SetValue(expected, true);
            Assert.Equal(expected, state);
        }
    }

    [Fact]
    public void ParseCommaListSetsEveryFlag()
    {
        var (state, unknown) = WaterLab.Parse("Foam,flatwater,RINGS , pixelsurf,normals");
        Assert.Empty(unknown);
        Assert.True(state.FlatWater && state.Pixelsurf && state.Normals && state.Foam && state.Rings);
        Assert.False(state.None);
    }

    [Fact]
    public void ParseReportsUnknownEntriesWithoutTurningFlagsOn()
    {
        var (state, unknown) = WaterLab.Parse("flatwater,waves,shaders,foam");
        Assert.Equal(new[] { "waves", "shaders" }, unknown);
        Assert.True(state.FlatWater);
        Assert.True(state.Foam);
        Assert.False(state.Normals);
    }

    [Fact]
    public void SetReplacesTheLiveStatePerFlag()
    {
        WithState(new WaterLabState(), () =>
        {
            Assert.False(WaterLab.Rings);
            WaterLab.Set(WaterLab.Current with { Rings = true, Foam = true });
            Assert.True(WaterLab.Rings);
            Assert.True(WaterLab.Foam);
            Assert.False(WaterLab.FlatWater);
        });
    }

    [Fact]
    public void ToStringListsTheActiveFlags()
    {
        Assert.Equal("(none)", new WaterLabState().ToString());
        Assert.Equal("flatwater, rings", new WaterLabState { FlatWater = true, Rings = true }.ToString());
    }
}
