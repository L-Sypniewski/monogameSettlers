using Homestead.Simulation.Buildings;

namespace Homestead.Simulation.Economy;

/// <summary>
/// Finished living houses release new carriers through their door, one per
/// <see cref="ProductionConfig.ResidentIntervalTicks"/>, until the house's number of residents
/// (<see cref="ProductionConfig.Residents"/>) has come out. Each newcomer walks out of the way at once,
/// so the door stays free for the next.
/// </summary>
public sealed class Housing
{
    private readonly World _world;
    private readonly ProductionConfig _config;

    /// <summary>Residents already released per house (by building id).</summary>
    private readonly Dictionary<int, int> _released = new();
    private readonly Dictionary<int, long> _lastRelease = new();

    internal Housing(World world, ProductionConfig config)
    {
        _world = world;
        _config = config;
    }

    public int Released { get; private set; }

    /// <summary>Residents the house has yet to release (null if it is no living house).</summary>
    public int? ResidentsLeft(Building house) => _config.Residents.TryGetValue(house.Definition.Name, out int total)
        ? total - _released.GetValueOrDefault(house.Id)
        : null;

    /// <summary>The house already has its residents (houses of a loaded map: the map lists its settlers itself).</summary>
    public void MarkPopulated(Building house)
    {
        if (_config.Residents.TryGetValue(house.Definition.Name, out int total))
            _released[house.Id] = total;
    }

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
            if (_world.Spawn(house.Door, Profession.Carrier) is not { } resident)
                continue;
            _released[house.Id] = _released.GetValueOrDefault(house.Id) + 1;
            _lastRelease[house.Id] = _world.Tick;
            Released++;
            _world.Idling.StrollAway(resident);
        }
    }
}
