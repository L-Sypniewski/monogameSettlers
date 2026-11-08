using Homestead.Simulation.Buildings;
using Homestead.Simulation.Economy;
using Homestead.Simulation.Events;
using Homestead.Simulation.Map;
using Homestead.Simulation.Pathfinding;
using Xunit.Abstractions;

namespace Homestead.Simulation.Tests;

public class MovementTests(ITestOutputHelper output)
{
    private static readonly BuildingCatalog Romans = BuildingCatalog.Load(Civilization.Roman);

    [Fact]
    public void Settler_walks_its_path_at_step_speed()
    {
        var world = new World(new GameMap(30));
        var settler = world.Spawn(new GridPoint(5, 5))!;
        var destination = new GridPoint(20, 12);
        Assert.True(world.WalkTo(settler, destination));
        int steps = PathFinder.Distance(settler.Position, destination);

        int ticks = RunUntil(world, () => settler.Destination is null, limit: 1000);

        Assert.Equal(destination, settler.Position);
        Assert.InRange(ticks, steps * World.StepTicks, steps * World.StepTicks + 2);
    }

    [Fact]
    public void Unreachable_destination_is_refused()
    {
        var map = new GameMap(20);
        map.SetTerrain(new GridPoint(10, 10), TerrainType.Water);
        var world = new World(map);
        var settler = world.Spawn(new GridPoint(2, 2))!;

        Assert.False(world.WalkTo(settler, new GridPoint(10, 10)));
        Assert.Null(settler.Destination);
    }

    [Fact]
    public void Settlers_meeting_in_a_corridor_swap_places()
    {
        var world = new World(Corridor(out var left, out var right));
        var a = world.Spawn(left)!;
        var b = world.Spawn(right)!;
        world.WalkTo(a, right);
        world.WalkTo(b, left);

        RunUntil(world, () => a.Destination is null && b.Destination is null, limit: 2000);

        Assert.Equal(right, a.Position);
        Assert.Equal(left, b.Position);
        Assert.Equal(0, world.PassThroughs);
    }

    [Fact]
    public void Idle_settler_in_the_way_steps_aside()
    {
        var world = new World(new GameMap(30));
        var walker = world.Spawn(new GridPoint(5, 15))!;
        var idle = world.Spawn(new GridPoint(15, 15))!;
        var destination = new GridPoint(15, 15); // exactly where the idle settler stands
        world.WalkTo(walker, destination);

        RunUntil(world, () => walker.Destination is null, limit: 1000);

        Assert.Equal(destination, walker.Position);
        Assert.NotEqual(destination, idle.Position);
        Assert.Equal(0, world.PassThroughs);
    }

    [Fact]
    public void Walker_pushes_idle_settlers_aside_without_stopping()
    {
        var world = new World(new GameMap(30));
        var walker = world.Spawn(new GridPoint(5, 15))!;
        world.Spawn(new GridPoint(10, 15));
        world.Spawn(new GridPoint(11, 15));
        world.Spawn(new GridPoint(15, 15));
        var destination = new GridPoint(25, 15);
        world.WalkTo(walker, destination);
        int steps = PathFinder.Distance(walker.Position, destination);

        int ticks = RunUntil(world, () => walker.Destination is null, limit: 1000);

        Assert.Equal(destination, walker.Position);
        Assert.InRange(ticks, steps * World.StepTicks, steps * World.StepTicks + 2);
        Assert.Equal(0, world.Detours);
        Assert.Equal(0, world.PassThroughs);
    }

    [Fact]
    public void Circular_blockade_resolves()
    {
        // Three settlers on a triangle, each wanting the field of the next one.
        var world = new World(new GameMap(30));
        var p0 = new GridPoint(10, 10);
        var p1 = p0.Neighbor(Direction.East);
        var p2 = p0.Neighbor(Direction.SouthEast);
        var settlers = new[] { world.Spawn(p0)!, world.Spawn(p1)!, world.Spawn(p2)! };
        world.WalkTo(settlers[0], p1);
        world.WalkTo(settlers[1], p2);
        world.WalkTo(settlers[2], p0);

        RunUntil(world, () => settlers.All(s => s.Destination is null), limit: 2000);

        Assert.Equal(p1, settlers[0].Position);
        Assert.Equal(p2, settlers[1].Position);
        Assert.Equal(p0, settlers[2].Position);
    }

    [Theory]
    [InlineData(7, 0.15, 150)]
    [InlineData(11, 0.25, 200)]
    [InlineData(23, 0.05, 400)]
    public void Crowd_on_a_cramped_map_never_gets_stuck(int seed, double obstacles, int settlerCount)
    {
        const int size = 64, ticks = 20_000;
        var random = new Random(seed);
        var map = new GameMap(size);
        for (int i = 0; i < map.PointCount; i++)
            if (random.NextDouble() < obstacles)
                map.SetTerrain(map.Point(i), TerrainType.Water);
        var world = new World(map);
        var walkable = Enumerable.Range(0, map.PointCount).Where(map.IsWalkable).Select(map.Point).ToArray();
        var settlers = Enumerable.Range(0, settlerCount)
            .Select(_ => world.Spawn(walkable[random.Next(walkable.Length)]))
            .OfType<Settler>().ToList();

        var lastProgress = new long[settlers.Count];
        var lastPosition = settlers.Select(s => s.Position).ToArray();
        long longestStall = 0;
        int arrivals = 0;

        for (int t = 0; t < ticks; t++)
        {
            foreach (var settler in settlers)
            {
                if (settler.Destination is not null || settler.State == SettlerState.Stepping)
                    continue;
                arrivals++;
                // New random destination; unreachable ones (other islands) are simply refused.
                while (!world.WalkTo(settler, walkable[random.Next(walkable.Length)])) { }
            }

            world.Step();

            for (int i = 0; i < settlers.Count; i++)
            {
                if (settlers[i].Position != lastPosition[i] || settlers[i].State == SettlerState.Stepping)
                {
                    lastPosition[i] = settlers[i].Position;
                    lastProgress[i] = world.Tick;
                }
                longestStall = Math.Max(longestStall, world.Tick - lastProgress[i]);
            }
        }

        output.WriteLine($"{settlers.Count} settlers, {ticks} ticks: {arrivals} arrivals, " +
                         $"{world.Detours} detours, {world.PassThroughs} pass-throughs, longest stall {longestStall} ticks");
        Assert.True(longestStall <= World.PassThroughAfterTicks + 2, $"A settler stood still for {longestStall} ticks.");
        Assert.True(arrivals > settlers.Count * 5);
    }

    [Fact]
    public void Walled_in_settler_is_moved_to_a_free_field_far_away()
    {
        // Ring of water r ≤ 19 around the settler: nearest free field is at distance 20.
        var map = new GameMap(60);
        var world = new World(map, TestConfig.NoTools);
        var settler = world.Spawn(new GridPoint(30, 30), Profession.Carrier)!;
        for (int dy = -19; dy <= 19; dy++)
            for (int dx = -19; dx <= 19; dx++)
            {
                var p = new GridPoint(30 + dx, 30 + dy);
                if (PathFinder.Distance(new GridPoint(30, 30), p) is > 0 and <= 19)
                    map.SetTerrain(p, TerrainType.Water);
            }
        world.PlaceBuilding(Romans["tower"], 0, new GridPoint(30, 30), force: true);
        Assert.Equal(20, PathFinder.Distance(new GridPoint(30, 30), settler.Position)); // freed (radius 12 alone would hang it)
        Assert.True(map.IsWalkable(settler.Position));
    }

    [Fact]
    public void A_settler_that_cannot_be_freed_is_reported()
    {
        var map = new GameMap(60);
        var world = new World(map, TestConfig.NoTools, events: EventConfig.Load());
        var settler = world.Spawn(new GridPoint(30, 30), Profession.Carrier)!;
        Assert.True(world.WalkTo(settler, new GridPoint(35, 35))); // holds an order when the walls go up
        for (int y = 0; y < 60; y++)
            for (int x = 0; x < 60; x++)
                if (!(x == 30 && y == 30))
                    map.SetTerrain(new GridPoint(x, y), TerrainType.Water); // full enclosure
        world.PlaceBuilding(Romans["tower"], 0, new GridPoint(30, 30), force: true);
        Assert.Contains(world.Events.Since(0, 0), e => e.Definition.Id == "settlerWalledIn");
        Assert.Equal(new GridPoint(30, 30), settler.Position); // defined end state: stays
        Assert.Equal(SettlerState.Idle, settler.State); // and drops its stale order
        Assert.Null(settler.Destination);
        Assert.Empty(settler.RemainingPath);
    }

    [Fact]
    public void Walled_in_settler_is_freed_up_to_the_map_size()
    {
        // Ring of water r <= 24 on a size-30 map: nearest free field is at distance 25, reachable
        // only when the widest rung clamps to the map size instead of being skipped.
        var map = new GameMap(30);
        var world = new World(map, TestConfig.NoTools, events: EventConfig.Load());
        var settler = world.Spawn(new GridPoint(15, 15), Profession.Carrier)!;
        for (int y = 0; y < 30; y++)
            for (int x = 0; x < 30; x++)
                if (PathFinder.Distance(new GridPoint(15, 15), new GridPoint(x, y)) is > 0 and <= 24)
                    map.SetTerrain(new GridPoint(x, y), TerrainType.Water);
        world.PlaceBuilding(Romans["tower"], 0, new GridPoint(15, 15), force: true);
        Assert.Equal(25, PathFinder.Distance(new GridPoint(15, 15), settler.Position));
        Assert.True(map.IsWalkable(settler.Position));
        Assert.DoesNotContain(world.Events.Since(0, 0), e => e.Definition.Id == "settlerWalledIn");
    }

    [Fact]
    public void Walled_in_settler_is_freed_by_radius_48()
    {
        // Ring of water r <= 32: nearest free field is at distance 33, found only by the unclamped 48 rung.
        var map = new GameMap(60);
        var world = new World(map, TestConfig.NoTools);
        var settler = world.Spawn(new GridPoint(30, 30), Profession.Carrier)!;
        for (int y = 0; y < 60; y++)
            for (int x = 0; x < 60; x++)
                if (PathFinder.Distance(new GridPoint(30, 30), new GridPoint(x, y)) is > 0 and <= 32)
                    map.SetTerrain(new GridPoint(x, y), TerrainType.Water);
        world.PlaceBuilding(Romans["tower"], 0, new GridPoint(30, 30), force: true);
        Assert.Equal(33, PathFinder.Distance(new GridPoint(30, 30), settler.Position));
        Assert.True(map.IsWalkable(settler.Position));
    }

    private static int RunUntil(World world, Func<bool> done, int limit)
    {
        for (int t = 1; t <= limit; t++)
        {
            world.Step();
            if (done())
                return t;
        }
        throw new Xunit.Sdk.XunitException($"Condition not reached within {limit} ticks.");
    }

    /// <summary>A one-field-wide horizontal corridor surrounded by water.</summary>
    private static GameMap Corridor(out GridPoint left, out GridPoint right)
    {
        var map = new GameMap(30);
        for (int i = 0; i < map.PointCount; i++)
        {
            var p = map.Point(i);
            if (p.Y != 10 || p.X < 2 || p.X > 27)
                map.SetTerrain(p, TerrainType.Water);
        }
        left = new GridPoint(3, 10);
        right = new GridPoint(26, 10);
        return map;
    }

    [Fact]
    public void Settlers_on_the_ground_of_a_new_building_are_put_beside_it()
    {
        var world = new World(new GameMap(60), Economy.ProductionConfig.None);
        var sawmill = Buildings.BuildingCatalog.Load(Buildings.Civilization.Roman)["sawmill"];
        var at = new GridPoint(30, 30);
        var settler = world.Spawn(at + sawmill.Blocked[0])!;

        Assert.NotNull(world.PlaceBuilding(sawmill, 0, at));

        Assert.True(world.Map!.IsWalkable(settler.Position));
        Assert.True(world.WalkTo(settler, new GridPoint(5, 5)));
    }
}
