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
