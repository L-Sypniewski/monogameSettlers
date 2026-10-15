using Homestead.Simulation.Buildings;

namespace Homestead.Simulation.Economy;

/// <summary>
/// Living houses and living space. Finished living houses release new carriers through their door, one
/// per <see cref="ProductionConfig.ResidentIntervalTicks"/>, until the house's number of residents
/// (<see cref="ProductionConfig.Residents"/>) has come out. Each newcomer walks out of the way at once,
/// so the door stays free for the next.
/// <para>
/// Each house gives room to the residents it has released; the start population of a loaded map has
/// room too (<see cref="SettleStartPopulation"/>). Carriers and workers need room, soldiers and
/// geologists do not. If a player has more of them than room, as many go on strike: idle carriers first
/// (they take no more jobs), then workers (they stay in their building once back, it stops working).
/// </para>
/// </summary>
public sealed class Housing
{
    /// <summary>How often the strikes are brought up to date.</summary>
    private const int StrikeInterval = World.TicksPerSecond;

    private readonly World _world;
    private readonly ProductionConfig _config;

    /// <summary>Residents already released per house (by building id).</summary>
    private readonly Dictionary<int, int> _released = new();
    private readonly Dictionary<int, long> _lastRelease = new();

    /// <summary>Room for the start population per player; null until it is settled (no strikes before).</summary>
    private Dictionary<int, int>? _startRoom;

    private readonly HashSet<Settler> _strikers = new();

    internal Housing(World world, ProductionConfig config)
    {
        _world = world;
        _config = config;
    }

    public int Released { get; private set; }

    /// <summary>Residents the house has yet to release (null if it is no living house).</summary>
    public int? ResidentsLeft(Building house) => Residents(house) is { } total
        ? total - _released.GetValueOrDefault(house.Id)
        : null;

    /// <summary>Residents the house releases in all (null if it is no living house); more for hardcore players.</summary>
    private int? Residents(Building house) => _config.Residents.TryGetValue(house.Definition.Name, out int total)
        ? _world.IsHardcore(house.Player) ? Hardcore.Residents(total) : total
        : null;

    /// <summary>Whether the building is a living house.</summary>
    public bool IsLivingHouse(Building building) => _config.Residents.ContainsKey(building.Definition.Name);

    /// <summary>The room the house gives: the residents it has released so far.</summary>
    public int RoomOf(Building house) => IsLivingHouse(house) ? _released.GetValueOrDefault(house.Id) : 0;

    /// <summary>The house already has its residents (houses of a loaded map: the map lists its settlers itself).</summary>
    public void MarkPopulated(Building house)
    {
        if (Residents(house) is { } total)
            _released[house.Id] = total;
    }

    /// <summary>
    /// Gives every player room for the settlers they have now beyond what their houses hold, and from
    /// then on lets settlers without room go on strike. Called once a map is loaded.
    /// </summary>
    public void SettleStartPopulation()
    {
        _startRoom = _world.Settlers.Where(NeedsRoom).GroupBy(s => s.Player)
            .ToDictionary(g => g.Key, g => Math.Max(0, g.Count() - HouseRoom(g.Key)));
    }

    /// <summary>Settlers that need living space: carriers and workers (also diggers and builders).</summary>
    public static bool NeedsRoom(Settler settler) =>
        !settler.IsDead && settler.Profession is Profession.Carrier or Profession.Worker or Profession.Digger or Profession.Builder;

    private int HouseRoom(int player) =>
        _world.Buildings.Where(b => b.Exists && b.Player == player).Sum(RoomOf);

    /// <summary>The player's living space (null while strikes are off).</summary>
    public int? Room(int player) => _startRoom is null ? null : _startRoom.GetValueOrDefault(player) + HouseRoom(player);

    /// <summary>The player's settlers that need living space.</summary>
    public int Needing(int player) => _world.Settlers.Count(s => s.Player == player && NeedsRoom(s));

    /// <summary>Settlers of the player on strike.</summary>
    public int Striking(int player) => _strikers.Count(s => s.Player == player);

    internal void Tick()
    {
        foreach (var house in _world.Buildings)
        {
            if (!house.Exists || house.State != BuildingState.Finished || ResidentsLeft(house) is not > 0)
                continue;
            if (_world.Tick - _lastRelease.GetValueOrDefault(house.Id, long.MinValue / 2) < _config.ResidentIntervalTicks)
                continue;
            if (_world.IsOccupied(house.Door))
                continue; // the previous one has not left the doorstep yet
            if (_world.Spawn(house.Door, Profession.Carrier, house.Player) is not { } resident)
                continue;
            _released[house.Id] = _released.GetValueOrDefault(house.Id) + 1;
            _lastRelease[house.Id] = _world.Tick;
            Released++;
            _world.Idling.StrollAway(resident);
        }

        if (_world.Tick % StrikeInterval == 0)
            UpdateStrikes();
    }

    /// <summary>Brings the number of strikers per player in line with the missing room.</summary>
    private void UpdateStrikes()
    {
        // Strikers who changed (became soldiers, died, were taken for a job) no longer strike.
        foreach (var striker in _strikers.Where(s => !CanStrike(s)).ToList())
            EndStrike(striker);
        if (_startRoom is null)
            return;

        foreach (var player in _world.Settlers.Where(NeedsRoom).Select(s => s.Player).Distinct().ToList())
        {
            int missing = Math.Max(0, Needing(player) - Room(player)!.Value);
            var mine = _strikers.Where(s => s.Player == player).ToList();
            // Workers return to work first.
            foreach (var striker in mine.OrderBy(s => s.IsCarrier).Take(Math.Max(0, mine.Count - missing)))
                EndStrike(striker);
            int more = missing - mine.Count;
            if (more <= 0)
                continue;
            var candidates = _world.Settlers
                .Where(s => s.Player == player && !s.IsStriking && CanStrike(s))
                .OrderBy(s => s.IsCarrier ? 0 : 1)
                .Take(more);
            foreach (var settler in candidates.ToList())
            {
                settler.IsStriking = true;
                settler.ActivityTicks = 0; // the strike animation starts over
                _strikers.Add(settler);
            }
        }
    }

    /// <summary>Idle carriers and workers with a building may strike (a worker stops once he is back inside).</summary>
    private static bool CanStrike(Settler s) =>
        !s.IsDead && (s.IsAvailableCarrier || s.Profession == Profession.Worker && s.Workplace is { Exists: true });

    private void EndStrike(Settler striker)
    {
        striker.IsStriking = false;
        _strikers.Remove(striker);
    }
}
