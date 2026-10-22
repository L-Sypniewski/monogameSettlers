using Homestead.Simulation.Map;

namespace Homestead.Simulation.Pathfinding;

/// <summary>
/// The map in square cells of <see cref="Size"/> points, each split into its parts: walkable points connected
/// within the cell. Parts of neighbouring cells touching each other form a coarse graph, small enough to plan a
/// route across the whole map at once (<see cref="Route"/>); the exact path is then searched leg by leg.
/// Kept up to date from the map's walkability changes: a changed point labels its cell again when next used.
/// </summary>
internal sealed class Clusters
{
    public const int Size = 16;

    /// <summary>A coarse node: cell × <see cref="MaxParts"/> + part.</summary>
    private const int MaxParts = 256;

    private const byte NoPart = 255;

    private readonly GameMap _map;
    private readonly int _cellsPerRow;
    private readonly byte[] _part;
    private readonly bool[] _dirty;
    /// <summary>Per cell, per part a point near its middle (map index), for distances between parts.</summary>
    private readonly int[][] _centers;
    /// <summary>Per cell, per part the touching parts of neighbouring cells; null until asked (and after a cell nearby changed).</summary>
    private readonly int[]?[][] _neighbors;
    private int _walkabilityVersion = -1;

    // Reused buffers; the search's per node, valid where the stamp is the search's.
    private readonly Queue<int> _queue = new();
    private readonly List<int> _points = new();
    private readonly HashSet<int> _found = new();
    private readonly int[] _cost, _cameFrom, _seenStamp, _closedStamp;
    private int _stamp;
    private readonly PriorityQueue<int, int> _open = new();

    public Clusters(GameMap map)
    {
        _map = map;
        _cellsPerRow = (map.Size + Size - 1) / Size;
        _part = new byte[map.PointCount];
        _dirty = new bool[_cellsPerRow * _cellsPerRow];
        _centers = new int[_dirty.Length][];
        _neighbors = new int[]?[_dirty.Length][];
        int nodes = _dirty.Length * MaxParts;
        _cost = new int[nodes];
        _cameFrom = new int[nodes];
        _seenStamp = new int[nodes];
        _closedStamp = new int[nodes];
    }

    public int CellOf(int index) => index % _map.Size / Size + index / _map.Size / Size * _cellsPerRow;

    /// <summary>The coarse node of a walkable point.</summary>
    public int NodeOf(int index)
    {
        int cell = CellOf(index);
        if (_dirty[cell])
            Label(cell);
        return cell * MaxParts + _part[index];
    }

    /// <summary>A point near the middle of the node's part (map index).</summary>
    public int CenterOf(int node) => _centers[node / MaxParts][node % MaxParts];

    public void Update()
    {
        if (_walkabilityVersion == _map.WalkabilityVersion)
            return;
        if (_walkabilityVersion >= 0 && _map.WalkabilityChangesSince(_walkabilityVersion, out var changes))
            foreach (int index in changes)
                MarkDirty(CellOf(index));
        else
        {
            Array.Fill(_dirty, true);
            Array.Clear(_neighbors);
        }
        _walkabilityVersion = _map.WalkabilityVersion;
    }

    /// <summary>
    /// The cell is labelled again when next used. The neighbours known around it go at once: they may name
    /// parts the cell will no longer have, reached before the cell itself is asked again.
    /// </summary>
    private void MarkDirty(int cell)
    {
        if (_dirty[cell])
            return;
        _dirty[cell] = true;
        ForgetNeighborsAround(cell);
    }

    private void ForgetNeighborsAround(int cell)
    {
        int cx = cell % _cellsPerRow, cy = cell / _cellsPerRow;
        for (int y = Math.Max(0, cy - 1); y <= Math.Min(_cellsPerRow - 1, cy + 1); y++)
        for (int x = Math.Max(0, cx - 1); x <= Math.Min(_cellsPerRow - 1, cx + 1); x++)
            _neighbors[y * _cellsPerRow + x] = null!;
    }

    /// <summary>
    /// The coarse nodes from the start's to the target's (both walkable points), each touching the next;
    /// null if there is no way. Call <see cref="Update"/> first.
    /// </summary>
    public List<int>? Route(int start, int target, int stepCost)
    {
        int from = NodeOf(start), to = NodeOf(target);
        var goal = _map.Point(target);
        if (++_stamp == int.MaxValue)
        {
            Array.Clear(_seenStamp);
            Array.Clear(_closedStamp);
            _stamp = 1;
        }
        _open.Clear();
        _cost[from] = 0;
        _seenStamp[from] = _stamp;
        _open.Enqueue(from, PathFinder.Distance(_map.Point(start), goal) * stepCost);
        while (_open.TryDequeue(out int node, out _))
        {
            if (_closedStamp[node] == _stamp)
                continue;
            _closedStamp[node] = _stamp;
            if (node == to)
            {
                var route = new List<int> { to };
                while (route[^1] != from)
                    route.Add(_cameFrom[route[^1]]);
                route.Reverse();
                return route;
            }
            var center = _map.Point(CenterOf(node));
            foreach (int next in Neighbors(node))
            {
                if (_closedStamp[next] == _stamp)
                    continue;
                var nextCenter = _map.Point(CenterOf(next));
                int cost = _cost[node] + Math.Max(1, PathFinder.Distance(center, nextCenter)) * stepCost;
                if (_seenStamp[next] == _stamp && _cost[next] <= cost)
                    continue;
                _seenStamp[next] = _stamp;
                _cost[next] = cost;
                _cameFrom[next] = node;
                _open.Enqueue(next, cost + PathFinder.Distance(nextCenter, goal) * stepCost);
            }
        }
        return null;
    }

    /// <summary>The parts of neighbouring cells that the node's part touches.</summary>
    private int[] Neighbors(int node)
    {
        int cell = node / MaxParts, part = node % MaxParts;
        if (_dirty[cell])
            Label(cell);
        var known = _neighbors[cell] ??= new int[]?[_centers[cell].Length];
        return known[part] ??= FindNeighbors(cell, part);
    }

    private int[] FindNeighbors(int cell, int part)
    {
        var found = _found;
        found.Clear();
        int size = _map.Size;
        int left = cell % _cellsPerRow * Size, top = cell / _cellsPerRow * Size;
        int right = Math.Min(left + Size, size) - 1, bottom = Math.Min(top + Size, size) - 1;
        for (int y = top; y <= bottom; y++)
        for (int x = left; x <= right; x++)
        {
            // Only the cell's edge touches other cells.
            if (x != left && x != right && y != top && y != bottom)
                continue;
            int index = y * size + x;
            if (_part[index] != part || !_map.IsWalkable(index))
                continue;
            for (int d = 0; d < DirectionExtensions.Count; d++)
            {
                var delta = ((Direction)d).Delta();
                int nx = x + delta.X, ny = y + delta.Y;
                if ((uint)nx >= (uint)size || (uint)ny >= (uint)size || nx >= left && nx <= right && ny >= top && ny <= bottom)
                    continue;
                int next = ny * size + nx;
                if (_map.IsWalkable(next))
                    found.Add(NodeOf(next));
            }
        }
        return found.ToArray();
    }

    /// <summary>Splits the cell into its parts (walkable points connected within it).</summary>
    private void Label(int cell)
    {
        _dirty[cell] = false;
        // The parts are new: their neighbours and those of the cells around are found again.
        ForgetNeighborsAround(cell);
        int size = _map.Size;
        int left = cell % _cellsPerRow * Size, top = cell / _cellsPerRow * Size;
        int right = Math.Min(left + Size, size) - 1, bottom = Math.Min(top + Size, size) - 1;
        for (int y = top; y <= bottom; y++)
        for (int x = left; x <= right; x++)
            _part[y * size + x] = NoPart;
        var centers = new List<int>();
        for (int y = top; y <= bottom; y++)
        for (int x = left; x <= right; x++)
        {
            int seed = y * size + x;
            if (_part[seed] != NoPart || !_map.IsWalkable(seed))
                continue;
            byte part = (byte)centers.Count;
            _part[seed] = part;
            _queue.Enqueue(seed);
            _points.Clear();
            long sumX = 0, sumY = 0;
            while (_queue.TryDequeue(out int index))
            {
                _points.Add(index);
                int px = index % size, py = index / size;
                sumX += px;
                sumY += py;
                for (int d = 0; d < DirectionExtensions.Count; d++)
                {
                    var delta = ((Direction)d).Delta();
                    int nx = px + delta.X, ny = py + delta.Y;
                    if (nx < left || nx > right || ny < top || ny > bottom)
                        continue;
                    int next = ny * size + nx;
                    if (_part[next] != NoPart || !_map.IsWalkable(next))
                        continue;
                    _part[next] = part;
                    _queue.Enqueue(next);
                }
            }
            var middle = new GridPoint((int)(sumX / _points.Count), (int)(sumY / _points.Count));
            centers.Add(_points.MinBy(p => PathFinder.Distance(_map.Point(p), middle)));
        }
        _centers[cell] = centers.ToArray();
    }
}
