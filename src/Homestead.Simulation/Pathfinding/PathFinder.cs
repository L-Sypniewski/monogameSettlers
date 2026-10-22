using Homestead.Simulation.Map;

namespace Homestead.Simulation.Pathfinding;

/// <summary>
/// A* search on the six-neighbor grid; a step costs by the ground it enters (see <see cref="SetStepCosts"/>:
/// trails cheaper, mountains dearer, as settlers walk there). Search buffers are allocated once
/// per map and reused, so a search allocates only its result. Not thread-safe.
/// Far targets go leg by leg (<see cref="FindRoute"/>): a coarse route over cells of the map, the exact path
/// only to the next part of it.
/// </summary>
public sealed class PathFinder
{
    private readonly GameMap _map;
    private readonly int[] _cost;
    private readonly int[] _steps;
    private int _plainCost = 1, _trailCost = 1, _mountainCost = 1, _minCost = 1;
    private readonly byte[] _arrivedFrom;
    private readonly int[] _seenStamp;
    private readonly int[] _closedStamp;
    private readonly PriorityQueue<int, long> _open = new();
    private int _stamp;

    private readonly Regions _regions;

    /// <summary>Like <see cref="_regions"/>, but neighbours only join when they have the same owner.</summary>
    private readonly Regions _landRegions;

    private readonly Clusters _clusters;

    /// <summary>Targets up to this many steps away are searched exactly at once (<see cref="FindRoute"/>).</summary>
    public const int ExactDistance = 96;

    /// <summary>Cells of the coarse route one leg goes ahead.</summary>
    private const int LegNodes = 4;

    public PathFinder(GameMap map)
    {
        _map = map;
        _cost = new int[map.PointCount];
        _steps = new int[map.PointCount];
        _arrivedFrom = new byte[map.PointCount];
        _seenStamp = new int[map.PointCount];
        _closedStamp = new int[map.PointCount];
        _regions = new Regions(map, sameOwner: false);
        _landRegions = new Regions(map, sameOwner: true);
        _clusters = new Clusters(map);
    }

    /// <summary>What a step onto plain ground, a trail and a mountain costs (proportional to the time it takes).</summary>
    public void SetStepCosts(int plain, int trail, int mountain)
    {
        (_plainCost, _trailCost, _mountainCost) = (plain, trail, mountain);
        _minCost = Math.Min(plain, Math.Min(trail, mountain));
    }

    private int StepCost(int index) =>
        _map.CoverAt(index) == GroundCover.Trail ? _trailCost
        : _map.TerrainAt(index) is TerrainType.Mountain or TerrainType.MountainBorder ? _mountainCost
        : _plainCost;

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
        if (!Searchable(start, target, avoid, ownLandOf, out var trivial))
            return trivial;
        return Search(start, target, goalNode: -1, avoid, ownLandOf);
    }

    /// <summary>
    /// Like <see cref="FindPath"/>, but a target further than <see cref="ExactDistance"/> (off the own land) is
    /// reached leg by leg: a coarse route over the map's cells, the exact path only a few cells ahead along it
    /// (<paramref name="partial"/>; the walker plans the next leg at its end). Near the target the path is exact.
    /// </summary>
    public IReadOnlyList<Direction>? FindRoute(GridPoint start, GridPoint target, out bool partial,
        IReadOnlySet<int>? avoid = null, int? ownLandOf = null)
    {
        partial = false;
        LastExpandedNodes = 0;
        if (!Searchable(start, target, avoid, ownLandOf, out var trivial))
            return trivial;
        if (ownLandOf is not null || Distance(start, target) <= ExactDistance)
            return Search(start, target, goalNode: -1, avoid, ownLandOf);
        // A start that is not walkable itself (a doorway): the route from a walkable neighbour.
        int startIndex = _map.Index(start);
        if (!_map.IsWalkable(startIndex))
        {
            for (int d = 0; d < DirectionExtensions.Count && !_map.IsWalkable(startIndex); d++)
                if (_map.IsWalkable(start.Neighbor((Direction)d)))
                    startIndex = _map.Index(start.Neighbor((Direction)d));
            if (!_map.IsWalkable(startIndex))
                return Search(start, target, goalNode: -1, avoid, ownLandOf);
        }
        _clusters.Update();
        if (_clusters.Route(startIndex, _map.Index(target), _minCost) is not { } route || route.Count <= LegNodes + 1)
            return Search(start, target, goalNode: -1, avoid, ownLandOf);
        var leg = Search(start, target, route[LegNodes], avoid, ownLandOf);
        // The leg's part may be cut off by what is to be avoided: then the whole way.
        if (leg is null)
            return Search(start, target, goalNode: -1, avoid, ownLandOf);
        partial = true;
        return leg;
    }

    /// <summary>Checks before a search; false with the answer when none is needed (null: unreachable, empty: there).</summary>
    private bool Searchable(GridPoint start, GridPoint target, IReadOnlySet<int>? avoid, int? ownLandOf, out IReadOnlyList<Direction>? answer)
    {
        answer = null;
        if (!_map.Contains(start) || !_map.IsWalkable(target) || avoid?.Contains(_map.Index(target)) == true)
            return false;
        if (ownLandOf is not null && !AreConnected(start, target, ownLandOf))
            return false; // fail fast instead of searching the whole land
        if (start == target)
        {
            answer = [];
            return false;
        }
        return MayBeConnected(start, target);
    }

    /// <summary>
    /// A* to the target, or with <paramref name="goalNode"/> to the first point of that coarse node
    /// (<see cref="Clusters"/>), heading for its middle.
    /// </summary>
    private Direction[]? Search(GridPoint start, GridPoint target, int goalNode, IReadOnlySet<int>? avoid, int? ownLandOf)
    {
        byte landOf = ownLandOf is { } player ? (byte)player : GameMap.NoOwner;
        if (goalNode >= 0)
            target = _map.Point(_clusters.CenterOf(goalNode));
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
        _steps[startIndex] = 0;
        _seenStamp[startIndex] = _stamp;
        _open.Enqueue(startIndex, Priority(0, Distance(start, target) * _minCost));

        while (_open.TryDequeue(out int current, out _))
        {
            if (_closedStamp[current] == _stamp)
                continue; // stale queue entry
            _closedStamp[current] = _stamp;
            LastExpandedNodes++;

            if (current == targetIndex || goalNode >= 0 && _clusters.NodeOf(current) == goalNode)
                return BuildPath(startIndex, current);

            int x = current % size, y = current / size;
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
                int nextCost = _cost[current] + StepCost(next);
                if (_seenStamp[next] == _stamp && _cost[next] <= nextCost)
                    continue;

                _seenStamp[next] = _stamp;
                _cost[next] = nextCost;
                _steps[next] = _steps[current] + 1;
                _arrivedFrom[next] = (byte)d;
                _open.Enqueue(next, Priority(nextCost, Distance(new GridPoint(nx, ny), target) * _minCost));
            }
        }
        return null;
    }

    /// <summary>Lower f first; on ties prefer nodes further along (higher cost), which keeps the search narrow.</summary>
    private static long Priority(int cost, int heuristic) => ((long)(cost + heuristic) << 32) - cost;

    private Direction[] BuildPath(int startIndex, int targetIndex)
    {
        var steps = new Direction[_steps[targetIndex]];
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
