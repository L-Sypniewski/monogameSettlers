using Homestead.Simulation.Buildings;
using Homestead.Simulation.Events;
using Homestead.Simulation.Map;

namespace Homestead.Simulation.Tests;

public class EventTests
{
    private static readonly EventConfig Attacks = new(new Dictionary<string, EventDefinition>
    {
        ["attacked"] = new("attacked", EventCategory.Combat, 60 * World.TicksPerSecond, "attacked"),
    });

    [Fact]
    public void A_fight_raises_one_attack_event_per_minute_for_the_attacked_player_only()
    {
        var world = new World(new GameMap(40), TestConfig.NoTools, events: Attacks);
        // Strong enough on both sides to fight on for a while.
        var own = world.SpawnSoldier(new GridPoint(20, 20), SoldierType.Swordsman, level: 3)!;
        world.SpawnSoldier(new GridPoint(22, 20), SoldierType.Swordsman, level: 3, player: 1);

        for (int t = 0; t < 400 && world.Events.Latest(0) is null; t++)
            world.Step();

        var first = world.Events.Latest(0);
        Assert.NotNull(first);
        Assert.Equal(EventCategory.Combat, first.Category);
        Assert.Same(own, first.Settler);

        // The fight goes on: no second event within the minute.
        for (int t = 0; t < 30 * World.TicksPerSecond; t++)
            world.Step();
        Assert.Single(world.Events.Since(0, 0));
    }

    [Fact]
    public void An_event_comes_again_after_its_cooldown()
    {
        var world = new World(new GameMap(20), TestConfig.NoTools, events: Attacks);
        var at = new GridPoint(5, 5);
        Assert.NotNull(world.Events.Raise("attacked", 0, at));
        Assert.Null(world.Events.Raise("attacked", 0, at));
        Assert.NotNull(world.Events.Raise("attacked", 1, at)); // each player has its own
        for (int t = 0; t < 60 * World.TicksPerSecond; t++)
            world.Step();
        Assert.NotNull(world.Events.Raise("attacked", 0, at));
        Assert.Equal(2, world.Events.Since(0, 0).Count());
        Assert.Null(world.Events.Raise("unknown", 0, at));
    }

    private static readonly BuildingCatalog Romans = BuildingCatalog.Load(Civilization.Roman);

    [Fact]
    public void A_finished_building_without_a_worker_is_reported_once_after_a_while()
    {
        var world = new World(new GameMap(60), TestConfig.NoTools, events: EventConfig.Load());
        var sawmill = world.PlaceBuilding(Romans["sawmill"], 0, new GridPoint(30, 30))!; // no carrier anywhere
        var noStaff = world.Events.Config.Events["noStaff"];

        Run(world, noStaff.AfterTicks - World.TicksPerSecond);
        Assert.Null(world.Events.Latest(0));
        Run(world, 2 * World.TicksPerSecond);
        var e = world.Events.Latest(0);
        Assert.NotNull(e);
        Assert.Equal("noStaff", e.Definition.Id);
        Assert.Equal(EventCategory.Info, e.Category);
        Assert.Same(sawmill, e.Building);

        Run(world, noStaff.CooldownTicks + World.TicksPerSecond);
        Assert.Single(world.Events.Since(0, 0)); // the same state of the same building only once
    }

    [Fact]
    public void A_lumberjack_without_trees_reports_that_it_finds_nothing()
    {
        var world = new World(new GameMap(60), TestConfig.NoTools, events: EventConfig.Load());
        var lumberjack = world.PlaceBuilding(Romans["lumberjack"], 0, new GridPoint(30, 30))!;
        Assert.NotNull(world.Spawn(new GridPoint(36, 34), Profession.Carrier));

        Run(world, 60 * World.TicksPerSecond);

        var e = world.Events.Since(0, 0).SingleOrDefault(e => e.Definition.Id == "noResources");
        Assert.NotNull(e);
        Assert.Equal(EventCategory.Resources, e.Category);
        Assert.Same(lumberjack, e.Building);
    }

    [Fact]
    public void Taking_a_tower_tells_the_winner_and_the_loser()
    {
        var world = new World(new GameMap(100), TestConfig.NoTools, events: EventConfig.Load());
        var tower = world.PlaceBuilding(Romans["tower"], 1, new GridPoint(50, 50))!;
        world.SpawnSoldier(tower.Door, SoldierType.Swordsman, player: 1);
        Run(world, 2_000, () => world.Military.IsOccupied(tower));
        var door = tower.Door;
        for (int i = 0; i < 3; i++)
            world.SpawnSoldier(new GridPoint(door.X - 10 + i, door.Y + 2), SoldierType.Swordsman, level: 3);
        Run(world, 20_000, () => tower.Player == 0);

        Assert.Equal(0, tower.Player);
        Assert.Contains(world.Events.Since(0, 1), e => e.Definition.Id == "buildingAttacked" && e.Building == tower);
        Assert.Contains(world.Events.Since(0, 1), e => e.Definition.Id == "buildingLost" && e.Building == tower);
        Assert.Contains(world.Events.Since(0, 0), e => e.Definition.Id == "buildingCaptured" && e.Building == tower);
    }

    [Fact]
    public void An_announcement_has_no_place()
    {
        var world = new World(new GameMap(20), TestConfig.NoTools);
        var e = world.Events.Announce("date:12-25", EventCategory.Info, "Frohe Weihnachten!", 0);
        Assert.Null(e.Where);
        Assert.Same(e, world.Events.Latest(0, EventCategory.Info));
    }

    private static void Run(World world, int ticks, Func<bool>? stopWhen = null)
    {
        for (int t = 0; t < ticks && stopWhen?.Invoke() != true; t++)
            world.Step();
    }
}
