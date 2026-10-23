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
    public void Occupied_military_buildings_see_a_little_beyond_their_land_and_the_land_itself_sees_nothing()
    {
        var map = new GameMap(120);
        var world = new World(map, TestConfig.NoTools);
        var tower = world.PlaceBuilding(BuildingCatalog.Load(Civilization.Roman)["tower"], 0, new GridPoint(60, 60), force: true)!;
        var vision = new Vision(120, player: 0);
        vision.Update(world);
        Assert.False(vision.IsVisible(tower.Position), "an empty tower sees nothing");

        world.SpawnSoldier(tower.Door, SoldierType.Swordsman);
        for (int t = 0; t < 200 && !world.Military.IsOccupied(tower); t++)
            world.Step();
        Assert.True(world.Military.IsOccupied(tower));
        vision.Update(world);
        int sight = world.Military.SightOfBuilding(tower)!.Value;
        Assert.Equal((int)Math.Ceiling(world.Military.Config.Territory["tower"]) + world.Military.Config.MilitarySightMargin, sight);
        Assert.True(vision.IsVisible(new GridPoint(tower.Position.X + sight, tower.Position.Y)));
        Assert.False(vision.IsVisible(new GridPoint(tower.Position.X + sight + 1, tower.Position.Y)));

        // Torn down: the land stays the player's, but nobody sees it any more.
        world.RemoveBuilding(tower);
        Assert.Equal(0, map.OwnerAt(tower.Position));
        vision.Update(world);
        Assert.False(vision.IsVisible(new GridPoint(tower.Position.X + 10, tower.Position.Y)));
        Assert.True(vision.IsExplored(120 * tower.Position.Y + tower.Position.X + 10));
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

    [Fact]
    public void Donkeys_hardly_see_so_they_are_no_scouts()
    {
        var world = new World(new GameMap(80), TestConfig.NoTools);
        world.Spawn(new GridPoint(40, 40), Profession.Donkey);
        var vision = new Vision(80, player: 0);

        vision.Update(world);

        Assert.True(vision.IsVisible(new GridPoint(43, 40)));
        Assert.False(vision.IsVisible(new GridPoint(44, 40)));
    }

    [Fact]
    public void Every_other_finished_building_sees_a_little_houses_and_storehouses_too()
    {
        var romans = BuildingCatalog.Load(Civilization.Roman);
        var world = new World(new GameMap(120), TestConfig.NoTools);
        var site = world.PlaceConstructionSite(romans["sawmill"], 0, new GridPoint(90, 90))!;
        var vision = new Vision(120, player: 0);
        foreach (var name in new[] { "sawmill", "stock", "small_livinghouse" })
        {
            var building = world.PlaceBuilding(romans[name], 0, new GridPoint(60, 30 + 30 * Array.IndexOf(new[] { "sawmill", "stock", "small_livinghouse" }, name)))!;
            vision.Update(world);
            Assert.Equal(5, world.Military.SightOfBuilding(building));
            Assert.True(vision.IsVisible(new GridPoint(building.Position.X + 5, building.Position.Y)), name);
            Assert.False(vision.IsVisible(new GridPoint(building.Position.X + 6, building.Position.Y)), name);
        }
        Assert.Null(world.Military.SightOfBuilding(site));
        Assert.False(vision.IsVisible(site.Position), "a construction site sees nothing");
    }
}
