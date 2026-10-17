using Homestead.Assets;
using Homestead.Assets.Maps;
using Homestead.Simulation.Ai;
using Homestead.Simulation.Buildings;
using Homestead.Simulation.Economy;
using Homestead.Simulation.Map;
using Homestead.Simulation.Pathfinding;
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
    public void The_ai_moves_the_work_area_of_an_idle_stonecutter_to_stone_nearby_instead_of_tearing_it_down()
    {
        var map = new GameMap(120);
        var world = new World(map, TestConfig.NoTools);
        var stonecutter = world.PlaceBuilding(Romans["stonecutter"], 0, new GridPoint(40, 60))!;
        world.PlaceBuilding(Romans["stock"], 0, new GridPoint(30, 40));
        for (int i = 0; i < 4; i++)
            world.Spawn(new GridPoint(36, 66), Profession.Carrier);
        // Out of the work area's reach, but within reach of a moved work area.
        var rock = new GridPoint(stonecutter.WorkCenter.X + 30, stonecutter.WorkCenter.Y);
        map.SetObjectCode(rock, MapObjectCodes.FullStone);
        var ai = new AiPlayer(world, Romans, 0);
        world.AiPlayers.Add(ai);

        for (int t = 0; t < 6 * 60 * World.TicksPerSecond && world.Production.StonesCut == 0; t++)
            world.Step();

        foreach (string line in ai.Log)
            output.WriteLine(line);
        Assert.Contains(ai.Log, l => l.Contains("moves the work area of stonecutter"));
        Assert.DoesNotContain(ai.Log, l => l.Contains("tears down stonecutter"));
        Assert.True(stonecutter.Exists);
        Assert.True(world.Production.StonesCut > 0);
    }

    [Fact]
    public void The_ai_stops_a_forester_with_enough_trees_and_restarts_it_once_they_are_gone()
    {
        var map = new GameMap(80);
        var world = new World(map, TestConfig.NoTools);
        var forester = world.PlaceBuilding(Romans["forester"], 0, new GridPoint(40, 40))!;
        var trees = Enumerable.Range(0, 10).Select(i => new GridPoint(34 + 2 * i, 50)).ToArray();
        foreach (var tree in trees)
            map.SetObject(tree, MapObjectKind.Tree);
        var ai = new AiPlayer(world, Romans, 0);
        world.AiPlayers.Add(ai);

        for (int t = 0; t < 15 * World.TicksPerSecond && !forester.IsPaused; t++)
            world.Step();
        Assert.True(forester.IsPaused, "ten trees in the work area");

        map.SetObjectCode(trees[0], MapObjectCodes.None);
        for (int t = 0; t < 15 * World.TicksPerSecond && forester.IsPaused; t++)
            world.Step();
        foreach (string line in ai.Log)
            output.WriteLine(line);
        Assert.False(forester.IsPaused, "nine trees left");
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

    [Theory]
    [InlineData("standard", false)]
    [InlineData("standard@military.reinforceRadius=0", true)]
    public void Scattered_enemies_nearby_count_as_the_strike_targets_defence(string character, bool strikes)
    {
        var map = new GameMap(160);
        var world = new World(map, TestConfig.NoTools) { TerritoryRules = true };
        var tower = world.PlaceBuilding(Romans["tower"], 0, new GridPoint(40, 80), force: true)!;
        world.SpawnSoldier(tower.Door, SoldierType.Swordsman); // the garrison
        world.Step();
        for (int i = 0; i < 3; i++)
            world.SpawnSoldier(new GridPoint(44 + i, 84), SoldierType.Swordsman);
        // One enemy alone, two more 20 fields behind him (out of military.threatRange, nobody is held back against
        // them): not one group, not on the way, but they would come to help.
        world.SpawnSoldier(new GridPoint(40, 135), SoldierType.Swordsman, player: 1);
        for (int i = 0; i < 2; i++)
            world.SpawnSoldier(new GridPoint(40 + i, 155), SoldierType.Swordsman, player: 1);
        var ai = new AiPlayer(world, Romans, 0, character: character);
        world.AiPlayers.Add(ai);

        for (int t = 0; t < 10 * World.TicksPerSecond && !ai.Log.Any(l => l.Contains("strikes")); t++)
            world.Step();

        foreach (string line in ai.Log)
            output.WriteLine(line);
        Assert.Equal(strikes, ai.Log.Any(l => l.Contains("strikes 1 enemies")));
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
        Assert.Equal("legion", new AiPlayer(world, Romans, 0).Character.Id); // the people's character
        Assert.Throws<InvalidDataException>(() => AiTrait.Create("nothing", default));
    }

    [Fact]
    public void Characters_can_be_made_up_from_traits_and_values()
    {
        var plain = AiConfig.Load(civilization: Civilization.Roman);
        var variant = AiConfig.Load(civilization: Civilization.Roman,
            character: "general@attack.ratio=1.7@buildOrderMoves.barrack=-3@pincer.feintShare=0.5");
        Assert.Equal(1.7, variant.Attack.Ratio);
        Assert.Equal(1.7, variant.Attack.ArmyRatio); // defaults to the ratio
        Assert.Equal(1.7, variant.Attack.AbortRatio);
        Assert.Equal(40, variant.Military.StrikeRange); // the general's own value stays
        int Barrack(AiConfig c) => c.BuildOrder.ToList().FindIndex(e => e.Building == "barrack");
        Assert.Equal(Barrack(plain) - 3, Barrack(variant));
        Assert.Equal(plain.BuildOrder.Count, variant.BuildOrder.Count);
        var traits = variant.Characters["general@attack.ratio=1.7@buildOrderMoves.barrack=-3@pincer.feintShare=0.5"];
        Assert.Equal(0.5, traits.Single(t => t.Trait == "pincer").Settings.GetProperty("feintShare").GetDouble());

        var world = new World(new GameMap(40), TestConfig.NoTools);
        Assert.Equal(["waves", "frontGarrison"], new AiPlayer(world, Romans, 0, character: "waves+frontGarrison").Character.Traits.Select(t => t.Id));
        Assert.Throws<ArgumentException>(() => new AiPlayer(world, Romans, 0, character: "waves+nothing"));
        Assert.Equal(["waves", "pincer", "everyOtherTower", "guardByThreat"], new AiPlayer(world, Romans, 0, character: "general+waves").Character.Traits.Select(t => t.Id));
    }

    [Fact]
    public void With_front_garrison_only_buildings_near_the_enemy_are_fully_staffed()
    {
        var world = new World(new GameMap(160), TestConfig.NoTools) { TerritoryRules = true, PlayerCount = 2 };
        var back = world.PlaceBuilding(Romans["tower"], 0, new GridPoint(30, 80), force: true)!;
        var front = world.PlaceBuilding(Romans["tower"], 0, new GridPoint(90, 80), force: true)!;
        world.PlaceBuilding(Romans["tower"], 1, new GridPoint(130, 80), force: true);
        world.SpawnSoldier(back.Door, SoldierType.Swordsman);
        world.SpawnSoldier(front.Door, SoldierType.Swordsman);
        world.Step();
        world.AiPlayers.Add(new AiPlayer(world, Romans, 0, character: "frontGarrison"));

        for (int t = 0; t < 10 * World.TicksPerSecond; t++)
            world.Step();

        int Wanted(Building b) => Enum.GetValues<SoldierType>().Sum(type => world.Military.Wanted(b, type));
        Assert.Equal(1, Wanted(back));
        Assert.Equal(Romans["tower"].Occupiers.Count, Wanted(front));
    }

    [Theory]
    [InlineData("standard", false)]
    [InlineData("standard@attack.commit=1", true)]
    public void A_committed_campaign_takes_all_spare_soldiers(string character, bool all)
    {
        var world = new World(new GameMap(160), TestConfig.NoTools) { TerritoryRules = true, PlayerCount = 2 };
        var own = world.PlaceBuilding(Romans["tower"], 0, new GridPoint(40, 80), force: true)!;
        var enemy = world.PlaceBuilding(Romans["tower"], 1, new GridPoint(100, 80), force: true)!;
        world.SpawnSoldier(own.Door, SoldierType.Swordsman);
        world.SpawnSoldier(enemy.Door, SoldierType.Swordsman, player: 1);
        world.Step();
        for (int i = 0; i < 10; i++)
            world.SpawnSoldier(new GridPoint(44 + i, 88), SoldierType.Swordsman);
        var ai = new AiPlayer(world, Romans, 0, character: character);
        world.AiPlayers.Add(ai);

        for (int t = 0; t < 20 * World.TicksPerSecond && !ai.Log.Any(l => l.Contains("gathers")); t++)
            world.Step();

        foreach (string line in ai.Log)
            output.WriteLine(line);
        Assert.Equal(all, ai.Log.Any(l => l.Contains("gathers 10 soldiers")));
        Assert.Contains(ai.Log, l => l.Contains("gathers"));
    }

    [Fact]
    public void An_early_army_moves_iron_melt_weaponsmith_and_barrack_behind_wood_and_stone()
    {
        var world = new World(new GameMap(40), TestConfig.NoTools);
        foreach (var people in Enum.GetValues<Civilization>())
        {
            var order = new AiPlayer(world, BuildingCatalog.Load(people), 0, character: "earlyArmy").BuildOrder;
            var plain = AiConfig.Load(civilization: people).BuildOrder;
            Assert.Equal(plain.Take(4), order.Take(4));
            Assert.Equal([("ironmelt", 1), ("weaponsmith", 1), ("barrack", 1)], order.Skip(4).Take(3));
            Assert.Equal(plain.Count, order.Count);
        }
    }

    [Fact]
    public void Idle_soldiers_gather_at_the_forward_base_and_fall_back_from_a_stronger_enemy()
    {
        var world = new World(new GameMap(200), TestConfig.NoTools) { TerritoryRules = true, PlayerCount = 2 };
        var home = world.PlaceBuilding(Romans["tower"], 0, new GridPoint(30, 100), force: true)!;
        var front = world.PlaceBuilding(Romans["tower"], 0, new GridPoint(90, 100), force: true)!;
        world.PlaceBuilding(Romans["tower"], 1, new GridPoint(170, 100), force: true);
        world.SpawnSoldier(home.Door, SoldierType.Swordsman);
        world.SpawnSoldier(front.Door, SoldierType.Swordsman);
        world.Step();
        var soldiers = Enumerable.Range(0, 3).Select(i => world.SpawnSoldier(new GridPoint(34 + i, 108), SoldierType.Swordsman)!).ToList();
        var ai = new AiPlayer(world, Romans, 0, character: "forwardBase");
        world.AiPlayers.Add(ai);

        for (int t = 0; t < 60 * World.TicksPerSecond; t++)
            world.Step();
        Assert.All(soldiers, s => Assert.True(Pathfinding.PathFinder.Distance(s.Position, front.Door) <= 10, $"at {s.Position}"));

        // Many enemies come close to the base (outside the own land): the soldiers fall back home.
        for (int i = 0; i < 12; i++)
            world.SpawnSoldier(new GridPoint(125 + i % 4, 96 + i / 4), SoldierType.Swordsman, player: 1);
        for (int t = 0; t < 60 * World.TicksPerSecond; t++)
            world.Step();

        foreach (string line in ai.Log)
            output.WriteLine(line);
        Assert.Contains(ai.Log, l => l.Contains("forwardBase: falls back"));
        Assert.All(soldiers.Where(s => !s.IsDead), s => Assert.True(Pathfinding.PathFinder.Distance(s.Position, home.Door) <= 10, $"at {s.Position}"));
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

    [Fact]
    public void Retreating_soldiers_take_up_a_strike_on_the_way()
    {
        var map = new GameMap(160);
        var world = new World(map, TestConfig.NoTools) { TerritoryRules = true };
        var tower = world.PlaceBuilding(Romans["tower"], 0, new GridPoint(40, 80), force: true)!;
        world.SpawnSoldier(tower.Door, SoldierType.Swordsman); // the garrison
        world.Step();
        var retreating = Enumerable.Range(0, 3).Select(i => world.SpawnSoldier(new GridPoint(44 + i, 84), SoldierType.Swordsman)!).ToList();
        world.Military.Move(retreating, new GridPoint(40, 20), forced: true);
        for (int i = 0; i < 2; i++)
            world.SpawnSoldier(new GridPoint(40 + i, 125), SoldierType.Swordsman, player: 1);
        var ai = new AiPlayer(world, Romans, 0);
        world.AiPlayers.Add(ai);

        for (int t = 0; t < 3 * World.TicksPerSecond && !ai.Log.Any(l => l.Contains("strikes")); t++)
            world.Step();

        foreach (string line in ai.Log)
            output.WriteLine(line);
        Assert.Contains(ai.Log, l => l.Contains("strikes 2 enemies"));
    }

    [Fact]
    public void Retreating_soldiers_walk_on_without_a_new_action()
    {
        var map = new GameMap(160);
        var world = new World(map, TestConfig.NoTools) { TerritoryRules = true };
        var tower = world.PlaceBuilding(Romans["tower"], 0, new GridPoint(40, 80), force: true)!;
        world.SpawnSoldier(tower.Door, SoldierType.Swordsman);
        world.PlaceBuilding(Romans["tower"], 0, new GridPoint(40, 30), force: true);
        world.Step();
        var retreating = Enumerable.Range(0, 3).Select(i => world.SpawnSoldier(new GridPoint(44 + i, 84), SoldierType.Swordsman)!).ToList();
        var to = new GridPoint(70, 80);
        world.Military.Move(retreating, to, forced: true);
        var ai = new AiPlayer(world, Romans, 0);
        world.AiPlayers.Add(ai);

        for (int t = 0; t < 3 * World.TicksPerSecond; t++)
            world.Step();

        // Nobody to fight: guarding does not send them elsewhere, they still retreat where they were sent.
        Assert.All(retreating, s => Assert.True(world.Military.ForcedMoveTarget(s) is { } target && PathFinder.Distance(target, to) <= 3));
    }

    [Fact]
    public void Soldiers_walking_to_a_post_take_up_a_strike_on_the_way()
    {
        var map = new GameMap(160);
        var world = new World(map, TestConfig.NoTools) { TerritoryRules = true };
        var tower = world.PlaceBuilding(Romans["tower"], 0, new GridPoint(40, 80), force: true)!;
        world.SpawnSoldier(tower.Door, SoldierType.Swordsman); // the garrison
        world.Step();
        var walking = Enumerable.Range(0, 3).Select(i => world.SpawnSoldier(new GridPoint(44 + i, 84), SoldierType.Swordsman)!).ToList();
        world.Military.Move(walking, new GridPoint(40, 20));
        for (int i = 0; i < 2; i++)
            world.SpawnSoldier(new GridPoint(40 + i, 125), SoldierType.Swordsman, player: 1);
        var ai = new AiPlayer(world, Romans, 0);
        world.AiPlayers.Add(ai);

        // Long before they would arrive up there.
        for (int t = 0; t < 3 * World.TicksPerSecond && !ai.Log.Any(l => l.Contains("strikes")); t++)
            world.Step();

        foreach (string line in ai.Log)
            output.WriteLine(line);
        Assert.Contains(ai.Log, l => l.Contains("strikes 2 enemies"));
    }

    [Fact]
    public void Enemy_geologists_are_struck_and_killed()
    {
        var map = new GameMap(160);
        var world = new World(map, TestConfig.NoTools) { TerritoryRules = true };
        var tower = world.PlaceBuilding(Romans["tower"], 0, new GridPoint(40, 80), force: true)!;
        world.SpawnSoldier(tower.Door, SoldierType.Swordsman);
        world.Step();
        world.SpawnSoldier(new GridPoint(44, 84), SoldierType.Swordsman);
        world.SpawnSoldier(new GridPoint(45, 84), SoldierType.Swordsman);
        var carrier = world.Spawn(new GridPoint(40, 122), Profession.Carrier, player: 1)!;
        world.Geology.Convert([carrier]);
        Assert.Equal(Profession.Geologist, carrier.Profession);
        var ai = new AiPlayer(world, Romans, 0);
        world.AiPlayers.Add(ai);

        for (int t = 0; t < 60 * World.TicksPerSecond && !carrier.IsDead; t++)
            world.Step();

        foreach (string line in ai.Log)
            output.WriteLine(line);
        Assert.Contains(ai.Log, l => l.Contains("strikes 1 enemies") && l.Contains("with 1 soldiers"));
        Assert.True(carrier.IsDead);
    }

    [Theory]
    [InlineData(20, false)] // deep in their land: left alone
    [InlineData(4, true)] // at the edge of it
    public void Enemies_standing_deep_in_their_land_are_left_alone_with_a_hinterland_depth(int landRadius, bool struck)
    {
        var map = new GameMap(160);
        var world = new World(map, TestConfig.NoTools) { TerritoryRules = true, PlayerCount = 2 };
        var tower = world.PlaceBuilding(Romans["tower"], 0, new GridPoint(40, 60), force: true)!;
        world.SpawnSoldier(tower.Door, SoldierType.Swordsman);
        world.Step();
        foreach (var p in PathFinder.Circle(new GridPoint(40, 120), landRadius))
            map.SetOwner(p, 1);
        for (int i = 0; i < 8; i++)
            world.SpawnSoldier(new GridPoint(42 + i, 66), SoldierType.Swordsman);
        world.SpawnSoldier(new GridPoint(40, 120), SoldierType.Swordsman, player: 1);
        var ai = new AiPlayer(world, Romans, 0, character: "standard@military.hinterlandDepth=8");
        world.AiPlayers.Add(ai);

        for (int t = 0; t < 10 * World.TicksPerSecond && !ai.Log.Any(l => l.Contains("strikes")); t++)
            world.Step();

        foreach (string line in ai.Log)
            output.WriteLine(line);
        Assert.Equal(struck, ai.Log.Any(l => l.Contains("strikes 1 enemies") && l.Contains("standing around")));
    }

    [Fact]
    public void Every_other_tower_gets_the_idle_soldiers()
    {
        var world = new World(new GameMap(200), TestConfig.NoTools) { TerritoryRules = true, PlayerCount = 2 };
        var towers = new[] { 30, 50, 70, 90 }.Select(x => world.PlaceBuilding(Romans["tower"], 0, new GridPoint(x, 80), force: true)!).ToList();
        world.PlaceBuilding(Romans["tower"], 1, new GridPoint(185, 80), force: true);
        foreach (var tower in towers)
            world.SpawnSoldier(tower.Door, SoldierType.Swordsman);
        world.Step();
        for (int i = 0; i < 8; i++)
            world.SpawnSoldier(new GridPoint(30 + i, 90), SoldierType.Swordsman);
        var ai = new AiPlayer(world, Romans, 0, character: "frontGarrison+everyOtherTower");
        world.AiPlayers.Add(ai);

        for (int t = 0; t < 40 * World.TicksPerSecond; t++)
            world.Step();

        foreach (string line in ai.Log)
            output.WriteLine(line);
        int Near(Building b) => world.Settlers.Count(s => s.Player == 0 && !s.IsInside && PathFinder.Distance(s.Position, b.Door) <= 6);
        // Front first: the tower nearest the enemy and the one after its neighbor.
        Assert.Equal([0, 4, 0, 4], towers.Select(Near));
    }

    [Fact]
    public void Front_towers_get_the_idle_soldiers()
    {
        var world = new World(new GameMap(200), TestConfig.NoTools) { TerritoryRules = true, PlayerCount = 2 };
        var back = world.PlaceBuilding(Romans["tower"], 0, new GridPoint(30, 80), force: true)!;
        var front = world.PlaceBuilding(Romans["tower"], 0, new GridPoint(70, 80), force: true)!;
        var side = world.PlaceBuilding(Romans["tower"], 0, new GridPoint(110, 30), force: true)!;
        world.PlaceBuilding(Romans["tower"], 1, new GridPoint(185, 80), force: true);
        foreach (var tower in new[] { back, front, side })
            world.SpawnSoldier(tower.Door, SoldierType.Swordsman);
        world.Step();
        for (int i = 0; i < 8; i++)
            world.SpawnSoldier(new GridPoint(30 + i, 90), SoldierType.Swordsman);
        var ai = new AiPlayer(world, Romans, 0, character: "frontGarrison+frontTowers");
        world.AiPlayers.Add(ai);

        for (int t = 0; t < 90 * World.TicksPerSecond; t++)
            world.Step();

        foreach (string line in ai.Log)
            output.WriteLine(line);
        int Near(Building b) => world.Settlers.Count(s => s.Player == 0 && !s.IsInside && PathFinder.Distance(s.Position, b.Door) <= 6);
        Assert.Equal(0, Near(back));
        Assert.Equal(4, Near(front));
        Assert.Equal(4, Near(side));
    }

    [Fact]
    public void Rotating_towers_change_every_minute()
    {
        var world = new World(new GameMap(200), TestConfig.NoTools) { TerritoryRules = true, PlayerCount = 2 };
        var towers = Enumerable.Range(0, 10).Select(i => world.PlaceBuilding(Romans["tower"], 0, new GridPoint(20 + i % 5 * 30, 50 + i / 5 * 60), force: true)!).ToList();
        foreach (var tower in towers)
            world.SpawnSoldier(tower.Door, SoldierType.Swordsman);
        world.Step();
        for (int i = 0; i < 6; i++)
            world.SpawnSoldier(new GridPoint(30 + i, 90), SoldierType.Swordsman);
        var ai = new AiPlayer(world, Romans, 0, character: "frontGarrison+rotatingTowers");
        world.AiPlayers.Add(ai);

        for (int t = 0; t < 130 * World.TicksPerSecond; t++)
            world.Step();

        foreach (string line in ai.Log)
            output.WriteLine(line);
        var rounds = ai.Log.Where(l => l.Contains("rotatingTowers: guards")).Select(l => l[(l.IndexOf("guards", StringComparison.Ordinal) + 7)..]).ToList();
        Assert.True(rounds.Count >= 3, string.Join(" | ", rounds));
        Assert.All(rounds, r => Assert.Equal(3, r.Split(' ').Length)); // 30 % of 10
        Assert.True(rounds.Distinct().Count() > 1);
    }

    /// <summary>Player 0 owns the land left of x = 80 with a staffed tower, player 1 the rest; carriers to become scouts.</summary>
    private static (World World, AiPlayer Ai) ThiefSetup(string character = "thief")
    {
        var map = new GameMap(160);
        var world = new World(map, TestConfig.NoTools) { PlayerCount = 2 };
        var tower = world.PlaceBuilding(Romans["tower"], 0, new GridPoint(60, 80), force: true)!;
        world.SpawnSoldier(tower.Door, SoldierType.Swordsman);
        world.Step();
        for (int y = 0; y < 160; y++)
        for (int x = 0; x < 160; x++)
            map.SetOwner(new GridPoint(x, y), x < 80 ? 0 : 1);
        for (int i = 0; i < 3; i++)
            world.Spawn(new GridPoint(55 + i, 90), Profession.Carrier);
        var ai = new AiPlayer(world, Romans, 0, character: character);
        world.AiPlayers.Add(ai);
        return (world, ai);
    }

    [Fact]
    public void A_thief_sends_scouts_for_gold_at_the_edge_of_enemy_land_before_coal_and_brings_it_home()
    {
        var (world, ai) = ThiefSetup();
        world.AddLooseGoods(new GridPoint(84, 76), Material.Coal, 4);
        world.AddLooseGoods(new GridPoint(85, 86), Material.Gold, 1);
        world.AddLooseGoods(new GridPoint(120, 80), Material.Gold, 4); // deep in the enemy land

        for (int t = 0; t < 120 * World.TicksPerSecond && !world.Piles.Any(p => p.Material == Material.Gold && world.Map!.OwnerAt(p.Position) == 0); t++)
            world.Step();

        foreach (string line in ai.Log)
            output.WriteLine(line);
        Assert.Equal(2, world.Settlers.Count(s => s.Profession == Profession.Scout));
        Assert.Contains(ai.Log, l => l.Contains("thief: sends a scout for Gold at 85,86"));
        Assert.Contains(world.Piles, p => p.Material == Material.Gold && world.Map!.OwnerAt(p.Position) == 0);
        Assert.Equal(4, world.Piles.Single(p => p.Position == new GridPoint(120, 80)).Count);
    }

    [Fact]
    public void A_thief_leaves_goods_alone_while_enemy_soldiers_are_near()
    {
        var (world, ai) = ThiefSetup();
        world.AddLooseGoods(new GridPoint(85, 80), Material.Gold, 4);
        world.SpawnSoldier(new GridPoint(90, 82), SoldierType.Swordsman, level: 3, player: 1);

        for (int t = 0; t < 20 * World.TicksPerSecond; t++)
            world.Step();

        foreach (string line in ai.Log)
            output.WriteLine(line);
        Assert.DoesNotContain(ai.Log, l => l.Contains("thief: sends"));
        Assert.Equal(4, world.Piles.Single(p => p.Material == Material.Gold).Count);
    }

    [Fact]
    public void A_thief_steals_tools_from_a_storehouse_right_at_the_border()
    {
        var amazons = BuildingCatalog.Load(Civilization.Amazon);
        var map = new GameMap(160);
        var world = new World(map, ProductionConfig.Load()) { PlayerCount = 2, TerritoryRules = true };
        // The own military building far away: the distance counts from the own land.
        var tower = world.PlaceBuilding(amazons["tower"], 1, new GridPoint(150, 80), force: true)!;
        world.SpawnSoldier(tower.Door, SoldierType.Swordsman, player: 1);
        world.Step();
        for (int y = 0; y < 160; y++)
        for (int x = 0; x < 160; x++)
            map.SetOwner(new GridPoint(x, y), x < 80 ? 0 : 1);
        var stock = world.PlaceBuilding(Romans["stock"], 0, new GridPoint(74, 80), force: true)!;
        world.AddLooseGoods(new GridPoint(60, 70), Material.Axe, 8);
        for (int i = 0; i < 4; i++)
            world.Spawn(new GridPoint(60 + i, 85), Profession.Carrier);
        for (int i = 0; i < 4; i++)
            world.Spawn(new GridPoint(105 + i, 90), Profession.Carrier, player: 1);
        for (int t = 0; t < 3000 && stock.Piles.Sum(p => p.Material == Material.Axe ? p.Count : 0) < 8; t++)
            world.Step();
        var ai = new AiPlayer(world, amazons, 1, character: "thief");
        world.AiPlayers.Add(ai);

        for (int t = 0; t < 60 * World.TicksPerSecond && !world.Piles.Any(p => p.Material == Material.Axe && map.OwnerAt(p.Position) == 1); t++)
            world.Step();

        foreach (string line in ai.Log)
            output.WriteLine(line);
        Assert.Contains(world.Piles, p => p.Material == Material.Axe && map.OwnerAt(p.Position) == 1);
    }
}
