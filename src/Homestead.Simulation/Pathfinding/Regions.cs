using Homestead.Simulation.Map;

namespace Homestead.Simulation.Pathfinding;

/// <summary>
/// Connected regions of walkable points (with sameOwner: of one owner each), for telling quickly
/// whether a point can be reached at all. Kept up to date from the map's walkability changes instead
/// of labelling the whole map again after every felled tree.
/// </summary>
/// <remarks>
/// Labels are merged with union-find, so a point's region is the root of its label. A point that
/// becomes walkable joins (and so merges) the regions around it. A point that becomes blocked may
/// split its region: that is ruled out by a short search showing that the walkable points around it
/// still reach each other; only where it cannot be shown (a narrow pass) is the whole map labelled
/// again. Owner changes (land claimed in bulk) always label the whole map.
/// </remarks>
internal sealed class Regions
{
    public const int None = -1;

    /// <summary>Points the search around a blocked point may visit before giving up.</summary>
    private const int SplitSearchLimit = 2000;

    private readonly GameMap _map;
    private readonly bool _sameOwner;
    private readonly int[] _label;
    private readonly List<int> _parent = new();
    private int _walkabilityVersion = -1;
    private int _ownerVersion = -1;

    // Reused buffers for updates.
    private readonly HashSet<int> _changed = new();
    private readonly Dictionary<int, List<int>> _borders = new();
    private readonly HashSet<int> _seen = new();
    private readonly HashSet<int> _targets = new();
    private readonly Queue<int> _queue = new();

    public Regions(GameMap map, bool sameOwner)
    {
        _map = map;
        _sameOwner = sameOwner;
        _label = new int[map.PointCount];
    }

    /// <summary>Times the whole map was labelled (for tests and profiling).</summary>
    public int FullLabels { get; private set; }

    /// <summary>The region of a point, <see cref="None"/> if it is not walkable. Call <see cref="Update"/> first.</summary>
    public int Of(int index) => _label[index] == None ? None : Root(_label[index]);

    public void Update()
    {
        if (_sameOwner && _ownerVersion != _map.OwnerVersion || _walkabilityVersion < 0)
        {
            LabelAll();
            return;
        }
        if (_walkabilityVersion == _map.WalkabilityVersion)
            return;
        if (!_map.WalkabilityChangesSince(_walkabilityVersion, out var changes) || !Apply(changes))
        {
            LabelAll();
            return;
        }
        _walkabilityVersion = _map.WalkabilityVersion;
    }

    private bool Apply(ReadOnlySpan<int> changes)
    {
        _changed.Clear();
        foreach (int index in changes)
            _changed.Add(index);

        // Blocked points: the walkable points next to them, by the region they were in.
        _borders.Clear();
        Span<int> neighbors = stackalloc int[DirectionExtensions.Count];
        foreach (int index in _changed)
        {
            if (_map.IsWalkable(index) || _label[index] == None)
                continue;
            foreach (int next in neighbors[..Neighbors(index, neighbors)])
            {
                if (!Joins(next, index))
                    continue;
                int region = Root(_label[next]);
                if (!_borders.TryGetValue(region, out var border))
                    _borders[region] = border = new List<int>();
                border.Add(next);
            }
        }
        // Any way through the blocked points led from one of these to another of the same region:
        // if they still reach each other, the region is whole.
        foreach (var border in _borders.Values)
            if (border.Count > 1 && !StillConnected(border))
                return false;
        foreach (int index in _changed)
            if (!_map.IsWalkable(index))
                _label[index] = None;

        // Points that became walkable join the regions around them.
        foreach (int index in _changed)
        {
            if (!_map.IsWalkable(index) || _label[index] != None)
                continue;
            int region = None;
            foreach (int next in neighbors[..Neighbors(index, neighbors)])
            {
                if (!Joins(next, index))
                    continue;
                int other = Root(_label[next]);
                if (region == None)
                    region = other;
                else if (other != region)
                    _parent[other] = region;
            }
            if (region == None)
            {
                region = _parent.Count;
                _parent.Add(region);
            }
            _label[index] = region;
        }
        return true;
    }

    /// <summary>A labelled walkable neighbor of <paramref name="index"/> that belongs to the same region kind.</summary>
    private bool Joins(int neighbor, int index) =>
        _map.IsWalkable(neighbor) && _label[neighbor] != None && (!_sameOwner || _map.OwnerAt(neighbor) == _map.OwnerAt(index));

    /// <summary>Whether the points reach each other over walkable points (bounded search from the first).</summary>
    private bool StillConnected(List<int> points)
    {
        _targets.Clear();
        foreach (int p in points)
            _targets.Add(p);
        int start = points[0];
        byte owner = _map.OwnerAt(start);
        int missing = _targets.Count - 1;
        _seen.Clear();
        _queue.Clear();
        _seen.Add(start);
        _queue.Enqueue(start);
        Span<int> neighbors = stackalloc int[DirectionExtensions.Count];
        while (missing > 0 && _queue.TryDequeue(out int current))
        {
            if (_seen.Count > SplitSearchLimit)
                return false;
            foreach (int next in neighbors[..Neighbors(current, neighbors)])
            {
                if (!_map.IsWalkable(next) || _sameOwner && _map.OwnerAt(next) != owner || !_seen.Add(next))
                    continue;
                if (_targets.Contains(next))
                    missing--;
                _queue.Enqueue(next);
            }
        }
        return missing == 0;
    }

    /// <summary>The neighbors of a point on the map, written to <paramref name="result"/>; returns their number.</summary>
    private int Neighbors(int index, Span<int> result)
    {
        int size = _map.Size;
        int x = index % size, y = index / size, count = 0;
        for (int d = 0; d < DirectionExtensions.Count; d++)
        {
            var delta = ((Direction)d).Delta();
            int nx = x + delta.X, ny = y + delta.Y;
            if ((uint)nx < (uint)size && (uint)ny < (uint)size)
                result[count++] = ny * size + nx;
        }
        return count;
    }

    private int Root(int label)
    {
        int root = label;
        while (_parent[root] != root)
            root = _parent[root];
        // Path compression: later lookups go straight to the root.
        while (_parent[label] != root)
            (label, _parent[label]) = (_parent[label], root);
        return root;
    }

    /// <summary>Labels the whole map from scratch.</summary>
    private void LabelAll()
    {
        FullLabels++;
        _walkabilityVersion = _map.WalkabilityVersion;
        _ownerVersion = _map.OwnerVersion;
        Array.Fill(_label, None);
        _parent.Clear();
        int size = _map.Size;
        for (int seed = 0; seed < _label.Length; seed++)
        {
            if (_label[seed] != None || !_map.IsWalkable(seed))
                continue;

            int region = _parent.Count;
            _parent.Add(region);
            _label[seed] = region;
            _queue.Clear();
            _queue.Enqueue(seed);
            while (_queue.TryDequeue(out int current))
            {
                int x = current % size, y = current / size;
                for (int d = 0; d < DirectionExtensions.Count; d++)
                {
                    var delta = ((Direction)d).Delta();
                    int nx = x + delta.X, ny = y + delta.Y;
                    if ((uint)nx >= (uint)size || (uint)ny >= (uint)size)
                        continue;
                    int next = ny * size + nx;
                    if (_label[next] == None && _map.IsWalkable(next)
                        && (!_sameOwner || _map.OwnerAt(next) == _map.OwnerAt(current)))
                    {
                        _label[next] = region;
                        _queue.Enqueue(next);
                    }
                }
            }
        }
    }
}
