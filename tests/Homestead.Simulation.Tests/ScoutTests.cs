using Homestead.Simulation.Ai;
using Homestead.Simulation.Buildings;
using Homestead.Simulation.Economy;
using Homestead.Simulation.Map;
using Xunit.Abstractions;

namespace Homestead.Simulation.Tests;

public class ScoutTests(ITestOutputHelper output)
{
    [Fact]
    public void Carriers_become_scouts_and_walk_where_they_are_sent()
    {
        var world = new World(new GameMap(60), TestConfig.NoTools);
        for (int i = 0; i < 10; i++)
            world.Spawn(new GridPoint(10 + i, 10), Profession.Carrier);

        var (now, _) = world.Geology.ConvertCarriers(0, 1, Profession.Scout);
        var scout = Assert.Single(world.Settlers, s => s.Profession == Profession.Scout);
        Assert.Equal(1, now);
        Assert.True(scout.Health > 0);

        var target = new GridPoint(40, 40);
        Assert.Equal(1, world.Geology.Send([scout], target));
        for (int t = 0; t < 2_000 && scout.HasTask; t++)
            world.Step();

        Assert.Equal(target, scout.Position);
        Assert.False(world.Geology.IsSearching(scout));
    }

    [Fact]
    public void Scouts_see_more_than_twice_as_far_as_other_settlers()
    {
        var world = new World(new GameMap(80), TestConfig.NoTools);
        world.Spawn(new GridPoint(40, 40), Profession.Scout);
        var vision = new Vision(80, player: 0);

        vision.Update(world);

        int sight = world.Military.Config.Scouts!.Sight;
        Assert.True(sight >= 2 * Vision.SettlerSight);
        Assert.True(vision.IsVisible(new GridPoint(40 + sight, 40)));
        Assert.False(vision.IsVisible(new GridPoint(40 + sight + 1, 40)));
    }

    [Fact]
    public void Enemy_soldiers_leave_a_scout_alone_until_they_come_within_ten_fields()
    {
        var world = new World(new GameMap(80), TestConfig.NoTools);
        var scout = world.Spawn(new GridPoint(40, 40), Profession.Scout)!;
        var soldier = world.SpawnSoldier(new GridPoint(52, 40), SoldierType.Swordsman, player: 1)!;
        float health = scout.Health;

        for (int t = 0; t < 100; t++)
            world.Step();
        Assert.False(scout.IsRevealedTo(1));
        Assert.False(soldier.HasTask, "a carrier of its own is no enemy");
        Assert.Equal(health, scout.Health);

        world.WalkTo(scout, new GridPoint(43, 40)); // 9 fields from the soldier
        for (int t = 0; t < 1_000 && !scout.IsDead; t++)
            world.Step();

        Assert.True(scout.IsDead, "revealed, he is fought");
    }

    [Fact]
    public void A_revealed_scout_stays_revealed_while_in_sight()
    {
        var map = new GameMap(120);
        var world = new World(map, TestConfig.NoTools);
        var scout = world.Spawn(new GridPoint(40, 40), Profession.Scout)!;
        world.SpawnSoldier(new GridPoint(45, 40), SoldierType.Swordsman, player: 1);
        world.Spawn(new GridPoint(58, 46), player: 1);
        world.Step();
        Assert.True(scout.IsRevealedTo(1));

        // Away from the soldier, but near a carrier of player 1: still revealed.
        world.Military.Move(world.Settlers.Where(s => s.Player == 1 && s.Profession == Profession.Soldier), new GridPoint(100, 40), forced: true);
        world.WalkTo(scout, new GridPoint(55, 46));
        for (int t = 0; t < 600; t++)
            world.Step();
        Assert.True(world.Military.Sees(1, scout.Position));
        Assert.True(scout.IsRevealedTo(1));

        // Out of everybody's sight: disguised again.
        world.WalkTo(scout, new GridPoint(20, 80));
        for (int t = 0; t < 3_000 && world.Military.Sees(1, scout.Position); t++)
            world.Step();
        world.Step();
        Assert.False(scout.IsRevealedTo(1));
    }

    [Fact]
    public void A_computer_player_sends_a_soldier_to_look_at_a_stranger_on_its_land()
    {
        var map = new GameMap(120);
        var world = new World(map, TestConfig.NoTools) { PlayerCount = 2 };
        var romans = BuildingCatalog.Load(Civilization.Roman);
        var tower = world.PlaceBuilding(romans["tower"], 1, new GridPoint(90, 60), force: true)!;
        world.SpawnSoldier(tower.Door, SoldierType.Swordsman, player: 1);
        world.SpawnSoldier(tower.Door, SoldierType.Bowman, player: 1);
        world.Military.ChangeWanted(tower, SoldierType.Bowman, 1);
        for (int t = 0; t < 200; t++)
            world.Step();
        Assert.Equal(2, world.Military.Occupants(tower).Count(o => o is not null));
        var ai = new AiPlayer(world, romans, 1);
        world.AiPlayers.Add(ai);
        var scout = world.Spawn(new GridPoint(50, 60), Profession.Scout)!;
        var on = Enumerable.Range(0, 60).Select(x => new GridPoint(50 + x, 60)).First(p => map.OwnerAt(p) == 1);

        // On its land he is not revealed at once (no shot from the tower), but a soldier comes out to look at him.
        world.WalkTo(scout, on);
        for (int t = 0; t < 2_000 && scout.Position != on; t++)
            world.Step();
        for (int t = 0; t < 2 * World.TicksPerSecond; t++)
            world.Step();
        Assert.False(scout.IsRevealedTo(1));
        for (int t = 0; t < 60 * World.TicksPerSecond && !scout.IsDead; t++)
            world.Step();

        foreach (string line in ai.Log)
            output.WriteLine(line);
        Assert.Contains(ai.Log, l => l.Contains("suspects a carrier"));
        Assert.True(scout.IsDead);
    }

    /// <summary>Player 0 owns the left half, player 1 the right one.</summary>
    private static World TwoLands()
    {
        var map = new GameMap(80);
        for (int y = 0; y < 80; y++)
        for (int x = 0; x < 80; x++)
            map.SetOwner(new GridPoint(x, y), x < 40 ? 0 : 1);
        return new World(map, TestConfig.NoTools);
    }

    [Fact]
    public void A_scout_steals_the_nearest_good_sneaks_home_and_drops_it_there()
    {
        var world = TwoLands();
        var near = world.AddLooseGoods(new GridPoint(53, 40), Material.Plank, 3)!;
        world.AddLooseGoods(new GridPoint(50, 44), Material.Stone, 3);
        world.AddLooseGoods(new GridPoint(60, 40), Material.Coal, 3); // too far
        var scout = world.Spawn(new GridPoint(50, 40), Profession.Scout)!;

        Assert.Equal(1, world.Theft.Steal([scout]));
        for (int t = 0; t < 500 && scout.Carrying is null; t++)
            world.Step();
        Assert.Equal(Material.Plank, scout.Carrying);
        Assert.Equal(2, near.Count);
        Assert.Equal(0, world.Theft.Steal([scout])); // one good at a time

        // Loaded he walks at half speed.
        world.WalkTo(scout, new GridPoint(30, 40));
        for (int t = 0; t < 3; t++)
            world.Step();
        Assert.Equal(World.BaseStepSpeed / 2, scout.StepSpeed);
        for (int t = 0; t < 3_000 && scout.Carrying is not null; t++)
            world.Step();

        Assert.Null(scout.Carrying);
        Assert.True(world.Map!.OwnerAt(scout.Position) == 0);
        var loot = Assert.Single(world.Piles, p => p.Material == Material.Plank && p != near);
        Assert.True(Pathfinding.PathFinder.Distance(loot.Position, new GridPoint(30, 40)) <= 1);
    }

    [Fact]
    public void A_scout_sent_to_steal_walks_there_first()
    {
        var world = TwoLands();
        var pile = world.AddLooseGoods(new GridPoint(60, 40), Material.Coal, 2)!;
        var scout = world.Spawn(new GridPoint(30, 40), Profession.Scout)!;

        Assert.Equal(1, world.Theft.StealAt([scout], new GridPoint(58, 40)));
        for (int t = 0; t < 3_000 && scout.Carrying is null; t++)
            world.Step();

        Assert.Equal(Material.Coal, scout.Carrying);
        Assert.Equal(1, pile.Count);
    }

    [Fact]
    public void A_killed_thief_leaves_the_stolen_good()
    {
        var world = TwoLands();
        world.AddLooseGoods(new GridPoint(55, 40), Material.Gold, 1);
        var scout = world.Spawn(new GridPoint(54, 40), Profession.Scout)!;
        world.Theft.Steal([scout]);
        for (int t = 0; t < 500 && scout.Carrying is null; t++)
            world.Step();
        Assert.Equal(Material.Gold, scout.Carrying);
        Assert.Empty(world.Piles);

        world.SpawnSoldier(new GridPoint(58, 40), SoldierType.Swordsman, level: 3, player: 1);
        for (int t = 0; t < 2_000 && !scout.IsDead; t++)
            world.Step();

        Assert.True(scout.IsDead);
        Assert.Single(world.Piles, p => p.Material == Material.Gold);
    }
}
