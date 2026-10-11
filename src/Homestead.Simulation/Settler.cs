namespace Homestead.Simulation;

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

    /// <summary>Remaining steps, for display and tests.</summary>
    public IEnumerable<Direction> RemainingPath => Path;

    /// <summary>Next step to take, if any.</summary>
    internal Direction? NextStep => Path.Count > 0 ? Path.Peek() : null;

    internal bool WantsToMove => State != SettlerState.Stepping && Path.Count > 0;
}
