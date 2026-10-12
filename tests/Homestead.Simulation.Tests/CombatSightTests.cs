using Homestead.Simulation.Map;
using Homestead.Simulation.Pathfinding;

namespace Homestead.Simulation.Tests;

public class CombatSightTests
{
    [Fact]
    public void A_moving_bowman_shoots_an_enemy_passing_at_a_distance()
    {
        var world = new World(new GameMap(80), TestConfig.NoTools);
        var bowman = world.SpawnSoldier(new GridPoint(10, 40), SoldierType.Bowman)!;
        var enemy = world.SpawnSoldier(new GridPoint(40, 34), SoldierType.Swordsman, player: 1)!; // 6 rows off the way
        float health = enemy.Health;
        world.Military.Move([bowman], new GridPoint(70, 40));

        int closest = int.MaxValue;
        for (int t = 0; t < 2_000 && enemy.Health == health; t++)
        {
            world.Step();
            closest = Math.Min(closest, PathFinder.Distance(bowman.Position, enemy.Position));
        }

        Assert.True(enemy.Health < health, "the bowman shot");
        Assert.True(closest > 1, $"it shot from a distance, closest {closest}");
    }

    [Fact]
    public void A_soldier_walks_around_a_row_of_fighting_friends_instead_of_queueing_behind_them()
    {
        var world = new World(new GameMap(80), TestConfig.NoTools);
        var friends = new List<Settler>();
        for (int y = 36; y <= 44; y++)
        {
            world.SpawnSoldier(new GridPoint(41, y), SoldierType.Pikeman, level: 3, player: 1);
            friends.Add(world.SpawnSoldier(new GridPoint(40, y), SoldierType.Swordsman, level: 3)!);
        }
        for (int t = 0; t < 30; t++)
            world.Step();
        Assert.All(friends, f => Assert.Equal(Activity.Fighting, f.Activity));
        // In front of the middle of the row.
        var newcomer = world.SpawnSoldier(new GridPoint(33, 40), SoldierType.Swordsman)!;

        bool joined = false;
        for (int t = 0; t < 200 && !joined; t++)
        {
            world.Step();
            joined = newcomer.Activity == Activity.Fighting && newcomer.State != SettlerState.Stepping
                     && friends.All(f => f.Position != newcomer.Position);
        }

        Assert.True(joined, $"it went around and fights too (at {newcomer.Position.X},{newcomer.Position.Y})");
    }
}
