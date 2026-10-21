using System.Text.Json;
using Homestead.Assets;
using Homestead.Assets.Gfx;
using Homestead.Assets.Maps;
using Homestead.Simulation.Buildings;
using Xunit.Abstractions;

namespace Homestead.Simulation.Tests;

/// <summary>The campaigns of data/campaign.json against the original installation; pass unchecked without one.</summary>
public class CampaignTests(ITestOutputHelper output)
{
    /// <summary>Every side of every campaign with its campaign's id.</summary>
    private static IEnumerable<(string Campaign, JsonProperty Side)> Sides(JsonDocument document) =>
        document.RootElement.GetProperty("campaigns").EnumerateObject()
            .SelectMany(c => c.Value.GetProperty("sides").EnumerateObject().Select(s => (c.Name, s)));

    [Fact]
    public void The_text_table_holds_the_mission_descriptions_in_german_and_english()
    {
        GameInstallation installation;
        try { installation = GameInstallation.Locate(); }
        catch (Exception) { output.WriteLine("No installation found, skipped."); return; }

        var texts = new GfxDirectory(installation.GfxDirectory).Open(7);
        var german = texts.ReadTexts(0);
        var english = texts.ReadTexts(1);
        Assert.Equal(204, german.Count);
        Assert.Equal(german.Count, english.Count);
        Assert.StartsWith("MISSIONSNAME:   ERSTE SCHRITTE", german[0]);
        Assert.Contains("Römische", german[0], StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("MISSION NAME:  FIRST STEPS", english[0]);
        Assert.Empty(texts.ReadTexts(8));
    }

    [Fact]
    public void Every_mission_has_its_map_voice_and_texts_and_loads()
    {
        GameInstallation installation;
        try { installation = GameInstallation.Locate(); }
        catch (Exception) { output.WriteLine("No installation found, skipped."); return; }

        var gfx = new GfxDirectory(installation.GfxDirectory);
        var texts = gfx.Open(7).ReadTexts(0);
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(BuildingCatalog.DefaultDataDirectory, "campaign.json")));
        foreach (var (campaign, side) in Sides(document))
        {
            Assert.NotNull(installation.FindPath(side.Value.GetProperty("intro").GetString()!));
            Assert.NotNull(installation.FindPath(side.Value.GetProperty("outro").GetString()!));
            foreach (var mission in side.Value.GetProperty("missions").EnumerateArray())
            {
                string map = mission.GetProperty("map").GetString()!;
                string? path = installation.FindPath(map);
                Assert.True(path is not null, $"{map} exists");
                Assert.NotNull(installation.FindPath(mission.GetProperty("voice").GetString()!));
                Assert.StartsWith("MISSIONSNAME", texts[mission.GetProperty("text").GetInt32()]);
                // The settlers' hints are a list, the Amazons' campaign headed "TIPS & TRICKS".
                Assert.Matches("^(-|TIPS & TRICKS)", texts[mission.GetProperty("hint").GetInt32()]);

                if (mission.TryGetProperty("preview", out var preview))
                    Assert.True(gfx.Open(preview[0].GetInt32()).ReadSettler(preview[1].GetInt32(), preview[2].GetInt32()).Width > 200);

                // The own player is the map's first: with the side's people (the three peoples' side: one of them),
                // a building or settlers to found one.
                var original = OriginalMap.Open(path!);
                var (world, report) = OriginalScenario.Load(original);
                var own = report.Catalogs[0].Civilization;
                if (Enum.TryParse<Civilization>(side.Name, out var people))
                    Assert.Equal(people, own);
                else
                    Assert.Equal(side.Name == "amazons", own == Civilization.Amazon);
                Assert.True(world.Buildings.Any(b => b.Player == 0) || world.Settlers.Any(s => s.Player == 0), $"{original.Name}: player 1 has something");
                output.WriteLine($"{campaign}/{side.Name} {original.Name}: {report.Buildings} buildings, {report.Settlers} settlers, skipped {report.Skipped.Count}");
            }
        }
    }
}
