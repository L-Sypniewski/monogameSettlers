using Homestead.Simulation.Map;

namespace Homestead.Simulation.Tests;

public class VisionTests
{
    [Fact]
    public void Own_settlers_see_around_them_and_what_they_saw_stays_explored()
    {
        var world = new World(new GameMap(80), TestConfig.NoTools);
        var carrier = world.Spawn(new GridPoint(20, 20))!;
        world.Spawn(new GridPoint(60, 60), player: 1);
        var vision = new Vision(80, player: 0);

        vision.Update(world);
        Assert.True(vision.IsVisible(new GridPoint(20 + Vision.SettlerSight, 20)));
        Assert.False(vision.IsVisible(new GridPoint(20 + Vision.SettlerSight + 1, 20)));
        Assert.False(vision.IsVisible(new GridPoint(60, 60)), "an enemy settler gives no sight");

        world.WalkTo(carrier, new GridPoint(50, 20));
        for (int t = 0; t < 2_000 && carrier.Position != new GridPoint(50, 20); t++)
            world.Step();
        vision.Update(world);

        var start = new GridPoint(20, 20);
        Assert.False(vision.IsVisible(start));
        Assert.True(vision.IsExplored(80 * start.Y + start.X));
        Assert.True(vision.IsVisible(new GridPoint(50, 20)));
    }

    [Fact]
    public void Soldiers_see_further_than_other_settlers()
    {
        var world = new World(new GameMap(80), TestConfig.NoTools);
        world.SpawnSoldier(new GridPoint(40, 40), SoldierType.Swordsman);
        var vision = new Vision(80, player: 0);

        vision.Update(world);

        int sight = world.Military.Config.Sight(SoldierType.Swordsman);
        Assert.True(sight > Vision.SettlerSight);
        Assert.True(vision.IsVisible(new GridPoint(40 + sight, 40)));
    }
}
