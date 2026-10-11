using Homestead.Simulation.Map;
using Homestead.Simulation.Pathfinding;

namespace Homestead.Simulation;

/// <summary>
/// Simulation state, advanced in fixed ticks independent of the frame rate.
/// </summary>
/// <remarks>
/// Settler movement: each settler owns the field it stands on and reserves the field it steps to.
/// When the next field is taken, conflicts are resolved in this order, so nobody can get stuck:
/// <list type="number">
/// <item>Two settlers wanting each other's field swap places.</item>
/// <item>An idle settler in the way is asked to step aside.</item>
/// <item>After <see cref="ReplanAfterTicks"/> the waiting settler looks for a detour around the field.</item>
/// <item>After <see cref="PassThroughAfterTicks"/> it walks through (watchdog; counted in <see cref="PassThroughs"/>).</item>
/// </list>
/// </remarks>
public sealed class World
{
    public const int TicksPerSecond = 20;

    /// <summary>Ticks for one grid step (0.4 s).</summary>
    public const int StepTicks = 8;

    public const int ReplanAfterTicks = 6;
    public const int PassThroughAfterTicks = 40;

    /// <summary>A detour may be at most this many steps longer than the blocked path.</summary>
    public const int MaxDetourSteps = 12;

    private const int NoOccupant = -1;

    private readonly List<Settler> _settlers = new();
    private readonly int[] _occupant;

    public World(GameMap? map = null)
    {
        Map = map;
        if (map is not null)
        {
            PathFinder = new PathFinder(map);
            _occupant = new int[map.PointCount];
            Array.Fill(_occupant, NoOccupant);
        }
        else
        {
            _occupant = [];
        }
    }

    public static TimeSpan TickDuration { get; } = TimeSpan.FromSeconds(1.0 / TicksPerSecond);

    public GameMap? Map { get; }

    public PathFinder? PathFinder { get; }

    /// <summary>Scripted walkers (walk PoC), independent of the map.</summary>
    public List<Walker> Walkers { get; } = new();

    public IReadOnlyList<Settler> Settlers => _settlers;

    public long Tick { get; private set; }

    /// <summary>How often the watchdog had to let a settler walk through another one.</summary>
    public int PassThroughs { get; private set; }

    /// <summary>How often a waiting settler switched to a detour.</summary>
    public int Detours { get; private set; }

    public void Step()
    {
        foreach (var walker in Walkers)
            walker.Tick();

        foreach (var settler in _settlers)
            if (settler.State == SettlerState.Stepping)
                AdvanceStep(settler);

        foreach (var settler in _settlers)
            if (settler.WantsToMove)
                TryStartStep(settler);

        // After a pass-through a settler may stand on a field it does not own; claim it once free.
        if (Map is not null)
            foreach (var settler in _settlers)
                if (settler.State != SettlerState.Stepping && _occupant[Map.Index(settler.Position)] == NoOccupant)
                    _occupant[Map.Index(settler.Position)] = settler.Id;

        Tick++;
    }

    /// <summary>Places a new settler on the nearest free walkable point.</summary>
    public Settler? Spawn(GridPoint near)
    {
        var map = RequireMap();
        var free = NearestFree(near, radius: 10);
        if (free is not { } position)
            return null;
        var settler = new Settler(_settlers.Count, position);
        _settlers.Add(settler);
        _occupant[map.Index(position)] = settler.Id;
        return settler;
    }

    /// <summary>Orders a settler to walk somewhere. Returns false if the target cannot be reached.</summary>
    public bool WalkTo(Settler settler, GridPoint destination)
    {
        // Plan from where the settler will be standing next.
        var from = settler.StepTarget;
        var path = PathFinder!.FindPath(from, destination);
        if (path is null)
            return false;

        settler.Path.Clear();
        foreach (var step in path)
            settler.Path.Enqueue(step);
        // Already there (or arriving with the current step): nothing left to do.
        settler.Destination = path.Count > 0 || settler.State == SettlerState.Stepping ? destination : null;
        settler.WaitingTicks = 0;
        if (settler.State == SettlerState.Waiting)
            settler.State = SettlerState.Idle;
        return true;
    }

    private void AdvanceStep(Settler settler)
    {
        if (++settler.TicksIntoStep < StepTicks)
            return;

        var map = Map!;
        var from = settler.Position;
        settler.Position = from.Neighbor(settler.Facing);
        settler.TicksIntoStep = 0;
        settler.State = SettlerState.Idle;
        if (_occupant[map.Index(from)] == settler.Id)
            _occupant[map.Index(from)] = NoOccupant;
        // After a pass-through the field may still belong to the other settler; take it once free.
        if (_occupant[map.Index(settler.Position)] == NoOccupant)
            _occupant[map.Index(settler.Position)] = settler.Id;

        if (settler.Path.Count == 0)
            settler.Destination = null;
    }

    private void TryStartStep(Settler settler)
    {
        var map = Map!;
        var direction = settler.NextStep!.Value;
        var next = settler.Position.Neighbor(direction);

        // The world may have changed since planning (tree planted, building placed).
        if (!map.IsWalkable(next))
        {
            Replan(settler, avoid: null);
            return;
        }

        int occupantId = _occupant[map.Index(next)];
        if (occupantId == NoOccupant || occupantId == settler.Id)
        {
            StartStep(settler, direction);
            return;
        }

        var other = _settlers[occupantId];
        if (other.WantsToMove && other.Position.Neighbor(other.NextStep!.Value) == settler.Position)
        {
            Swap(settler, other);
            return;
        }

        if (other.State == SettlerState.Idle && other.Path.Count == 0)
            StepAside(other, keepFree: settler.Position);

        Wait(settler, next);
    }

    private void StartStep(Settler settler, Direction direction)
    {
        var map = Map!;
        settler.Path.Dequeue();
        settler.Facing = direction;
        settler.State = SettlerState.Stepping;
        settler.TicksIntoStep = 0;
        settler.WaitingTicks = 0;
        var target = settler.Position.Neighbor(direction);
        if (_occupant[map.Index(target)] == NoOccupant)
            _occupant[map.Index(target)] = settler.Id;
    }

    /// <summary>Head-on: both start their step at once and hand over their fields.</summary>
    private void Swap(Settler a, Settler b)
    {
        var map = Map!;
        int fieldA = map.Index(a.Position), fieldB = map.Index(b.Position);
        StartStep(a, a.NextStep!.Value);
        StartStep(b, b.NextStep!.Value);
        _occupant[fieldA] = b.Id;
        _occupant[fieldB] = a.Id;
    }

    private void Wait(Settler settler, GridPoint blocked)
    {
        settler.State = SettlerState.Waiting;
        settler.WaitingTicks++;

        if (settler.WaitingTicks == ReplanAfterTicks)
        {
            Replan(settler, avoid: new HashSet<int> { Map!.Index(blocked) });
        }
        else if (settler.WaitingTicks >= PassThroughAfterTicks)
        {
            PassThroughs++;
            StartStep(settler, settler.NextStep!.Value);
        }
    }

    private void Replan(Settler settler, IReadOnlySet<int>? avoid)
    {
        if (settler.Destination is not { } destination)
        {
            settler.Path.Clear();
            return;
        }

        var path = PathFinder!.FindPath(settler.Position, destination, avoid);
        if (path is null)
        {
            // No way around: keep waiting (the watchdog will push through) unless the
            // destination itself became unreachable.
            if (avoid is null)
            {
                settler.Path.Clear();
                settler.Destination = null;
                settler.State = SettlerState.Idle;
            }
            return;
        }
        if (avoid is not null && path.Count > settler.Path.Count + MaxDetourSteps)
            return;

        if (avoid is not null)
            Detours++;
        settler.Path.Clear();
        foreach (var step in path)
            settler.Path.Enqueue(step);
    }

    /// <summary>Moves an idle settler one step to a free neighbor, preferring one away from <paramref name="keepFree"/>.</summary>
    private void StepAside(Settler settler, GridPoint keepFree)
    {
        var map = Map!;
        GridPoint? best = null;
        int bestDistance = -1;
        for (int d = 0; d < DirectionExtensions.Count; d++)
        {
            var candidate = settler.Position.Neighbor((Direction)d);
            if (candidate == keepFree || !map.IsWalkable(candidate) || _occupant[map.Index(candidate)] != NoOccupant)
                continue;
            int distance = PathFinder.Distance(candidate, keepFree);
            if (distance > bestDistance)
            {
                best = candidate;
                bestDistance = distance;
            }
        }
        if (best is { } target)
            WalkTo(settler, target);
    }

    private GridPoint? NearestFree(GridPoint near, int radius)
    {
        var map = RequireMap();
        for (int r = 0; r <= radius; r++)
        for (int dy = -r; dy <= r; dy++)
        for (int dx = -r; dx <= r; dx++)
        {
            var p = new GridPoint(near.X + dx, near.Y + dy);
            if (PathFinder.Distance(near, p) == r && map.IsWalkable(p) && _occupant[map.Index(p)] == NoOccupant)
                return p;
        }
        return null;
    }

    private GameMap RequireMap() => Map ?? throw new InvalidOperationException("This world has no map.");
}
