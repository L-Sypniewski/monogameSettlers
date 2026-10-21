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
    private static JsonElement Missions(JsonDocument document) =>
        document.RootElement.GetProperty("campaigns").GetProperty("settlers").GetProperty("peoples");

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

        var texts = new GfxDirectory(installation.GfxDirectory).Open(7).ReadTexts(0);
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(BuildingCatalog.DefaultDataDirectory, "campaign.json")));
        foreach (var people in Missions(document).EnumerateObject())
        {
            Assert.NotNull(installation.FindPath(people.Value.GetProperty("intro").GetString()!));
            Assert.NotNull(installation.FindPath(people.Value.GetProperty("outro").GetString()!));
            foreach (var mission in people.Value.GetProperty("missions").EnumerateArray())
            {
                string map = mission.GetProperty("map").GetString()!;
                string? path = installation.FindPath(map);
                Assert.True(path is not null, $"{map} exists");
                Assert.NotNull(installation.FindPath(mission.GetProperty("voice").GetString()!));
                Assert.StartsWith("MISSIONSNAME", texts[mission.GetProperty("text").GetInt32()]);
                Assert.StartsWith("-", texts[mission.GetProperty("hint").GetInt32()]);

                // The own player is the map's first, with the people of the campaign.
                var original = OriginalMap.Open(path!);
                var (world, report) = OriginalScenario.Load(original);
                Assert.Equal(Enum.Parse<Civilization>(people.Name), report.Catalogs[0].Civilization);
                Assert.Contains(world.Buildings, b => b.Player == 0);
                output.WriteLine($"{people.Name} {original.Name}: {report.Buildings} buildings, {report.Settlers} settlers, skipped {report.Skipped.Count}");
            }
        }
    }
}
