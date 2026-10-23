using Homestead.Simulation.Map;
using Homestead.Simulation.Pathfinding;

namespace Homestead.Simulation.Tests;

public class PathFinderTests
{
    [Fact]
    public void Distance_matches_breadth_first_search_on_open_map()
    {
        var map = new GameMap(30);
        var from = new GridPoint(12, 15);
        var steps = Reference.Distances(map, from);
        for (int i = 0; i < map.PointCount; i++)
            Assert.Equal(steps[i], PathFinder.Distance(from, map.Point(i)));
    }

    [Fact]
    public void Path_on_open_map_has_minimal_length()
    {
        var map = new GameMap(50);
        var finder = new PathFinder(map);
        var start = new GridPoint(3, 40);
        var target = new GridPoint(45, 5);

        var path = finder.FindPath(start, target);

        Assert.NotNull(path);
        Assert.Equal(PathFinder.Distance(start, target), path.Count);
        AssertValid(map, start, target, path);
    }

    [Fact]
    public void Unreachable_target_gives_the_reachable_point_nearest_to_it()
    {
        // A lake with an island in it: the island and the water are out of reach from the west shore.
        var map = new GameMap(40);
        for (int y = 10; y < 30; y++)
        for (int x = 10; x < 30; x++)
            map.SetTerrain(new GridPoint(x, y), TerrainType.Water);
        for (int y = 18; y < 22; y++)
        for (int x = 18; x < 22; x++)
            map.SetTerrain(new GridPoint(x, y), TerrainType.Grass);
        var finder = new PathFinder(map);
        var from = new GridPoint(3, 20);

        Assert.Equal(new GridPoint(5, 5), finder.NearestReachable(from, new GridPoint(5, 5)));
        // In the water near the west shore, on the island: a shore field, none reachable nearer to the target.
        foreach (var target in new[] { new GridPoint(11, 20), new GridPoint(20, 12), new GridPoint(20, 20) })
            AssertNearestReachable(map, finder, from, target);
        // From the island nothing outside it.
        var back = finder.NearestReachable(new GridPoint(20, 20), new GridPoint(3, 20));
        Assert.Equal(18, back!.Value.X);
        Assert.Null(new PathFinder(map).NearestReachable(new GridPoint(20, 20), new GridPoint(3, 20), maxRadius: 5));
    }

    private static void AssertNearestReachable(GameMap map, PathFinder finder, GridPoint from, GridPoint target)
    {
        var found = finder.NearestReachable(from, target);
        Assert.NotNull(found);
        Assert.NotNull(finder.FindPath(from, found.Value));
        var steps = Reference.Distances(map, from);
        int nearest = Enumerable.Range(0, map.PointCount).Where(i => steps[i] != Reference.Unreachable).Min(i => PathFinder.Distance(map.Point(i), target));
        Assert.Equal(nearest, PathFinder.Distance(found.Value, target));
    }

    [Fact]
    public void Path_leads_through_the_gap_in_a_wall()
    {
        var map = new GameMap(30);
        for (int y = 0; y < 30; y++)
            if (y != 25)
                map.SetTerrain(new GridPoint(15, y), TerrainType.Water);
        var finder = new PathFinder(map);
        var start = new GridPoint(5, 5);
        var target = new GridPoint(25, 5);

        var path = finder.FindPath(start, target);

        Assert.NotNull(path);
        AssertValid(map, start, target, path);
        Assert.Equal(Reference.Distances(map, start)[map.Index(target)], path.Count);
    }

    [Fact]
    public void Enclosed_target_is_rejected_without_searching()
    {
        var map = new GameMap(30);
        var target = new GridPoint(20, 20);
        for (int d = 0; d < DirectionExtensions.Count; d++)
            map.SetTerrain(target.Neighbor((Direction)d), TerrainType.Water);
        var finder = new PathFinder(map);

        Assert.Null(finder.FindPath(new GridPoint(2, 2), target));
        Assert.Equal(0, finder.LastExpandedNodes);
    }

    [Fact]
    public void Blocked_target_and_outside_start_return_null()
    {
        var map = new GameMap(20);
        map.SetObject(new GridPoint(10, 10), MapObjectKind.Tree);
        var finder = new PathFinder(map);

        Assert.Null(finder.FindPath(new GridPoint(1, 1), new GridPoint(10, 10)));
        Assert.Null(finder.FindPath(new GridPoint(-1, 1), new GridPoint(5, 5)));
    }

    [Fact]
    public void Start_equal_to_target_gives_empty_path()
    {
        var finder = new PathFinder(new GameMap(10));
        Assert.Empty(finder.FindPath(new GridPoint(4, 4), new GridPoint(4, 4))!);
    }

    [Fact]
    public void Start_may_be_blocked()
    {
        var map = new GameMap(20);
        var start = new GridPoint(5, 5);
        map.SetObject(start, MapObjectKind.Tree);
        var finder = new PathFinder(map);

        var path = finder.FindPath(start, new GridPoint(15, 12));

        Assert.NotNull(path);
        Assert.Equal(PathFinder.Distance(start, new GridPoint(15, 12)), path.Count);
    }

    [Fact]
    public void Walkability_changes_are_picked_up()
    {
        var map = new GameMap(30);
        for (int y = 0; y < 30; y++)
            if (y != 25)
                map.SetTerrain(new GridPoint(15, y), TerrainType.Water);
        var finder = new PathFinder(map);
        var start = new GridPoint(5, 5);
        var target = new GridPoint(25, 5);
        Assert.NotNull(finder.FindPath(start, target));

        map.SetObject(new GridPoint(15, 25), MapObjectKind.Tree); // close the gap
        Assert.Null(finder.FindPath(start, target));

        map.SetObject(new GridPoint(15, 25), MapObjectKind.None); // tree cut down
        Assert.NotNull(finder.FindPath(start, target));
    }

    [Fact]
    public void Random_maps_agree_with_breadth_first_search()
    {
        var random = new Random(1234);
        for (int round = 0; round < 100; round++)
        {
            var map = new GameMap(40);
            for (int i = 0; i < map.PointCount; i++)
                if (random.NextDouble() < 0.35)
                    map.SetTerrain(map.Point(i), TerrainType.Water);
            var finder = new PathFinder(map);

            for (int query = 0; query < 10; query++)
            {
                var start = new GridPoint(random.Next(40), random.Next(40));
                var target = new GridPoint(random.Next(40), random.Next(40));
                var distances = Reference.Distances(map, start);
                int expected = map.IsWalkable(target) ? distances[map.Index(target)] : Reference.Unreachable;

                var path = finder.FindPath(start, target);

                if (expected == Reference.Unreachable)
                {
                    Assert.Null(path);
                }
                else
                {
                    Assert.NotNull(path);
                    Assert.Equal(expected, path.Count);
                    AssertValid(map, start, target, path);
                }
            }
        }
    }

    [Fact]
    public void Regions_kept_up_to_date_agree_with_fresh_ones()
    {
        var random = new Random(99);
        var map = new GameMap(40);
        for (int i = 0; i < map.PointCount; i++)
        {
            if (random.NextDouble() < 0.3)
                map.SetTerrain(map.Point(i), TerrainType.Water);
            map.SetOwner(map.Point(i), map.Point(i).X < 20 ? 0 : 1);
        }
        var finder = new PathFinder(map);
        var blocked = new List<GridPoint>();
        for (int round = 0; round < 300; round++)
        {
            // A few trees felled and planted, now and then a whole building.
            int changes = random.Next(4) == 0 ? 20 : random.Next(1, 4);
            for (int i = 0; i < changes; i++)
            {
                if (blocked.Count > 0 && random.Next(2) == 0)
                {
                    int k = random.Next(blocked.Count);
                    map.RemoveBlocker(blocked[k]);
                    blocked.RemoveAt(k);
                }
                else
                {
                    var p = new GridPoint(random.Next(40), random.Next(40));
                    map.AddBlocker(p);
                    blocked.Add(p);
                }
            }
            var fresh = new PathFinder(map);
            for (int query = 0; query < 30; query++)
            {
                var a = new GridPoint(random.Next(40), random.Next(40));
                var b = new GridPoint(random.Next(40), random.Next(40));
                Assert.Equal(fresh.AreConnected(a, b), finder.AreConnected(a, b));
                int player = random.Next(2);
                Assert.Equal(fresh.AreConnected(a, b, player), finder.AreConnected(a, b, player));
            }
        }
        // Most changes were handled without dividing the whole map again.
        Assert.True(finder.FullRegionLabels < 150, $"{finder.FullRegionLabels} full labels");
    }

    /// <summary>Follows <see cref="PathFinder.FindRoute"/> leg by leg; the points walked, or null if it gave up.</summary>
    private static List<GridPoint>? WalkRoute(GameMap map, PathFinder finder, GridPoint start, GridPoint target, int maxLegExpanded)
    {
        var walked = new List<GridPoint> { start };
        for (int legs = 0; legs < 200; legs++)
        {
            var path = finder.FindRoute(walked[^1], target, out bool partial);
            if (path is null)
                return null;
            Assert.True(!partial || finder.LastExpandedNodes <= maxLegExpanded, $"a leg expanded {finder.LastExpandedNodes} nodes");
            foreach (var step in path)
            {
                walked.Add(walked[^1].Neighbor(step));
                Assert.True(map.IsWalkable(walked[^1]), $"Route crosses blocked point {walked[^1]}.");
            }
            if (!partial)
                return walked;
            Assert.NotEmpty(path);
        }
        return null;
    }

    [Fact]
    public void Far_targets_are_reached_leg_by_leg_almost_as_short_as_exact()
    {
        var random = new Random(7);
        var map = new GameMap(320);
        // Lakes of all sizes.
        for (int lake = 0; lake < 260; lake++)
        {
            var center = new GridPoint(random.Next(320), random.Next(320));
            foreach (var p in PathFinder.Circle(center, random.Next(1, 9)))
                if (map.Contains(p))
                    map.SetTerrain(p, TerrainType.Water);
        }
        var finder = new PathFinder(map);
        int routes = 0;
        for (int query = 0; query < 40; query++)
        {
            var start = new GridPoint(random.Next(320), random.Next(320));
            var target = new GridPoint(random.Next(320), random.Next(320));
            if (!map.IsWalkable(start) || !finder.AreConnected(start, target) || PathFinder.Distance(start, target) <= PathFinder.ExactDistance)
                continue;
            var exact = finder.FindPath(start, target)!;
            int exactExpanded = finder.LastExpandedNodes;

            var walked = WalkRoute(map, finder, start, target, maxLegExpanded: exactExpanded / 4 + 500);

            Assert.NotNull(walked);
            Assert.Equal(target, walked[^1]);
            Assert.True(walked.Count - 1 <= exact.Count * 1.2, $"{walked.Count - 1} steps instead of {exact.Count}");
            routes++;
        }
        Assert.True(routes >= 10, $"only {routes} far routes");
    }

    [Fact]
    public void Legs_follow_walkability_changes()
    {
        // A wall across the map with a gap at either end.
        var map = new GameMap(300);
        for (int y = 0; y < 300; y++)
            if (y is not 10 and not 290)
                map.SetTerrain(new GridPoint(150, y), TerrainType.Water);
        var finder = new PathFinder(map);
        var start = new GridPoint(20, 20);
        var target = new GridPoint(280, 30);
        Assert.NotNull(WalkRoute(map, finder, start, target, maxLegExpanded: 20_000));

        map.SetObject(new GridPoint(150, 10), MapObjectKind.Tree); // the near gap closes
        var walked = WalkRoute(map, finder, start, target, maxLegExpanded: 20_000);

        Assert.NotNull(walked);
        Assert.Equal(target, walked[^1]);
        Assert.Contains(new GridPoint(150, 290), walked);
    }

    [Fact]
    public void Route_survives_a_cell_merging_its_parts_next_to_a_known_one()
    {
        // A short wall splits the cell at 32..47 × 0..15 in two parts; the cell east of it knows the second.
        var map = new GameMap(300);
        for (int y = 0; y < 16; y++)
            map.SetTerrain(new GridPoint(40, y), TerrainType.Water);
        var finder = new PathFinder(map);
        var start = new GridPoint(60, 8);
        for (int i = 0; i < 2; i++) // the second time all cells are labelled and their neighbours kept
            Assert.NotNull(finder.FindRoute(start, new GridPoint(280, 8), out _));

        for (int y = 0; y < 16; y++)
            map.SetTerrain(new GridPoint(40, y), TerrainType.Grass); // one part now

        Assert.NotNull(WalkRoute(map, finder, start, new GridPoint(10, 200), maxLegExpanded: 20_000));
    }

    [Fact]
    public void A_settler_walks_a_far_way_leg_by_leg_to_its_destination()
    {
        var map = new GameMap(300);
        var world = new World(map, Homestead.Simulation.Economy.ProductionConfig.None);
        var soldier = world.SpawnSoldier(new GridPoint(10, 10), SoldierType.Swordsman)!;
        var destination = new GridPoint(290, 280);

        Assert.True(world.WalkTo(soldier, destination));
        Assert.True(soldier.RemainingPath.Count() < PathFinder.Distance(soldier.Position, destination), "only a leg planned");
        for (int t = 0; t < 400 * World.TicksPerSecond && soldier.Position != destination; t++)
            world.Step();

        Assert.Equal(destination, soldier.Position);
        Assert.Null(soldier.Destination);
    }

    internal static void AssertValid(GameMap map, GridPoint start, GridPoint target, IReadOnlyList<Direction> path)
    {
        var position = start;
        foreach (var step in path)
        {
            position = position.Neighbor(step);
            Assert.True(map.IsWalkable(position), $"Path crosses blocked point {position}.");
        }
        Assert.Equal(target, position);
    }
}

/// <summary>Plain breadth-first search as ground truth.</summary>
internal static class Reference
{
    public const int Unreachable = int.MaxValue;

    public static int[] Distances(GameMap map, GridPoint start)
    {
        var distance = Enumerable.Repeat(Unreachable, map.PointCount).ToArray();
        var queue = new Queue<GridPoint>();
        distance[map.Index(start)] = 0;
        queue.Enqueue(start);
        while (queue.TryDequeue(out var current))
        {
            for (int d = 0; d < DirectionExtensions.Count; d++)
            {
                var next = current.Neighbor((Direction)d);
                if (!map.IsWalkable(next) || distance[map.Index(next)] != Unreachable)
                    continue;
                distance[map.Index(next)] = distance[map.Index(current)] + 1;
                queue.Enqueue(next);
            }
        }
        return distance;
    }
}
