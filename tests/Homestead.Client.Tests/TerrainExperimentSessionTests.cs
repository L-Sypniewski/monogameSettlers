using Homestead.Client.Terrain;

namespace Homestead.Client.Tests;

public class TerrainExperimentSessionTests
{
    [Fact]
    public void Without_the_variable_nothing_runs_and_F5_cycles_the_four_stops_in_order()
    {
        var session = new TerrainExperimentSession(null);

        Assert.Equal(TerrainExperiments.Off, session.Active);
        Assert.False(session.IsOn);
        Assert.Equal(1, session.NextPresetIndex);

        Assert.Equal(TerrainExperiments.All, session.Cycle());
        Assert.Equal("all", session.ActiveName);
        Assert.Equal(TerrainExperiments.Water, session.Cycle());
        Assert.Equal("water", session.ActiveName);
        Assert.Equal(TerrainExperiments.Terrain, session.Cycle());
        Assert.Equal("terrain", session.ActiveName);
        Assert.Equal(TerrainExperiments.Off, session.Cycle());
        Assert.Equal("off", session.ActiveName);
        // The ring wraps: after off comes the first stop's successor again.
        Assert.Equal(TerrainExperiments.All, session.Cycle());
    }

    [Fact]
    public void The_presets_are_the_fixed_sets_of_the_ring()
    {
        Assert.False(TerrainExperiments.Off.IsOn);
        Assert.Equal(new TerrainExperiments(true, true, true, true, 4, true), TerrainExperiments.All);
        Assert.Equal(new TerrainExperiments(false, false, true, true, 4, true), TerrainExperiments.Water);
        Assert.Equal(new TerrainExperiments(true, true, false, false), TerrainExperiments.Terrain);
    }

    [Fact]
    public void An_empty_variable_starts_off_like_none()
    {
        var session = new TerrainExperimentSession("  ");

        Assert.Equal(TerrainExperiments.Off, session.Active);
        Assert.Empty(session.Unknown);
        Assert.Equal(TerrainExperiments.All, session.Cycle());
    }

    [Fact]
    public void A_variable_set_equal_to_a_preset_starts_the_ring_at_that_stop()
    {
        // The water preset: the first F5 goes to its successor.
        var water = new TerrainExperimentSession("depth,foam,tess4,softlight");
        Assert.Equal(TerrainExperiments.Water, water.Active);
        Assert.Equal(TerrainExperiments.Terrain, water.Cycle());

        // The terrain preset is the last stop: its successor wraps to off.
        var terrain = new TerrainExperimentSession("upscale2x,mips");
        Assert.Equal(TerrainExperiments.Terrain, terrain.Active);
        Assert.Equal(TerrainExperiments.Off, terrain.Cycle());
    }

    [Fact]
    public void A_custom_variable_set_runs_until_the_first_F5_goes_to_off()
    {
        var session = new TerrainExperimentSession("upscale2x,depth");
        var custom = new TerrainExperiments(true, false, true, false);

        Assert.Equal(custom, session.Active);
        Assert.Equal("upscale2x, depth", session.ActiveName);

        Assert.Equal(TerrainExperiments.Off, session.Cycle());
        // From there the ring continues normally.
        Assert.Equal(TerrainExperiments.All, session.Cycle());
        Assert.Equal(TerrainExperiments.Water, session.Cycle());
    }

    [Fact]
    public void Unknown_entries_stay_out_of_the_set()
    {
        var session = new TerrainExperimentSession("depth,rtx,,4k");

        Assert.Equal(["rtx", "4k"], session.Unknown);
        Assert.Equal(new TerrainExperiments(false, false, true, false), session.Active);
        Assert.Equal(TerrainExperiments.Off, session.Cycle());
    }

    [Fact]
    public void A_variable_of_only_unknown_entries_starts_off()
    {
        var session = new TerrainExperimentSession("raytracing");

        Assert.False(session.Active.IsOn);
        Assert.Equal(["raytracing"], session.Unknown);
        Assert.Equal(TerrainExperiments.All, session.Cycle());
    }

    [Fact]
    public void A_failed_upscale2x_leaves_the_active_set_and_every_preset_without_it()
    {
        var session = new TerrainExperimentSession("upscale2x,mips");
        Assert.Equal(TerrainExperiments.Off, session.Cycle());
        Assert.Equal(TerrainExperiments.All, session.Cycle());
        session.Upscale2xFailed();

        // The stop that was on keeps its name and set, minus upscale2x.
        Assert.Equal(new TerrainExperiments(false, true, true, true, 4, true), session.Active);
        Assert.Equal("all", session.ActiveName);

        // Water never had upscale2x; the terrain stop is degraded to mips; off stays off.
        Assert.Equal(TerrainExperiments.Water, session.Cycle());
        Assert.Equal(new TerrainExperiments(false, true, false, false), session.Cycle());
        Assert.Equal(TerrainExperiments.Off, session.Cycle());
        Assert.Equal(new TerrainExperiments(false, true, true, true, 4, true), session.Cycle());
    }
}
