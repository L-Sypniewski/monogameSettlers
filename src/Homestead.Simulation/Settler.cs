using Homestead.Simulation.Buildings;
using Homestead.Simulation.Economy;

namespace Homestead.Simulation;

public enum Profession
{
    /// <summary>No job assigned (e.g. scripted walkers).</summary>
    None,

    /// <summary>Transports goods between piles.</summary>
    Carrier,

    /// <summary>Levels the ground of construction sites.</summary>
    Digger,

    /// <summary>Builds construction sites from delivered material.</summary>
    Builder,

    /// <summary>Works in a building (see <see cref="Settler.Trade"/> and <see cref="Settler.Workplace"/>).</summary>
    Worker,
}

/// <summary>What a settler is doing on the spot, for animation.</summary>
public enum Activity
{
    None,
    PickingUp,
    Dropping,
    Digging,
    Building,
    Chopping,
    Mining,
    Planting,
}

public enum SettlerState
{
    /// <summary>Standing, nothing to do.</summary>
    Idle,

    /// <summary>In the middle of a step between two grid points.</summary>
    Stepping,

    /// <summary>Wants to step but the next field is taken.</summary>
    Waiting,
}

/// <summary>
/// A figure on the map that walks along paths. Movement and conflict handling are done by
/// <see cref="World"/>; this class only holds the state.
/// </summary>
public sealed class Settler
{
    internal readonly Queue<Direction> Path = new();

    internal Settler(int id, GridPoint position)
    {
        Id = id;
        Position = position;
    }

    public int Id { get; }

    /// <summary>The grid point the settler stands on, or leaves during a step.</summary>
    public GridPoint Position { get; internal set; }

    public Direction Facing { get; internal set; } = Direction.SouthWest;

    public SettlerState State { get; internal set; }

    /// <summary>Target of the current step (equals <see cref="Position"/> when not stepping).</summary>
    public GridPoint StepTarget => State == SettlerState.Stepping ? Position.Neighbor(Facing) : Position;

    public int TicksIntoStep { get; internal set; }

    /// <summary>Where the settler is walking to, or null when it has no order.</summary>
    public GridPoint? Destination { get; internal set; }

    /// <summary>Consecutive ticks spent waiting for a free field.</summary>
    public int WaitingTicks { get; internal set; }

    /// <summary>Tick of the last detour around a blocked field.</summary>
    internal long LastDetourTick { get; set; } = long.MinValue / 2;

    public Profession Profession { get; internal set; }

    /// <summary>For workers: the trade as named in the building data (e.g. LUMBERJACK, SAWMILLER).</summary>
    public string? Trade { get; internal set; }

    public Building? Workplace { get; internal set; }

    /// <summary>Inside a building: not drawn, not occupying a field.</summary>
    public bool IsInside { get; internal set; }

    /// <summary>Carriers take transport jobs from the logistics system.</summary>
    public bool IsCarrier => Profession == Profession.Carrier;

    /// <summary>Animation hint while <see cref="IsWorking"/>.</summary>
    public Activity Activity { get; internal set; }

    /// <summary>Ticks spent in the current activity, for animation.</summary>
    public int ActivityTicks { get; internal set; }

    /// <summary>The good in the settler's hands, if any.</summary>
    public Material? Carrying { get; internal set; }

    /// <summary>Busy on the spot (e.g. picking something up): others must not push it aside.</summary>
    public bool IsWorking { get; internal set; }

    /// <summary>
    /// Has a job (transport, construction). Such settlers are not pushed aside when idle for a
    /// moment: they may just have arrived and their job has not reacted yet.
    /// </summary>
    public bool HasTask { get; internal set; }

    /// <summary>Remaining steps, for display and tests.</summary>
    public IEnumerable<Direction> RemainingPath => Path;

    /// <summary>Next step to take, if any.</summary>
    internal Direction? NextStep => Path.Count > 0 ? Path.Peek() : null;

    internal bool WantsToMove => State != SettlerState.Stepping && Path.Count > 0;
}
