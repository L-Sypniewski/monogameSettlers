using Homestead.Simulation.Buildings;
using Homestead.Simulation.Economy;
using Homestead.Simulation.Pathfinding;

namespace Homestead.Simulation.Military;

/// <summary>
/// Soldiers and territory: barracks turn carriers into soldiers (one weapon each), military buildings
/// call idle soldiers of their player into their soldier places, and an occupied military building
/// claims the land around it.
/// </summary>
/// <remarks>
/// Territory: a building claims every point of its circle (<see cref="MilitaryConfig.Territory"/>) that
/// belongs to nobody or to a player whose own buildings do not cover it. When a building is lost, its
/// land goes to another building of the same player covering it, else to another player's covering
/// building, else to nobody. Land of a loaded map keeps the owners stored in the map.
/// Watchdog: a soldier or trainee that makes no progress towards the door for <see cref="StallTicks"/>
/// is released and the place called again.
/// </remarks>
public sealed partial class Military
{
    public const int StallTicks = 400;
    private const int DispatchInterval = 10;

    private readonly World _world;
    private readonly MilitaryConfig _config;
    private readonly Dictionary<int, Garrison> _garrisons = new();
    private readonly Dictionary<int, Barrack> _barracks = new();

    internal Military(World world, MilitaryConfig config)
    {
        _world = world;
        _config = config;
    }

    public MilitaryConfig Config => _config;

    public int SoldiersTrained { get; private set; }

    /// <summary>Soldiers in the building's places (null for a free place); empty for other buildings.</summary>
    public IReadOnlyList<Settler?> Occupants(Building building) =>
        _garrisons.TryGetValue(building.Id, out var g) ? g.Places : [];

    public bool IsOccupied(Building building) => _garrisons.TryGetValue(building.Id, out var g) && g.Places.Any(p => p is not null);

    /// <summary>A short description for the player (null for non-military buildings).</summary>
    public string? Status(Building building)
    {
        if (_barracks.TryGetValue(building.Id, out var barrack))
            return barrack.Trainee is null ? "barracks: waiting for weapons and carriers"
                : barrack.Training ? "training a soldier" : "a recruit is on the way";
        if (!_garrisons.TryGetValue(building.Id, out var g))
            return null;
        int inside = g.Places.Count(p => p is not null);
        return $"{inside}/{g.Places.Length} soldiers" + (g.Coming.Count > 0 ? $", {g.Coming.Count} on the way" : "")
               + (inside == 0 ? " (claims no land yet)" : "");
    }

    internal void Tick()
    {
        if (_world.Tick % DispatchInterval == 0)
        {
            Discover();
            foreach (var barrack in _barracks.Values.ToList())
                CallTrainee(barrack);
            // One soldier per building and round, emptiest first: every building gets its first
            // soldier (and claims its land) before any building fills up.
            foreach (var garrison in _garrisons.Values.OrderBy(g => g.Places.Count(p => p is not null) + g.Coming.Count).ToList())
                CallSoldier(garrison);
        }
        foreach (var barrack in _barracks.Values.ToList())
            AdvanceBarrack(barrack);
        foreach (var garrison in _garrisons.Values.ToList())
            AdvanceGarrison(garrison);
        TickCombat();
    }

    private void Discover()
    {
        foreach (var building in _world.Buildings)
        {
            if (!building.Exists || building.State != BuildingState.Finished)
                continue;
            if (building.Definition.Occupiers.Count > 0 && !_garrisons.ContainsKey(building.Id))
                _garrisons[building.Id] = new Garrison(building);
            else if (building.Definition.Name == "barrack" && !_barracks.ContainsKey(building.Id))
                _barracks[building.Id] = new Barrack(building);
        }
    }

    // ---- barracks ----

    private void CallTrainee(Barrack b)
    {
        if (!b.Building.Exists || b.Trainee is not null || !HasWeapon(b.Building) || !_world.Recruitment.MayConvert(b.Building.Player))
            return;
        var door = b.Building.Door;
        var pathFinder = _world.PathFinder!;
        var trainee = _world.Settlers
            .Where(s => s.IsAvailableCarrier && s.Player == b.Building.Player && _world.Reachable(s, door))
            .MinBy(s => PathFinder.Distance(s.Position, door));
        if (trainee is null)
            return;
        b.Trainee = trainee;
        b.Walk = new WalkWatch(_world.Tick);
        trainee.HasTask = true;
        if (!_world.WalkTo(trainee, door))
            ReleaseTrainee(b);
    }

    private bool HasWeapon(Building barrack) =>
        barrack.Piles.Any(p => p.Role == PileRole.Request && p.Count > 0 && p.Material is { } m && _config.Weapons.ContainsKey(m));

    private void AdvanceBarrack(Barrack b)
    {
        if (!b.Building.Exists)
        {
            ReleaseTrainee(b);
            _barracks.Remove(b.Building.Id);
            return;
        }
        if (b.Trainee is not { } trainee)
            return;

        if (b.Training)
        {
            if (--b.TicksLeft > 0)
                return;
            _world.LeaveBuilding(trainee);
            trainee.Profession = Profession.Soldier;
            trainee.SoldierType = b.Becomes;
            // The level the player's upgrades (small temple) give newly trained soldiers of the type.
            trainee.Level = _world.Magic.SoldierLevel(b.Building.Player, b.Becomes);
            trainee.Health = _config.MaxHealth(b.Becomes, trainee.Level);
            trainee.HasTask = false;
            SoldiersTrained++;
            b.Trainee = null;
            b.Training = false;
            return;
        }

        if (!Arrived(trainee, b.Building.Door, b.Walk!, out bool stalled))
        {
            if (stalled)
                ReleaseTrainee(b);
            return;
        }
        // The weapon with the most units in stock.
        var pile = b.Building.Piles
            .Where(p => p.Role == PileRole.Request && p.Count > 0 && p.Material is { } m && _config.Weapons.ContainsKey(m))
            .MaxBy(p => p.Count);
        if (pile is null)
        {
            ReleaseTrainee(b);
            return;
        }
        pile.Count--;
        b.Becomes = _config.Weapons[pile.Material!.Value];
        pile.ResetMaterialIfEmpty();
        _world.EnterBuilding(trainee);
        b.Training = true;
        b.TicksLeft = _config.TrainingTicks;
    }

    private void ReleaseTrainee(Barrack b)
    {
        if (b.Trainee is { } trainee)
        {
            if (trainee.IsInside)
                _world.LeaveBuilding(trainee);
            trainee.HasTask = false;
        }
        b.Trainee = null;
        b.Training = false;
    }

    // ---- military buildings ----

    private void CallSoldier(Garrison g)
    {
        if (!g.Building.Exists)
            return;
        var door = g.Building.Door;
        var pathFinder = _world.PathFinder!;
        for (int i = 0; i < g.Places.Length; i++)
        {
            if (g.Places[i] is not null || g.Coming.ContainsKey(i) || g.Defending.ContainsKey(i))
                continue;
            var place = g.Building.Definition.Occupiers[i];
            var soldier = _world.Settlers
                .Where(s => s.Profession == Profession.Soldier && !s.HasTask && !s.IsInside && s.Player == g.Building.Player
                            && s.SoldierType is { } type && place.Accepts(type) && pathFinder.AreConnected(s.Position, door))
                .MinBy(s => PathFinder.Distance(s.Position, door));
            if (soldier is null)
                continue;
            soldier.HasTask = true;
            g.Coming[i] = (soldier, new WalkWatch(_world.Tick));
            if (!_world.WalkTo(soldier, door))
                Release(g, i);
            return;
        }
    }

    private void AdvanceGarrison(Garrison g)
    {
        if (!g.Building.Exists)
        {
            Abandon(g);
            return;
        }
        foreach (var (place, (soldier, watch)) in g.Coming.ToList())
        {
            if (soldier.IsDead)
            {
                g.Coming.Remove(place);
                continue;
            }
            if (!Arrived(soldier, g.Building.Door, watch, out bool stalled))
            {
                if (stalled)
                    Release(g, place);
                continue;
            }
            g.Coming.Remove(place);
            _world.EnterBuilding(soldier);
            bool first = !g.Places.Any(p => p is not null);
            g.Places[place] = soldier;
            if (first)
                Claim(g.Building);
        }
    }

    private void Release(Garrison g, int place)
    {
        if (g.Coming.Remove(place, out var coming))
            coming.Soldier.HasTask = false;
    }

    /// <summary>The building is gone: its soldiers come out, its land is given up.</summary>
    private void Abandon(Garrison g)
    {
        foreach (var place in g.Coming.Keys.ToList())
            Release(g, place);
        g.Defending.Clear();
        bool occupied = false;
        for (int i = 0; i < g.Places.Length; i++)
        {
            if (g.Places[i] is not { } soldier)
                continue;
            occupied = true;
            _world.LeaveBuilding(soldier);
            soldier.HasTask = false;
            g.Places[i] = null;
        }
        _garrisons.Remove(g.Building.Id);
        if (!occupied)
            return;
        Unclaim(g.Building);
        // Buildings on the land given up go down with it.
        var map = _world.Map!;
        int player = g.Building.Player;
        var lost = TerritoryOf(g.Building).Where(p => map.OwnerAt(p) != player).ToHashSet();
        foreach (var other in _world.Buildings.Where(b => b.Exists && b.Player == player && lost.Contains(b.Position)).ToList())
            _world.RemoveBuilding(other);
    }

    // ---- territory ----

    private double TerritoryRadius(Building building) => _config.Territory.GetValueOrDefault(building.Definition.Name);

    private static bool InCircle(GridPoint center, GridPoint p, double radius)
    {
        int dx = p.X - center.X, dy = p.Y - center.Y;
        return dx * dx + dy * dy - dx * dy <= radius * radius;
    }

    private IEnumerable<GridPoint> TerritoryOf(Building building)
    {
        double radius = TerritoryRadius(building);
        var map = _world.Map!;
        return PathFinder.Circle(building.Position, (int)Math.Ceiling(radius))
            .Where(p => map.Contains(p) && InCircle(building.Position, p, radius));
    }

    /// <summary>Whether an occupied military building of the player (other than <paramref name="except"/>) covers the point.</summary>
    private bool Covers(int player, GridPoint p, Building? except = null) => _garrisons.Values.Any(g =>
        g.Building != except && g.Building.Exists && g.Building.Player == player && g.Places.Any(s => s is not null)
        && InCircle(g.Building.Position, p, TerritoryRadius(g.Building)));

    private void Claim(Building building)
    {
        var map = _world.Map!;
        foreach (var p in TerritoryOf(building))
            if (map.OwnerAt(p) is not { } owner || owner != building.Player && !Covers(owner, p))
                map.SetOwner(p, building.Player);
    }

    private void Unclaim(Building building)
    {
        var map = _world.Map!;
        foreach (var p in TerritoryOf(building))
        {
            if (map.OwnerAt(p) != building.Player || Covers(building.Player, p, building))
                continue;
            var other = _garrisons.Values.FirstOrDefault(g => g.Building != building && g.Building.Exists && g.Places.Any(s => s is not null)
                                                              && InCircle(g.Building.Position, p, TerritoryRadius(g.Building)));
            map.SetOwner(p, other?.Building.Player);
        }
    }

    // ---- walking ----

    /// <summary>True once the settler stands at the door; <paramref name="stalled"/> is the watchdog.</summary>
    private bool Arrived(Settler settler, GridPoint door, WalkWatch watch, out bool stalled)
    {
        stalled = false;
        if (settler.State != SettlerState.Stepping && settler.Destination is null)
        {
            if (settler.Position == door)
                return true;
            if (!_world.WalkTo(settler, door))
            {
                stalled = true;
                return false;
            }
        }
        int remaining = settler.RemainingPath.Count();
        if (remaining < watch.BestRemaining)
        {
            watch.BestRemaining = remaining;
            watch.LastProgress = _world.Tick;
        }
        stalled = _world.Tick - watch.LastProgress > StallTicks;
        return false;
    }

    private sealed class WalkWatch(long now)
    {
        public int BestRemaining { get; set; } = int.MaxValue;
        public long LastProgress { get; set; } = now;
    }

    private sealed class Garrison(Building building)
    {
        public Building Building { get; } = building;
        public Settler?[] Places { get; } = new Settler?[building.Definition.Occupiers.Count];
        public Dictionary<int, (Settler Soldier, WalkWatch Watch)> Coming { get; } = new();

        /// <summary>Soldiers out of their place to fight attackers at the door (by place).</summary>
        public Dictionary<int, Settler> Defending { get; } = new();
    }

    private sealed class Barrack(Building building)
    {
        public Building Building { get; } = building;
        public Settler? Trainee { get; set; }
        public WalkWatch? Walk { get; set; }
        public bool Training { get; set; }
        public int TicksLeft { get; set; }
        public SoldierType Becomes { get; set; }
    }
}
