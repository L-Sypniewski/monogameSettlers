using Homestead.Assets;
using Homestead.Assets.Maps;
using Homestead.Simulation.Ai;
using Homestead.Simulation.Buildings;
using Homestead.Simulation.Economy;
using Homestead.Simulation.Map;
using Xunit.Abstractions;

namespace Homestead.Simulation.Tests;

public class AiTests(ITestOutputHelper output)
{
    private static readonly BuildingCatalog Romans = BuildingCatalog.Load(Civilization.Roman);

    [Fact]
    public void The_ai_expands_towards_quarry_stone_its_start_land_lacks()
    {
        var map = new GameMap(160);
        var world = new World(map, TestConfig.NoTools) { TerritoryRules = true };
        var tower = world.PlaceBuilding(Romans["tower"], 0, new GridPoint(40, 80), force: true)!; // like the start equipment
        for (int i = 0; i < 10; i++)
            world.SpawnSoldier(tower.Door, SoldierType.Swordsman);
        for (int i = 0; i < 20; i++)
            world.Spawn(new GridPoint(45, 88), Profession.Carrier);
        // Without tools sites do not recruit: diggers and builders come along.
        for (int i = 0; i < 4; i++)
        {
            world.Spawn(new GridPoint(45, 88), Profession.Digger);
            world.Spawn(new GridPoint(45, 88), Profession.Builder);
        }
        for (int i = 0; i < 4; i++)
        {
            world.AddLooseGoods(new GridPoint(48 + i * 2, 74), Material.Plank, 8);
            world.AddLooseGoods(new GridPoint(48 + i * 2, 70), Material.Stone, 8);
        }
        // Quarry stones far outside the start land.
        var stones = new[] { new GridPoint(112, 80), new GridPoint(116, 82), new GridPoint(114, 86) };
        foreach (var stone in stones)
            map.SetObjectCode(stone, MapObjectCodes.FullStone);
        var ai = new AiPlayer(world, Romans, 0);
        world.AiPlayers.Add(ai);

        for (int t = 0; t < 15 * 60 * World.TicksPerSecond && !stones.Any(s => map.OwnerAt(s) == 0); t++)
            world.Step();

        foreach (string line in ai.Log)
            output.WriteLine(line);
        foreach (var b in world.Buildings.Where(b => b.Exists))
            output.WriteLine($"{b.Definition.Name} at {b.Position.X},{b.Position.Y}: {world.StatusOf(b)}");
        output.WriteLine($"tick {world.Tick}, soldiers idle {world.Settlers.Count(s => s.Profession == Profession.Soldier && !s.HasTask && !s.IsInside)}");
        Assert.Contains(ai.Log, l => l.Contains("expands towards stone"));
        Assert.Contains(stones, s => map.OwnerAt(s) == 0);
    }

    [Fact]
    public void Ai_players_build_up_an_economy_on_a_multiplayer_map()
    {
        GameInstallation installation;
        try { installation = GameInstallation.Locate(); }
        catch (Exception) { output.WriteLine("No installation found, skipped."); return; }

        var original = installation.MapFiles.Select(OriginalMap.Open).First(m => !m.IsSinglePlayer && m.Buildings.Count == 0);
        var (world, report) = OriginalScenario.Load(original);
        for (int player = 0; player < report.Catalogs.Count; player++)
            world.AiPlayers.Add(new AiPlayer(world, report.Catalogs[player], player));

        for (int t = 0; t < 20 * 60 * World.TicksPerSecond; t++)
            world.Step();

        foreach (var ai in world.AiPlayers)
        {
            var own = world.Buildings.Where(b => b.Exists && b.Player == ai.Player && b.State == BuildingState.Finished).ToList();
            output.WriteLine($"player {ai.Player + 1}: {own.Count} buildings, land {ai.Land}: " +
                             string.Join(", ", own.GroupBy(b => b.Definition.Name).Select(g => $"{g.Key} {g.Count()}")));
            Assert.True(own.Count >= 15, $"player {ai.Player + 1} has {own.Count} buildings");
            foreach (string name in new[] { "lumberjack", "sawmill", "stonecutter", "small_livinghouse" })
                Assert.Contains(own, b => b.Definition.Name == name);
            Assert.True(own.Count(b => b.Definition.Name == "tower") >= 2, $"player {ai.Player + 1} expanded");
            // Nothing stuck: every construction site of the start phase got finished.
            Assert.True(world.Buildings.Count(b => b.Exists && b.Player == ai.Player && b.State != BuildingState.Finished) <= 4);
        }
    }
}
