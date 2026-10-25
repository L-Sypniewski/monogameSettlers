using Homestead.Simulation.Buildings;
using Homestead.Simulation.Map;
using Homestead.Simulation.Pathfinding;

namespace Homestead.Simulation.Tests;

public class PioneerTests
{
    private static readonly BuildingCatalog Romans = BuildingCatalog.Load(Civilization.Roman);

    private static void Run(World world, int ticks, Func<bool>? stopWhen = null)
    {
        for (int t = 0; t < ticks && stopWhen?.Invoke() != true; t++)
            world.Step();
    }

    private static int Owned(GameMap map, int player)
    {
        int count = 0;
        for (int y = 0; y < map.Size; y++)
        for (int x = 0; x < map.Size; x++)
            if (map.OwnerAt(new GridPoint(x, y)) == player)
                count++;
        return count;
    }

    [Fact]
    public void Carriers_become_pioneers_and_back()
    {
        var world = new World(new GameMap(60), TestConfig.NoTools);
        for (int i = 0; i < 10; i++)
            world.Spawn(new GridPoint(10 + i, 10), Profession.Carrier);

        var (now, _) = world.Geology.ConvertCarriers(0, 2, Profession.Pioneer);
        Assert.Equal(2, now);
        Assert.All(world.Settlers.Where(s => s.Profession == Profession.Pioneer), p => Assert.True(p.Health > 0));

        Assert.Equal(1, world.Geology.PioneersToCarriers(0, 1));
        Assert.Single(world.Settlers, s => s.Profession == Profession.Pioneer);
    }

    [Fact]
    public void Carriers_given_are_converted_in_their_order_and_no_others()
    {
        var world = new World(new GameMap(60), TestConfig.NoTools);
        var carriers = Enumerable.Range(0, 10).Select(i => world.Spawn(new GridPoint(10 + i, 10), Profession.Carrier)!).ToList();
        var among = new[] { carriers[7], carriers[3] };

        var chosen = new List<Settler>();
        var (now, _) = world.Geology.ConvertCarriers(0, 5, Profession.Pioneer, chosen, among);

        Assert.Equal(2, now);
        Assert.Equal(among, chosen);
        Assert.Equal(2, world.Settlers.Count(s => s.Profession == Profession.Pioneer));
    }

    [Fact]
    public void At_the_edge_of_the_land_a_pioneer_grows_it_field_by_field()
    {
        var map = new GameMap(80);
        for (int y = 0; y < 80; y++)
        for (int x = 0; x < 30; x++)
            map.SetOwner(new GridPoint(x, y), 0);
        var world = new World(map, TestConfig.NoTools);
        var pioneer = world.Spawn(new GridPoint(28, 40), Profession.Pioneer)!;
        int before = Owned(map, 0);

        Assert.Equal(1, world.Pioneering.Claim([pioneer]));
        Run(world, 400, () => Owned(map, 0) > before);
        Assert.Equal(before + 1, Owned(map, 0)); // one border post at a time
        Assert.True(map.OwnerAt(new GridPoint(30, 40)) == 0 || map.OwnerAt(new GridPoint(30, 39)) == 0 || map.OwnerAt(new GridPoint(31, 41)) == 0);

        Run(world, 3_000);
        int grown = Owned(map, 0) - before;
        Assert.True(grown >= 10, $"only {grown} fields");
        Assert.True(world.Pioneering.IsClaiming(pioneer));
        // The land grows around where he was sent, joined to the old land.
        for (int y = 0; y < 80; y++)
        for (int x = 30; x < 80; x++)
            if (map.OwnerAt(new GridPoint(x, y)) == 0)
                Assert.True(PathFinder.Distance(new GridPoint(x, y), new GridPoint(29, 40)) <= 12, $"{x},{y}");
    }

    [Fact]
    public void Sent_to_free_land_a_pioneer_takes_it_there()
    {
        var map = new GameMap(80);
        var world = new World(map, TestConfig.NoTools);
        var pioneer = world.Spawn(new GridPoint(10, 10), Profession.Pioneer)!;

        Assert.Equal(1, world.Pioneering.ClaimAt([pioneer], new GridPoint(50, 50)));
        Run(world, 3_000, () => Owned(map, 0) >= 3);

        Assert.True(Owned(map, 0) >= 3);
        for (int y = 0; y < 80; y++)
        for (int x = 0; x < 80; x++)
            if (map.OwnerAt(new GridPoint(x, y)) == 0)
                Assert.True(PathFinder.Distance(new GridPoint(x, y), new GridPoint(50, 50)) <= 3, $"{x},{y}");
    }

    [Fact]
    public void Unprotected_land_of_others_is_taken_with_their_buildings()
    {
        var map = new GameMap(80);
        for (int y = 0; y < 80; y++)
        for (int x = 0; x < 80; x++)
            map.SetOwner(new GridPoint(x, y), x < 30 ? 0 : 1);
        var world = new World(map, TestConfig.NoTools);
        var hut = world.PlaceBuilding(Romans["mill"], 1, new GridPoint(32, 40), force: true)!;
        var pioneer = world.Spawn(new GridPoint(29, 40), Profession.Pioneer)!;

        world.Pioneering.Claim([pioneer]);
        Run(world, 4_000, () => !hut.Exists);

        Assert.False(hut.Exists);
        Assert.Equal(0, map.OwnerAt(new GridPoint(32, 40)));
    }

    [Fact]
    public void In_protected_enemy_land_a_pioneer_heads_for_free_land_nearby()
    {
        var map = new GameMap(120);
        var world = new World(map, TestConfig.NoTools);
        var tower = world.PlaceBuilding(Romans["tower"], 1, new GridPoint(80, 60), force: true)!;
        world.SpawnSoldier(tower.Door, SoldierType.Swordsman, player: 1);
        Run(world, 2_000, () => world.Military.IsOccupied(tower));
        Assert.True(world.Military.IsOccupied(tower));
        int enemy = Owned(map, 1);
        var pioneer = world.Spawn(new GridPoint(55, 60), Profession.Pioneer)!;
        Assert.Equal(1, map.OwnerAt(pioneer.Position));

        Assert.Equal(1, world.Pioneering.Claim([pioneer]));
        Run(world, 2_000, () => Owned(map, 0) >= 3);

        Assert.True(Owned(map, 0) >= 3);
        Assert.Equal(enemy, Owned(map, 1));
    }

    [Fact]
    public void At_the_shore_a_pioneer_takes_the_water_off_it_too()
    {
        var map = new GameMap(80);
        for (int y = 0; y < 80; y++)
        for (int x = 0; x < 80; x++)
        {
            var p = new GridPoint(x, y);
            if (x < 30)
                map.SetOwner(p, 0);
            else if (x >= 32)
                map.SetTerrain(p, TerrainType.Water);
        }
        var world = new World(map, TestConfig.NoTools);
        var pioneer = world.Spawn(new GridPoint(29, 40), Profession.Pioneer)!;

        world.Pioneering.Claim([pioneer]);
        Run(world, 3_000, () => map.OwnerAt(new GridPoint(31, 40)) == 0);

        Assert.Equal(0, map.OwnerAt(new GridPoint(31, 40)));
        // Water out to waterReach (6) from the shore: room for a dock, no border post at the shore.
        for (int x = 32; x <= 37; x++)
            Assert.Equal(0, map.OwnerAt(new GridPoint(x, 40)));
        Assert.Null(map.OwnerAt(new GridPoint(38, 40)));
    }

    [Fact]
    public void Protected_enemy_land_far_from_free_land_stops_a_pioneer()
    {
        var map = new GameMap(120);
        var world = new World(map, TestConfig.NoTools);
        var tower = world.PlaceBuilding(Romans["tower"], 1, new GridPoint(60, 60), force: true)!;
        world.SpawnSoldier(tower.Door, SoldierType.Swordsman, player: 1);
        Run(world, 2_000, () => world.Military.IsOccupied(tower));
        var pioneer = world.Spawn(new GridPoint(64, 64), Profession.Pioneer)!;

        Assert.Equal(0, world.Pioneering.Claim([pioneer]));
        Assert.False(pioneer.HasTask);
    }
}

public class PioneerEnclosureTests
{
    /// <summary>Own land left of x = 30 but for a hole around (12, 30) with a free corridor along y = 30 to the open land.</summary>
    private static (World World, GameMap Map) Holed(int? holeOwner)
    {
        var map = new GameMap(60);
        var hole = new GridPoint(12, 30);
        for (int y = 0; y < 60; y++)
        for (int x = 0; x < 30; x++)
        {
            var p = new GridPoint(x, y);
            if (Pathfinding.PathFinder.Distance(p, hole) <= 3)
                map.SetOwner(p, holeOwner);
            else if (y != 30 || x < 16)
                map.SetOwner(p, 0);
        }
        return (new World(map, TestConfig.NoTools), map);
    }

    [Fact]
    public void Closing_the_ring_takes_the_enclosed_free_land_at_once()
    {
        var (world, map) = Holed(holeOwner: null);
        var pioneer = world.Spawn(new GridPoint(25, 31), Profession.Pioneer)!;

        world.Pioneering.Claim([pioneer]);
        for (int t = 0; t < 400 && map.OwnerAt(new GridPoint(25, 30)) != 0; t++)
            world.Step();

        Assert.Equal(0, map.OwnerAt(new GridPoint(25, 30)));
        Assert.Equal(0, map.OwnerAt(new GridPoint(12, 30))); // the hole
        Assert.Equal(0, map.OwnerAt(new GridPoint(20, 30))); // the corridor behind him
        Assert.Null(map.OwnerAt(new GridPoint(28, 30)));     // still open towards the free land
    }

    [Fact]
    public void Enclosed_land_of_another_player_is_not_taken_at_once()
    {
        var (world, map) = Holed(holeOwner: 1);
        var pioneer = world.Spawn(new GridPoint(25, 31), Profession.Pioneer)!;

        world.Pioneering.Claim([pioneer]);
        for (int t = 0; t < 400 && map.OwnerAt(new GridPoint(25, 30)) != 0; t++)
            world.Step();

        Assert.Equal(0, map.OwnerAt(new GridPoint(25, 30)));
        Assert.Equal(1, map.OwnerAt(new GridPoint(12, 30)));
        Assert.Null(map.OwnerAt(new GridPoint(20, 30)));
    }
}
