using Homestead.Simulation.Pathfinding;

namespace Homestead.Simulation.Economy;

/// <summary>
/// Keeps idle carriers, soldiers, diggers, builders and geologists out of the way: one that has had nothing to do for <see cref="IdleTicks"/> and
/// stands in a busy place (around a building, next to a pile, in a crowd) strolls to a quiet free
/// field nearby. Strolling carriers stay available: logistics and recruitment can take them any time.
/// Idle soldiers spread out at once (checked every tick) and keep a field between each other.
/// Carriers and workers stranded outside the own land (lost under their feet) flee to the nearest own land; with
/// none to reach they wander about aimlessly and die after <see cref="MinLostSeconds"/> to <see cref="MaxLostSeconds"/>.
/// </summary>
/// <remarks>
/// Found while watching the demo economy: carriers drop their goods at the storehouse and then stand
/// in its yard, so every later delivery has to squeeze through them. Found by the AI: an idle digger
/// on the door of a new living house kept all its residents in.
/// </remarks>
public sealed class Idling
{
    public const int IdleTicks = 40;
    public const int Interval = 10;
    public const int SearchRadius = 8;

    /// <summary>How far a stranded settler looks for own land (beyond it: the nearest own building).</summary>
    public const int RefugeRadius = 64;

    public const int MinLostSeconds = 30;
    public const int MaxLostSeconds = 60;

    /// <summary>How often a settler with no way home looks again (it may have become free meanwhile).</summary>
    private const int RefugeRetryTicks = 2 * World.TicksPerSecond;

    private const int WanderRadius = 6;

    private readonly World _world;

    /// <summary>Tick from which a settler has been idle.</summary>
    private readonly Dictionary<int, long> _idleSince = new();

    /// <summary>Tick from which a settler that found no quiet spot looks again.</summary>
    private readonly Dictionary<int, long> _retryAt = new();

    /// <summary>Tick at which a stranded settler with no way to the own land dies.</summary>
    private readonly Dictionary<int, long> _diesAt = new();

    internal Idling(World world)
    {
        _world = world;
    }

    public int Strolls { get; private set; }

    internal void Tick()
    {
        // Soldiers every tick, everyone else every Interval ticks.
        bool everyone = _world.Tick % Interval == 0;
        HashSet<GridPoint>? taken = null;
        List<Settler>? perished = null;
        foreach (var settler in _world.Settlers)
        {
            if (!everyone && settler.Profession != Profession.Soldier)
                continue;
            // Lost: back on the own land it is saved, otherwise its time runs out (also while wandering about).
            if (_diesAt.TryGetValue(settler.Id, out long dies))
            {
                if (_world.Map!.OwnerAt(settler.Position) == settler.Player)
                    _diesAt.Remove(settler.Id);
                else if (_world.Tick >= dies)
                {
                    (perished ??= new()).Add(settler);
                    continue;
                }
            }
            if (settler.IsStrolling && settler.Destination is null)
                settler.IsStrolling = false;
            if (!IsIdle(settler))
            {
                _idleSince.Remove(settler.Id);
                _retryAt.Remove(settler.Id);
                continue;
            }
            // Stranded outside the own land (it was lost): flee to the nearest own land, or wander about lost.
            if (_world.TerritoryRules && !World.RoamsFreely(settler) && _world.Map!.OwnerAt(settler.Position) != settler.Player)
            {
                if (_retryAt.TryGetValue(settler.Id, out long again) && _world.Tick < again)
                    continue;
                if (Refuge(settler) is { } refuge && _world.WalkTo(settler, refuge))
                {
                    settler.IsStrolling = true;
                    _diesAt.Remove(settler.Id);
                    _retryAt.Remove(settler.Id);
                }
                else
                {
                    _diesAt.TryAdd(settler.Id, _world.Tick + LostTicks(settler));
                    Wander(settler);
                    _retryAt[settler.Id] = _world.Tick + RefugeRetryTicks;
                }
                continue;
            }
            long since = _idleSince.TryGetValue(settler.Id, out long s) ? s : _idleSince[settler.Id] = _world.Tick;
            bool soldier = settler.Profession == Profession.Soldier;
            if (!soldier && _world.Tick - since < IdleTicks || IsQuiet(settler.Position, settler))
                continue;
            // Found no quiet spot a moment ago: not again every tick.
            if (_retryAt.TryGetValue(settler.Id, out long retry) && _world.Tick < retry)
                continue;

            taken ??= StrollTargets();
            if (Stroll(settler, taken))
            {
                _idleSince.Remove(settler.Id);
                _retryAt.Remove(settler.Id);
            }
            else
                _retryAt[settler.Id] = _world.Tick + Interval;
        }
        foreach (var settler in perished ?? [])
            Perish(settler);
    }

    /// <summary>The nearest reachable free field of the own land (within <see cref="RefugeRadius"/>, else an own building's door).</summary>
    private GridPoint? Refuge(Settler settler)
    {
        var map = _world.Map!;
        var from = settler.Position;
        var fields = new List<(GridPoint Point, int Distance)>();
        for (int y = from.Y - RefugeRadius; y <= from.Y + RefugeRadius; y++)
        for (int x = from.X - RefugeRadius; x <= from.X + RefugeRadius; x++)
        {
            var p = new GridPoint(x, y);
            if (map.Contains(p) && map.OwnerAt(p) == settler.Player && map.IsWalkable(p) && !_world.IsOccupied(p))
                fields.Add((p, PathFinder.Distance(from, p)));
        }
        foreach (var (p, _) in fields.OrderBy(f => f.Distance))
            if (_world.CanFleeTo(settler, p))
                return p;
        return _world.Buildings.Where(b => b.Exists && b.Player == settler.Player && map.OwnerAt(b.Door) == settler.Player && _world.CanFleeTo(settler, b.Door))
            .MinBy(b => PathFinder.Distance(b.Door, from))?.Door;
    }

    /// <summary>A few aimless steps somewhere near.</summary>
    private void Wander(Settler settler)
    {
        var map = _world.Map!;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            int hash = Hash(settler.Id, (int)_world.Tick, attempt);
            var p = new GridPoint(settler.Position.X + hash % (2 * WanderRadius + 1) - WanderRadius,
                settler.Position.Y + hash / 31 % (2 * WanderRadius + 1) - WanderRadius);
            if (p != settler.Position && map.Contains(p) && map.IsWalkable(p) && !_world.IsOccupied(p) && _world.WalkTo(settler, p))
                return;
        }
    }

    /// <summary>How long a lost settler lives on: <see cref="MinLostSeconds"/> to <see cref="MaxLostSeconds"/>, fixed per settler and game.</summary>
    private long LostTicks(Settler settler) =>
        (MinLostSeconds + Hash(settler.Id, _world.Seed, 0) % (MaxLostSeconds - MinLostSeconds + 1)) * (long)World.TicksPerSecond;

    /// <summary>A fixed mix (not <see cref="HashCode"/>: it is salted per process, a loaded game would go on differently).</summary>
    private static int Hash(int a, int b, int c)
    {
        uint h = unchecked((uint)a * 0x9E3779B1u ^ (uint)b * 0x85EBCA77u ^ (uint)c * 0xC2B2AE3Du);
        h ^= h >> 15;
        h = unchecked(h * 0x2C1B3C6Du);
        h ^= h >> 12;
        return (int)(h & int.MaxValue);
    }

    private void Perish(Settler settler)
    {
        _diesAt.Remove(settler.Id);
        _idleSince.Remove(settler.Id);
        _retryAt.Remove(settler.Id);
        _world.History.Killed(settler, null);
        _world.AddGhost(settler);
        _world.Cue(new SoundCue(SoundCueKind.Death, settler.Position, settler.Player, People: settler.People ?? _world.PeopleOf(settler.Player)));
        _world.Remove(settler);
    }

    /// <summary>Sends a carrier to a quiet spot right away (e.g. a newcomer leaving its house).</summary>
    internal void StrollAway(Settler settler) => Stroll(settler, StrollTargets());

    private HashSet<GridPoint> StrollTargets() =>
        new(_world.Settlers.Where(s => s.IsStrolling && s.Destination is not null).Select(s => s.Destination!.Value));

    private bool Stroll(Settler settler, HashSet<GridPoint> taken)
    {
        if (QuietSpotNear(settler, taken) is not { } spot || !_world.WalkTo(settler, spot))
            return false;
        settler.IsStrolling = true;
        taken.Add(spot);
        Strolls++;
        return true;
    }

    private static bool IsIdle(Settler s) =>
        (s.IsCarrier || s.Profession is Profession.Soldier or Profession.Priest or Profession.Digger or Profession.Builder or Profession.Geologist or Profession.Scout or Profession.Pioneer
         or Profession.Donkey
         || s.Workplace is null && s.Trade is not null)
        && !s.HasTask && !s.IsInside && s.Carrying is null && s.Destination is null && s.State != SettlerState.Stepping;

    /// <summary>Free ground (no building, its surroundings or pile), no pile next to it, at most one neighbor (soldiers: none).</summary>
    private bool IsQuiet(GridPoint p, Settler? self)
    {
        int allowed = self?.Profession == Profession.Soldier ? 0 : 1;
        var map = _world.Map!;
        if (!map.IsWalkable(p) || !_world.IsFreeGround(p))
            return false;
        int neighbors = 0;
        for (int d = 0; d < DirectionExtensions.Count; d++)
        {
            var n = p.Neighbor((Direction)d);
            if (!map.Contains(n))
                continue;
            if (_world.PileAt(n) is not null)
                return false;
            if (_world.IsOccupied(n) && (self is null || self.Position != n))
                neighbors++;
        }
        return neighbors <= allowed;
    }

    private GridPoint? QuietSpotNear(Settler settler, HashSet<GridPoint> taken)
    {
        var pathFinder = _world.PathFinder!;
        var from = settler.Position;
        // Soldiers only step apart; the others leave the busy place.
        for (int r = settler.Profession == Profession.Soldier ? 1 : 2; r <= SearchRadius; r++)
        for (int dy = -r; dy <= r; dy++)
        for (int dx = -r; dx <= r; dx++)
        {
            var p = new GridPoint(from.X + dx, from.Y + dy);
            if (PathFinder.Distance(from, p) != r || taken.Contains(p) || !_world.Map!.Contains(p) || _world.IsOccupied(p))
                continue;
            // Keep a field between strollers' destinations too.
            if (taken.Any(t => PathFinder.Distance(t, p) <= 1))
                continue;
            if (IsQuiet(p, settler) && _world.Reachable(settler, p))
                return p;
        }
        return null;
    }
}
