using Homestead.Simulation.Buildings;
using Homestead.Simulation.Map;
using Homestead.Simulation.Pathfinding;

namespace Homestead.Simulation.Economy;

/// <summary>
/// Workers in finished buildings: workshops turn inputs into outputs, mines turn food into ore from
/// the ground, gatherers cut trees and stone, plant trees, fish, draw water and farm around their
/// building. Also lets trees and grain grow and depleted ground regenerate (if configured).
/// </summary>
/// <remarks>
/// A finished building with a recipe recruits a carrier (who first fetches the trade's tool, see
/// <see cref="Recruitment"/>) and walks it to the door. A smith picks its product each cycle
/// (<see cref="ChooseProduct"/>) and puts it down at its drop position for the carriers.
/// Workshop cycle: leave, fetch each input from the input piles, go in, work, bring the output to
/// the output pile, go in. A mine works the same with food as input (one unit lasts several trips,
/// see <see cref="MineRecipe"/>); the ore comes
/// from the richest field around it (see <see cref="ProductionConfig.OrePerAmount"/>). Gatherer
/// cycle: rest inside, walk to a target in the work radius, work there, bring the result (if any)
/// to the output pile, go in.
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
    internal const int DefaultRadius = 12;
    private const int ExhaustedRetryTicks = 200;
    private const int RecruitInterval = 10;

    private readonly World _world;
    private readonly ProductionConfig _config;
    private readonly Dictionary<int, Workplace> _workplaces = new();
    private readonly HashSet<int> _reservedTargets = new();
    private readonly Dictionary<int, long> _unreachableUntil = new();
    /// <summary>Saplings and crops (by point index) and when they change next.</summary>
    private readonly Dictionary<int, long> _growing = new();

    /// <summary>Stones already taken at the current size of a quarry stone (see StonesPerCut).</summary>
    private readonly Dictionary<int, int> _stoneCuts = new();

    /// <summary>Units already taken at the current amount step of a resource field (see OrePerAmount).</summary>
    private readonly Dictionary<int, int> _resourceTaken = new();

    /// <summary>
    /// Per kind of resource (stone, mountain, fish): percent points collected towards the next unit that
    /// uses up the ground. Exactly the configured share counts, without chance.
    /// </summary>
    private readonly int[] _depletionDue = new int[3];

    /// <summary>Depleted resource fields: their start amount and when they regain the next step.</summary>
    private readonly Dictionary<int, (int Start, long Next)> _regenerating = new();
    private readonly Dictionary<string, int> _problems = new();
    private readonly Random _random;

    internal Production(World world, ProductionConfig config)
    {
        _world = world;
        _random = new Random(12345 + world.Seed);
        _config = config;
    }

    public ProductionConfig Config => _config;

    public int Produced { get; private set; }
    public int TreesCut { get; private set; }
    public int StonesCut { get; private set; }
    public int TreesPlanted { get; private set; }
    public int TreesGrown { get; private set; }
    public int OreMined { get; private set; }
    public int FishCaught { get; private set; }
    public int WaterDrawn { get; private set; }
    public int FieldsSown { get; private set; }
    public int CropHarvested { get; private set; }
    public int ToolsMade { get; private set; }

    public IReadOnlyDictionary<string, int> Problems => _problems;

    public Settler? WorkerOf(Building building) => _workplaces.GetValueOrDefault(building.Id)?.Worker;

    /// <summary>The worker has arrived and works there (the building shows its flag).</summary>
    public bool HasWorker(Building building) =>
        _workplaces.GetValueOrDefault(building.Id) is { Worker: not null, Phase: not (Phase.NeedsWorker or Phase.Recruiting or Phase.WorkerComing) };

    /// <summary>
    /// The player's production buildings that will need a worker from the carriers: construction sites
    /// and finished buildings whose worker has not been recruited yet.
    /// </summary>
    public int AwaitingWorkers(int player) => _world.Buildings.Count(b =>
        b.Exists && b.Player == player && Produces(b.Definition.Name)
        && (b.State != BuildingState.Finished || _workplaces.GetValueOrDefault(b.Id) is null or { Phase: Phase.NeedsWorker }));

    private bool Produces(string name) =>
        _config.Workshops.ContainsKey(name) || _config.Smiths.ContainsKey(name) || _config.Mines.ContainsKey(name) || _config.Gatherers.ContainsKey(name);

    /// <summary>What the workplace is doing or waiting for (null for buildings without production).</summary>
    public string? Status(Building building)
    {
        if (_workplaces.GetValueOrDefault(building.Id) is not { } w)
            return null;
        var tool = _world.Recruitment.ToolFor(building.Definition.Worker);
        return w.Phase switch
        {
            Phase.NeedsWorker when tool is { } t && !_world.Recruitment.ToolAvailable(t, building.Player) => $"no worker: tool missing ({t})",
            Phase.NeedsWorker => "no worker: no free carrier",
            Phase.Recruiting when tool is { } t => $"worker is fetching the tool ({t})",
            Phase.Recruiting or Phase.WorkerComing => "worker is on the way",
            Phase.Inside when w.Worker?.IsStriking == true => "on strike",
            Phase.Inside => w.Waiting ?? "resting",
            Phase.ToPile or Phase.PickingUp or Phase.ToDoorToWork =>
                w.Mine is null ? "fetching " + string.Join(" and ", w.Workshop!.Inputs) : "fetching food",
            Phase.Producing when w.Product is { } product => $"making {product}",
            Phase.Producing => w.Mine is null ? "working" : "mining",
            Phase.ToSpot or Phase.Gathering or Phase.AfterFelling => "working outside",
            Phase.ToOutput or Phase.Dropping => $"bringing {w.Worker?.Carrying} out",
            _ => "going home",
        };
    }

    /// <summary>Mining runs a mine's hit rate looks back on.</summary>
    public const int HitRateRuns = 10;

    /// <summary>Share of the mine's last <see cref="HitRateRuns"/> runs that brought ore, in percent (null: not a mine or no run yet).</summary>
    public int? MineHitRate(Building building) =>
        _workplaces.GetValueOrDefault(building.Id) is { Mine: not null, RecentRuns.Count: > 0 } w
            ? (int)Math.Round(100.0 * w.RecentRuns.Count(hit => hit) / w.RecentRuns.Count)
            : null;

    private static void RecordRun(Workplace w, bool hit)
    {
        w.RecentRuns.Enqueue(hit);
        while (w.RecentRuns.Count > HitRateRuns)
            w.RecentRuns.Dequeue();
    }

    /// <summary>A mine that found nothing left to dig on its last try.</summary>
    public bool IsExhausted(Building building) => _workplaces.GetValueOrDefault(building.Id)?.Exhausted == true;

    /// <summary>Ore a mine can still get from the ground around it (null for other buildings).</summary>
    public int? OreLeft(Building building)
    {
        if (!_config.Mines.TryGetValue(building.Definition.Name, out var mine))
            return null;
        var map = _world.Map!;
        int left = 0;
        foreach (var p in Area(building.Position, mine.Radius))
        {
            int amount = map.ResourceAmount(p, mine.Resource);
            if (amount > 0)
                left += amount * _config.OrePerAmount - _resourceTaken.GetValueOrDefault(map.Index(p));
        }
        return left;
    }

    internal void Tick()
    {
        if (_world.Tick % 10 == 0)
        {
            DiscoverWorkplaces();
            Grow();
            Regenerate();
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
            else if (_config.Smiths.TryGetValue(name, out var smith))
                _workplaces[building.Id] = new Workplace(building, new WorkshopRecipe(smith.Inputs, [], smith.Ticks), null) { Smith = smith };
            else if (_config.Mines.TryGetValue(name, out var mine))
                _workplaces[building.Id] = new Workplace(building, new WorkshopRecipe([], [mine.Output], mine.Ticks), null) { Mine = mine };
            else if (_config.Gatherers.TryGetValue(name, out var gatherer))
                _workplaces[building.Id] = new Workplace(building, null, gatherer);
        }
    }

    /// <summary>Saplings become trees; crops grow stage by stage; stumps of felled trees disappear.</summary>
    private void Grow()
    {
        var map = _world.Map!;
        foreach (var (index, due) in _growing.ToList())
        {
            if (_world.Tick < due)
                continue;
            _growing.Remove(index);
            var p = map.Point(index);
            byte code = map.ObjectCodeAt(p);
            if (code == MapObjectCodes.TreeStump)
                map.SetObjectCode(p, MapObjectCodes.None);
            else if (code == MapObjectCodes.Sapling)
            {
                map.SetObjectCode(p, (byte)(MapObjectCodes.FirstTree + _random.Next(MapObjectCodes.LastTree - MapObjectCodes.FirstTree + 1)));
                TreesGrown++;
            }
            else if (_config.CropOf(code) is { } crop && code < crop.Ripe)
            {
                map.SetObjectCode(p, (byte)(code + 1));
                if (code + 1 < crop.Ripe)
                    _growing[index] = _world.Tick + StageTicks(crop, p);
            }
        }
    }

    /// <summary>Time for one growing stage of a crop at a point (faster on slopes for crops with a slope bonus).</summary>
    private int StageTicks(CropRecipe crop, GridPoint p)
    {
        double ticks = (double)crop.GrowTicks / Math.Max(1, crop.Ripe - crop.First);
        if (crop.SlopeBonus > 0)
        {
            var map = _world.Map!;
            int slope = 0;
            for (int d = 0; d < DirectionExtensions.Count; d++)
            {
                var n = p.Neighbor((Direction)d);
                if (map.Contains(n))
                    slope = Math.Max(slope, Math.Abs(map.HeightAt(n) - map.HeightAt(p)));
            }
            ticks /= 1 + crop.SlopeBonus * Math.Min(1.0, (double)slope / Math.Max(1, crop.SlopeHeight));
        }
        return Math.Max(1, (int)Math.Round(ticks));
    }

    private CropRecipe CropOf(Workplace w) => _config.Crops[w.Gatherer!.Crop];

    /// <summary>Depleted fields regain one amount step every <see cref="ProductionConfig.RegenerateTicks"/>.</summary>
    private void Regenerate()
    {
        var map = _world.Map!;
        foreach (var (index, (start, next)) in _regenerating.ToList())
        {
            if (_world.Tick < next)
                continue;
            var p = map.Point(index);
            var (type, amount) = map.ResourceAt(p);
            map.SetResource(p, type, amount + 1);
            if (amount + 1 >= start)
                _regenerating.Remove(index);
            else
                _regenerating[index] = (start, next + _config.RegenerateTicks);
        }
    }

    /// <summary>Whether the unit just taken uses up the ground: <paramref name="percent"/> of all units of the kind do.</summary>
    private bool UsesUp(int kind, int percent)
    {
        _depletionDue[kind] += Math.Clamp(percent, 0, 100);
        if (_depletionDue[kind] < 100)
            return false;
        _depletionDue[kind] -= 100;
        return true;
    }

    /// <summary>Takes one unit from a resource field; every <paramref name="perAmount"/> units lower its amount by one.</summary>
    private void TakeResource(GridPoint p, int perAmount)
    {
        var map = _world.Map!;
        int index = map.Index(p);
        int taken = _resourceTaken.GetValueOrDefault(index) + 1;
        if (taken < perAmount)
        {
            _resourceTaken[index] = taken;
            return;
        }
        _resourceTaken.Remove(index);
        var (type, amount) = map.ResourceAt(p);
        if (_config.RegenerateTicks > 0 && !_regenerating.ContainsKey(index))
            _regenerating[index] = (amount, _world.Tick + _config.RegenerateTicks);
        map.SetResource(p, type, amount - 1);
    }

    /// <summary>The richest field of the mine's resource around it (nearest on ties), if any is left.</summary>
    private GridPoint? FindOre(Workplace w)
    {
        var mine = w.Mine!;
        var map = _world.Map!;
        var center = w.Building.Position;
        return Area(center, mine.Radius)
            .Where(p => map.ResourceAmount(p, mine.Resource) > 0)
            .OrderByDescending(p => map.ResourceAmount(p, mine.Resource))
            .ThenBy(p => PathFinder.Distance(center, p))
            .Cast<GridPoint?>()
            .FirstOrDefault();
    }

    /// <summary>Map points of the round area (work area, mine area) around <paramref name="center"/>.</summary>
    private IEnumerable<GridPoint> Area(GridPoint center, int radius) =>
        PathFinder.Circle(center, radius).Where(_world.Map!.Contains);

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
                if (_world.Tick % RecruitInterval == 0)
                    Recruit(w);
                break;

            case Phase.Recruiting:
                if (w.Recruit!.State == RecruitState.Failed)
                {
                    Report("recruit did not get its tool");
                    w.Recruit = null;
                    w.Phase = Phase.NeedsWorker;
                }
                else if (w.Recruit.State == RecruitState.Done)
                {
                    w.Worker = w.Recruit.Settler;
                    w.Recruit = null;
                    w.Worker.HasTask = true;
                    w.Worker.Workplace = w.Building;
                    if (!WalkTo(w, w.Building.Door, Phase.WorkerComing))
                        ReleaseWorker(w);
                }
                break;

            case Phase.WorkerComing:
                if (Arrived(w, out bool stalledComing))
                {
                    _world.EnterBuilding(worker!);
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
                if (worker!.IsStriking)
                {
                    w.TicksLeft = 1; // no living space: he stays inside until the strike ends
                    break;
                }
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
                    if (w.Mine is { } fed && worker!.Carrying is { } meal)
                        w.FoodRuns = fed.RunsFor(meal);
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
                if (w.Mine is not null)
                {
                    // The food is eaten either way; the ground may have run out meanwhile.
                    if (FindOre(w) is not { } field)
                    {
                        Report("mine ran out while digging");
                        RecordRun(w, hit: false);
                        w.Exhausted = true;
                        Rest(w, ExhaustedRetryTicks);
                        break;
                    }
                    if (UsesUp(1, _config.Depletion.Mountain))
                        TakeResource(field, _config.OrePerAmount);
                    RecordRun(w, hit: true);
                    OreMined++;
                    w.FoodRuns--;
                }
                else if (w.Smith is not null)
                {
                    ToolsMade++;
                }
                else
                {
                    Produced++;
                }
                worker!.Carrying = w.Product ?? w.Workshop!.Outputs[0];
                _world.LeaveBuilding(worker);
                DeliverOutput(w);
                break;

            case Phase.ToSpot:
                if (Arrived(w, out bool stalledSpot))
                {
                    worker!.Facing = w.Facing;
                    BeginHandling(w, ActivityAt(w), Phase.Gathering);
                    w.TicksLeft = w.Gatherer!.WorkTicks;
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

            case Phase.AfterFelling:
                if (--w.TicksLeft > 0)
                    break;
                worker!.Carrying = Material.Trunk;
                DeliverOutput(w);
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
        if (_world.Recruitment.Start(Profession.Worker, w.Building.Definition.Worker, w.Building.Door, w.Building.Player) is not { } recruit)
            return;
        // A jobless worker of the trade comes at once: taken now, or another building of its trade would
        // take it in the same tick and two workplaces would drive one worker.
        if (recruit.State == RecruitState.Done)
            recruit.Settler.HasTask = true;
        w.Recruit = recruit;
        w.Phase = Phase.Recruiting;
    }

    /// <summary>Workplaces still waiting for a worker whose trade needs this tool.</summary>
    internal int WaitingFor(Material tool, int player) => _workplaces.Values.Count(w =>
        w.Building.Player == player && w.Phase is Phase.NeedsWorker && _world.Recruitment.ToolFor(w.Building.Definition.Worker) == tool);

    /// <summary>
    /// What a smith makes next: the tool most short for waiting workplaces and construction, otherwise
    /// the product with the fewest units around while below the stock target; null to rest.
    /// </summary>
    private Material? ChooseProduct(SmithRecipe smith, int player)
    {
        // Only the smith's own player counts: another player's tools are no use here.
        var piles = _world.Piles.Where(p => _world.BelongsTo(p, player)).ToList();
        Material? best = null;
        int bestShortage = 0;
        foreach (var product in smith.Products)
        {
            int free = piles.Where(p => p.Material == product).Sum(p => p.Available);
            int shortage = WaitingFor(product, player) + _world.Construction.WaitingFor(product, player) - free;
            if (shortage > bestShortage)
            {
                best = product;
                bestShortage = shortage;
            }
        }
        if (best is not null)
            return best;

        var (fewest, count) = smith.Products
            .Select(m => (m, piles.Where(p => p.Material == m).Sum(p => p.Count) + _world.Settlers.Count(s => s.Player == player && s.Carrying == m)))
            .MinBy(x => x.Item2);
        return count < smith.KeepInStock ? fewest : null;
    }

    private void StartWorkshopCycle(Workplace w, WorkshopRecipe recipe)
    {
        var piles = w.Building.Piles;
        if (w.Smith is { } smith)
        {
            w.Product = ChooseProduct(smith, w.Building.Player);
            if (w.Product is null)
            {
                w.Waiting = "everything in stock, resting";
                w.TicksLeft = RetryTicks;
                return;
            }
        }
        else
        {
            var output = piles.FirstOrDefault(p => p.Role == PileRole.Offer && p.Material == recipe.Outputs[0]);
            if (output is null || output.Count >= output.Capacity)
            {
                w.Waiting = "output pile is full";
                w.TicksLeft = RetryTicks;
                return;
            }
        }
        w.Inputs.Clear();
        if (w.Mine is { } mine)
        {
            w.Exhausted = FindOre(w) is null;
            if (w.Exhausted)
            {
                // Each try on an exhausted mine counts as a miss.
                RecordRun(w, hit: false);
                w.Waiting = "mine is exhausted";
                w.TicksLeft = ExhaustedRetryTicks;
                return;
            }
            if (w.FoodRuns > 0)
            {
                // Still fed from the last meal: straight back into the mine.
                w.Waiting = null;
                w.Phase = Phase.Producing;
                w.TicksLeft = recipe.Ticks;
                return;
            }
            // The preferred food first, otherwise the fullest pile so the kinds get used evenly.
            var food = piles.Where(p => p.Role == PileRole.Request && p.Count > 0)
                .OrderByDescending(p => p.Material == mine.PreferredFood).ThenByDescending(p => p.Count).FirstOrDefault();
            if (food is null)
            {
                w.Waiting = "waiting for food";
                w.TicksLeft = RetryTicks;
                return;
            }
            w.Inputs.Enqueue(food);
        }
        foreach (var material in recipe.Inputs)
        {
            var pile = piles.FirstOrDefault(p => p.Role == PileRole.Request && p.Material == material && p.Count > 0);
            if (pile is null)
            {
                w.Waiting = $"waiting for {material}";
                w.TicksLeft = RetryTicks;
                return;
            }
            w.Inputs.Enqueue(pile);
        }
        w.Waiting = null;
        _world.LeaveBuilding(w.Worker!);
        if (!WalkTo(w, w.Inputs.Peek().Position, Phase.ToPile))
            GiveUp(w, "input pile unreachable");
    }

    private void StartGathering(Workplace w, GathererRecipe recipe)
    {
        var target = FindTarget(w, recipe.Action);
        if (target is not { } t)
        {
            w.Waiting = recipe.Action switch
            {
                GatherAction.CutTree => "no tree in range",
                GatherAction.CutStone => "no stone in range",
                GatherAction.PlantTree => "no free ground to plant on",
                GatherAction.Fish => "no fish in range",
                GatherAction.DrawWater => "no river in range",
                _ => "no field to sow or harvest",
            };
            w.TicksLeft = RetryTicks;
            return;
        }
        w.Waiting = null;
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
                _growing[map.Index(w.Target)] = _world.Tick + _config.StumpTicks; // the stump rots away
                TreesCut++;
                if (w.Gatherer.PickupTicks > 0)
                {
                    // The tree has fallen: a moment before the trunk is picked up.
                    w.Phase = Phase.AfterFelling;
                    w.TicksLeft = w.Gatherer.PickupTicks;
                    return;
                }
                worker.Carrying = Material.Trunk;
                DeliverOutput(w);
                return;
            case GatherAction.CutStone when MapObjectCodes.StoneLeft(code) > 0:
                // Each of the 13 sizes yields StonesPerCut stones before the rock shrinks.
                int index = map.Index(w.Target);
                int cuts = _stoneCuts.GetValueOrDefault(index) + (UsesUp(0, _config.Depletion.Stone) ? 1 : 0);
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
            case GatherAction.Fish when map.ResourceAmount(w.Target, ResourceType.Fish) > 0:
                if (UsesUp(2, _config.Depletion.Fish))
                    TakeResource(w.Target, _config.FishPerAmount);
                FishCaught++;
                worker.Carrying = Material.Fish;
                DeliverOutput(w);
                return;
            case GatherAction.DrawWater when IsRiver(w.Target):
                WaterDrawn++;
                worker.Carrying = Material.Water;
                DeliverOutput(w);
                return;
            case GatherAction.Farm when code == CropOf(w).Ripe:
                // Vines and beehives start over, grain and rice leave free ground.
                var crop = CropOf(w);
                map.SetObjectCode(w.Target, crop.Regrows ? crop.First : MapObjectCodes.None);
                if (crop.Regrows)
                    _growing[map.Index(w.Target)] = _world.Tick + StageTicks(crop, w.Target);
                CropHarvested++;
                worker.Carrying = w.Gatherer.Output ?? Material.Crop;
                DeliverOutput(w);
                return;
            case GatherAction.Farm when CanSow(w.Target, CropOf(w), ignoreWorker: worker):
                map.SetObjectCode(w.Target, CropOf(w).First);
                _growing[map.Index(w.Target)] = _world.Tick + StageTicks(CropOf(w), w.Target);
                FieldsSown++;
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
        if (output is null && w.Building.Definition.DropPosition is { } drop)
        {
            // Smiths put their products down next to the building; carriers take them from there.
            if (!WalkTo(w, w.Building.Position + drop, Phase.ToOutput))
                GiveUp(w, "drop position unreachable");
            return;
        }
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
    /// a quarry stone with a reachable spot two fields away (its ring is blocked), water with fish
    /// next to the shore, a river (waterworks), ripe grain, or a free spot to plant or sow on (randomized a little so
    /// forests and fields spread).
    /// </summary>
    private (GridPoint Target, GridPoint Spot, Direction Facing)? FindTarget(Workplace w, GatherAction action)
    {
        if (action == GatherAction.Farm)
            return FindTarget(w, Want.RipeGrain) ?? FindTarget(w, Want.FreeField);
        return FindTarget(w, action switch
        {
            GatherAction.CutTree => Want.Tree,
            GatherAction.CutStone => Want.Stone,
            GatherAction.PlantTree => Want.TreeSpot,
            GatherAction.Fish => Want.Fish,
            _ => Want.Water,
        });
    }

    private (GridPoint Target, GridPoint Spot, Direction Facing)? FindTarget(Workplace w, Want want)
    {
        var map = _world.Map!;
        var pathFinder = _world.PathFinder!;
        var definition = w.Building.Definition;
        var center = w.Building.WorkCenter;
        var door = w.Building.Door;
        int radius = definition.WorkRadius ?? DefaultRadius;
        var candidates = new List<(GridPoint Target, GridPoint Spot, Direction Facing, int Distance)>();

        foreach (var p in Area(center, radius))
        {
            int index = map.Index(p);
            if (_reservedTargets.Contains(index) || _unreachableUntil.TryGetValue(index, out long until) && until > _world.Tick)
                continue;
            // Workers stay on their own land.
            if (_world.TerritoryRules && map.OwnerAt(p) != w.Building.Player)
                continue;

            bool fits = want switch
            {
                Want.Tree => MapObjectCodes.IsAdultTree(map.ObjectCodeAt(p)),
                Want.Stone => MapObjectCodes.StoneLeft(map.ObjectCodeAt(p)) > 0,
                Want.TreeSpot => CanPlant(p),
                Want.Fish => map.ResourceAmount(p, ResourceType.Fish) > 0 && IsWater(p),
                Want.Water => IsRiver(p),
                Want.RipeGrain => map.ObjectCodeAt(p) == CropOf(w).Ripe,
                Want.FreeField => CanSow(p, CropOf(w), ignoreWorker: null),
                _ => false,
            };
            if (!fits)
                continue;

            int reach = want switch
            {
                Want.Stone => 2,
                Want.RipeGrain or Want.FreeField => 0, // the farmer stands in the field
                _ => 1,
            };
            var spot = SpotNear(p, reach, door);
            if (spot is not { } s || !_world.Reachable(w.Building.Player, door, s)
                || _world.TerritoryRules && map.OwnerAt(s) != w.Building.Player)
                continue;
            candidates.Add((p, s, s == p ? Direction.SouthWest : FacingTowards(s, p), PathFinder.Distance(door, s)));
        }

        if (candidates.Count == 0)
            return null;
        if (want is Want.TreeSpot or Want.FreeField)
        {
            // Pick among the nearer half at random, so foresters and farmers do not plant a single line.
            var near = candidates.OrderBy(c => c.Distance).Take(Math.Max(1, candidates.Count / 2)).ToList();
            var pick = near[_random.Next(near.Count)];
            return (pick.Target, pick.Spot, pick.Facing);
        }
        var best = candidates.MinBy(c => c.Distance);
        return (best.Target, best.Spot, best.Facing);
    }

    private bool IsWater(GridPoint p) => _world.Map!.TerrainAt(p) is TerrainType.Water or TerrainType.River;

    /// <summary>Waterworks draw fresh water from rivers only, not from the sea.</summary>
    private bool IsRiver(GridPoint p) => _world.Map!.TerrainAt(p) == TerrainType.River;

    /// <summary>Free ground of the crop's kind for a new field (fields may touch each other).</summary>
    private bool CanSow(GridPoint p, CropRecipe crop, Settler? ignoreWorker)
    {
        var map = _world.Map!;
        return map.Contains(p) && map.IsWalkable(p) && map.ObjectCodeAt(p) == MapObjectCodes.None
               && crop.Ground.Contains(map.TerrainAt(p)) && _world.IsFreeGround(p)
               && (!_world.IsOccupied(p) || ignoreWorker is not null && ignoreWorker.Position == p);
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

    private Activity ActivityAt(Workplace w) => w.Gatherer!.Action switch
    {
        GatherAction.CutTree => Activity.Chopping,
        GatherAction.CutStone => Activity.Mining,
        GatherAction.Fish => Activity.Fishing,
        GatherAction.DrawWater => Activity.DrawingWater,
        GatherAction.Farm => _world.Map!.ObjectCodeAt(w.Target) == CropOf(w).Ripe ? Activity.Harvesting : Activity.Sowing,
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

    /// <summary>
    /// The worker leaves the building without a job. It keeps its trade and tool for good (as in the
    /// original): the next building of its trade takes it before recruiting a carrier.
    /// </summary>
    private void ReleaseWorker(Workplace w)
    {
        if (w.Worker is { } worker)
        {
            if (worker.IsInside)
                _world.LeaveBuilding(worker);
            StopActivity(worker);
            DropCarried(worker);
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
        if (w.Recruit is { State: RecruitState.FetchingTool } recruit)
            _world.Recruitment.Cancel(recruit);
        else if (w.Recruit is { State: RecruitState.Done } done)
            w.Worker = done.Settler; // converted meanwhile: release it below
        ReleaseWorker(w);
        _workplaces.Remove(w.Building.Id);
    }

    private void Report(string reason) => _problems[reason] = _problems.GetValueOrDefault(reason) + 1;

    private enum Phase
    {
        NeedsWorker,
        Recruiting,
        WorkerComing,
        Inside,
        ToPile,
        PickingUp,
        ToDoorToWork,
        Producing,
        ToSpot,
        Gathering,
        AfterFelling,
        ToOutput,
        Dropping,
        ToDoorToRest,
    }

    /// <summary>What a gatherer looks for.</summary>
    private enum Want
    {
        Tree,
        Stone,
        TreeSpot,
        Fish,
        Water,
        RipeGrain,
        FreeField,
    }

    private sealed class Workplace(Building building, WorkshopRecipe? workshop, GathererRecipe? gatherer)
    {
        public Building Building { get; } = building;
        public WorkshopRecipe? Workshop { get; } = workshop;
        public GathererRecipe? Gatherer { get; } = gatherer;
        public MineRecipe? Mine { get; init; }
        public SmithRecipe? Smith { get; init; }

        /// <summary>Why the last cycle could not start (shown to the player), null while working.</summary>
        public string? Waiting { get; set; }

        /// <summary>Mine: outcome of the last runs (true: ore), oldest first.</summary>
        public Queue<bool> RecentRuns { get; } = new();

        /// <summary>Smith: what the current cycle makes.</summary>
        public Material? Product { get; set; }
        public Recruit? Recruit { get; set; }

        /// <summary>Mine: trips into the mine left before the worker has to eat again.</summary>
        public int FoodRuns { get; set; }

        /// <summary>Mine: nothing left in the ground on the last try.</summary>
        public bool Exhausted { get; set; }
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
