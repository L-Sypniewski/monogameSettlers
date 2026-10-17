using System.Text.Json;
using Homestead.Simulation.Buildings;
using Homestead.Simulation.Economy;
using Homestead.Simulation.Pathfinding;

namespace Homestead.Simulation.Ai.Traits;

/// <summary>
/// Thief (<c>thief</c> in docs/KI-TACTICS.md): keeps <c>scouts</c> scouts (free carriers become ones) and
/// sends them for goods lying at the edge of enemy land – piles on another player's land at most
/// <c>edgeDepth</c> fields from where it ends, within <c>maxDistance</c> of the own land. The
/// goods are ranked by <c>ranks</c> (gold, gems, weapons and ores first, then food, then the rest); the
/// best pile goes to the nearest idle scout, who takes the best good around it. No enemy soldier outside
/// the buildings may stand within <c>soldierRadius</c> of the pile; one coming that near the scout or his
/// pile calls him home (on a computer player's land a soldier will come to look at him), and that pile is left alone for <c>retrySeconds</c>. Loaded scouts walk to the
/// nearest field of the own land, where they drop the goods. Lost scouts are replaced at most every
/// <c>replaceSeconds</c>. Rechecked every <c>checkSeconds</c>.
/// </summary>
internal sealed class ThiefTrait : AiTrait
{
    public const string TraitId = "thief";

    private static readonly string[][] DefaultRanks =
    [
        ["GOLD", "GEMS", "SWORD", "BOW", "SPEAR", "GOLDORE", "IRONORE"],
        ["BREAD", "FISH", "MEAT"],
    ];

    private readonly int _scouts;
    private readonly int _edgeDepth;
    private readonly int _maxDistance;
    private readonly int _soldierRadius;
    private readonly double _checkSeconds;
    private readonly double _retrySeconds;
    private readonly double _replaceSeconds;
    private readonly Dictionary<Material, int> _ranks = new();
    private readonly int _unranked;

    /// <summary>The pile each scout was sent for, by scout id.</summary>
    private readonly Dictionary<int, Pile> _targets = new();

    /// <summary>Piles a scout was called home from, with the tick they may be tried again.</summary>
    private readonly Dictionary<Pile, long> _avoided = new();

    private long _nextCheck;
    private long _nextScout;

    public ThiefTrait(JsonElement settings)
    {
        _scouts = (int)Setting(settings, "scouts", 2);
        _edgeDepth = (int)Setting(settings, "edgeDepth", 12);
        _maxDistance = (int)Setting(settings, "maxDistance", 30);
        _soldierRadius = (int)Setting(settings, "soldierRadius", 12);
        _checkSeconds = Setting(settings, "checkSeconds", 3);
        _retrySeconds = Setting(settings, "retrySeconds", 60);
        _replaceSeconds = Setting(settings, "replaceSeconds", 90);
        var ranks = settings.ValueKind == JsonValueKind.Object && settings.TryGetProperty("ranks", out var value)
            ? value.EnumerateArray().Select(tier => tier.EnumerateArray().Select(m => m.GetString()!).ToArray()).ToArray()
            : DefaultRanks;
        for (int rank = 0; rank < ranks.Length; rank++)
            foreach (string name in ranks[rank])
                if (Materials.Parse(name) is { } material)
                    _ranks.TryAdd(material, rank);
        _unranked = ranks.Length;
    }

    public override string Id => TraitId;

    private int RankOf(Material material) => _ranks.GetValueOrDefault(material, _unranked);

    internal override void RunErrands(AiPlayer ai, List<Building> military)
    {
        var world = ai.World;
        if (world.Tick < _nextCheck || military.Count == 0)
            return;
        _nextCheck = world.Tick + (long)(_checkSeconds * World.TicksPerSecond);
        var map = world.Map!;
        var scouts = world.Settlers.Where(s => s.Player == ai.Player && s.Profession == Profession.Scout && !s.IsDead && !s.IsInside).ToList();
        var enemySoldiers = world.Settlers
            .Where(s => s.Profession == Profession.Soldier && s.Player != ai.Player && !s.IsInside && !s.IsDead)
            .Select(s => s.Position).ToList();
        bool guarded(GridPoint p) => enemySoldiers.Any(e => PathFinder.Distance(e, p) <= _soldierRadius);
        var border = OwnBorder(map, ai.Player);
        // The nearest walkable field of the own land (a scout drops his loot standing there).
        GridPoint homeFor(Settler scout) =>
            border.Where(map.IsWalkable).DefaultIfEmpty(military[0].Door).MinBy(q => PathFinder.Distance(q, scout.Position));

        foreach (int id in _targets.Keys.Where(id => !scouts.Any(s => s.Id == id)).ToList())
            _targets.Remove(id);

        var idle = new List<Settler>();
        foreach (var scout in scouts)
        {
            if (scout.Carrying is { } loot)
            {
                // Home with the loot (he drops it standing on the own land).
                if (_targets.Remove(scout.Id))
                    ai.Note($"thief: a scout got {loot}, brings it home");
                if (map.OwnerAt(scout.Position) != ai.Player && !world.Geology.IsSent(scout))
                    world.Geology.Send([scout], homeFor(scout));
                continue;
            }
            if (world.Theft.IsStealing(scout))
            {
                if (_targets.TryGetValue(scout.Id, out var pile) && (guarded(scout.Position) || guarded(pile.Position)))
                {
                    _targets.Remove(scout.Id);
                    _avoided[pile] = world.Tick + (long)(_retrySeconds * World.TicksPerSecond);
                    world.Geology.Send([scout], homeFor(scout)); // ends the theft
                    ai.Note($"thief: calls a scout home from {pile.Position.X},{pile.Position.Y}: enemy soldiers near");
                }
                continue;
            }
            _targets.Remove(scout.Id);
            if (world.Geology.IsSent(scout))
                continue;
            idle.Add(scout);
        }

        var piles = Targets(ai, border, guarded);
        foreach (var pile in piles)
        {
            if (idle.Count == 0)
                break;
            var scout = idle.MinBy(s => PathFinder.Distance(s.Position, pile.Position))!;
            if (world.Theft.StealAt([scout], pile.Position, _ranks) == 0)
                continue;
            idle.Remove(scout);
            _targets[scout.Id] = pile;
            ai.Note($"thief: sends a scout for {pile.Material} at {pile.Position.X},{pile.Position.Y}");
        }

        // Idle scouts with nothing to go for wait on the own land.
        foreach (var scout in idle.Where(s => map.OwnerAt(s.Position) != ai.Player))
            world.Geology.Send([scout], homeFor(scout));

        // More scouts while there is something to steal (lost ones not at once: carriers are worth something too).
        int missing = _scouts - scouts.Count;
        if (missing > 0 && piles.Count > 0 && world.Tick >= _nextScout)
        {
            var carriers = world.Settlers.Where(s => s.Player == ai.Player && s.IsAvailableCarrier).OrderBy(s => s.Id).Take(missing);
            if (world.Geology.Convert(carriers, Profession.Scout).Now is var made and > 0)
            {
                ai.Note($"thief: {made} carriers become scouts");
                _nextScout = world.Tick + (long)(_replaceSeconds * World.TicksPerSecond);
            }
        }
    }

    /// <summary>Piles worth a scout, best first: not yet targeted, at the edge of enemy land, near the own land, unguarded.</summary>
    private List<Pile> Targets(AiPlayer ai, List<GridPoint> border, Func<GridPoint, bool> guarded)
    {
        var world = ai.World;
        var map = world.Map!;
        var taken = _targets.Values.ToHashSet();
        foreach (var (pile, until) in _avoided.ToList())
            if (world.Tick >= until || !world.HasPile(pile))
                _avoided.Remove(pile);
        return world.Piles
            .Where(p => p.Material is not null && p.Count - p.Reserved > 0 && !taken.Contains(p) && !_avoided.ContainsKey(p)
                        && map.OwnerAt(p.Position) is { } owner && owner != ai.Player
                        && (p.Owner is null || p.Owner.Player != ai.Player))
            .Where(p => AtEdge(map, p.Position) && !guarded(p.Position))
            .Select(p => (Pile: p, Distance: border.Count == 0 ? int.MaxValue : border.Min(q => PathFinder.Distance(q, p.Position))))
            .Where(t => t.Distance <= _maxDistance)
            .OrderBy(t => RankOf(t.Pile.Material!.Value)).ThenBy(t => t.Distance).ThenBy(t => t.Pile.Id)
            .Select(t => t.Pile).ToList();
    }

    /// <summary>The fields of the player's land next to a field that is not.</summary>
    private static List<GridPoint> OwnBorder(Map.GameMap map, int player)
    {
        var border = new List<GridPoint>();
        for (int y = 0; y < map.Size; y++)
        for (int x = 0; x < map.Size; x++)
        {
            var p = new GridPoint(x, y);
            if (map.OwnerAt(p) != player)
                continue;
            for (int d = 0; d < 6; d++)
                if (p.Neighbor((Direction)d) is var n && map.Contains(n) && map.OwnerAt(n) != player)
                {
                    border.Add(p);
                    break;
                }
        }
        return border;
    }

    /// <summary>Whether the land the point lies on ends within <c>edgeDepth</c> fields (the map border does not count).</summary>
    private bool AtEdge(Map.GameMap map, GridPoint p)
    {
        int? owner = map.OwnerAt(p);
        return PathFinder.Circle(p, _edgeDepth).Any(q => map.Contains(q) && map.OwnerAt(q) != owner);
    }
}
