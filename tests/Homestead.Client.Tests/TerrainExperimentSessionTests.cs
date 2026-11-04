using Homestead.Client.Terrain;

namespace Homestead.Client.Tests;

public class TerrainExperimentSessionTests
{
    [Fact]
    public void Without_the_variable_nothing_runs_and_F5_selects_all_four()
    {
        var session = new TerrainExperimentSession(null);

        Assert.Equal(TerrainExperiments.Off, session.Active);
        Assert.Equal(4, TerrainExperiments.All.Tessellation);
        Assert.True(TerrainExperiments.All.SoftLight);
        Assert.False(session.IsOn);
        Assert.Equal(TerrainExperiments.All, session.Selected);
        Assert.True(TerrainExperiments.All.IsOn);

        Assert.Equal(TerrainExperiments.All, session.Toggle());
        Assert.Equal(TerrainExperiments.Off, session.Toggle());
        Assert.Equal(TerrainExperiments.All, session.Toggle());
    }

    [Fact]
    public void An_empty_variable_selects_all_four_and_runs_nothing()
    {
        var session = new TerrainExperimentSession("  ");

        Assert.Equal(TerrainExperiments.Off, session.Active);
        Assert.Equal(TerrainExperiments.All, session.Selected);
        Assert.Empty(session.Unknown);
    }

    [Fact]
    public void The_variable_runs_at_startup_and_F5_flips_to_its_set()
    {
        var session = new TerrainExperimentSession("upscale2x,depth");
        var expected = new TerrainExperiments(true, false, true, false);

        Assert.Equal(expected, session.Active);
        Assert.Equal(expected, session.Selected);

        Assert.Equal(TerrainExperiments.Off, session.Toggle());
        Assert.Equal(expected, session.Toggle());
    }

    [Fact]
    public void Unknown_entries_stay_out_of_both_sets()
    {
        var session = new TerrainExperimentSession("depth,rtx,,4k");

        Assert.Equal(["rtx", "4k"], session.Unknown);
        // The variable is set, so its (known) entries run at startup.
        Assert.Equal(new TerrainExperiments(false, false, true, false), session.Active);
        Assert.Equal(TerrainExperiments.Off, session.Toggle());
        Assert.Equal(new TerrainExperiments(false, false, true, false), session.Toggle());
    }

    [Fact]
    public void A_variable_of_only_unknown_entries_selects_all_four()
    {
        var session = new TerrainExperimentSession("raytracing");

        Assert.False(session.Active.IsOn);
        Assert.Equal(TerrainExperiments.All, session.Selected);
        Assert.Equal(["raytracing"], session.Unknown);
    }

    [Fact]
    public void A_failed_upscale2x_leaves_both_sets_without_it()
    {
        var session = new TerrainExperimentSession("upscale2x,mips");
        var withoutUpscale = new TerrainExperiments(false, true, false, false);
        session.Toggle();
        session.Upscale2xFailed();

        // Off stays off; what F5 turns on afterwards is the selected set without upscale2x.
        Assert.Equal(TerrainExperiments.Off, session.Active);
        Assert.Equal(withoutUpscale, session.Selected);
        Assert.Equal(withoutUpscale, session.Toggle());
    }
}
