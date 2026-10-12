using Homestead.Simulation.Pathfinding;

namespace Homestead.Simulation.Economy;

/// <summary>
/// Keeps idle carriers, soldiers, diggers, builders and geologists out of the way: one that has had nothing to do for <see cref="IdleTicks"/> and
/// stands in a busy place (around a building, next to a pile, in a crowd) strolls to a quiet free
/// field nearby. Strolling carriers stay available: logistics and recruitment can take them any time.
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

    private readonly World _world;

    /// <summary>Tick from which a settler has been idle.</summary>
    private readonly Dictionary<int, long> _idleSince = new();

    internal Idling(World world)
    {
        _world = world;
    }

    public int Strolls { get; private set; }

    internal void Tick()
    {
        if (_world.Tick % Interval != 0)
            return;

        var taken = StrollTargets();
        foreach (var settler in _world.Settlers)
        {
            if (settler.IsStrolling && settler.Destination is null)
                settler.IsStrolling = false;
            if (!IsIdle(settler))
            {
                _idleSince.Remove(settler.Id);
                continue;
            }
            // Stranded outside the own land (it was lost): home to the nearest own building.
            if (_world.TerritoryRules && settler.Profession != Profession.Soldier && _world.Map!.OwnerAt(settler.Position) != settler.Player)
            {
                var home = _world.Buildings.Where(b => b.Exists && b.Player == settler.Player && _world.Map.OwnerAt(b.Door) == settler.Player)
                    .MinBy(b => PathFinder.Distance(b.Door, settler.Position));
                if (home is not null && _world.WalkTo(settler, home.Door))
                    settler.IsStrolling = true;
                continue;
            }
            long since = _idleSince.TryGetValue(settler.Id, out long s) ? s : _idleSince[settler.Id] = _world.Tick;
            if (_world.Tick - since < IdleTicks || IsQuiet(settler.Position, settler))
                continue;

            if (Stroll(settler, taken))
                _idleSince.Remove(settler.Id);
        }
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
        (s.IsCarrier || s.Profession is Profession.Soldier or Profession.Digger or Profession.Builder or Profession.Geologist
         || s.Workplace is null && s.Trade is not null)
        && !s.HasTask && !s.IsInside && s.Carrying is null && s.Destination is null && s.State != SettlerState.Stepping;

    /// <summary>Free ground (no building, its surroundings or pile), no pile next to it, at most one neighbor.</summary>
    private bool IsQuiet(GridPoint p, Settler? self)
    {
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
        return neighbors <= 1;
    }

    private GridPoint? QuietSpotNear(Settler settler, HashSet<GridPoint> taken)
    {
        var pathFinder = _world.PathFinder!;
        var from = settler.Position;
        for (int r = 2; r <= SearchRadius; r++)
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
