using Homestead.Simulation.Buildings;
using Homestead.Simulation.Map;

namespace Homestead.Simulation.Tests;

public class MissionGoalsTests
{
    private static readonly BuildingCatalog Romans = BuildingCatalog.Load(Civilization.Roman);

    private static void Seconds(World world, int seconds)
    {
        for (int t = 0; t < seconds * World.TicksPerSecond; t++)
            world.Step();
    }

    [Fact]
    public void A_mission_is_won_once_no_other_player_has_a_building()
    {
        var world = new World(new GameMap(80), TestConfig.NoTools) { Goals = new MissionGoals() };
        world.PlaceBuilding(Romans["sawmill"], 0, new GridPoint(20, 20), force: true);
        var first = world.PlaceBuilding(Romans["sawmill"], 1, new GridPoint(50, 50), force: true)!;
        var second = world.PlaceBuilding(Romans["lumberjack"], 2, new GridPoint(60, 30), force: true)!;
        Seconds(world, 2);
        Assert.Equal(MissionOutcome.Open, world.Goals.Outcome);

        world.RemoveBuilding(first);
        Seconds(world, 2);
        Assert.Equal(MissionOutcome.Open, world.Goals.Outcome);

        world.RemoveBuilding(second);
        Seconds(world, 2);
        Assert.Equal(MissionOutcome.Won, world.Goals.Outcome);
        Assert.InRange(world.Goals.DecidedAt, 1, world.Tick);

        // Decided once: losing everything afterwards changes nothing.
        foreach (var building in world.Buildings.ToList())
            world.RemoveBuilding(building);
        Seconds(world, 2);
        Assert.Equal(MissionOutcome.Won, world.Goals.Outcome);
    }

    [Fact]
    public void A_mission_is_lost_without_an_own_building()
    {
        var world = new World(new GameMap(80), TestConfig.NoTools) { Goals = new MissionGoals() };
        var own = world.PlaceBuilding(Romans["sawmill"], 0, new GridPoint(20, 20), force: true)!;
        world.PlaceBuilding(Romans["sawmill"], 1, new GridPoint(50, 50), force: true);
        Seconds(world, 2);
        Assert.Equal(MissionOutcome.Open, world.Goals.Outcome);

        world.RemoveBuilding(own);
        Seconds(world, 2);
        Assert.Equal(MissionOutcome.Lost, world.Goals.Outcome);
    }

    [Fact]
    public void Free_games_have_no_goals()
    {
        var world = new World(new GameMap(40), TestConfig.NoTools);
        Seconds(world, 2);
        Assert.Null(world.Goals);
    }
}
