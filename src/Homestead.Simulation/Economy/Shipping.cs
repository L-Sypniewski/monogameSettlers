using Homestead.Simulation.Buildings;
using Homestead.Simulation.Map;
using Homestead.Simulation.Pathfinding;

namespace Homestead.Simulation.Economy;

/// <summary>
/// A dock: a straight wooden pier from a shore field (<see cref="Land"/>) <see cref="Length"/> fields out into the water
/// in one of the six directions; ships will lie at its end.
/// </summary>
public readonly record struct Dock(GridPoint Land, Direction Direction, int Length)
{
    /// <summary>The water fields the pier covers, from the shore out.</summary>
    public IEnumerable<GridPoint> Pier
    {
        get
        {
            var p = Land;
            for (int i = 0; i < Length; i++)
                yield return p = p.Neighbor(Direction);
        }
    }

    /// <summary>The pier's last field.</summary>
    public GridPoint End => Pier.Last();

    /// <summary>The water two fields beyond the pier's end, where a ship is built and lies (alongside, see <see cref="ShipFacing"/>).</summary>
    public GridPoint Berth => End.Neighbor(Direction).Neighbor(Direction);

    /// <summary>Which way a ship at the berth points: across the pier.</summary>
    public Direction ShipFacing => (Direction)(((int)Direction + DirectionExtensions.Count - 1) % DirectionExtensions.Count);
}

/// <summary>The ships a shipyard builds.</summary>
public enum ShipKind
{
    Ferry,
    TradeShip,
}

/// <summary>
/// A ship on the water: it goes where its player sends it (like a soldier), over open water only
/// (<see cref="Shipping.IsNavigable"/>), one field at a time, keeping <see cref="Shipping.Spacing"/> from other ships.
/// </summary>
public sealed class Ship
{
    internal readonly Queue<Direction> Path = new();

    internal Ship(int id, int player, ShipKind kind, Civilization people, GridPoint position, Direction facing)
    {
        Id = id;
        Player = player;
        Kind = kind;
        People = people;
        Position = position;
        Facing = facing;
    }

    public int Id { get; }
    public int Player { get; }
    public ShipKind Kind { get; }

    /// <summary>The people whose shipyard built it (its look).</summary>
    public Civilization People { get; }

    /// <summary>The field it is on (while moving: the one it left).</summary>
    public GridPoint Position { get; internal set; }

    /// <summary>Where it points; while moving also the way to the next field.</summary>
    public Direction Facing { get; internal set; }

    /// <summary>Ticks into the step to the next field (0: not moving between fields).</summary>
    public int StepTicks { get; internal set; }

    /// <summary>Where it was sent, while it is on the way.</summary>
    public GridPoint? Destination { get; internal set; }

    internal int WaitedTicks { get; set; }

    public bool IsMoving => StepTicks > 0 || Path.Count > 0;
}

/// <summary>
/// Shipping. A finished shipyard (<see cref="ShippingRules.Dockyard"/>) first needs a dock: the player clicks into the
/// water and the pier goes at once to the best place near the click (<see cref="PlaceDock"/>). The pier is drawn as map
/// objects (<see cref="MapObjectCodes.Dock"/>), can be walked on and goes with its shipyard.
/// </summary>
/// <remarks>
/// Then the player orders a ship (<see cref="Order"/>; one at a time, until it is done or the shipyard comes down). Carriers
/// bring its material (<see cref="ShippingRules.Costs"/>) to the shipyard's piles (<see cref="Wants"/>); its worker takes
/// one unit after the other along the pier to the hull at the berth and hammers it in (<see cref="Production"/>), and
/// with every unit the hull grows (<see cref="Progress"/>). With the last one the ship is launched (<see cref="Ships"/>).
/// </remarks>
public sealed class Shipping
{
    /// <summary>Most fields a ship's way is searched over.</summary>
    private const int MaxSearch = 150_000;

    /// <summary>A ship waiting this long for a field another ship holds gives up its way.</summary>
    private const int MaxWaitTicks = 3 * World.TicksPerSecond;

    /// <summary>A ship's field has water this far around it: the hull (several fields long) keeps off the shore.</summary>
    public const int ShoreClearance = 3;

    /// <summary>Fewest steps between two ships (they are big).</summary>
    public const int Spacing = 6;

    private readonly World _world;
    private readonly Dictionary<int, Dock> _docks = new();
    private readonly Dictionary<int, ShipOrder> _orders = new();
    private readonly List<Ship> _ships = new();
    private int _nextShipId = 1;

    internal Shipping(World world) => _world = world;

    internal ShippingRules Rules => _world.Production.Config.Shipping ?? ShippingRules.Default;

    /// <summary>Ticks a ship takes from one field to the next.</summary>
    public int ShipStepTicks => Rules.ShipStepTicks;

    /// <summary>Ships on the water.</summary>
    public IReadOnlyList<Ship> Ships => _ships;

    /// <summary>What a ship of the kind costs.</summary>
    public IReadOnlyDictionary<Material, int> CostOf(ShipKind kind) => Rules.Costs.GetValueOrDefault(kind) ?? new Dictionary<Material, int>();

    /// <summary>The ship the shipyard is building, if any.</summary>
    public ShipKind? Building(Building dockyard) => _orders.TryGetValue(dockyard.Id, out var order) ? order.Kind : null;

    /// <summary>Units of the material built into the ship on the stocks so far.</summary>
    public int BuiltIn(Building dockyard, Material material) =>
        _orders.TryGetValue(dockyard.Id, out var order) ? order.Built.GetValueOrDefault(material) : 0;

    /// <summary>Units of the material for the ship on the stocks at the shipyard so far: built in, on the way to the hull or in its pile.</summary>
    public int Delivered(Building dockyard, Material material) =>
        _orders.TryGetValue(dockyard.Id, out var order)
            ? order.Taken.GetValueOrDefault(material) + dockyard.Piles.Where(p => p.Role == PileRole.Request && p.Material == material).Sum(p => p.Count)
            : 0;

    /// <summary>How far the ship on the stocks is (0-1: units built in of all it needs); null without one.</summary>
    public double? Progress(Building dockyard)
    {
        if (!_orders.TryGetValue(dockyard.Id, out var order))
            return null;
        int total = CostOf(order.Kind).Values.Sum();
        return total == 0 ? 1 : (double)order.Built.Values.Sum() / total;
    }

    /// <summary>
    /// Starts a ship of the kind at the shipyard's dock: false without a finished shipyard with a dock, or while it builds one.
    /// </summary>
    public bool Order(Building dockyard, ShipKind kind)
    {
        if (!dockyard.Exists || dockyard.State != BuildingState.Finished || !IsDockyard(dockyard) || DockOf(dockyard) is null
            || _orders.ContainsKey(dockyard.Id))
            return false;
        _orders[dockyard.Id] = new ShipOrder(kind);
        return true;
    }

    /// <summary>Whether carriers should bring the material to the shipyard's pile: the ship on the stocks still needs it.</summary>
    internal bool Wants(Pile request, Material material)
    {
        if (request.Owner is not { } dockyard || !_orders.TryGetValue(dockyard.Id, out var order))
            return false;
        return request.Count + request.Incoming + order.Taken.GetValueOrDefault(material) < CostOf(order.Kind).GetValueOrDefault(material);
    }

    /// <summary>A pile of the shipyard holding a unit the ship still needs (for its worker), or null.</summary>
    internal Pile? NextUnit(Building dockyard)
    {
        if (!_orders.TryGetValue(dockyard.Id, out var order))
            return null;
        var cost = CostOf(order.Kind);
        return dockyard.Piles.FirstOrDefault(p => p.Role == PileRole.Request && p.Count > 0 && p.Material is { } m
                                                  && order.Taken.GetValueOrDefault(m) < cost.GetValueOrDefault(m));
    }

    /// <summary>The worker took a unit for the ship (or, <paramref name="back"/>, lost it on the way).</summary>
    internal void Take(Building dockyard, Material material, bool back = false)
    {
        if (_orders.TryGetValue(dockyard.Id, out var order))
            order.Taken[material] = order.Taken.GetValueOrDefault(material) + (back ? -1 : 1);
    }

    /// <summary>The worker hammered a unit in; with the last one the ship is launched.</summary>
    internal void Build(Building dockyard, Material material)
    {
        if (!_orders.TryGetValue(dockyard.Id, out var order))
            return;
        order.Built[material] = order.Built.GetValueOrDefault(material) + 1;
        var cost = CostOf(order.Kind);
        if (cost.Any(c => order.Built.GetValueOrDefault(c.Key) < c.Value))
            return;
        _orders.Remove(dockyard.Id);
        var dock = DockOf(dockyard)!.Value;
        var at = Nearest(dock.Berth, p => _ships.All(o => !Near(o, p))) ?? dock.Berth;
        _ships.Add(new Ship(_nextShipId++, dockyard.Player, order.Kind, dockyard.Definition.Civilization, at, dock.ShipFacing));
    }

    /// <summary>Builds a ship of the kind at the shipyard at once, without material (captures, cheats); false as <see cref="Order"/>.</summary>
    public bool Launch(Building dockyard, ShipKind kind)
    {
        if (!Order(dockyard, kind))
            return false;
        foreach (var (material, count) in CostOf(kind))
            for (int i = 0; i < count; i++)
                Build(dockyard, material);
        return true;
    }

    /// <summary>Where the worker stands to work on the hull (the pier's end) and which way he looks.</summary>
    internal (GridPoint Spot, Direction Facing)? WorkSpot(Building dockyard) => DockOf(dockyard) is { } dock ? (dock.End, dock.Direction) : null;

    public bool IsDockyard(Building building) => building.Definition.Name == Rules.Dockyard;

    /// <summary>The shipyard's dock, if it has one.</summary>
    public Dock? DockOf(Building dockyard) => _docks.TryGetValue(dockyard.Id, out var dock) ? dock : null;

    /// <summary>
    /// Where a dock for <paramref name="dockyard"/> would go for a click on <paramref name="clicked"/>: of all places within
    /// <see cref="ShippingRules.DockDistance"/> of the shipyard, the one whose pier end is nearest the click (null: none).
    /// The shore field is free, walkable, own land the shipyard's door connects to; the pier runs over own open water
    /// (past its first field no land around it, so it points out from the shore) not used by another dock.
    /// </summary>
    public Dock? DockSpot(Building dockyard, GridPoint clicked)
    {
        var map = _world.Map!;
        int length = Rules.DockLength;
        Dock? best = null;
        int bestSquared = int.MaxValue, bestSteps = int.MaxValue;
        foreach (var land in PathFinder.Circle(dockyard.Position, Rules.DockDistance))
        {
            if (!map.Contains(land) || !map.IsWalkable(land) || map.TerrainAt(land) == TerrainType.Water || !Own(map, land, dockyard.Player)
                || !_world.IsFreeGround(land))
                continue;
            for (int d = 0; d < DirectionExtensions.Count; d++)
            {
                var dock = new Dock(land, (Direction)d, length);
                var end = dock.End;
                int ex = clicked.X - end.X, ey = clicked.Y - end.Y;
                int squared = ex * ex + ey * ey - ex * ey;
                int steps = PathFinder.Distance(end, clicked);
                if (squared > bestSquared || squared == bestSquared && steps >= bestSteps || !PierFits(map, dockyard, dock))
                    continue;
                if (!_world.PathFinder!.AreConnected(dockyard.Door, land, null))
                    break;
                (best, bestSquared, bestSteps) = (dock, squared, steps);
            }
        }
        return best;
    }

    /// <summary>
    /// Builds the shipyard's dock at the best place for the click (<see cref="DockSpot"/>), at once; an old one goes.
    /// False if the building is no finished shipyard or there is no place.
    /// </summary>
    public bool PlaceDock(Building dockyard, GridPoint clicked)
    {
        if (!dockyard.Exists || dockyard.State != BuildingState.Finished || !IsDockyard(dockyard) || _orders.ContainsKey(dockyard.Id)
            || DockSpot(dockyard, clicked) is not { } dock)
            return false;
        RemoveDock(dockyard);
        _docks[dockyard.Id] = dock;
        foreach (var p in dock.Pier)
            _world.Map!.SetObjectCode(p, MapObjectCodes.Dock);
        return true;
    }

    /// <summary>The shipyard's dock goes (the building came down), and the ship on its stocks.</summary>
    internal void RemoveDock(Building dockyard)
    {
        _orders.Remove(dockyard.Id);
        if (!_docks.Remove(dockyard.Id, out var dock))
            return;
        foreach (var p in dock.Pier)
            if (_world.Map!.ObjectCodeAt(p) == MapObjectCodes.Dock)
                _world.Map.SetObjectCode(p, MapObjectCodes.None);
    }

    /// <summary>"no dock" until the shipyard has one (then its work counts).</summary>
    public string? Status(Building dockyard) =>
        IsDockyard(dockyard) && dockyard.State == BuildingState.Finished && DockOf(dockyard) is null ? "shipyard: no dock" : null;

    /// <summary>
    /// Whether a ship can be on the point: water (not a pier) with water <see cref="ShoreClearance"/> around, so it keeps
    /// clear of the shore.
    /// </summary>
    public bool IsNavigable(GridPoint p)
    {
        var map = _world.Map!;
        if (!map.Contains(p) || map.TerrainAt(p) != TerrainType.Water || map.ObjectCodeAt(p) == MapObjectCodes.Dock)
            return false;
        foreach (var q in PathFinder.Circle(p, ShoreClearance))
            if (!map.Contains(q) || map.TerrainAt(q) != TerrainType.Water)
                return false;
        return true;
    }

    /// <summary>The ship nearest the point within <see cref="Spacing"/> (on its field or the one it steps onto), if any.</summary>
    public Ship? ShipAt(GridPoint p) => _ships.Where(s => Near(s, p)).MinBy(s => PathFinder.Distance(s.Position, p));

    /// <summary>Whether the ship is (or steps) closer than <see cref="Spacing"/> to the point.</summary>
    private static bool Near(Ship ship, GridPoint p) => Gap(ship, p) < Spacing;

    private static int Gap(Ship ship, GridPoint p) => ship.StepTicks > 0
        ? Math.Min(PathFinder.Distance(ship.Position, p), PathFinder.Distance(ship.Position.Neighbor(ship.Facing), p))
        : PathFinder.Distance(ship.Position, p);

    /// <summary>
    /// Whether the ship may go from <paramref name="from"/> to <paramref name="to"/>: not nearer than <see cref="Spacing"/> to
    /// another ship (lying still, or any with <paramref name="moving"/>) – unless it moves away from it.
    /// </summary>
    private bool Clear(Ship ship, GridPoint from, GridPoint to, bool moving) => _ships.All(o =>
        o == ship || !moving && o.IsMoving || Gap(o, to) >= Spacing || Gap(o, to) >= Gap(o, from));

    /// <summary>
    /// Sends the ships towards <paramref name="target"/>: each to the free point nearest it that it can get to over the water
    /// (another sea, land: the nearest open water it can reach), apart from each other. Returns how many go.
    /// </summary>
    public int Move(IEnumerable<Ship> ships, GridPoint target) => Move(ships, target, avoidMoving: false);

    private int Move(IEnumerable<Ship> ships, GridPoint target, bool avoidMoving)
    {
        var goals = new List<GridPoint>();
        int going = 0;
        foreach (var ship in ships.Where(s => _ships.Contains(s)).OrderBy(s => PathFinder.Distance(s.Position, target)))
        {
            ship.Path.Clear();
            ship.Destination = null;
            if (Route(ship, target, goals, avoidMoving) is not { } route)
                continue;
            goals.Add(route.Goal);
            foreach (var step in route.Steps)
                ship.Path.Enqueue(step);
            ship.Destination = route.Goal;
            ship.WaitedTicks = 0;
            going++;
        }
        return going;
    }

    /// <summary>
    /// Breadth-first over the water from the ship: the way to the reachable point nearest the target, at
    /// <see cref="Spacing"/> from the <paramref name="goals"/> of the others sent with it and from ships lying still (with
    /// <paramref name="avoidMoving"/> also from moving ones).
    /// </summary>
    private (GridPoint Goal, List<Direction> Steps)? Route(Ship ship, GridPoint target, List<GridPoint> goals, bool avoidMoving)
    {
        var map = _world.Map!;
        var start = ship.StepTicks > 0 ? ship.Position.Neighbor(ship.Facing) : ship.Position;
        var from = new Dictionary<int, (int Parent, Direction Step)> { [map.Index(start)] = (-1, default) };
        var queue = new Queue<GridPoint>();
        queue.Enqueue(start);
        GridPoint? best = null;
        int bestSquared = int.MaxValue;
        while (queue.Count > 0 && from.Count < MaxSearch)
        {
            var p = queue.Dequeue();
            int ex = target.X - p.X, ey = target.Y - p.Y;
            int squared = ex * ex + ey * ey - ex * ey;
            if (squared < bestSquared && goals.All(g => PathFinder.Distance(g, p) >= Spacing)
                && _ships.All(o => o == ship || o.IsMoving || !Near(o, p)))
            {
                (best, bestSquared) = (p, squared);
                if (squared == 0)
                    break;
            }
            for (int d = 0; d < DirectionExtensions.Count; d++)
            {
                var n = p.Neighbor((Direction)d);
                if (from.ContainsKey(map.Index(n)) || !IsNavigable(n) || !Clear(ship, start, n, avoidMoving))
                    continue;
                from[map.Index(n)] = (map.Index(p), (Direction)d);
                queue.Enqueue(n);
            }
        }
        if (best is not { } goal)
            return null;
        var steps = new List<Direction>();
        for (int i = map.Index(goal); from[i].Parent >= 0; i = from[i].Parent)
            steps.Add(from[i].Step);
        steps.Reverse();
        return (goal, steps);
    }

    /// <summary>The navigable point nearest <paramref name="center"/> (ring by ring) that satisfies <paramref name="free"/>.</summary>
    private GridPoint? Nearest(GridPoint center, Func<GridPoint, bool> free)
    {
        for (int radius = 0; radius <= 12; radius++)
            foreach (var p in PathFinder.Circle(center, radius * 2).Where(p => PathFinder.Distance(center, p) == radius))
                if (IsNavigable(p) && free(p))
                    return p;
        return null;
    }

    internal void Tick()
    {
        foreach (var ship in _ships)
            Sail(ship);
    }

    private void Sail(Ship ship)
    {
        if (ship.StepTicks > 0)
        {
            if (++ship.StepTicks < Rules.ShipStepTicks)
                return;
            ship.Position = ship.Position.Neighbor(ship.Facing);
            ship.StepTicks = 0;
        }
        if (ship.Path.Count == 0)
        {
            ship.Destination = null;
            return;
        }
        var direction = ship.Path.Peek();
        var next = ship.Position.Neighbor(direction);
        if (!IsNavigable(next) || !Clear(ship, ship.Position, next, moving: true))
        {
            // Another ship in the way: wait a little, then go round it (all ships in the way), or stop short of the destination.
            if (++ship.WaitedTicks >= MaxWaitTicks && ship.Destination is { } destination)
            {
                ship.WaitedTicks = 0;
                Move([ship], destination, avoidMoving: true);
            }
            return;
        }
        ship.Path.Dequeue();
        ship.WaitedTicks = 0;
        ship.Facing = direction;
        ship.StepTicks = 1;
    }

    private bool Own(GameMap map, GridPoint p, int player) => !_world.TerritoryRules || map.OwnerAt(p) == player;

    private bool PierFits(GameMap map, Building dockyard, Dock dock)
    {
        if (!IsNavigable(dock.Berth))
            return false;
        var own = DockOf(dockyard);
        int i = 0;
        foreach (var p in dock.Pier)
        {
            if (!map.Contains(p) || map.TerrainAt(p) != TerrainType.Water || !Own(map, p, dockyard.Player))
                return false;
            // Its own old pier may be moved; anything else in the water (reefs, other docks) is in the way.
            byte code = map.ObjectCodeAt(p);
            if (code != MapObjectCodes.None && !(code == MapObjectCodes.Dock && own is { } old && old.Pier.Contains(p)))
                return false;
            if (i++ > 0)
                for (int d = 0; d < DirectionExtensions.Count; d++)
                    if (p.Neighbor((Direction)d) is var n && (!map.Contains(n) || map.TerrainAt(n) != TerrainType.Water))
                        return false;
        }
        return true;
    }
}

/// <summary>A ship on a shipyard's stocks: the units its worker took so far and those he built in.</summary>
internal sealed class ShipOrder(ShipKind kind)
{
    public ShipKind Kind { get; } = kind;
    public Dictionary<Material, int> Taken { get; } = new();
    public Dictionary<Material, int> Built { get; } = new();
}
