using Homestead.Simulation.Economy;
using Homestead.Simulation.Pathfinding;

namespace Homestead.Simulation.Buildings;

/// <summary>
/// Sends diggers and builders to construction sites and runs their work.
/// </summary>
/// <remarks>
/// Diggers level one footprint point per work cycle, up to <see cref="MaxDiggersPerSite"/> per site.
/// When all points are level, the site becomes solid and builders take the builder spots of the
/// building definition. Each build cycle uses one unit of delivered material; without material a
/// builder waits on its spot. Workers whose site disappears are simply released.
/// If sites need more diggers or builders than there are, carriers are recruited, each fetching its
/// tool first (shovel/hammer, see <see cref="ProductionConfig.Tools"/>). Without tool rules for these
/// trades (e.g. <see cref="ProductionConfig.None"/>) nobody is recruited and only existing ones work.
/// </remarks>
public sealed class Construction
{
    public const int DigTicks = 24;
    public const int BuildTicks = 30;
    public const int MaxDiggersPerSite = 3;
    public const int DispatchInterval = 5;
    private const string DiggerTrade = "DIGGER";
    private const string BuilderTrade = "BRICKLAYER";

    private readonly World _world;
    private readonly Dictionary<int, Assignment> _assignments = new();
    private readonly List<Recruit> _recruits = new();
    private readonly Dictionary<int, int> _missingDiggers = new();
    private readonly Dictionary<int, int> _missingBuilders = new();

    internal Construction(World world)
    {
        _world = world;
    }

    public int Assigned => _assignments.Count;

    /// <summary>Diggers/builders the sites still lack and cannot recruit because the tool is missing.</summary>
    internal int WaitingFor(Material tool, int player) =>
        (_world.Recruitment.ToolFor(DiggerTrade) == tool ? _missingDiggers.GetValueOrDefault(player) : 0)
        + (_world.Recruitment.ToolFor(BuilderTrade) == tool ? _missingBuilders.GetValueOrDefault(player) : 0);

    internal void Tick()
    {
        foreach (var assignment in _assignments.Values.ToList())
            Advance(assignment);
        if (_world.Tick % DispatchInterval == 0)
        {
            Dispatch();
            RecruitWorkers();
        }
    }

    /// <summary>Recruits diggers and builders while the sites have more free spots than there are workers.</summary>
    private void RecruitWorkers()
    {
        _recruits.RemoveAll(r => r.State != RecruitState.FetchingTool);
        _missingDiggers.Clear();
        _missingBuilders.Clear();
        foreach (var sites in _world.Buildings.Where(b => b.Exists && b.State != BuildingState.Finished).GroupBy(b => b.Player))
        {
            int player = sites.Key;
            _missingDiggers[player] = Recruit(Profession.Digger, DiggerTrade, player, sites.ToList(), site => site.State == BuildingState.Flattening
                ? Math.Min(MaxDiggersPerSite, site.Unlevelled.Count) - _assignments.Values.Count(a => a.Site == site)
                : 0);
            _missingBuilders[player] = Recruit(Profession.Builder, BuilderTrade, player, sites.ToList(), site => site.State == BuildingState.Constructing
                ? site.Definition.BuilderSpots.Count - _assignments.Values.Count(a => a.Site == site)
                : 0);
        }
    }

    /// <summary>Starts recruitments for the shortfall; returns how many could not be started.</summary>
    private int Recruit(Profession profession, string trade, int player, List<Building> sites, Func<Building, int> freeSpots)
    {
        if (_world.Recruitment.ToolFor(trade) is null)
            return 0;
        int wanted = sites.Sum(s => Math.Max(0, freeSpots(s)));
        int idle = _world.Settlers.Count(s => s.Profession == profession && s.Player == player && !_assignments.ContainsKey(s.Id));
        int coming = _recruits.Count(r => r.Profession == profession && r.Settler.Player == player);
        int missing = wanted - idle - coming;
        // Diggers and builders leave the carrier reserve's last conversions to the workers of the
        // buildings under way: otherwise they take them all and the finished buildings stay empty.
        // One of each is always allowed, else those buildings would never be built.
        int headroom = _world.Recruitment.Convertible(player) - _world.Production.AwaitingWorkers(player);
        if (headroom <= 0 && !_world.Settlers.Any(s => s.Profession == profession && s.Player == player))
            headroom = 1;
        while (missing > 0 && headroom-- > 0)
        {
            var site = sites.First(s => freeSpots(s) > 0);
            if (_world.Recruitment.Start(profession, trade, site.Door, site.Player) is not { } recruit)
                break;
            if (recruit.State == RecruitState.FetchingTool)
                _recruits.Add(recruit);
            missing--;
        }
        return missing;
    }

    private void Dispatch()
    {
        var pathFinder = _world.PathFinder!;
        var sites = _world.Buildings.Where(b => b.Exists && b.State != BuildingState.Finished).ToList();
        if (sites.Count == 0)
            return;

        foreach (var worker in _world.Settlers)
        {
            if (worker.Profession is not (Profession.Digger or Profession.Builder)
                || _assignments.ContainsKey(worker.Id) || !worker.IsStrolling && (worker.Destination is not null || worker.State == SettlerState.Stepping))
                continue;

            Assignment? best = null;
            int bestDistance = int.MaxValue;
            foreach (var site in sites)
            {
                if (site.Player != worker.Player)
                    continue;
                var spot = worker.Profession == Profession.Digger ? FreeDigSpot(site, worker.Position, null) : FreeBuilderSpot(site);
                if (spot is not { } s)
                    continue;
                int distance = PathFinder.Distance(worker.Position, s.Spot);
                if (distance < bestDistance && _world.Reachable(worker, s.Spot))
                {
                    best = new Assignment(worker, site, s.Spot, s.Facing);
                    bestDistance = distance;
                }
            }
            if (best is null)
                continue;

            _assignments[worker.Id] = best;
            worker.HasTask = true;
            if (!_world.WalkTo(worker, best.Spot))
                Release(best);
        }
    }

    private void Advance(Assignment a)
    {
        var worker = a.Worker;
        var site = a.Site;
        bool digging = worker.Profession == Profession.Digger;
        if (!site.Exists || site.State != (digging ? BuildingState.Flattening : BuildingState.Constructing))
        {
            Release(a);
            return;
        }

        if (!a.Working)
        {
            if (worker.Destination is not null || worker.State == SettlerState.Stepping)
                return;
            if (worker.Position != a.Spot)
            {
                if (!_world.WalkTo(worker, a.Spot))
                    Release(a);
                return;
            }
            a.Working = true;
            worker.IsWorking = true;
            worker.Facing = a.Facing;
            a.TicksLeft = digging ? DigTicks : 0;
            if (digging)
                SetActivity(worker, Activity.Digging);
        }

        if (digging)
            AdvanceDigging(a);
        else
            AdvanceBuilding(a);
    }

    private void AdvanceDigging(Assignment a)
    {
        if (--a.TicksLeft > 0)
            return;

        var site = a.Site;
        _world.Map!.SetHeight(a.Spot, site.TargetHeight);
        site.Unlevelled.Remove(a.Spot);

        if (FreeDigSpot(site, a.Spot, a.Worker) is { } next)
        {
            // Carry on with the nearest remaining point of the same site.
            a.Spot = next.Spot;
            a.Working = false;
            a.Worker.IsWorking = false;
            SetActivity(a.Worker, Activity.None);
            _world.WalkTo(a.Worker, a.Spot);
            return;
        }

        Release(a);
        bool othersDigging = _assignments.Values.Any(o => o.Site == site && o.Worker.Profession == Profession.Digger);
        if (site.Unlevelled.Count == 0 && !othersDigging)
            _world.BeginConstruction(site);
    }

    private void AdvanceBuilding(Assignment a)
    {
        var site = a.Site;
        if (a.TicksLeft == 0)
        {
            // Start a build cycle if material is there, otherwise wait on the spot.
            if (!_world.ConsumeConstructionMaterial(site))
            {
                SetActivity(a.Worker, Activity.None);
                return;
            }
            a.TicksLeft = BuildTicks;
            SetActivity(a.Worker, Activity.Building);
        }

        if (--a.TicksLeft > 0)
            return;

        site.BuildStepsDone++;
        if (site.BuildStepsDone >= site.BuildStepsTotal)
        {
            _world.FinishConstruction(site);
            foreach (var other in _assignments.Values.Where(o => o.Site == site).ToList())
                Release(other);
        }
    }

    /// <summary>
    /// Nearest footprint point still to level that no other digger works on, or null if there is none
    /// or the site already has enough diggers (not counting <paramref name="self"/>).
    /// </summary>
    private (GridPoint Spot, Direction Facing)? FreeDigSpot(Building site, GridPoint from, Settler? self)
    {
        if (site.State != BuildingState.Flattening)
            return null;
        var others = _assignments.Values.Where(o => o.Site == site && o.Worker != self).ToList();
        if (others.Count >= MaxDiggersPerSite)
            return null;
        var taken = others.Select(o => o.Spot).ToHashSet();
        var free = site.Unlevelled.Where(p => !taken.Contains(p)).ToList();
        if (free.Count == 0)
            return null;
        return (free.MinBy(p => PathFinder.Distance(from, p)), Direction.SouthWest);
    }

    private (GridPoint Spot, Direction Facing)? FreeBuilderSpot(Building site)
    {
        if (site.State != BuildingState.Constructing)
            return null;
        var map = _world.Map!;
        var taken = _assignments.Values.Where(o => o.Site == site).Select(o => o.Spot).ToHashSet();
        foreach (var spot in site.Definition.BuilderSpots)
        {
            var p = site.Position + spot.Offset;
            if (!taken.Contains(p) && map.IsWalkable(p))
                return (p, spot.Facing);
        }
        return null;
    }

    private void Release(Assignment a)
    {
        _assignments.Remove(a.Worker.Id);
        a.Worker.HasTask = false;
        a.Worker.IsWorking = false;
        SetActivity(a.Worker, Activity.None);
    }

    private static void SetActivity(Settler settler, Activity activity)
    {
        if (settler.Activity == activity)
            return;
        settler.Activity = activity;
        settler.ActivityTicks = 0;
    }

    private sealed class Assignment(Settler worker, Building site, GridPoint spot, Direction facing)
    {
        public Settler Worker { get; } = worker;
        public Building Site { get; } = site;
        public GridPoint Spot { get; set; } = spot;
        public Direction Facing { get; } = facing;
        public bool Working { get; set; }
        public int TicksLeft { get; set; }
    }
}
