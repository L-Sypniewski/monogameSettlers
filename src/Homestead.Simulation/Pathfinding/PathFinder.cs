using Homestead.Simulation.Map;

namespace Homestead.Simulation.Pathfinding;

/// <summary>
/// A* search on the six-neighbor grid with uniform step costs. Search buffers are allocated once
/// per map and reused, so a search allocates only its result. Not thread-safe.
/// </summary>
public sealed class PathFinder
{
    private readonly GameMap _map;
    private readonly int[] _cost;
    private readonly byte[] _arrivedFrom;
    private readonly int[] _seenStamp;
    private readonly int[] _closedStamp;
    private readonly PriorityQueue<int, long> _open = new();
    private int _stamp;

    private readonly Regions _regions;

    /// <summary>Like <see cref="_regions"/>, but neighbours only join when they have the same owner.</summary>
    private readonly Regions _landRegions;

    public PathFinder(GameMap map)
    {
        _map = map;
        _cost = new int[map.PointCount];
        _arrivedFrom = new byte[map.PointCount];
        _seenStamp = new int[map.PointCount];
        _closedStamp = new int[map.PointCount];
        _regions = new Regions(map, sameOwner: false);
        _landRegions = new Regions(map, sameOwner: true);
    }

    /// <summary>Number of nodes expanded by the last search (for tests and profiling).</summary>
    public int LastExpandedNodes { get; private set; }

    /// <summary>Times the whole map was divided into regions again (for tests and profiling).</summary>
    public int FullRegionLabels => _regions.FullLabels + _landRegions.FullLabels;

    /// <summary>
    /// Whether <paramref name="p"/> lies in the round area of <paramref name="radius"/> around
    /// <paramref name="center"/>: straight-line distance on the hex grid, where all six neighbors are at 1
    /// (squared: dx² + dy² − dx·dy, as in JSettlers' MapCircle). Work areas are such circles.
    /// </summary>
    public static bool InCircle(GridPoint center, GridPoint p, int radius)
    {
        int dx = p.X - center.X, dy = p.Y - center.Y;
        return dx * dx + dy * dy - dx * dy <= radius * radius;
    }

    /// <summary>All points of the circle (see <see cref="InCircle"/>), map bounds not checked.</summary>
    public static IEnumerable<GridPoint> Circle(GridPoint center, int radius)
    {
        // |dx| and |dy| can exceed the radius by up to 2/√3 in the diagonal directions.
        int reach = radius * 2;
        for (int dy = -reach; dy <= reach; dy++)
        for (int dx = -reach; dx <= reach; dx++)
        {
            var p = new GridPoint(center.X + dx, center.Y + dy);
            if (InCircle(center, p, radius))
                yield return p;
        }
    }

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
    /// (e.g. a field another settler stands on). With <paramref name="ownLandOf"/> the path stays on
    /// that player's land.
    /// </summary>
    public IReadOnlyList<Direction>? FindPath(GridPoint start, GridPoint target, IReadOnlySet<int>? avoid = null, int? ownLandOf = null)
    {
        LastExpandedNodes = 0;
        if (!_map.Contains(start) || !_map.IsWalkable(target) || avoid?.Contains(_map.Index(target)) == true)
            return null;
        byte landOf = ownLandOf is { } player ? (byte)player : GameMap.NoOwner;
        if (ownLandOf is not null && !AreConnected(start, target, ownLandOf))
            return null; // fail fast instead of searching the whole land
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
                if (!_map.IsWalkable(next) || _closedStamp[next] == _stamp || avoid?.Contains(next) == true
                    || ownLandOf is not null && _map.OwnerAt(next) != landOf)
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

    /// <summary>Whether a settler at <paramref name="a"/> could in principle walk to <paramref name="b"/>.</summary>
    public bool AreConnected(GridPoint a, GridPoint b) =>
        _map.Contains(a) && _map.IsWalkable(b) && (a == b || MayBeConnected(a, b));

    /// <summary>
    /// Like <see cref="AreConnected(GridPoint, GridPoint)"/>; with <paramref name="ownLandOf"/> only over
    /// that player's land.
    /// </summary>
    public bool AreConnected(GridPoint a, GridPoint b, int? ownLandOf)
    {
        if (ownLandOf is not { } player)
            return AreConnected(a, b);
        if (!_map.Contains(a) || !_map.IsWalkable(b) || _map.OwnerAt(_map.Index(b)) != player)
            return false;
        if (a == b)
            return true;
        _landRegions.Update();
        int targetRegion = _landRegions.Of(_map.Index(b));
        if (_map.IsWalkable(a) && _map.OwnerAt(_map.Index(a)) == player)
            return _landRegions.Of(_map.Index(a)) == targetRegion;
        // A start that is not walkable land itself (a doorway): any neighbour on the land will do.
        for (int d = 0; d < DirectionExtensions.Count; d++)
        {
            var neighbor = a.Neighbor((Direction)d);
            if (_map.IsWalkable(neighbor) && _landRegions.Of(_map.Index(neighbor)) == targetRegion)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Cheap rejection of unreachable targets via connected regions of walkable points.
    /// A non-walkable start counts as part of every region next to it.
    /// </summary>
    private bool MayBeConnected(GridPoint start, GridPoint target)
    {
        _regions.Update();
        int targetRegion = _regions.Of(_map.Index(target));
        if (_map.IsWalkable(start))
            return _regions.Of(_map.Index(start)) == targetRegion;

        for (int d = 0; d < DirectionExtensions.Count; d++)
        {
            var neighbor = start.Neighbor((Direction)d);
            if (_map.IsWalkable(neighbor) && _regions.Of(_map.Index(neighbor)) == targetRegion)
                return true;
        }
        return false;
    }
}
