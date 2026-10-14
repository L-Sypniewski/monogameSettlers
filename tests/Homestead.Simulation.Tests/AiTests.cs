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
    public void Defenders_strike_the_enemies_they_stand_against()
    {
        var map = new GameMap(160);
        var world = new World(map, TestConfig.NoTools) { TerritoryRules = true };
        var tower = world.PlaceBuilding(Romans["tower"], 0, new GridPoint(40, 80), force: true)!;
        world.SpawnSoldier(tower.Door, SoldierType.Swordsman); // the garrison
        world.Step();
        // Three idle soldiers against two enemies close to the land: all three would be held back
        // against them, yet together they are strong enough to strike.
        for (int i = 0; i < 3; i++)
            world.SpawnSoldier(new GridPoint(44 + i, 84), SoldierType.Swordsman);
        for (int i = 0; i < 2; i++)
            world.SpawnSoldier(new GridPoint(40 + i, 125), SoldierType.Swordsman, player: 1);
        Assert.NotEqual(0, map.OwnerAt(new GridPoint(40, 125)));
        var ai = new AiPlayer(world, Romans, 0);
        world.AiPlayers.Add(ai);

        for (int t = 0; t < 30 * World.TicksPerSecond && !ai.Log.Any(l => l.Contains("strikes")); t++)
            world.Step();

        foreach (string line in ai.Log)
            output.WriteLine(line);
        Assert.Contains(ai.Log, l => l.Contains("strikes 2 enemies"));
    }

    [Fact]
    public void The_ai_needs_more_soldiers_against_enemies_on_their_own_land()
    {
        var map = new GameMap(160);
        var world = new World(map, TestConfig.NoTools) { TerritoryRules = true, PlayerCount = 2 };
        var tower = world.PlaceBuilding(Romans["tower"], 0, new GridPoint(40, 80), force: true)!;
        world.SpawnSoldier(tower.Door, SoldierType.Swordsman); // the garrison
        world.Step();
        // The enemies stand on their own land: full strength there, the AI's soldiers only half.
        foreach (var p in Pathfinding.PathFinder.Circle(new GridPoint(40, 125), 12))
            map.SetOwner(p, 1);
        for (int i = 0; i < 6; i++)
            world.SpawnSoldier(new GridPoint(42 + i, 84), SoldierType.Swordsman);
        for (int i = 0; i < 2; i++)
            world.SpawnSoldier(new GridPoint(40 + i, 125), SoldierType.Swordsman, player: 1);
        var ai = new AiPlayer(world, Romans, 0);
        world.AiPlayers.Add(ai);

        for (int t = 0; t < 30 * World.TicksPerSecond && !ai.Log.Any(l => l.Contains("strikes")); t++)
            world.Step();

        foreach (string line in ai.Log)
            output.WriteLine(line);
        // 2 enemies × 1.5 (strikeRatio) at full strength take 6 soldiers at 50 %, not 3.
        Assert.Contains(ai.Log, l => l.Contains("strikes 2 enemies") && l.Contains("with 6 soldiers"));
    }

    [Fact]
    public void A_general_feints_and_strikes_the_enemy_tower_from_the_side()
    {
        var map = new GameMap(160);
        var world = new World(map, TestConfig.NoTools) { TerritoryRules = true, PlayerCount = 2 };
        var own = world.PlaceBuilding(Romans["tower"], 0, new GridPoint(40, 80), force: true)!;
        var enemy = world.PlaceBuilding(Romans["tower"], 1, new GridPoint(100, 80), force: true)!;
        world.SpawnSoldier(own.Door, SoldierType.Swordsman);
        world.SpawnSoldier(enemy.Door, SoldierType.Swordsman, player: 1);
        world.Step();
        for (int i = 0; i < 8; i++)
            world.SpawnSoldier(new GridPoint(44 + i, 88), SoldierType.Swordsman);
        var ai = new AiPlayer(world, Romans, 0, character: "general");
        world.AiPlayers.Add(ai);
        Assert.Equal("general", ai.Character.Id);

        bool sideways = false;
        for (int t = 0; t < 5 * 60 * World.TicksPerSecond && enemy.Player != 0; t++)
        {
            world.Step();
            // When the feint attacks, the main force waits off to the side, not behind the feint.
            if (!sideways && ai.Log.Any(l => l.Contains("feint against")))
                sideways = world.Settlers.Count(s => s.Player == 0 && !s.IsInside
                                                     && Pathfinding.PathFinder.Distance(s.Position, own.Door) > 30
                                                     && Pathfinding.PathFinder.Distance(s.Position, enemy.Position) < 25) >= 2;
        }

        foreach (string line in ai.Log)
            output.WriteLine(line);
        Assert.Contains(ai.Log, l => l.Contains("pincer against the tower"));
        Assert.Contains(ai.Log, l => l.Contains("feint against"));
        Assert.Contains(ai.Log, l => l.Contains("from the side"));
        Assert.True(sideways, "the main force stands at the side when the feint attacks");
        Assert.Equal(0, enemy.Player);
    }

    [Theory]
    [InlineData("standard", false)]
    [InlineData("general", true)]
    public void Idle_soldiers_gather_at_home_only_with_guarding(string character, bool gathers)
    {
        var world = new World(new GameMap(160), TestConfig.NoTools) { TerritoryRules = true };
        var tower = world.PlaceBuilding(Romans["tower"], 0, new GridPoint(40, 80), force: true)!;
        world.SpawnSoldier(tower.Door, SoldierType.Swordsman); // the garrison
        world.Step();
        var soldier = world.SpawnSoldier(new GridPoint(70, 80), SoldierType.Swordsman)!;
        var start = soldier.Position;
        world.AiPlayers.Add(new AiPlayer(world, Romans, 0, character: character));

        for (int t = 0; t < 30 * World.TicksPerSecond; t++)
            world.Step();

        if (gathers)
            Assert.True(Pathfinding.PathFinder.Distance(soldier.Position, tower.Door) <= 10, $"at {soldier.Position}");
        else
            Assert.True(Pathfinding.PathFinder.Distance(soldier.Position, start) <= 3, $"at {soldier.Position}");
    }

    [Fact]
    public void Unknown_characters_and_traits_are_refused()
    {
        var world = new World(new GameMap(40), TestConfig.NoTools);
        Assert.Throws<ArgumentException>(() => new AiPlayer(world, Romans, 0, character: "nobody"));
        Assert.Equal("standard", new AiPlayer(world, Romans, 0).Character.Id);
        Assert.Throws<InvalidDataException>(() => AiTrait.Create("nothing", default));
    }

    [Fact]
    public void The_ai_upgrades_swordsmen_bowmen_pikemen_to_level_2_and_then_to_level_3()
    {
        var map = new GameMap(100);
        var world = new World(map, TestConfig.NoTools) { TerritoryRules = true };
        world.PlaceBuilding(Romans["tower"], 0, new GridPoint(50, 50), force: true);
        world.PlaceBuilding(Romans["temple"], 0, new GridPoint(40, 50), force: true);
        world.Magic.AddMana(0, 1000);
        var ai = new AiPlayer(world, Romans, 0);
        world.AiPlayers.Add(ai);

        for (int t = 0; t < 10 * World.TicksPerSecond && world.Magic.UpgradeThreshold(0) is not null; t++)
            world.Step();

        var upgrades = ai.Log.Where(l => l.Contains("upgrades")).ToList();
        foreach (string line in upgrades)
            output.WriteLine(line);
        Assert.Equal(["Swordsman to level 2", "Bowman to level 2", "Pikeman to level 2", "Swordsman to level 3", "Bowman to level 3", "Pikeman to level 3"],
            upgrades.Select(l => l[(l.IndexOf("upgrades ") + 9)..l.IndexOf(" (")]));
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
