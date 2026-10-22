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
            // Videos and spoken briefings where there are (not on the Mission CD).
            foreach (var video in new[] { "intro", "outro" })
                if (side.Value.TryGetProperty(video, out var path))
                    Assert.NotNull(installation.FindPath(path.GetString()!));
            foreach (var mission in side.Value.GetProperty("missions").EnumerateArray())
            {
                string map = mission.GetProperty("map").GetString()!;
                string? path = installation.FindPath(map);
                Assert.True(path is not null, $"{map} exists");
                if (mission.TryGetProperty("voice", out var voice))
                    Assert.NotNull(installation.FindPath(voice.GetString()!));
                Assert.StartsWith("MISSIONSNAME", texts[mission.GetProperty("text").GetInt32()]);
                // The settlers' hints are a list, the Mission CD's and the Amazons' campaign's headed "TIPS & TRICKS".
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
                // The map's thieves (settler type 23) come as scouts.
                Assert.Equal(original.Settlers.Count(s => s.Player == 0 && s.Type == 23),
                    world.Settlers.Count(s => s.Player == 0 && s.Profession == Profession.Scout));
                output.WriteLine($"{campaign}/{side.Name} {original.Name}: {report.Buildings} buildings, {report.Settlers} settlers, skipped {report.Skipped.Count}");
            }
        }
    }

    [Fact]
    public void Military_buildings_of_a_mission_hold_their_soldiers_and_its_priests_come_as_priests()
    {
        GameInstallation installation;
        try { installation = GameInstallation.Locate(); }
        catch (Exception) { output.WriteLine("No installation found, skipped."); return; }

        // The Amazon campaign's first mission of the three peoples: three priests (settler type 36) of player 1,
        // the Amazons' towers (type 15) with a swordsman and two bowmen, castles (18) with six infantry and four bowmen.
        var original = OriginalMap.Open(installation.FindPath("S3QOTA/MIS_M/G/G01.MAP")!);
        var (world, _) = OriginalScenario.Load(original);
        var priests = world.Settlers.Where(s => s.Player == 0 && s.Profession == Profession.Priest).ToList();
        Assert.Equal(3, priests.Count);
        Assert.All(priests, p => Assert.Equal(world.PeopleOf(0), p.People));

        var military = world.Buildings.Where(b => b.Player == 1 && b.Definition.Occupiers.Count > 0).ToList();
        Assert.Contains(military, b => b.Definition.Name == "tower");
        Assert.DoesNotContain(world.Buildings, b => b.Definition.Name == "baker" && world.Military.IsOccupied(b));
        foreach (var (building, map) in military.Zip(original.Buildings.Where(b => b.Player == 1 && b.Type is 15 or 16 or 18)))
        {
            var inside = world.Military.Occupants(building).Count(s => s is not null);
            if (map.Garrison.Count == 0)
            {
                // Empty in the map: one swordsman of level 1, as in the original.
                Assert.Equal(1, inside);
                Assert.Equal(1, world.Military.Assigned(building, SoldierType.Swordsman));
                continue;
            }
            int bowmen = map.Garrison.Bowmen.Sum(), bowPlaces = building.Definition.Occupiers.Count(o => o.ForBowman);
            Assert.Equal(Math.Min(map.Garrison.Count - bowmen, building.Definition.Occupiers.Count - bowPlaces) + Math.Min(bowmen, bowPlaces), inside);
            if (inside > 0)
                Assert.All(Enum.GetValues<SoldierType>(), t => Assert.Equal(world.Military.Assigned(building, t), world.Military.Wanted(building, t)));
        }
        Assert.Contains(military, b => world.Military.IsOccupied(b));
        // Player 1's towers stand empty in the map: each gets its swordsman.
        Assert.All(world.Buildings.Where(b => b.Player == 0 && b.Definition.Occupiers.Count > 0), b => Assert.True(world.Military.IsOccupied(b)));
    }
}
