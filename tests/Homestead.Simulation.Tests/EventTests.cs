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
}
