using Homestead.Simulation.Buildings;
using Homestead.Simulation.Map;
using Homestead.Simulation.Pathfinding;

namespace Homestead.Simulation.Economy;

/// <summary>
/// Sea trade, like land trade with a market and donkeys (<see cref="Trade"/>): a harbor gets a dock as a shipyard does, its
/// route lies on the water (stops, then the destination near a beach – no dock there). Every unit carriers bring to the
/// harbor's piles calls the nearest trade ship without a voyage that can get there; it sails to the dock, loads up to
/// <see cref="ShippingRules.CargoPiles"/> piles of <see cref="ShippingRules.CargoPileSize"/> units of one good each
/// (waiting <see cref="ShippingRules.LoadWaitTicks"/> for more while the harbor wants goods), sails the stops to the
/// destination, puts each pile down on the beach there and comes back the same way.
/// </summary>
/// <remarks>
/// Watchdog: a point the ship does not get near after a few tries is skipped (a stop) or sends it home (the destination;
/// the cargo then goes ashore next to the dock). A trade ship on a voyage takes no orders.
/// </remarks>
public sealed partial class Shipping
{
    private const int TradeDispatchInterval = 10;

    /// <summary>Tries to get near a point before the ship gives up on it.</summary>
    private const int MaxSailTries = 3;

    /// <summary>A ship this near a point of its route is there (others may lie on the very point).</summary>
    private const int ArrivedDistance = 2 * Spacing;

    /// <summary>Voyages of trade ships, by ship id.</summary>
    private readonly Dictionary<int, Voyage> _voyages = new();

    /// <summary>Per point the open water it belongs to (navigable points connected), worked out when needed.</summary>
    [NonSerialized]
    private int[]? _seaRegions;

    /// <summary>The cargo of the ship: per pile its good and units.</summary>
    public IReadOnlyList<(Material Material, int Count)> CargoOf(Ship ship) =>
        _voyages.TryGetValue(ship.Id, out var voyage) ? voyage.Cargo.Select(c => (c.Material, c.Count)).ToList() : [];

    /// <summary>The harbor the trade ship sails for, if any.</summary>
    public Building? HarborOf(Ship ship) => _voyages.TryGetValue(ship.Id, out var voyage) ? voyage.Harbor : null;

    /// <summary>Trade ships sailing for the harbor right now.</summary>
    public int ShipsOf(Building harbor) => _voyages.Values.Count(v => v.Harbor == harbor);

    /// <summary>
    /// Where a trade ship of the harbor goes for <paramref name="point"/>: the open water nearest it (ring by ring) that the
    /// harbor's dock reaches over the water; as the <paramref name="destination"/> also with land near
    /// (<see cref="ShippingRules.LandingDistance"/>) to put the goods on. Null without a dock or such water near the point.
    /// </summary>
    public GridPoint? SeaPoint(Building harbor, GridPoint point, bool destination)
    {
        if (DockOf(harbor) is not { } dock || SeaRegion(dock.Berth) is not { } region)
            return null;
        for (int radius = 0; radius <= 24; radius++)
            foreach (var p in PathFinder.Circle(point, radius * 2).Where(p => PathFinder.Distance(point, p) == radius))
                if (SeaRegion(p) == region && (!destination || ShoreAround(p).Any(IsBeach)))
                    return p;
        return null;
    }

    /// <summary>The open water the point belongs to, or null on land, near the shore or on a pier.</summary>
    private int? SeaRegion(GridPoint p)
    {
        var map = _world.Map!;
        if (!map.Contains(p))
            return null;
        if (_seaRegions is null || _seaRegions.Length != map.PointCount)
        {
            _seaRegions = new int[map.PointCount];
            int next = 0;
            var queue = new Queue<GridPoint>();
            for (int i = 0; i < map.PointCount; i++)
            {
                if (_seaRegions[i] != 0 || !IsNavigable(map.Point(i)))
                    continue;
                _seaRegions[i] = ++next;
                queue.Enqueue(map.Point(i));
                while (queue.Count > 0)
                {
                    var q = queue.Dequeue();
                    for (int d = 0; d < DirectionExtensions.Count; d++)
                        if (q.Neighbor((Direction)d) is var n && map.Contains(n) && _seaRegions[map.Index(n)] == 0 && IsNavigable(n))
                        {
                            _seaRegions[map.Index(n)] = next;
                            queue.Enqueue(n);
                        }
                }
            }
        }
        return _seaRegions[map.Index(p)] is var region and > 0 ? region : null;
    }

    /// <summary>Land a ship can put goods on: walkable ground, not a pier.</summary>
    private bool IsBeach(GridPoint p) => _world.Map!.TerrainAt(p) != TerrainType.Water;

    private void SeaTrade()
    {
        foreach (var voyage in _voyages.Values.ToList())
            Advance(voyage);
        if (_world.Tick % TradeDispatchInterval == 0)
            DispatchTradeShips();
    }

    /// <summary>A harbor with goods waiting and no ship coming for them yet calls the nearest free trade ship.</summary>
    private void DispatchTradeShips()
    {
        foreach (var harbor in _world.Buildings)
        {
            if (!harbor.Exists || harbor.State != BuildingState.Finished || !_world.Trade.IsHarbor(harbor)
                || _world.Trade.DestinationOf(harbor) is null || DockOf(harbor) is not { } dock
                || !harbor.Piles.Any(p => p.Role == PileRole.Trade && p.Count > 0)
                || _voyages.Values.Any(v => v.Harbor == harbor && v.Phase is VoyagePhase.ToHarbor or VoyagePhase.Loading))
                continue;
            var region = SeaRegion(dock.Berth);
            var ship = _ships.Where(s => s.Kind == ShipKind.TradeShip && s.Player == harbor.Player && !_voyages.ContainsKey(s.Id)
                                         && region is not null && SeaRegion(s.StepTicks > 0 ? s.Position.Neighbor(s.Facing) : s.Position) == region)
                .MinBy(s => PathFinder.Distance(s.Position, dock.Berth));
            if (ship is null)
                continue;
            var voyage = new Voyage(ship, harbor);
            _voyages[ship.Id] = voyage;
            SailTo(voyage, dock.Berth, VoyagePhase.ToHarbor);
        }
    }

    private void Advance(Voyage voyage)
    {
        var ship = voyage.Ship;
        bool home = voyage.Harbor.Exists && DockOf(voyage.Harbor) is not null;
        switch (voyage.Phase)
        {
            case VoyagePhase.ToHarbor:
                if (!home)
                    EndVoyage(voyage);
                else if (Reached(voyage, out bool stalled))
                {
                    voyage.Phase = VoyagePhase.Loading;
                    voyage.TicksLeft = Rules.CargoTicks;
                    voyage.WaitedTicks = 0;
                }
                else if (stalled)
                    EndVoyage(voyage);
                break;

            case VoyagePhase.Loading:
                if (!home)
                {
                    EndVoyage(voyage);
                    break;
                }
                if (--voyage.TicksLeft > 0)
                    break;
                voyage.TicksLeft = Rules.CargoTicks;
                if (LoadUnit(voyage))
                {
                    voyage.WaitedTicks = 0;
                    break;
                }
                // Not full: wait a while for more the harbor still wants.
                bool full = voyage.Cargo.Count >= Rules.CargoPiles && voyage.Cargo.All(c => c.Count >= Rules.CargoPileSize);
                voyage.WaitedTicks += Rules.CargoTicks;
                if (!full && voyage.WaitedTicks < Rules.LoadWaitTicks && _world.Trade.DestinationOf(voyage.Harbor) is not null
                    && (voyage.Cargo.Count == 0 || WantsMore(voyage.Harbor)))
                    break;
                if (voyage.Cargo.Count == 0 || _world.Trade.DestinationOf(voyage.Harbor) is null)
                {
                    PutAshore(voyage, DockOf(voyage.Harbor)!.Value.Land);
                    EndVoyage(voyage);
                    break;
                }
                voyage.Way.Clear();
                voyage.Way.AddRange(_world.Trade.Route(voyage.Harbor));
                voyage.Leg = 0;
                SailTo(voyage, voyage.Way[0], VoyagePhase.Travelling);
                break;

            case VoyagePhase.Travelling:
            {
                bool last = voyage.Leg == voyage.Way.Count - 1;
                if (Reached(voyage, out bool stalled) || stalled && !last)
                {
                    if (last)
                    {
                        voyage.Phase = VoyagePhase.Unloading;
                        voyage.TicksLeft = Rules.CargoTicks;
                        break;
                    }
                    voyage.Leg++;
                    SailTo(voyage, voyage.Way[voyage.Leg], VoyagePhase.Travelling);
                }
                else if (stalled)
                    TurnHome(voyage);
                break;
            }

            case VoyagePhase.Unloading:
                if (--voyage.TicksLeft > 0)
                    break;
                voyage.TicksLeft = Rules.CargoTicks;
                if (voyage.Cargo.Count > 0 && UnloadPile(voyage, ship.Position))
                    break;
                TurnHome(voyage);
                break;

            case VoyagePhase.Returning:
                if (Reached(voyage, out bool stalledHome) || stalledHome)
                {
                    if (voyage.Leg < voyage.Way.Count - 1)
                    {
                        voyage.Leg++;
                        SailTo(voyage, voyage.Way[voyage.Leg], VoyagePhase.Returning);
                        break;
                    }
                    // Home with goods it could not deliver: ashore next to the dock.
                    if (home)
                        PutAshore(voyage, DockOf(voyage.Harbor)!.Value.Land);
                    EndVoyage(voyage);
                }
                break;
        }
    }

    /// <summary>Whether the harbor still wants any good brought (and so a ship not yet full may wait for it).</summary>
    private bool WantsMore(Building harbor) =>
        harbor.Piles.Any(p => p.Role == PileRole.Trade && p.Incoming > 0)
        || Enum.GetValues<Material>().Any(m => _world.Trade.Remaining(harbor, m) > 0);

    /// <summary>One unit from the harbor's piles aboard: onto a pile of its good with room, else a new pile. False if none fits.</summary>
    private bool LoadUnit(Voyage voyage)
    {
        foreach (var pile in voyage.Harbor.Piles.Where(p => p.Role == PileRole.Trade && p.Count > 0 && _world.HasPile(p)))
        {
            var material = pile.Material!.Value;
            var cargo = voyage.Cargo.FirstOrDefault(c => c.Material == material && c.Count < Rules.CargoPileSize);
            if (cargo is null && voyage.Cargo.Count >= Rules.CargoPiles)
                continue;
            if (cargo is null)
                voyage.Cargo.Add(cargo = new CargoPile(material));
            cargo.Count++;
            pile.Count--;
            pile.ResetMaterialIfEmpty();
            return true;
        }
        return false;
    }

    /// <summary>The next pile of the cargo onto the free beach nearest the ship; false when there is no room (it stays aboard).</summary>
    private bool UnloadPile(Voyage voyage, GridPoint at)
    {
        if (ShoreAround(at).Where(IsBeach).Cast<GridPoint?>().FirstOrDefault() is not { } landing)
            return false;
        var cargo = voyage.Cargo[^1];
        if (FreeLandFrom(landing).Where(p => IsBeach(p) && _world.IsFreeGround(p) && _world.Map!.ObjectAt(p) == MapObjectKind.None)
                .Cast<GridPoint?>().FirstOrDefault() is not { } spot
            || _world.AddLooseGoods(spot, cargo.Material, cargo.Count) is null)
            return false;
        voyage.Cargo.RemoveAt(voyage.Cargo.Count - 1);
        _world.Trade.ShippedBySea(cargo.Count);
        return true;
    }

    /// <summary>All the cargo goes ashore near the point (back home, or the voyage ends).</summary>
    private void PutAshore(Voyage voyage, GridPoint near)
    {
        foreach (var cargo in voyage.Cargo)
            for (int i = 0; i < cargo.Count; i++)
                _world.DropGoods(near, cargo.Material);
        voyage.Cargo.Clear();
    }

    /// <summary>Home the way it came: the stops passed in reverse, then the dock.</summary>
    private void TurnHome(Voyage voyage)
    {
        var back = new List<GridPoint>();
        if (voyage.Phase is VoyagePhase.Travelling or VoyagePhase.Unloading)
            for (int i = Math.Min(voyage.Leg, voyage.Way.Count - 1) - 1; i >= 0; i--)
                back.Add(voyage.Way[i]);
        if (DockOf(voyage.Harbor) is { } dock)
            back.Add(dock.Berth);
        if (back.Count == 0)
        {
            EndVoyage(voyage);
            return;
        }
        voyage.Way.Clear();
        voyage.Way.AddRange(back);
        voyage.Leg = 0;
        SailTo(voyage, back[0], VoyagePhase.Returning);
    }

    private void SailTo(Voyage voyage, GridPoint target, VoyagePhase phase)
    {
        voyage.Phase = phase;
        voyage.Target = target;
        voyage.Tries = 0;
        Move([voyage.Ship], target, avoidMoving: false);
    }

    /// <summary>True once the ship lies near its target; <paramref name="stalled"/>: it did not get there after a few tries.</summary>
    private bool Reached(Voyage voyage, out bool stalled)
    {
        stalled = false;
        var ship = voyage.Ship;
        if (ship.IsMoving)
            return false;
        if (PathFinder.Distance(ship.Position, voyage.Target) <= ArrivedDistance)
            return true;
        if (++voyage.Tries > MaxSailTries)
        {
            stalled = true;
            return false;
        }
        Move([ship], voyage.Target, avoidMoving: true);
        return false;
    }

    private void EndVoyage(Voyage voyage)
    {
        // The ship keeps what it carries only on a voyage: what is left goes ashore where it is, if there is land near.
        if (voyage.Cargo.Count > 0)
            PutAshore(voyage, ShoreAround(voyage.Ship.Position).Where(IsBeach).Cast<GridPoint?>().FirstOrDefault() ?? voyage.Ship.Position);
        _voyages.Remove(voyage.Ship.Id);
    }

    private enum VoyagePhase
    {
        ToHarbor,
        Loading,
        Travelling,
        Unloading,
        Returning,
    }

    /// <summary>A pile of one good aboard a trade ship.</summary>
    private sealed class CargoPile(Material material)
    {
        public Material Material { get; } = material;
        public int Count { get; set; }
    }

    /// <summary>A trade ship sailing for a harbor.</summary>
    private sealed class Voyage(Ship ship, Building harbor)
    {
        public Ship Ship { get; } = ship;
        public Building Harbor { get; } = harbor;
        public VoyagePhase Phase { get; set; }

        /// <summary>The points it sails to in turn (out: stops and the destination; home: back and the dock).</summary>
        public List<GridPoint> Way { get; } = new();
        public int Leg { get; set; }
        public List<CargoPile> Cargo { get; } = new();
        public GridPoint Target { get; set; }
        public int Tries { get; set; }
        public int TicksLeft { get; set; }
        public int WaitedTicks { get; set; }
    }
}
