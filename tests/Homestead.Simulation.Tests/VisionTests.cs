using Homestead.Simulation.Buildings;
using Homestead.Simulation.Map;
using Homestead.Simulation.Pathfinding;

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

    [Fact]
    public void A_lookout_tower_sees_far()
    {
        var world = new World(new GameMap(120), TestConfig.NoTools);
        var tower = world.PlaceBuilding(BuildingCatalog.Load(Civilization.Roman)["lookout_tower"], 0, new GridPoint(60, 60), force: true)!;
        var vision = new Vision(120, player: 0);

        vision.Update(world);

        int sight = world.Military.Config.BuildingSight["lookout_tower"];
        Assert.True(sight > world.Military.Config.SightRange);
        Assert.True(vision.IsVisible(new GridPoint(tower.Position.X + sight, tower.Position.Y)));
        Assert.False(vision.IsVisible(new GridPoint(tower.Position.X + sight + 1, tower.Position.Y)));
    }

    [Fact]
    public void The_seen_area_is_the_round_circle_of_the_pathfinder()
    {
        var world = new World(new GameMap(80), TestConfig.NoTools);
        var center = new GridPoint(40, 40);
        world.Spawn(center);
        var vision = new Vision(80, player: 0);

        vision.Update(world);

        for (int y = 0; y < 80; y++)
        for (int x = 0; x < 80; x++)
        {
            var p = new GridPoint(x, y);
            Assert.True(PathFinder.InCircle(center, p, Vision.SettlerSight) == vision.IsVisible(p), $"{x},{y}");
        }
    }
}
