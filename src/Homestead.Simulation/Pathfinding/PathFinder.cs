using Homestead.Simulation.Map;

namespace Homestead.Simulation.Pathfinding;

/// <summary>
/// A* search on the six-neighbor grid with uniform step costs. Search buffers are allocated once
/// per map and reused, so a search allocates only its result. Not thread-safe.
/// </summary>
public sealed class PathFinder
{
    private const int NoRegion = -1;

    private readonly GameMap _map;
    private readonly int[] _cost;
    private readonly byte[] _arrivedFrom;
    private readonly int[] _seenStamp;
    private readonly int[] _closedStamp;
    private readonly PriorityQueue<int, long> _open = new();
    private int _stamp;

    private readonly int[] _region;
    private int _regionVersion = -1;

    public PathFinder(GameMap map)
    {
        _map = map;
        _cost = new int[map.PointCount];
        _arrivedFrom = new byte[map.PointCount];
        _seenStamp = new int[map.PointCount];
        _closedStamp = new int[map.PointCount];
        _region = new int[map.PointCount];
    }

    /// <summary>Number of nodes expanded by the last search (for tests and profiling).</summary>
    public int LastExpandedNodes { get; private set; }

    /// <summary>Number of steps without obstacles.</summary>
    public static int Distance(GridPoint a, GridPoint b)
    {
        int dx = b.X - a.X, dy = b.Y - a.Y;
        return Math.Max(Math.Max(Math.Abs(dx), Math.Abs(dy)), Math.Abs(dx - dy));
    }

    /// <summary>
    /// Shortest path as a list of steps, or null if the target cannot be reached.
    /// The start itself does not need to be walkable (a settler may stand in a doorway).
    /// <paramref name="avoid"/> lists point indices to treat as blocked for this search only
    /// (e.g. a field another settler stands on).
    /// </summary>
    public IReadOnlyList<Direction>? FindPath(GridPoint start, GridPoint target, IReadOnlySet<int>? avoid = null)
    {
        LastExpandedNodes = 0;
        if (!_map.Contains(start) || !_map.IsWalkable(target) || avoid?.Contains(_map.Index(target)) == true)
            return null;
        if (start == target)
            return [];
        if (!MayBeConnected(start, target))
            return null;

        if (++_stamp == int.MaxValue)
        {
            Array.Clear(_seenStamp);
            Array.Clear(_closedStamp);
            _stamp = 1;
        }

        int size = _map.Size;
        int startIndex = _map.Index(start);
        int targetIndex = _map.Index(target);
        _open.Clear();
        _cost[startIndex] = 0;
        _seenStamp[startIndex] = _stamp;
        _open.Enqueue(startIndex, Priority(0, Distance(start, target)));

        while (_open.TryDequeue(out int current, out _))
        {
            if (_closedStamp[current] == _stamp)
                continue; // stale queue entry
            _closedStamp[current] = _stamp;
            LastExpandedNodes++;

            if (current == targetIndex)
                return BuildPath(startIndex, targetIndex);

            int x = current % size, y = current / size;
            int nextCost = _cost[current] + 1;
            for (int d = 0; d < DirectionExtensions.Count; d++)
            {
                var delta = ((Direction)d).Delta();
                int nx = x + delta.X, ny = y + delta.Y;
                if ((uint)nx >= (uint)size || (uint)ny >= (uint)size)
                    continue;
                int next = ny * size + nx;
                if (!_map.IsWalkable(next) || _closedStamp[next] == _stamp || avoid?.Contains(next) == true)
                    continue;
                if (_seenStamp[next] == _stamp && _cost[next] <= nextCost)
                    continue;

                _seenStamp[next] = _stamp;
                _cost[next] = nextCost;
                _arrivedFrom[next] = (byte)d;
                _open.Enqueue(next, Priority(nextCost, Distance(new GridPoint(nx, ny), target)));
            }
        }
        return null;
    }

    /// <summary>Lower f first; on ties prefer nodes further along (higher cost), which keeps the search narrow.</summary>
    private static long Priority(int cost, int heuristic) => ((long)(cost + heuristic) << 32) - cost;

    private Direction[] BuildPath(int startIndex, int targetIndex)
    {
        var steps = new Direction[_cost[targetIndex]];
        int size = _map.Size;
        for (int index = targetIndex, i = steps.Length - 1; index != startIndex; i--)
        {
            var direction = (Direction)_arrivedFrom[index];
            steps[i] = direction;
            var delta = direction.Delta();
            index -= delta.Y * size + delta.X;
        }
        return steps;
    }

    /// <summary>
    /// Cheap rejection of unreachable targets via connected regions of walkable points.
    /// A non-walkable start counts as part of every region next to it.
    /// </summary>
    private bool MayBeConnected(GridPoint start, GridPoint target)
    {
        UpdateRegions();
        int targetRegion = _region[_map.Index(target)];
        if (_map.IsWalkable(start))
            return _region[_map.Index(start)] == targetRegion;

        for (int d = 0; d < DirectionExtensions.Count; d++)
        {
            var neighbor = start.Neighbor((Direction)d);
            if (_map.IsWalkable(neighbor) && _region[_map.Index(neighbor)] == targetRegion)
                return true;
        }
        return false;
    }

    private void UpdateRegions()
    {
        if (_regionVersion == _map.WalkabilityVersion)
            return;
        _regionVersion = _map.WalkabilityVersion;

        Array.Fill(_region, NoRegion);
        int size = _map.Size;
        var queue = new Queue<int>();
        int regionCount = 0;
        for (int seed = 0; seed < _region.Length; seed++)
        {
            if (_region[seed] != NoRegion || !_map.IsWalkable(seed))
                continue;

            int region = regionCount++;
            _region[seed] = region;
            queue.Enqueue(seed);
            while (queue.TryDequeue(out int current))
            {
                int x = current % size, y = current / size;
                for (int d = 0; d < DirectionExtensions.Count; d++)
                {
                    var delta = ((Direction)d).Delta();
                    int nx = x + delta.X, ny = y + delta.Y;
                    if ((uint)nx >= (uint)size || (uint)ny >= (uint)size)
                        continue;
                    int next = ny * size + nx;
                    if (_region[next] == NoRegion && _map.IsWalkable(next))
                    {
                        _region[next] = region;
                        queue.Enqueue(next);
                    }
                }
            }
        }
    }
}
