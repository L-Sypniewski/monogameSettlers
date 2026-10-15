using Homestead.Assets.Maps;
using Homestead.Simulation.Economy;
using Homestead.Simulation.Pathfinding;

namespace Homestead.Simulation.Map.Generation;

/// <summary>
/// Makes a random multiplayer map in the original format (<see cref="OriginalMap"/>), so it loads like any
/// other map: terrain with the original's transition rings, heights, trees, stones, decorations, ore,
/// fish and piles of goods (treasures). The same settings and seed always give the same map.
/// </summary>
/// <remarks>
/// <para>
/// The land is an island in the middle of the (square) grid. Seen in the grid's own geometry — the six
/// neighbors at equal distance, <see cref="Uv"/> — it is a disc, so it can be mirrored around its center:
/// reflections at multiples of 30° map the grid onto itself, which makes the mirrored maps exactly fair
/// for 2, 3, 4 and 6 players (5 players: rounded).
/// </para>
/// <para>
/// Steps: ground classes (sea, grass, mountain, swamp, lakes, rivers), every player's surroundings
/// (a mountain with coal, iron and gold, a pond fed by a stream, a forest, stones, a flat start area),
/// heights, objects, ore and fish, treasures; then the symmetry (mirroring or copying the start tile)
/// and last the landscape bytes: water depth, beaches and the border rings of mountains, snow and swamps
/// are derived from the classes, one field per ring, so every triangle has an original transition tile.
/// Features keep enough distance from each other that the rings do not meet other ground.
/// </para>
/// </remarks>
public sealed class RandomMapGenerator
{
    private enum Ground : byte
    {
        Water,
        Grass,
        Mountain,
        Snow,
        Mud,
        River,
    }

    private static readonly (int X, int Y)[] Deltas = [(1, 0), (1, 1), (0, 1), (-1, 0), (-1, -1), (0, -1)];
    private static readonly double Sqrt3Half = Math.Sqrt(3) / 2;

    // Landscape bytes of the original maps (docs/formate/map.md).
    private const byte LGrass = 16, LMountainBorderOuter = 17, LMudBorderOuter = 23, LMountain = 32, LMountainBorder = 33,
        LSnowBorderOuter = 35, LSand = 48, LRiver = 96, LSnow = 128, LSnowBorder = 129, LMud = 144, LMudBorder = 145;

    /// <summary>Tree codes without the palms (78/79), first of each pair of variants.</summary>
    private static readonly byte[] TreeKinds = [68, 70, 72, 74, 76, 80];
    private static readonly byte[] GrassDecorations = [35, 36, 37, 38, 39, 40, 42, 43, 44, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59, 60, 61];
    private static readonly byte[] RockDecorations = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 21, 22, 23, 24, 25, 26, 27, 28];
    private static readonly byte[] Reeds = [62, 63, 64, 65, 66, 67];
    private static readonly byte[] BeachDecorations = [21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33];
    private const byte Grave = 34;

    /// <summary>Radius of the flat, empty start area.</summary>
    private const int StartClear = 10;

    private readonly RandomMapSettings _settings;
    private readonly RandomMapConfig _config;
    private readonly int _seed;
    private Random _random = null!;
    private Noise _noise = null!;

    private int _size;
    private int Count => _size * _size;
    private Ground[] _ground = [];
    private float[] _height = [];
    private byte[] _object = [];
    private byte[] _resource = [];
    private byte[] _pileType = [];
    private byte[] _pileCount = [];
    /// <summary>Forest planned around the start points (filled with trees together with the others).</summary>
    private bool[] _forest = [];
    /// <summary>The players' surroundings (pond, forest, stones with a margin): lakes, rivers and swamps placed later keep off.</summary>
    private bool[] _reserved = [];
    private int[] _starts = [];
    private int[] _startDistance = [];
    private double _centerU, _centerV, _landRadius;

    // Symmetry: mirrors at angle _axis + k·π/_mirrors; _canonical maps every field to its image in the first wedge.
    private int _mirrors;
    private double _axis;
    private int[] _canonical = [];

    private RandomMapGenerator(RandomMapSettings settings, RandomMapConfig config, int seed)
    {
        _settings = settings with { Players = Math.Clamp(settings.Players, RandomMapSettings.MinPlayers, RandomMapSettings.MaxPlayers) };
        _config = config;
        _seed = seed;
    }

    /// <summary>Name shown for a random map.</summary>
    public static string NameOf(RandomMapSettings settings, int seed) => $"Zufallskarte {settings.Players}-{(uint)seed:X8}";

    public static OriginalMap Generate(RandomMapSettings settings, int seed, RandomMapConfig? config = null) =>
        new RandomMapGenerator(settings, config ?? RandomMapConfig.Load(), seed).Build();

    private OriginalMap Build()
    {
        // A map whose starts cannot reach each other (forest or lake walls) is made again with the next draw.
        for (int attempt = 0; ; attempt++)
        {
            _random = new Random(HashCode.Combine(_seed, attempt, 0x5eed));
            _noise = new Noise(_random.Next());
            var area = Make();
            if (attempt >= 7 || StartsConnected(area))
                return OriginalMap.Create(NameOf(_settings, _seed), area,
                    _starts.Select(s => new MapPlayer(255, s % _size, s / _size, "")).ToArray(), Piles(), _config.StartResources);
        }
    }

    private MapArea Make()
    {
        _size = _config.SizeFor(_settings.Players);
        _ground = new Ground[Count];
        _height = new float[Count];
        _object = new byte[Count];
        _resource = new byte[Count];
        _pileType = new byte[Count];
        _pileCount = new byte[Count];
        _forest = new bool[Count];
        _reserved = new bool[Count];

        Layout();
        Coast();
        Mountains();
        var homes = _settings.Symmetry == MapSymmetry.None ? _starts : [_starts[0]];
        foreach (int start in homes)
            Home(start);
        Lakes();
        Rivers();
        Swamps();
        Heights();
        Forests();
        Stones();
        Ores();
        Treasures();
        Decorations();
        if (_settings.Symmetry == MapSymmetry.Mirror)
            Mirror();
        else if (_settings.Symmetry == MapSymmetry.Repeat)
            RepeatHome();
        ClearStarts();
        return ToArea();
    }

    // --- Geometry -------------------------------------------------------------------------------------------

    private int X(int i) => i % _size;
    private int Y(int i) => i / _size;
    private int Index(int x, int y) => y * _size + x;
    private bool Inside(int x, int y) => (uint)x < (uint)_size && (uint)y < (uint)_size;

    /// <summary>Position in the grid's own geometry: neighbors are at distance 1 in all six directions.</summary>
    private static (double U, double V) Uv(int x, int y) => (x - y / 2.0, y * Sqrt3Half);

    private int FromUv(double u, double v)
    {
        int y = (int)Math.Round(v / Sqrt3Half);
        int x = (int)Math.Round(u + y / 2.0);
        return Inside(x, y) ? Index(x, y) : -1;
    }

    private (double U, double V) Uv(int i) => Uv(X(i), Y(i));

    /// <summary>Distance from the center and angle, in grid geometry.</summary>
    private (double R, double Angle) Polar(int i)
    {
        var (u, v) = Uv(i);
        double du = u - _centerU, dv = v - _centerV;
        return (Math.Sqrt(du * du + dv * dv), Math.Atan2(dv, du));
    }

    private int Distance(int a, int b) => PathFinder.Distance(new GridPoint(X(a), Y(a)), new GridPoint(X(b), Y(b)));

    private IEnumerable<int> Neighbors(int i)
    {
        int x = X(i), y = Y(i);
        foreach (var (dx, dy) in Deltas)
            if (Inside(x + dx, y + dy))
                yield return Index(x + dx, y + dy);
    }

    /// <summary>Fields within <paramref name="radius"/> (grid steps) of <paramref name="center"/>.</summary>
    private IEnumerable<int> Disc(int center, int radius)
    {
        int cx = X(center), cy = Y(center);
        for (int dy = -radius; dy <= radius; dy++)
        for (int dx = -radius; dx <= radius; dx++)
        {
            // Hex distance for this neighbor set: |dx|, |dy| or |dx - dy|, whichever is largest.
            if (Math.Max(Math.Max(Math.Abs(dx), Math.Abs(dy)), Math.Abs(dx - dy)) > radius || !Inside(cx + dx, cy + dy))
                continue;
            yield return Index(cx + dx, cy + dy);
        }
    }

    /// <summary>Steps from every field to the nearest source (0 on the sources), at most <paramref name="max"/>.</summary>
    private int[] DistanceFrom(Func<int, bool> source, int max = int.MaxValue)
    {
        var distance = new int[Count];
        Array.Fill(distance, max);
        var queue = new Queue<int>();
        for (int i = 0; i < Count; i++)
            if (source(i))
            {
                distance[i] = 0;
                queue.Enqueue(i);
            }
        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            int next = distance[i] + 1;
            if (next >= max)
                continue;
            int x = X(i), y = Y(i);
            foreach (var (dx, dy) in Deltas)
            {
                if (!Inside(x + dx, y + dy))
                    continue;
                int n = Index(x + dx, y + dy);
                if (distance[n] > next)
                {
                    distance[n] = next;
                    queue.Enqueue(n);
                }
            }
        }
        return distance;
    }

    /// <summary>Steps to the nearest start point.</summary>
    private int StartDistance(int i) => _startDistance[i];

    // --- Layout and symmetry --------------------------------------------------------------------------------

    private void Layout()
    {
        int players = _settings.Players;
        var (cu, cv) = Uv((_size - 1) / 2, (_size - 1) / 2);
        (_centerU, _centerV) = (cu, cv);
        // The square grid is a rhombus with 60° angles in grid geometry: its inscribed circle has 0.433 × size.
        _landRadius = _size * Sqrt3Half / 2 - _config.SeaMargin - _size * 0.04;

        bool exact = players is 2 or 3 or 4 or 6;
        _mirrors = players == 4 ? 2 : players;
        // Mirrors along the grid's own axes (multiples of 30°) map fields onto fields.
        _axis = exact ? _random.Next(12) * Math.PI / 6 : _random.NextDouble() * 2 * Math.PI;
        double first = _axis + (players == 4 ? Math.PI / 4 : 0);
        bool jitter = _settings.Symmetry == MapSymmetry.None;
        _starts = new int[players];
        for (int p = 0; p < players; p++)
        {
            double angle = first + p * 2 * Math.PI / players + (jitter ? (_random.NextDouble() - 0.5) * 0.5 * Math.PI / players : 0);
            double radius = _landRadius * _config.StartDistance * (jitter ? 0.92 + _random.NextDouble() * 0.16 : 1);
            _starts[p] = FromUv(_centerU + radius * Math.Cos(angle), _centerV + radius * Math.Sin(angle));
        }

        _canonical = new int[Count];
        for (int i = 0; i < Count; i++)
            _canonical[i] = _settings.Symmetry == MapSymmetry.Mirror ? Fold(i) : i;
        // Mirrored: the other starts are exact images of the first (each rounded on its own may be off by a field).
        if (_settings.Symmetry == MapSymmetry.Mirror && _canonical[_starts[0]] >= 0)
            _starts[0] = _canonical[_starts[0]];
        if (_settings.Symmetry == MapSymmetry.Mirror)
            for (int p = 1; p < players; p++)
                if (Disc(_starts[p], 2).Where(i => _canonical[i] == _starts[0]).OrderBy(i => Distance(i, _starts[p])).FirstOrDefault(-1) is var image and >= 0)
                    _starts[p] = image;
        _startDistance = DistanceFrom(i => _starts.Contains(i));
    }

    /// <summary>The field's image in the first wedge between two mirrors (-1 if it falls off the grid).</summary>
    private int Fold(int i)
    {
        var (r, angle) = Polar(i);
        double wedge = Math.PI / _mirrors;
        double t = ((angle - _axis) % (2 * wedge) + 2 * wedge) % (2 * wedge);
        if (t > wedge)
            t = 2 * wedge - t;
        return FromUv(_centerU + r * Math.Cos(t + _axis), _centerV + r * Math.Sin(t + _axis));
    }

    /// <summary>Whether the field is generated (with mirroring only the first wedge is, the rest is copied).</summary>
    private bool InDomain(int i) => _canonical[i] == i;

    private void Mirror()
    {
        var ground = (Ground[])_ground.Clone();
        var height = (float[])_height.Clone();
        var objects = (byte[])_object.Clone();
        var resource = (byte[])_resource.Clone();
        var pileType = (byte[])_pileType.Clone();
        var pileCount = (byte[])_pileCount.Clone();
        for (int i = 0; i < Count; i++)
        {
            int from = _canonical[i];
            if (from < 0)
            {
                (_ground[i], _height[i], _object[i], _resource[i], _pileType[i], _pileCount[i]) = (Ground.Water, 0, 0, 0, 0, 0);
                continue;
            }
            (_ground[i], _height[i], _object[i], _resource[i], _pileType[i], _pileCount[i]) =
                (ground[from], height[from], objects[from], resource[from], pileType[from], pileCount[from]);
        }
    }

    /// <summary>Copies player 1's surroundings to every other start (shifted, so walking distances stay the same).</summary>
    private void RepeatHome()
    {
        int radius = _config.HomeRadius;
        const int blend = 6;
        int home = _starts[0];
        foreach (int start in _starts.Skip(1))
        {
            int dx = X(start) - X(home), dy = Y(start) - Y(home);
            foreach (int from in Disc(home, radius))
            {
                int x = X(from) + dx, y = Y(from) + dy;
                if (!Inside(x, y))
                    continue;
                int to = Index(x, y);
                int d = Distance(from, home);
                // The outer ring only blends the heights, so the tile has no cliff at its edge.
                if (d > radius - blend)
                {
                    float w = (d - (radius - blend)) / (float)blend;
                    _height[to] = _height[from] * (1 - w) + _height[to] * w;
                    if (d > radius - 2)
                        continue;
                }
                else
                    _height[to] = _height[from];
                (_ground[to], _object[to], _resource[to], _pileType[to], _pileCount[to]) =
                    (_ground[from], _object[from], _resource[from], _pileType[from], _pileCount[from]);
            }
        }
    }

    // --- Ground classes --------------------------------------------------------------------------------------

    private void Coast()
    {
        for (int i = 0; i < Count; i++)
        {
            var (r, _) = Polar(i);
            var (u, v) = Uv(i);
            double edge = _landRadius * (1 + 0.09 * _noise.Fbm(u / 45, v / 45, 3));
            _ground[i] = r < edge ? Ground.Grass : Ground.Water;
        }
    }

    private void Mountains()
    {
        var coast = DistanceFrom(i => _ground[i] == Ground.Water, 16);
        var value = new double[Count];
        var candidates = new List<int>();
        for (int i = 0; i < Count; i++)
        {
            // Mirrored maps: the share counts in the wedge that is mirrored.
            if (coast[i] < 10 || StartDistance(i) < 22 || !InDomain(i))
                continue;
            var (u, v) = Uv(i);
            value[i] = 0.7 * _noise.Fbm(u / 100 + 31, v / 100 + 17, 4) + 0.3 * _noise.Ridge(u / 55 + 5, v / 55 - 9, 3);
            candidates.Add(i);
        }
        int land = Enumerable.Range(0, Count).Count(i => _ground[i] != Ground.Water && InDomain(i));
        int wanted = (int)(land * _config.MountainShare);
        if (candidates.Count == 0 || wanted == 0)
            return;
        double threshold = candidates.Select(i => value[i]).OrderByDescending(x => x).ElementAt(Math.Min(wanted, candidates.Count - 1));
        for (int i = 0; i < Count; i++)
        {
            int c = _canonical[i];
            if (c >= 0 && value[c] > threshold)
                _ground[i] = Ground.Mountain;
        }
        RemoveSmall(Ground.Mountain, 60);
    }

    /// <summary>Turns patches of <paramref name="ground"/> smaller than <paramref name="minimum"/> fields back into grass.</summary>
    private void RemoveSmall(Ground ground, int minimum)
    {
        var seen = new bool[Count];
        for (int i = 0; i < Count; i++)
        {
            if (seen[i] || _ground[i] != ground)
                continue;
            var patch = new List<int> { i };
            seen[i] = true;
            for (int k = 0; k < patch.Count; k++)
                foreach (int n in Neighbors(patch[k]))
                    if (!seen[n] && _ground[n] == ground)
                    {
                        seen[n] = true;
                        patch.Add(n);
                    }
            if (patch.Count < minimum)
                foreach (int p in patch)
                    _ground[p] = Ground.Grass;
        }
    }

    /// <summary>A noisy disc of <paramref name="ground"/> (on fields <paramref name="allowed"/> accepts).</summary>
    private List<int> Blob(int center, double radius, Ground ground, Func<int, bool> allowed)
    {
        var placed = new List<int>();
        double offset = _random.NextDouble() * 100;
        foreach (int i in Disc(center, (int)Math.Ceiling(radius * 1.35)))
        {
            var (u, v) = Uv(i);
            double edge = radius * (1 + 0.3 * _noise.Fbm(u / 6 + offset, v / 6 - offset, 2));
            if (Distance(i, center) <= edge && allowed(i))
            {
                _ground[i] = ground;
                placed.Add(i);
            }
        }
        return placed;
    }

    private void Reserve(int center, int radius)
    {
        foreach (int i in Disc(center, radius))
            _reserved[i] = true;
    }

    /// <summary>A random field <paramref name="min"/>-<paramref name="max"/> steps from <paramref name="from"/> that <paramref name="fits"/>, or -1.</summary>
    private int Around(int from, int min, int max, Func<int, bool> fits, int tries = 60)
    {
        var (u, v) = Uv(from);
        for (int t = 0; t < tries; t++)
        {
            double angle = _random.NextDouble() * 2 * Math.PI;
            double r = min + _random.NextDouble() * (max - min);
            int i = FromUv(u + r * Math.Cos(angle), v + r * Math.Sin(angle));
            if (i >= 0 && InDomain(i) && fits(i))
                return i;
        }
        return -1;
    }

    private bool IsLand(int i) => _ground[i] != Ground.Water;

    /// <summary>
    /// Every player's surroundings, the same recipe for all: no mountain near the start, a mountain with coal,
    /// iron and gold 24-32 steps away, a pond 14-20 steps away fed by a stream from the mountain's side, a forest
    /// and stones nearby.
    /// </summary>
    private void Home(int start)
    {
        foreach (int i in Disc(start, 18))
            if (_ground[i] == Ground.Mountain)
                _ground[i] = Ground.Grass;
        foreach (int i in Disc(start, StartClear + 1))
            _ground[i] = Ground.Grass;

        var coast = DistanceFrom(i => _ground[i] == Ground.Water, 16);
        int mountain = Around(start, 24, 30, i => coast[i] >= 12 && Distance(i, start) >= 24);
        if (mountain >= 0)
        {
            var fields = Blob(mountain, 6.5, Ground.Mountain, i => IsLand(i) && Distance(i, start) >= 17);
            // The same three ores in every home, as small veins next to each other.
            var veins = new[] { ResourceType.Coal, ResourceType.IronOre, ResourceType.GoldOre };
            var level = _config.Resources[(int)_settings.Resources];
            for (int k = 0; k < veins.Length && fields.Count > 0; k++)
            {
                int center = fields[_random.Next(fields.Count)];
                Vein(center, 3, veins[k], Math.Max(level.Min, (level.Min + level.Max) / 2), overwrite: true);
            }
        }

        var mountainDistance = DistanceFrom(i => _ground[i] == Ground.Mountain, 12);
        int pond = Around(start, 14, 20, i => coast[i] >= 8 && mountainDistance[i] >= 9 && Distance(i, start) >= 14);
        if (pond >= 0)
        {
            var water = Blob(pond, 3, Ground.Water, i => mountainDistance[i] >= 7 && Distance(i, start) >= StartClear + 3);
            Reserve(pond, 7);
            // The stream starts between the mountain and the pond and runs into it.
            if (water.Count > 0)
            {
                var toPond = DistanceFrom(i => _ground[i] == Ground.Water && Distance(i, pond) <= 6, 40);
                bool Fits(int i) => _ground[i] == Ground.Grass && mountainDistance[i] >= 4 && Distance(i, start) > StartClear + 4;
                int source = Around(start, 18, 26, i => Fits(i) && mountainDistance[i] <= 7 && toPond[i] is >= 8 and < 40);
                if (source < 0)
                    source = Around(pond, 8, 14, i => Fits(i) && toPond[i] is >= 6 and < 40, tries: 200);
                if (source >= 0)
                    Flow(source, toPond, mountainDistance, start);
            }
        }

        int forest = Around(start, 13, 19, i => _ground[i] == Ground.Grass && mountainDistance[i] >= 4);
        if (forest >= 0)
            Reserve(forest, 8);
        if (forest >= 0)
            foreach (int i in Disc(forest, 6))
                if (Distance(i, forest) <= 4 + 2 * _noise.Fbm(X(i) / 4.0, Y(i) / 4.0, 1) + 2 && Distance(i, start) > StartClear + 1)
                    _forest[i] = true;

        var wet = DistanceFrom(i => _ground[i] is Ground.Water or Ground.River or Ground.Mud, 8);
        var rock = DistanceFrom(i => _ground[i] is Ground.Mountain or Ground.Snow, 4);
        int stones = Around(start, 12, 16, i => _ground[i] == Ground.Grass && !_forest[i] && wet[i] >= 6 && rock[i] >= 4
                                                && Distance(i, start) >= 12, tries: 200);
        if (stones >= 0)
        {
            Reserve(stones, 7);
            StoneCluster(stones, 7, start, wet, rock);
        }
    }

    private void Lakes()
    {
        int count = FeatureCount(_config.LakesPerPlayer[(int)_settings.Rivers]);
        var mountainDistance = DistanceFrom(i => _ground[i] == Ground.Mountain, 12);
        var coast = DistanceFrom(i => _ground[i] == Ground.Water, 16);
        for (int k = 0; k < count; k++)
        {
            int center = RandomField(i => coast[i] >= 14 && mountainDistance[i] >= 12 && StartDistance(i) >= 26);
            if (center >= 0)
                Blob(center, 4 + _random.NextDouble() * 5, Ground.Water,
                    i => IsLand(i) && !_reserved[i] && mountainDistance[i] >= 7 && StartDistance(i) >= StartClear + 6);
        }
    }

    /// <summary>Rounds a fractional count at random (1.5 → 1 or 2).</summary>
    private int Count2(double value) => (int)value + (_random.NextDouble() < value - Math.Floor(value) ? 1 : 0);

    /// <summary>
    /// How many of a feature to place for <paramref name="perPlayer"/> on the whole map: with mirroring only
    /// the first wedge is generated, the mirrors make the rest.
    /// </summary>
    private int FeatureCount(double perPlayer, int minimum = 0)
    {
        double total = perPlayer * _settings.Players;
        return Math.Max(minimum, _settings.Symmetry == MapSymmetry.Mirror ? Count2(total / (2.0 * _mirrors)) : Count2(total));
    }

    /// <summary>A random generated field that <paramref name="fits"/>, or -1.</summary>
    private int RandomField(Func<int, bool> fits, int tries = 400)
    {
        for (int t = 0; t < tries; t++)
        {
            int i = _random.Next(Count);
            if (InDomain(i) && fits(i))
                return i;
        }
        return -1;
    }

    private void Rivers()
    {
        // Mirrored rivers often meet their mirror image: twice as many in the wedge.
        int count = FeatureCount(_config.RiversPerPlayer[(int)_settings.Rivers] * (_settings.Symmetry == MapSymmetry.Mirror ? 2 : 1));
        var sea = Sea();
        for (int k = 0; k < count; k++)
        {
            var mountainDistance = DistanceFrom(i => _ground[i] == Ground.Mountain, 12);
            var toWater = DistanceFrom(i => sea[i] || _ground[i] == Ground.River);
            // Rivers spring at the foot of a mountain if there is one, and run to the sea (or join another river).
            int source = RandomField(i => _ground[i] == Ground.Grass && mountainDistance[i] is >= 3 and <= 6 && toWater[i] >= 15
                                          && StartDistance(i) > StartClear + 4);
            if (source < 0)
                source = RandomField(i => _ground[i] == Ground.Grass && toWater[i] >= 20 && StartDistance(i) > StartClear + 4);
            if (source >= 0)
                Flow(source, toWater, mountainDistance, -1);
        }
    }

    /// <summary>Water connected to the map's edge.</summary>
    private bool[] Sea()
    {
        var sea = new bool[Count];
        var queue = new Queue<int>();
        queue.Enqueue(0);
        sea[0] = true;
        while (queue.Count > 0)
            foreach (int n in Neighbors(queue.Dequeue()))
                if (!sea[n] && _ground[n] == Ground.Water)
                {
                    sea[n] = true;
                    queue.Enqueue(n);
                }
        return sea;
    }

    /// <summary>
    /// A river from <paramref name="source"/> downhill on <paramref name="target"/> (steps to the water it runs into),
    /// two fields wide, meandering; it keeps away from mountains and start areas and ends at water or another river.
    /// </summary>
    private void Flow(int source, int[] target, int[] mountainDistance, int home)
    {
        int at = source;
        int direction = _random.Next(6);
        double offset = _random.NextDouble() * 1000;
        var path = new List<int>();
        for (int step = 0; step < 4 * target[source] + 20; step++)
        {
            path.Add(at);
            if (Neighbors(at).Any(n => _ground[n] is Ground.Water or Ground.River && !path.Contains(n)))
                break;
            int best = -1;
            double bestScore = double.MaxValue;
            for (int d = 0; d < 6; d++)
            {
                int x = X(at) + Deltas[d].X, y = Y(at) + Deltas[d].Y;
                if (!Inside(x, y))
                    continue;
                int n = Index(x, y);
                if (path.Contains(n) || mountainDistance[n] < 3 || _ground[n] == Ground.Mountain)
                    continue;
                if ((home >= 0 ? Distance(n, home) : StartDistance(n)) <= StartClear + 2 || (home < 0 && _reserved[n]))
                    continue;
                // Downhill on a noisy slope, so the river swings in bends instead of following the grid.
                var (u, v) = Uv(n);
                double turn = Math.Min((d - direction + 6) % 6, (direction - d + 6) % 6);
                double score = target[n] - target[at] + 12 * _noise.Fbm(u / 18 + offset, v / 18, 2) + 0.3 * turn + _random.NextDouble() * 0.6;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = d;
                }
            }
            if (best < 0)
                break;
            direction = best;
            at = Index(X(at) + Deltas[best].X, Y(at) + Deltas[best].Y);
        }
        if (path.Count < 4)
            return;
        foreach (int p in path)
        {
            _ground[p] = Ground.River;
            // The second field on a fixed side: the river is two fields wide.
            int x = X(p) + 1, y = Y(p);
            if (Inside(x, y) && _ground[Index(x, y)] == Ground.Grass && mountainDistance[Index(x, y)] >= 3)
                _ground[Index(x, y)] = Ground.River;
        }
    }

    private void Swamps()
    {
        // At least one: rice grows only there.
        int count = FeatureCount(_config.SwampsPerPlayer, minimum: 1);
        var mountainDistance = DistanceFrom(i => _ground[i] == Ground.Mountain, 12);
        var water = DistanceFrom(i => _ground[i] is Ground.Water or Ground.River, 16);
        for (int k = 0; k < count; k++)
        {
            int center = RandomField(i => water[i] is >= 9 and <= 15 && mountainDistance[i] >= 10 && StartDistance(i) >= 22);
            if (center >= 0)
                Blob(center, 3 + _random.NextDouble() * 2.5, Ground.Mud,
                    i => _ground[i] == Ground.Grass && !_reserved[i] && water[i] >= 5 && mountainDistance[i] >= 6 && StartDistance(i) >= 16);
        }
    }

    // --- Heights ---------------------------------------------------------------------------------------------

    private void Heights()
    {
        var inside = DistanceFrom(i => _ground[i] != Ground.Mountain, 16);
        var mountain = DistanceFrom(i => _ground[i] == Ground.Mountain, 10);
        var water = DistanceFrom(i => _ground[i] == Ground.Water, 12);
        for (int i = 0; i < Count; i++)
        {
            if (_ground[i] == Ground.Water)
            {
                _height[i] = 0;
                continue;
            }
            var (u, v) = Uv(i);
            double h = 3 + 24 * Unit(_noise.Fbm(u / 80 - 13, v / 80 + 7, 4));
            if (_ground[i] == Ground.Mountain)
                h += 8 + 7.5 * Math.Min(inside[i], 13) * (0.75 + 0.5 * Unit(_noise.Fbm(u / 18, v / 18, 3)));
            else
                h += 8 * Math.Max(0, 1 - mountain[i] / 6.0);
            if (_ground[i] is Ground.River or Ground.Mud)
                h *= 0.75;
            // Down to the shore.
            double shore = Math.Clamp(water[i] / 10.0, 0, 1);
            h = 1 + (h - 1) * shore * shore * (3 - 2 * shore);
            _height[i] = (float)h;
        }
        // The start areas: flat.
        foreach (int start in _starts)
        {
            float level = (float)Disc(start, 6).Average(i => _height[i]);
            foreach (int i in Disc(start, StartClear + 4))
            {
                int d = Distance(i, start);
                float w = d <= StartClear - 2 ? 1 : 1 - (d - (StartClear - 2)) / 6f;
                _height[i] = _height[i] * (1 - w) + level * w;
            }
        }
        Smooth(2);
        // Snow on the highest peaks, far enough inside the mountain for its rings.
        for (int i = 0; i < Count; i++)
            if (_ground[i] == Ground.Mountain && inside[i] >= 5 && _height[i] >= _config.SnowLine)
                _ground[i] = Ground.Snow;
    }

    private static double Unit(double noise) => Math.Clamp(noise * 0.5 + 0.5, 0, 1);

    private void Smooth(int passes)
    {
        for (int pass = 0; pass < passes; pass++)
        {
            var copy = (float[])_height.Clone();
            for (int i = 0; i < Count; i++)
            {
                if (_ground[i] == Ground.Water)
                    continue;
                float sum = copy[i] * 2;
                int n = 2;
                foreach (int k in Neighbors(i))
                {
                    sum += copy[k];
                    n++;
                }
                _height[i] = sum / n;
            }
        }
    }

    // --- Objects ---------------------------------------------------------------------------------------------

    /// <summary>Whether trees, stones and piles may go on the field: plain grass, away from water, rivers and start areas.</summary>
    private bool Free(int i, int[] water, int[] mountain) =>
        _ground[i] == Ground.Grass && _object[i] == 0 && water[i] >= 3 && mountain[i] >= 2;

    private void Forests()
    {
        var water = DistanceFrom(i => _ground[i] is Ground.Water or Ground.River or Ground.Mud, 4);
        var mountain = DistanceFrom(i => _ground[i] is Ground.Mountain or Ground.Snow, 4);
        var value = new double[Count];
        var grass = new List<int>();
        for (int i = 0; i < Count; i++)
        {
            if (!Free(i, water, mountain) || StartDistance(i) <= StartClear + 1)
                continue;
            var (u, v) = Uv(i);
            value[i] = _noise.Fbm(u / 32 + 50, v / 32 - 20, 4);
            grass.Add(i);
        }
        int wanted = (int)(grass.Count * _config.ForestShare[(int)_settings.Forests]);
        double threshold = grass.Count == 0 ? 1 : grass.Select(i => value[i]).OrderByDescending(x => x).ElementAt(Math.Clamp(wanted, 0, grass.Count - 1));
        foreach (int i in grass)
        {
            bool forest = value[i] > threshold || _forest[i];
            double chance = forest ? _config.TreeDensity : 0.004;
            if (_random.NextDouble() >= chance)
                continue;
            // Each wood has its own mix of trees.
            var (u, v) = Uv(i);
            int kind = (int)(Unit(_noise.Fbm(u / 60 - 70, v / 60 + 30, 2)) * TreeKinds.Length * 1.5 + _random.Next(2)) % TreeKinds.Length;
            _object[i] = (byte)(TreeKinds[kind] + _random.Next(2));
        }
    }

    private void Stones()
    {
        int count = FeatureCount(_config.StoneClustersPerPlayer[(int)_settings.Stones]);
        var water = DistanceFrom(i => _ground[i] is Ground.Water or Ground.River or Ground.Mud, 4);
        var mountain = DistanceFrom(i => _ground[i] is Ground.Mountain or Ground.Snow, 4);
        for (int k = 0; k < count; k++)
        {
            int center = RandomField(i => Free(i, water, mountain) && StartDistance(i) >= 18);
            if (center >= 0)
                StoneCluster(center, 4 + _random.Next(8), -1, water, mountain);
        }
    }

    /// <summary>Quarry stones around <paramref name="center"/>, one field apart (they block their neighbors).</summary>
    private void StoneCluster(int center, int stones, int home, int[] water, int[] mountain)
    {
        int placed = 0;
        for (int t = 0; t < stones * 30 && placed < stones; t++)
        {
            int i = Around(center, 0, 2 + placed / 2, _ => true, tries: 1);
            if (i < 0 || !Free(i, water, mountain) || Neighbors(i).Any(n => _object[n] != 0 || _ground[n] != Ground.Grass))
                continue;
            if ((home >= 0 ? Distance(i, home) : StartDistance(i)) <= StartClear + 2)
                continue;
            // Mostly full stones, a few smaller ones.
            _object[i] = (byte)(MapObjectCodes.FullStone + (_random.NextDouble() < 0.25 ? _random.Next(1, 5) : 0));
            placed++;
        }
    }

    private void Ores()
    {
        var level = _config.Resources[(int)_settings.Resources];
        var fields = Enumerable.Range(0, Count).Where(i => _ground[i] is Ground.Mountain or Ground.Snow && InDomain(i)).ToList();
        int covered = fields.Count(i => _resource[i] != 0);
        int total = _config.VeinWeights.Sum(v => v.Weight);
        for (int t = 0; t < 2000 && covered < fields.Count * level.Cover && fields.Count > 0; t++)
        {
            int roll = _random.Next(total);
            var type = _config.VeinWeights.First(v => (roll -= v.Weight) < 0).Type;
            covered += Vein(fields[_random.Next(fields.Count)], 3 + _random.Next(3), type, level.Min + _random.Next(level.Max - level.Min + 1), overwrite: false);
        }
        // Fish in shallow water.
        var land = DistanceFrom(i => _ground[i] != Ground.Water, 6);
        for (int i = 0; i < Count; i++)
            if (_ground[i] == Ground.Water && land[i] <= 5)
                _resource[i] = (byte)Math.Clamp((int)Math.Round(level.Fish * (1 - (land[i] - 1) / 6.0)), 1, 15);
    }

    /// <summary>A vein of ore under mountain fields, thinning out to its edge. Returns the fields it took.</summary>
    private int Vein(int center, int radius, ResourceType type, int amount, bool overwrite)
    {
        int taken = 0;
        foreach (int i in Disc(center, radius))
        {
            if (_ground[i] is not (Ground.Mountain or Ground.Snow) || (!overwrite && _resource[i] != 0))
                continue;
            int value = (int)Math.Round(amount * (1 - 0.4 * Distance(i, center) / radius));
            if (value <= 0)
                continue;
            if (_resource[i] == 0)
                taken++;
            _resource[i] = (byte)(((int)type << 4) | Math.Clamp(value, 1, 15));
        }
        return taken;
    }

    private void Treasures()
    {
        int count = FeatureCount(_config.TreasureSitesPerPlayer[(int)_settings.Treasures]);
        if (count == 0 || _config.TreasureGoods.Count == 0)
            return;
        var water = DistanceFrom(i => _ground[i] is Ground.Water or Ground.River or Ground.Mud, 4);
        var mountain = DistanceFrom(i => _ground[i] is Ground.Mountain or Ground.Snow, 4);
        int total = _config.TreasureGoods.Sum(g => g.Weight);
        for (int k = 0; k < count; k++)
        {
            // Out in the land between the players: whoever gets there first.
            int center = RandomField(i => Free(i, water, mountain) && StartDistance(i) >= 32);
            if (center < 0)
                continue;
            int piles = 1 + _random.Next(3);
            foreach (int i in Disc(center, 2).OrderBy(_ => _random.Next()))
            {
                if (piles == 0)
                    break;
                if (!Free(i, water, mountain) || _pileCount[i] != 0)
                    continue;
                int roll = _random.Next(total);
                var material = _config.TreasureGoods.First(g => (roll -= g.Weight) < 0).Material;
                _pileType[i] = OriginalStackType(material);
                _pileCount[i] = (byte)(3 + _random.Next(Materials.MaxPileSize - 2));
                piles--;
            }
        }
    }

    private static byte OriginalStackType(Material material)
    {
        for (int code = 1; code < 64; code++)
            if (Materials.FromOriginalStack(code) == material)
                return (byte)code;
        throw new ArgumentException($"{material} has no pile type in original maps.");
    }

    private void Decorations()
    {
        double share = _config.DecorationShare[(int)_settings.Decorations];
        var water = DistanceFrom(i => _ground[i] is Ground.Water or Ground.River or Ground.Mud, 3);
        for (int i = 0; i < Count; i++)
        {
            if (_object[i] != 0 || _pileCount[i] != 0 || !InDomain(i))
                continue;
            byte[]? kinds = _ground[i] switch
            {
                Ground.Grass when water[i] == 1 => Reeds,
                Ground.Grass when water[i] == 2 => BeachDecorations,
                Ground.Grass => GrassDecorations,
                Ground.Mud => Reeds,
                Ground.Mountain => RockDecorations,
                _ => null,
            };
            double chance = share * (kinds == Reeds ? 4 : kinds == RockDecorations ? 1.5 : kinds == BeachDecorations ? 0.3 : 1);
            if (kinds is null || _random.NextDouble() >= chance)
                continue;
            _object[i] = kinds == GrassDecorations && _random.NextDouble() < 0.02 ? Grave : kinds[_random.Next(kinds.Length)];
        }
    }

    /// <summary>Start areas stay empty (the start tower, piles and settlers go there).</summary>
    private void ClearStarts()
    {
        foreach (int start in _starts)
            foreach (int i in Disc(start, StartClear))
            {
                if (_ground[i] is not (Ground.Grass or Ground.River))
                    _ground[i] = Ground.Grass;
                _object[i] = 0;
                (_pileType[i], _pileCount[i]) = (0, 0);
            }
    }

    // --- Output ----------------------------------------------------------------------------------------------

    private MapArea ToArea()
    {
        var area = new MapArea(_size);
        var land = DistanceFrom(i => _ground[i] != Ground.Water, 9);
        var water = DistanceFrom(i => _ground[i] == Ground.Water, 3);
        var mountain = DistanceFrom(i => _ground[i] is Ground.Mountain or Ground.Snow, 2);
        var snow = DistanceFrom(i => _ground[i] == Ground.Snow, 2);
        var mud = DistanceFrom(i => _ground[i] == Ground.Mud, 2);
        bool Edge(int i, Ground ground) => Neighbors(i).Any(n => _ground[n] != ground && !(ground == Ground.Mountain && _ground[n] == Ground.Snow));
        for (int i = 0; i < Count; i++)
        {
            byte landscape = _ground[i] switch
            {
                // Depth rings: 0 at the shore down to 7.
                Ground.Water => (byte)Math.Min(land[i] - 1, 7),
                Ground.River => LRiver,
                Ground.Snow => Edge(i, Ground.Snow) ? LSnowBorder : LSnow,
                Ground.Mountain when Edge(i, Ground.Mountain) => LMountainBorder,
                Ground.Mountain => snow[i] == 1 ? LSnowBorderOuter : LMountain,
                Ground.Mud => Edge(i, Ground.Mud) ? LMudBorder : LMud,
                _ when water[i] <= 2 => LSand,
                _ when mountain[i] == 1 => LMountainBorderOuter,
                _ when mud[i] == 1 => LMudBorderOuter,
                _ => LGrass,
            };
            area.Landscape[i] = landscape;
            area.Height[i] = (byte)Math.Clamp((int)Math.Round(_height[i]), 0, 225);
            area.Owner[i] = 255;
            // Trees and stones only on grass (rings and beaches came after them).
            byte code = _object[i];
            bool grassy = landscape is LGrass or LMountainBorderOuter;
            if ((MapObjectKinds.FromOriginal(code) is MapObjectKind.Tree or MapObjectKind.ResourceStone && !grassy)
                || _ground[i] is Ground.Water or Ground.River or Ground.Snow)
                code = 0;
            area.Object[i] = code;
            area.Resources[i] = _ground[i] is Ground.Mountain or Ground.Snow or Ground.Water ? _resource[i] : (byte)0;
        }
        for (int i = 0; i < Count; i++)
            area.Accessibility[i] = (byte)(Walkable(area, i) ? 0 : 1);
        return area;
    }

    private bool Walkable(MapArea area, int i)
    {
        if (!TerrainTypes.FromOriginal(area.Landscape[i]).IsWalkable())
            return false;
        var kind = MapObjectKinds.FromOriginal(area.Object[i]);
        if (kind.BlocksWalking())
            return false;
        return !Neighbors(i).Any(n => MapObjectKinds.FromOriginal(area.Object[n]).BlocksNeighbors());
    }

    private bool StartsConnected(MapArea area)
    {
        var reached = new bool[Count];
        var queue = new Queue<int>();
        queue.Enqueue(_starts[0]);
        reached[_starts[0]] = true;
        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            foreach (int n in Neighbors(i))
                if (!reached[n] && area.Accessibility[n] == 0)
                {
                    reached[n] = true;
                    queue.Enqueue(n);
                }
        }
        return _starts.All(s => reached[s]);
    }

    private IReadOnlyList<MapStack> Piles()
    {
        var piles = new List<MapStack>();
        for (int i = 0; i < Count; i++)
            if (_pileCount[i] > 0 && _ground[i] == Ground.Grass && _object[i] == 0)
                piles.Add(new MapStack(X(i), Y(i), _pileType[i], _pileCount[i]));
        return piles;
    }

    /// <summary>Gradient noise (Perlin) with its own permutation per map.</summary>
    private sealed class Noise
    {
        private readonly int[] _permutation = new int[512];

        public Noise(int seed)
        {
            var random = new Random(seed);
            var values = Enumerable.Range(0, 256).OrderBy(_ => random.Next()).ToArray();
            for (int i = 0; i < 512; i++)
                _permutation[i] = values[i & 255];
        }

        /// <summary>Roughly -1..1.</summary>
        public double Value(double x, double y)
        {
            int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y);
            double xf = x - xi, yf = y - yi;
            xi &= 255;
            yi &= 255;
            double u = Fade(xf), v = Fade(yf);
            int aa = _permutation[_permutation[xi] + yi], ab = _permutation[_permutation[xi] + yi + 1];
            int ba = _permutation[_permutation[xi + 1] + yi], bb = _permutation[_permutation[xi + 1] + yi + 1];
            double x1 = Lerp(Gradient(aa, xf, yf), Gradient(ba, xf - 1, yf), u);
            double x2 = Lerp(Gradient(ab, xf, yf - 1), Gradient(bb, xf - 1, yf - 1), u);
            return Lerp(x1, x2, v) * 1.4;
        }

        /// <summary>Octaves of <see cref="Value"/>, roughly -1..1.</summary>
        public double Fbm(double x, double y, int octaves)
        {
            double sum = 0, amplitude = 1, norm = 0;
            for (int o = 0; o < octaves; o++)
            {
                sum += amplitude * Value(x, y);
                norm += amplitude;
                amplitude *= 0.5;
                x = x * 2 + 17.3;
                y = y * 2 - 9.1;
            }
            return sum / norm;
        }

        /// <summary>Ridges (1 on the crest line), roughly -1..1.</summary>
        public double Ridge(double x, double y, int octaves) => 1 - 2 * Math.Abs(Fbm(x, y, octaves));

        private static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);

        private static double Lerp(double a, double b, double t) => a + t * (b - a);

        private static double Gradient(int hash, double x, double y) => (hash & 7) switch
        {
            0 => x + y,
            1 => -x + y,
            2 => x - y,
            3 => -x - y,
            4 => x,
            5 => -x,
            6 => y,
            _ => -y,
        };
    }
}
