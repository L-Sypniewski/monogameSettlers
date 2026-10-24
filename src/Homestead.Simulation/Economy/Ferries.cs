using Homestead.Simulation.Pathfinding;

namespace Homestead.Simulation.Economy;

/// <summary>
/// Ferries: a ship lying near land (<see cref="ShippingRules.LandingDistance"/>) takes units aboard – soldiers, priests,
/// geologists, scouts and pioneers sent to it walk to the shore nearest it and go on board, up to
/// <see cref="ShippingRules.Passengers"/>. On board they are inside (nobody sees, fights or orders them) and go with the
/// ship; <see cref="Unload"/> puts them all ashore on the free land nearest it.
/// </summary>
public sealed partial class Shipping
{
    /// <summary>Units on their way to a ship, by settler id.</summary>
    private readonly Dictionary<int, Boarding> _boarding = new();

    /// <summary>How many units the ship takes aboard.</summary>
    public int Capacity(Ship ship) => Rules.Passengers.GetValueOrDefault(ship.Kind);

    /// <summary>Whether the unit may go on board a ship: soldiers, priests, geologists, scouts and pioneers outside.</summary>
    public static bool CanBoard(Settler settler) =>
        settler.Profession is Profession.Soldier or Profession.Priest or Profession.Geologist or Profession.Scout or Profession.Pioneer
        && !settler.IsDead && !settler.IsInside;

    /// <summary>The ship the unit is on its way to, if any.</summary>
    public Ship? BoardingShip(Settler settler) => _boarding.TryGetValue(settler.Id, out var boarding) ? boarding.Ship : null;

    /// <summary>
    /// Whether the ship lies near land: still, with a walkable field (shore or pier) within
    /// <see cref="ShippingRules.LandingDistance"/>.
    /// </summary>
    public bool IsNearLand(Ship ship) => !ship.IsMoving && ShoreAround(ship).Any();

    /// <summary>
    /// Sends the units aboard the ship (a ferry lying near land): each walks to the shore field nearest the ship it can
    /// get to and goes on board there. As many go as there is room for (the nearest first); returns how many.
    /// </summary>
    public int Board(IEnumerable<Settler> units, Ship ship)
    {
        if (!_ships.Contains(ship) || !IsNearLand(ship))
            return 0;
        var list = units.Where(s => s.Player == ship.Player && CanBoard(s) && !(s.Profession == Profession.Priest && _world.Magic.IsBusy(s)))
            .Distinct().ToList();
        int room = Capacity(ship) - ship.Aboard.Count - _boarding.Values.Count(b => b.Ship == ship && !list.Contains(b.Settler));
        var shore = ShoreAround(ship).ToList();
        int going = 0;
        foreach (var unit in list.OrderBy(s => PathFinder.Distance(s.Position, ship.Position)))
        {
            if (going >= room)
                break;
            // The free field nearest the ship this unit can get to (taken ones after: the unit steps on as soon as it is near).
            var spot = shore.Where(p => _world.PathFinder!.AreConnected(unit.Position, p, null))
                .OrderBy(p => _world.IsOccupied(p) || _boarding.Values.Any(b => b.Spot == p) ? 1 : 0)
                .Cast<GridPoint?>().FirstOrDefault();
            if (spot is not { } to)
                continue;
            if (unit.Profession == Profession.Soldier)
            {
                _world.Military.Move([unit], to, forced: true);
                to = _world.Military.ForcedMoveTarget(unit) ?? to;
            }
            else
            {
                StopJobs(unit);
                if (unit.Position != to && !_world.WalkTo(unit, to))
                    continue;
            }
            _boarding[unit.Id] = new Boarding(unit, ship, to);
            going++;
        }
        return going;
    }

    /// <summary>
    /// Puts everybody on board ashore: the ship (lying still) stops, the units stand on the free walkable fields nearest it
    /// (on the shore within <see cref="ShippingRules.LandingDistance"/> and on from there). Returns how many went ashore
    /// (0: no land near, or nobody on board).
    /// </summary>
    public int Unload(Ship ship)
    {
        if (!_ships.Contains(ship) || ship.Aboard.Count == 0 || ship.StepTicks > 0)
            return 0;
        if (ShoreAround(ship).Where(p => !_world.IsOccupied(p)).Cast<GridPoint?>().FirstOrDefault() is not { } landing)
            return 0;
        ship.Path.Clear();
        ship.Destination = null;
        int ashore = 0;
        foreach (var spot in FreeLandFrom(landing).Take(ship.Aboard.Count).ToList())
        {
            var unit = ship.Aboard[0];
            unit.IsInside = false;
            if (!_world.Teleport(unit, spot))
            {
                unit.IsInside = true;
                break;
            }
            ship.Aboard.RemoveAt(0);
            unit.Facing = Towards(ship.Position, spot);
            ashore++;
        }
        return ashore;
    }

    /// <summary>Units near their ship go on board; those given another order (or whose ship left) are no longer boarding.</summary>
    private void Embark()
    {
        foreach (var boarding in _boarding.Values.ToList())
        {
            var (unit, ship) = (boarding.Settler, boarding.Ship);
            if (unit.IsDead || unit.IsInside || unit.Player != ship.Player || !_ships.Contains(ship))
            {
                _boarding.Remove(unit.Id);
                continue;
            }
            bool near = !ship.IsMoving && PathFinder.Distance(unit.Position, ship.Position) <= Rules.LandingDistance;
            if (unit.State != SettlerState.Stepping && near)
            {
                _boarding.Remove(unit.Id);
                if (ship.Aboard.Count < Capacity(ship))
                    GoAboard(unit, ship);
                continue;
            }
            bool onTheWay = unit.Profession == Profession.Soldier
                ? _world.Military.ForcedMoveTarget(unit) == boarding.Spot
                : unit.Destination == boarding.Spot;
            if (!onTheWay && unit.State != SettlerState.Stepping)
                _boarding.Remove(unit.Id);
        }
    }

    private void GoAboard(Settler unit, Ship ship)
    {
        if (unit.Profession == Profession.Soldier)
            _world.Military.Release(unit);
        else
            StopJobs(unit);
        unit.HasTask = false;
        unit.Activity = Activity.None;
        unit.IsWorking = false;
        _world.EnterBuildingAt(unit, ship.Position);
        ship.Aboard.Add(unit);
    }

    /// <summary>The geologist, scout or pioneer leaves what he was sent to do.</summary>
    private void StopJobs(Settler unit)
    {
        _world.Geology.Stop(unit);
        _world.Theft.Stop(unit);
        _world.Pioneering.Stop(unit);
    }

    /// <summary>Walkable fields (shore, piers) within <see cref="ShippingRules.LandingDistance"/> of the ship, nearest first.</summary>
    private IEnumerable<GridPoint> ShoreAround(Ship ship)
    {
        var map = _world.Map!;
        return PathFinder.Circle(ship.Position, Rules.LandingDistance)
            .Where(p => map.Contains(p) && map.IsWalkable(p))
            .OrderBy(p => PathFinder.Distance(ship.Position, p)).ThenBy(p => p.Y).ThenBy(p => p.X);
    }

    /// <summary>Free walkable fields from <paramref name="start"/> on, nearest first (over walkable ground).</summary>
    private IEnumerable<GridPoint> FreeLandFrom(GridPoint start)
    {
        var map = _world.Map!;
        var seen = new HashSet<GridPoint> { start };
        var queue = new Queue<GridPoint>();
        queue.Enqueue(start);
        while (queue.Count > 0 && seen.Count < 4096)
        {
            var p = queue.Dequeue();
            if (!_world.IsOccupied(p))
                yield return p;
            for (int d = 0; d < DirectionExtensions.Count; d++)
                if (p.Neighbor((Direction)d) is var n && map.Contains(n) && map.IsWalkable(n) && seen.Add(n))
                    queue.Enqueue(n);
        }
    }

    private static Direction Towards(GridPoint from, GridPoint to)
    {
        var best = Direction.SouthWest;
        int bestDistance = int.MaxValue;
        for (int d = 0; d < DirectionExtensions.Count; d++)
            if (PathFinder.Distance(from.Neighbor((Direction)d), to) is var distance && distance < bestDistance)
                (best, bestDistance) = ((Direction)d, distance);
        return best;
    }

    /// <summary>A unit on its way to a ship, to the shore field <see cref="Spot"/>.</summary>
    private sealed class Boarding(Settler settler, Ship ship, GridPoint spot)
    {
        public Settler Settler { get; } = settler;
        public Ship Ship { get; } = ship;
        public GridPoint Spot { get; } = spot;
    }
}
