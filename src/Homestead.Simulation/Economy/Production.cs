using Homestead.Simulation.Buildings;
using Homestead.Simulation.Map;
using Homestead.Simulation.Pathfinding;

namespace Homestead.Simulation.Economy;

/// <summary>
/// Workers in finished buildings: workshops turn inputs into outputs, gatherers cut trees, cut stone
/// and plant trees around their building. Also lets planted trees grow.
/// </summary>
/// <remarks>
/// A finished building with a recipe calls the nearest idle carrier, who becomes its worker.
/// Workshop cycle: leave, fetch each input from the input piles, go in, work, bring the output to
/// the output pile, go in. Gatherer cycle: rest inside, walk to a target in the work radius, work
/// there, bring the result (if any) to the output pile, go in.
/// Watchdog: a worker that makes no progress on a walk for <see cref="StallTicks"/> gives up the
/// current step (dropping what it carries); a target it could not reach is skipped for a while.
/// If the building disappears, the worker becomes a carrier again.
/// </remarks>
public sealed class Production
{
    public const int StallTicks = 400;
    public const int HandleTicks = 8;
    private const int RetryTicks = 40;
    private const int UnreachableForgetTicks = 2000;
    private const int DefaultRadius = 12;

    private readonly World _world;
    private readonly ProductionConfig _config;
    private readonly Dictionary<int, Workplace> _workplaces = new();
    private readonly HashSet<int> _reservedTargets = new();
    private readonly Dictionary<int, long> _unreachableUntil = new();
    private readonly Dictionary<int, long> _growing = new();

    /// <summary>Stones already taken at the current size of a quarry stone (see StonesPerCut).</summary>
    private readonly Dictionary<int, int> _stoneCuts = new();
    private readonly Dictionary<string, int> _problems = new();
    private readonly Random _random = new(12345);

    internal Production(World world, ProductionConfig config)
    {
        _world = world;
        _config = config;
    }

    public ProductionConfig Config => _config;

    public int Produced { get; private set; }
    public int TreesCut { get; private set; }
    public int StonesCut { get; private set; }
    public int TreesPlanted { get; private set; }
    public int TreesGrown { get; private set; }

    public IReadOnlyDictionary<string, int> Problems => _problems;

    public Settler? WorkerOf(Building building) => _workplaces.GetValueOrDefault(building.Id)?.Worker;

    internal void Tick()
    {
        if (_world.Tick % 10 == 0)
        {
            DiscoverWorkplaces();
            GrowTrees();
        }
        foreach (var workplace in _workplaces.Values.ToList())
            Advance(workplace);
    }

    private void DiscoverWorkplaces()
    {
        foreach (var building in _world.Buildings)
        {
            if (!building.Exists || building.State != BuildingState.Finished || _workplaces.ContainsKey(building.Id))
                continue;
            string name = building.Definition.Name;
            if (_config.Workshops.TryGetValue(name, out var workshop))
                _workplaces[building.Id] = new Workplace(building, workshop, null);
            else if (_config.Gatherers.TryGetValue(name, out var gatherer))
                _workplaces[building.Id] = new Workplace(building, null, gatherer);
        }
    }

    private void GrowTrees()
    {
        foreach (var (index, ready) in _growing.ToList())
        {
            if (_world.Tick < ready)
                continue;
            _growing.Remove(index);
            var p = _world.Map!.Point(index);
            if (_world.Map.ObjectCodeAt(p) != MapObjectCodes.Sapling)
                continue;
            byte code = (byte)(MapObjectCodes.FirstTree + _random.Next(MapObjectCodes.LastTree - MapObjectCodes.FirstTree + 1));
            _world.Map.SetObjectCode(p, code);
            TreesGrown++;
        }
    }

    private void Advance(Workplace w)
    {
        if (!w.Building.Exists)
        {
            Dismiss(w);
            return;
        }

        var worker = w.Worker;
        switch (w.Phase)
        {
            case Phase.NeedsWorker:
                Recruit(w);
                break;

            case Phase.WorkerComing:
                if (Arrived(w, out bool stalledComing))
                {
                    worker!.Profession = Profession.Worker;
                    worker.Trade = w.Building.Definition.Worker;
                    worker.Workplace = w.Building;
                    worker.Carrying = null;
                    _world.EnterBuilding(worker);
                    Rest(w, 0);
                }
                else if (stalledComing)
                {
                    Report("recruit could not reach the door");
                    ReleaseWorker(w);
                }
                break;

            case Phase.Inside:
                if (--w.TicksLeft > 0)
                    break;
                if (w.Workshop is { } recipe)
                    StartWorkshopCycle(w, recipe);
                else
                    StartGathering(w, w.Gatherer!);
                break;

            case Phase.ToPile:
                if (Arrived(w, out bool stalledPile))
                    BeginHandling(w, Activity.PickingUp, Phase.PickingUp);
                else if (stalledPile)
                    GiveUp(w, "no progress to an input pile");
                break;

            case Phase.PickingUp:
                if (--w.TicksLeft > 0)
                    break;
                StopActivity(worker!);
                var pile = w.Inputs.Dequeue();
                if (!_world.HasPile(pile) || pile.Count == 0 || pile.Material is not { } material)
                {
                    GiveUp(w, "input gone");
                    break;
                }
                pile.Count--;
                pile.ResetMaterialIfEmpty();
                worker!.Carrying = material;
                if (w.Inputs.Count > 0)
                    WalkTo(w, w.Inputs.Peek().Position, Phase.ToPile);
                else
                    WalkTo(w, w.Building.Door, Phase.ToDoorToWork);
                break;

            case Phase.ToDoorToWork:
                if (Arrived(w, out bool stalledWork))
                {
                    worker!.Carrying = null;
                    _world.EnterBuilding(worker);
                    w.Phase = Phase.Producing;
                    w.TicksLeft = w.Workshop!.Ticks;
                }
                else if (stalledWork)
                {
                    GiveUp(w, "no progress back to the workshop");
                }
                break;

            case Phase.Producing:
                if (--w.TicksLeft > 0)
                    break;
                Produced++;
                worker!.Carrying = w.Workshop!.Outputs[0];
                _world.LeaveBuilding(worker);
                DeliverOutput(w);
                break;

            case Phase.ToSpot:
                if (Arrived(w, out bool stalledSpot))
                {
                    worker!.Facing = w.Facing;
                    BeginHandling(w, ActivityOf(w.Gatherer!.Action), Phase.Gathering);
                    w.TicksLeft = w.Gatherer.WorkTicks;
                }
                else if (stalledSpot)
                {
                    _unreachableUntil[_world.Map!.Index(w.Target)] = _world.Tick + UnreachableForgetTicks;
                    GiveUp(w, "no progress to a work spot");
                }
                break;

            case Phase.Gathering:
                if (--w.TicksLeft > 0)
                    break;
                StopActivity(worker!);
                FinishGathering(w);
                break;

            case Phase.ToOutput:
                if (Arrived(w, out bool stalledOutput))
                    BeginHandling(w, Activity.Dropping, Phase.Dropping);
                else if (stalledOutput)
                    GiveUp(w, "no progress to the output pile");
                break;

            case Phase.Dropping:
                if (--w.TicksLeft > 0)
                    break;
                StopActivity(worker!);
                var output = w.Building.Piles.FirstOrDefault(p => p.Role == PileRole.Offer && p.Material == worker!.Carrying);
                if (output is not null && output.Count < output.Capacity)
                    output.Count++;
                else
                    _world.DropGoods(worker!.Position, worker.Carrying!.Value);
                worker!.Carrying = null;
                WalkTo(w, w.Building.Door, Phase.ToDoorToRest);
                break;

            case Phase.ToDoorToRest:
                if (Arrived(w, out bool stalledRest))
                {
                    _world.EnterBuilding(worker!);
                    Rest(w, w.Gatherer?.RestTicks ?? 0);
                }
                else if (stalledRest)
                {
                    // Cannot get home: give up the job entirely, the building calls a new worker.
                    Report("worker could not get back home");
                    DropCarried(worker!);
                    ReleaseWorker(w);
                }
                break;
        }
    }

    private void Recruit(Workplace w)
    {
        var door = w.Building.Door;
        var pathFinder = _world.PathFinder!;
        var recruit = _world.Settlers
            .Where(s => s.Profession == Profession.Carrier && !s.HasTask && s.Carrying is null && s.Destination is null
                        && s.State != SettlerState.Stepping && pathFinder.AreConnected(s.Position, door))
            .MinBy(s => PathFinder.Distance(s.Position, door));
        if (recruit is null)
            return;
        w.Worker = recruit;
        recruit.HasTask = true;
        if (!WalkTo(w, door, Phase.WorkerComing))
            ReleaseWorker(w);
    }

    private void StartWorkshopCycle(Workplace w, WorkshopRecipe recipe)
    {
        var piles = w.Building.Piles;
        var output = piles.FirstOrDefault(p => p.Role == PileRole.Offer && p.Material == recipe.Outputs[0]);
        if (output is null || output.Count >= output.Capacity)
        {
            w.TicksLeft = RetryTicks;
            return;
        }
        w.Inputs.Clear();
        foreach (var material in recipe.Inputs)
        {
            var pile = piles.FirstOrDefault(p => p.Role == PileRole.Request && p.Material == material && p.Count > 0);
            if (pile is null)
            {
                w.TicksLeft = RetryTicks;
                return;
            }
            w.Inputs.Enqueue(pile);
        }
        _world.LeaveBuilding(w.Worker!);
        if (!WalkTo(w, w.Inputs.Peek().Position, Phase.ToPile))
            GiveUp(w, "input pile unreachable");
    }

    private void StartGathering(Workplace w, GathererRecipe recipe)
    {
        var target = FindTarget(w, recipe.Action);
        if (target is not { } t)
        {
            w.TicksLeft = RetryTicks;
            return;
        }
        w.Target = t.Target;
        w.Facing = t.Facing;
        _reservedTargets.Add(_world.Map!.Index(t.Target));
        _world.LeaveBuilding(w.Worker!);
        if (!WalkTo(w, t.Spot, Phase.ToSpot))
        {
            _unreachableUntil[_world.Map.Index(t.Target)] = _world.Tick + UnreachableForgetTicks;
            GiveUp(w, "work spot unreachable");
        }
    }

    private void FinishGathering(Workplace w)
    {
        var map = _world.Map!;
        _reservedTargets.Remove(map.Index(w.Target));
        byte code = map.ObjectCodeAt(w.Target);
        var worker = w.Worker!;
        switch (w.Gatherer!.Action)
        {
            case GatherAction.CutTree when MapObjectCodes.IsAdultTree(code):
                map.SetObjectCode(w.Target, MapObjectCodes.TreeStump);
                TreesCut++;
                worker.Carrying = Material.Trunk;
                DeliverOutput(w);
                return;
            case GatherAction.CutStone when MapObjectCodes.StoneLeft(code) > 0:
                // Each of the 13 sizes yields StonesPerCut stones before the rock shrinks.
                int index = map.Index(w.Target);
                int cuts = _stoneCuts.GetValueOrDefault(index) + 1;
                if (cuts >= _config.StonesPerCut)
                {
                    map.SetObjectCode(w.Target, (byte)(code + 1));
                    _stoneCuts.Remove(index);
                }
                else
                {
                    _stoneCuts[index] = cuts;
                }
                StonesCut++;
                worker.Carrying = Material.Stone;
                DeliverOutput(w);
                return;
            case GatherAction.PlantTree when CanPlant(w.Target):
                map.SetObjectCode(w.Target, MapObjectCodes.Sapling);
                _growing[map.Index(w.Target)] = _world.Tick + _config.TreeGrowTicks;
                TreesPlanted++;
                break;
            default:
                Report("target gone before the work was done");
                break;
        }
        WalkTo(w, w.Building.Door, Phase.ToDoorToRest);
    }

    private void DeliverOutput(Workplace w)
    {
        var output = w.Building.Piles.FirstOrDefault(p => p.Role == PileRole.Offer && p.Material == w.Worker!.Carrying);
        if (output is null)
        {
            DropCarried(w.Worker!);
            WalkTo(w, w.Building.Door, Phase.ToDoorToRest);
            return;
        }
        if (!WalkTo(w, output.Position, Phase.ToOutput))
            GiveUp(w, "output pile unreachable");
    }

    /// <summary>
    /// Nearest usable target in the work radius: an adult tree with a free neighbor to stand on,
    /// a quarry stone with a reachable spot two fields away (its ring is blocked), or a free spot to
    /// plant on (randomized a little so the forest spreads).
    /// </summary>
    private (GridPoint Target, GridPoint Spot, Direction Facing)? FindTarget(Workplace w, GatherAction action)
    {
        var map = _world.Map!;
        var pathFinder = _world.PathFinder!;
        var center = w.Building.Position;
        var door = w.Building.Door;
        int radius = w.Building.Definition.WorkRadius ?? DefaultRadius;
        var candidates = new List<(GridPoint Target, GridPoint Spot, Direction Facing, int Distance)>();

        for (int dy = -radius; dy <= radius; dy++)
        for (int dx = -radius; dx <= radius; dx++)
        {
            var p = new GridPoint(center.X + dx, center.Y + dy);
            if (!map.Contains(p) || PathFinder.Distance(center, p) > radius)
                continue;
            int index = map.Index(p);
            if (_reservedTargets.Contains(index) || _unreachableUntil.TryGetValue(index, out long until) && until > _world.Tick)
                continue;

            bool fits = action switch
            {
                GatherAction.CutTree => MapObjectCodes.IsAdultTree(map.ObjectCodeAt(p)),
                GatherAction.CutStone => MapObjectCodes.StoneLeft(map.ObjectCodeAt(p)) > 0,
                GatherAction.PlantTree => CanPlant(p),
                _ => false,
            };
            if (!fits)
                continue;

            int reach = action == GatherAction.CutStone ? 2 : 1;
            var spot = SpotNear(p, reach, door);
            if (spot is not { } s || !pathFinder.AreConnected(door, s))
                continue;
            candidates.Add((p, s, FacingTowards(s, p), PathFinder.Distance(door, s)));
        }

        if (candidates.Count == 0)
            return null;
        if (action == GatherAction.PlantTree)
        {
            // Pick among the nearer half at random, so the forester does not plant a single line.
            var near = candidates.OrderBy(c => c.Distance).Take(Math.Max(1, candidates.Count / 2)).ToList();
            var pick = near[_random.Next(near.Count)];
            return (pick.Target, pick.Spot, pick.Facing);
        }
        var best = candidates.MinBy(c => c.Distance);
        return (best.Target, best.Spot, best.Facing);
    }

    /// <summary>A walkable point exactly <paramref name="distance"/> steps from the target, nearest to <paramref name="from"/>.</summary>
    private GridPoint? SpotNear(GridPoint target, int distance, GridPoint from)
    {
        var map = _world.Map!;
        GridPoint? best = null;
        int bestDistance = int.MaxValue;
        for (int dy = -distance; dy <= distance; dy++)
        for (int dx = -distance; dx <= distance; dx++)
        {
            var p = new GridPoint(target.X + dx, target.Y + dy);
            if (PathFinder.Distance(target, p) != distance || !map.IsWalkable(p) || !_world.IsFreeGround(p))
                continue;
            int d = PathFinder.Distance(from, p);
            if (d < bestDistance)
            {
                best = p;
                bestDistance = d;
            }
        }
        return best;
    }

    /// <summary>Free grass, not near buildings or piles, and no tree right next to it (keeps paths open).</summary>
    private bool CanPlant(GridPoint p)
    {
        var map = _world.Map!;
        if (!map.Contains(p) || !map.IsWalkable(p) || map.ObjectCodeAt(p) != MapObjectCodes.None
            || map.TerrainAt(p) != TerrainType.Grass || !_world.IsFreeGround(p) || _world.IsOccupied(p))
            return false;
        for (int d = 0; d < DirectionExtensions.Count; d++)
        {
            var n = p.Neighbor((Direction)d);
            if (map.Contains(n) && map.ObjectAt(n) == MapObjectKind.Tree)
                return false;
        }
        return true;
    }

    private static Direction FacingTowards(GridPoint from, GridPoint to)
    {
        var best = Direction.SouthWest;
        int bestDistance = int.MaxValue;
        for (int d = 0; d < DirectionExtensions.Count; d++)
        {
            int distance = PathFinder.Distance(from.Neighbor((Direction)d), to);
            if (distance < bestDistance)
            {
                best = (Direction)d;
                bestDistance = distance;
            }
        }
        return best;
    }

    private static Activity ActivityOf(GatherAction action) => action switch
    {
        GatherAction.CutTree => Activity.Chopping,
        GatherAction.CutStone => Activity.Mining,
        _ => Activity.Planting,
    };

    private void Rest(Workplace w, int ticks)
    {
        w.Phase = Phase.Inside;
        w.TicksLeft = Math.Max(1, ticks);
    }

    private bool WalkTo(Workplace w, GridPoint destination, Phase phase)
    {
        w.Phase = phase;
        w.WalkingTo = destination;
        w.BestRemaining = int.MaxValue;
        w.LastProgress = _world.Tick;
        var worker = w.Worker!;
        if (worker.Position == destination && worker.State != SettlerState.Stepping)
            return true;
        return _world.WalkTo(worker, destination);
    }

    /// <summary>True once the worker stands at the destination; <paramref name="stalled"/> reports the watchdog.</summary>
    private bool Arrived(Workplace w, out bool stalled)
    {
        var worker = w.Worker!;
        stalled = false;
        if (worker.State != SettlerState.Stepping && worker.Destination is null)
        {
            if (worker.Position == w.WalkingTo)
                return true;
            // Walk ended elsewhere (path vanished): try again, the watchdog limits retries.
            if (!_world.WalkTo(worker, w.WalkingTo))
            {
                stalled = true;
                return false;
            }
        }
        int remaining = worker.Destination == w.WalkingTo ? worker.RemainingPath.Count() : int.MaxValue;
        if (remaining < w.BestRemaining)
        {
            w.BestRemaining = remaining;
            w.LastProgress = _world.Tick;
        }
        stalled = _world.Tick - w.LastProgress > StallTicks;
        return false;
    }

    private void BeginHandling(Workplace w, Activity activity, Phase phase)
    {
        w.Phase = phase;
        w.TicksLeft = HandleTicks;
        var worker = w.Worker!;
        worker.IsWorking = true;
        worker.Activity = activity;
        worker.ActivityTicks = 0;
    }

    private static void StopActivity(Settler worker)
    {
        worker.IsWorking = false;
        worker.Activity = Activity.None;
    }

    /// <summary>Abandons the current step: drop what is carried, release the target, go home.</summary>
    private void GiveUp(Workplace w, string reason)
    {
        Report(reason);
        _reservedTargets.Remove(_world.Map!.Index(w.Target));
        var worker = w.Worker!;
        StopActivity(worker);
        DropCarried(worker);
        if (!WalkTo(w, w.Building.Door, Phase.ToDoorToRest))
        {
            Report("worker could not get back home");
            ReleaseWorker(w);
        }
    }

    private void DropCarried(Settler worker)
    {
        if (worker.Carrying is { } material)
            _world.DropGoods(worker.Position, material);
        worker.Carrying = null;
    }

    /// <summary>The worker turns back into a carrier; the building will call a new one.</summary>
    private void ReleaseWorker(Workplace w)
    {
        if (w.Worker is { } worker)
        {
            if (worker.IsInside)
                _world.LeaveBuilding(worker);
            StopActivity(worker);
            DropCarried(worker);
            worker.Profession = Profession.Carrier;
            worker.Trade = null;
            worker.Workplace = null;
            worker.HasTask = false;
            if (worker.Destination is not null)
                _world.WalkTo(worker, worker.StepTarget);
        }
        _reservedTargets.Remove(_world.Map!.Index(w.Target));
        w.Worker = null;
        w.Phase = Phase.NeedsWorker;
    }

    private void Dismiss(Workplace w)
    {
        ReleaseWorker(w);
        _workplaces.Remove(w.Building.Id);
    }

    private void Report(string reason) => _problems[reason] = _problems.GetValueOrDefault(reason) + 1;

    private enum Phase
    {
        NeedsWorker,
        WorkerComing,
        Inside,
        ToPile,
        PickingUp,
        ToDoorToWork,
        Producing,
        ToSpot,
        Gathering,
        ToOutput,
        Dropping,
        ToDoorToRest,
    }

    private sealed class Workplace(Building building, WorkshopRecipe? workshop, GathererRecipe? gatherer)
    {
        public Building Building { get; } = building;
        public WorkshopRecipe? Workshop { get; } = workshop;
        public GathererRecipe? Gatherer { get; } = gatherer;
        public Settler? Worker { get; set; }
        public Phase Phase { get; set; } = Phase.NeedsWorker;
        public int TicksLeft { get; set; }
        public Queue<Pile> Inputs { get; } = new();
        public GridPoint Target { get; set; }
        public Direction Facing { get; set; }
        public GridPoint WalkingTo { get; set; }
        public int BestRemaining { get; set; }
        public long LastProgress { get; set; }
    }
}
