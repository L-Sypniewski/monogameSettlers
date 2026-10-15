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

    /// <summary>Fights and occupies military buildings (see <see cref="Settler.SoldierType"/>).</summary>
    Soldier,

    /// <summary>Searches mountains for resources and marks them with signs (see <see cref="Economy.Geology"/>).</summary>
    Geologist,

    /// <summary>Comes from the big temple and casts its people's spell (see <see cref="Magic.Magic"/>).</summary>
    Priest,
}

public enum SoldierType
{
    Swordsman,
    Pikeman,
    Bowman,
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
    Sowing,
    Harvesting,
    Fishing,
    DrawingWater,
    Fighting,
    Shooting,
    Surveying,
    Signing,
    Casting,
    Healing,
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

    /// <summary>The player the settler belongs to (0-based, as in the original maps).</summary>
    public int Player { get; internal set; }

    public SoldierType? SoldierType { get; internal set; }

    /// <summary>Soldiers: health left (see <c>data/military.json</c>); 0 for everyone else.</summary>
    public float Health { get; internal set; }

    public bool IsDead { get; internal set; }

    /// <summary>Soldier rank 1-3.</summary>
    public int Level { get; internal set; } = 1;

    /// <summary>
    /// The people the settler looks like when it differs from its player's: priests (their temple's) and
    /// soldiers won over by a spell (they keep their own people, only the color changes).
    /// </summary>
    public Civilization? People { get; internal set; }

    /// <summary>Frozen by a spell until this tick: neither walks nor fights.</summary>
    public long FrozenUntil { get; internal set; }

    /// <summary>Strikes faster (spell) until this tick.</summary>
    public long HastedUntil { get; internal set; }

    /// <summary>How many times as fast it strikes while hasted.</summary>
    internal double HasteFactor { get; set; } = 1;

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

    /// <summary>An idle carrier walking out of the way (see <see cref="Economy.Idling"/>); free for any job.</summary>
    public bool IsStrolling { get; internal set; }

    /// <summary>Idle and free for a job: standing around, or strolling out of the way.</summary>
    /// <summary>Becomes a specialist once its current delivery is done: no new tasks meanwhile.</summary>
    internal bool ConversionPending { get; set; }

    /// <summary>A carrier with nothing to do (may be strolling out of the way): free for a new task.</summary>
    public bool IsAvailableCarrier => IsCarrier && !HasTask && Carrying is null && !ConversionPending
                                        && (IsStrolling || Destination is null && State != SettlerState.Stepping);

    /// <summary>
    /// On strike for lack of living space (see <see cref="Economy.Housing"/>): a carrier takes no transport
    /// jobs, a worker stays in his building.
    /// </summary>
    public bool IsStriking { get; internal set; }

    /// <summary>The tool a specialist took up when it was recruited (put down again if it becomes a carrier).</summary>
    public Material? Tool { get; internal set; }

    /// <summary>Bowmen: tick of the last shot and ticks until the next, for the shooting animation.</summary>
    public long LastShotTick { get; internal set; } = long.MinValue / 2;

    public int ShotTicks { get; internal set; } = 1;

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
