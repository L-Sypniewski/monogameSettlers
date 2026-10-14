using Homestead.Simulation.Buildings;
using Homestead.Simulation.Pathfinding;

namespace Homestead.Simulation.Economy;

/// <summary>
/// Matches offers (loose, output and storehouse piles) with requests (input piles with space) and hands
/// the transports to idle carriers: for each unit first the kind of target, then the nearest one
/// (see <see cref="ChooseTarget"/>).
/// </summary>
/// <remarks>
/// Every transport reserves a unit at the source and space at the target, so no two carriers go
/// for the same unit. A job can fail at any point; the watchdog rules guarantee that goods and
/// carriers never get lost:
/// <list type="bullet">
/// <item>Source gone or unreachable before pick-up: the job is cancelled, reservations released.</item>
/// <item>Target gone or unreachable while carrying: the carrier drops the good where it stands,
/// where it becomes a loose pile and is offered again.</item>
/// <item>A carrier that makes no progress towards its destination for <see cref="StallTicks"/>
/// (e.g. two carriers taking turns pushing each other away) is treated the same way.</item>
/// </list>
/// </remarks>
public sealed class Logistics
{
    public const int PickUpTicks = 8;
    public const int DropTicks = 8;

    /// <summary>Ticks without getting closer to the destination before the watchdog steps in (20 s).</summary>
    public const int StallTicks = 400;

    /// <summary>Requests are matched every few ticks, not every tick.</summary>
    public const int DispatchInterval = 5;

    private readonly World _world;
    private readonly Dictionary<int, TransportJob> _jobs = new();
    private readonly Random _random;

    internal Logistics(World world)
    {
        _world = world;
        _random = new Random(7 + world.Seed);
    }

    public IReadOnlyCollection<TransportJob> Jobs => _jobs.Values;

    public int Delivered { get; private set; }

    public int Cancelled { get; private set; }

    /// <summary>Goods a carrier had to put down on the way (target gone, unreachable, timeout).</summary>
    public int DroppedOnTheWay { get; private set; }

    private readonly Dictionary<string, int> _problems = new();

    /// <summary>Why jobs were cancelled or goods dropped, with counts (for debugging and tests).</summary>
    public IReadOnlyDictionary<string, int> Problems => _problems;

    public TransportJob? JobOf(Settler settler) => _jobs.GetValueOrDefault(settler.Id);

    internal void Tick()
    {
        foreach (var job in _jobs.Values.ToList())
            Advance(job);

        if (_world.Tick % DispatchInterval == 0)
            Dispatch();
    }

    private void Dispatch()
    {
        var idleCarriers = _world.Settlers
            .Where(s => s.IsAvailableCarrier && !s.IsStriking && !_jobs.ContainsKey(s.Id))
            .ToList();
        if (idleCarriers.Count == 0)
            return;
        var offers = _world.Piles.Where(p => p.Available > 0 && p.Material is not null).OrderBy(p => p.Id).ToList();
        var requests = _world.Piles.Where(p => p.Space > 0 && p.Owner is { Exists: true }).ToList();
        if (offers.Count == 0 || requests.Count == 0)
            return;

        var pathFinder = _world.PathFinder!;
        foreach (var offer in offers)
        {
            while (offer.Available > 0 && idleCarriers.Count > 0)
            {
                var players = idleCarriers.Select(c => c.Player).ToHashSet();
                if (ChooseTarget(offer, requests, players) is not { } target)
                    break;
                int player = target.Owner!.Player;
                // The target is chosen; the goods come from the pile of that material nearest to it.
                var material = offer.Material!.Value;
                var source = offers
                    .Where(o => o.Available > 0 && o.Material == material && o.Owner != target.Owner && _world.BelongsTo(o, player)
                                && Allowed(o, target, material))
                    .OrderBy(o => PathFinder.Distance(o.Position, target.Position))
                    .FirstOrDefault(o => _world.Reachable(player, o.Position, target.Position)) ?? offer;
                var carrier = idleCarriers
                    .Where(c => c.Player == player && _world.Reachable(c, source.Position))
                    .MinBy(c => PathFinder.Distance(c.Position, source.Position));
                if (carrier is null)
                    break;
                idleCarriers.Remove(carrier);
                Start(carrier, source, target);
            }
            if (idleCarriers.Count == 0)
                break;
        }
    }

    /// <summary>
    /// Where a unit of the offer goes. First the kind of target: construction sites, then buildings
    /// that use it, then the storehouse; among buildings, goods with a distribution rule
    /// (<see cref="ProductionConfig.Distribution"/>, e.g. food for mines 90:10) draw the building type
    /// by its weight. Then the nearest target of that kind with room.
    /// </summary>
    private Pile? ChooseTarget(Pile offer, List<Pile> requests, HashSet<int> players)
    {
        var material = offer.Material!.Value;
        var candidates = requests.Where(r => r.Space > 0 && r.Accepts(material) && r.Owner != offer.Owner
                                             && players.Contains(r.Owner!.Player) && _world.BelongsTo(offer, r.Owner.Player)
                                             && Allowed(offer, r, material)).ToList();
        if (candidates.Count == 0)
            return null;

        var pathFinder = _world.PathFinder!;
        foreach (var kind in candidates.GroupBy(Kind).OrderBy(g => g.Key))
        {
            var targets = kind.ToList();
            if (kind.Key == 1 && _world.Production.Config.Distribution.TryGetValue(material, out var weights))
            {
                string type = DrawType(targets.Select(t => t.Owner!.Definition.Name).Distinct().ToList(), weights);
                var ofType = targets.Where(t => t.Owner!.Definition.Name == type).ToList();
                if (Nearest(ofType) is { } drawn)
                    return drawn;
            }
            if (Nearest(targets) is { } nearest)
                return nearest;
        }
        return null;

        Pile? Nearest(IEnumerable<Pile> targets) => targets
            .OrderBy(t => PathFinder.Distance(offer.Position, t.Position))
            .FirstOrDefault(t => _world.Reachable(t.Owner!.Player, offer.Position, t.Position));

        static int Kind(Pile request) => request.Role == PileRole.Storage ? 2 : request.Owner!.State == BuildingState.Finished ? 1 : 0;
    }

    private bool Allowed(Pile offer, Pile request, Material material)
    {
        if (request.Role != PileRole.Storage)
            return true;
        if (!_world.Production.Config.IsStored(material))
            return false;
        // Storehouse to storehouse would only shuffle goods around.
        if (offer.Role == PileRole.Storage)
            return false;
        // An empty storehouse pile starts a new material only if no pile there holds it with room
        // left; otherwise one material spreads over all piles and the storehouse is full.
        return !(request.Material is null && request.Owner!.Piles.Any(o =>
            o != request && o.Role == PileRole.Storage && o.Material == material && o.Space > 0));
    }

    /// <summary>A building type drawn by weight; types without a weight only when none has one.</summary>
    private string DrawType(List<string> types, IReadOnlyDictionary<string, int> weights)
    {
        int total = types.Sum(t => weights.GetValueOrDefault(t));
        if (total <= 0)
            return types[_random.Next(types.Count)];
        int roll = _random.Next(total);
        foreach (string type in types)
        {
            roll -= weights.GetValueOrDefault(type);
            if (roll < 0)
                return type;
        }
        return types[^1];
    }

    private void Start(Settler carrier, Pile source, Pile target)
    {
        var material = source.Material!.Value;
        source.Reserved++;
        target.Incoming++;
        target.Material ??= material;

        var job = new TransportJob(carrier, source, target, material, _world.Tick);
        _jobs[carrier.Id] = job;
        carrier.HasTask = true;
        if (!Walk(job, source.Position))
            Cancel(job, "source unreachable");
    }

    private void Advance(TransportJob job)
    {
        var carrier = job.Carrier;
        // Walking phases only: picking up and dropping are short and bounded anyway.
        if (job.Phase is TransportPhase.ToSource or TransportPhase.ToTarget && Stalled(job))
        {
            Fail(job, $"no progress in {job.Phase}");
            return;
        }

        switch (job.Phase)
        {
            case TransportPhase.ToSource:
                if (!_world.HasPile(job.Source) || job.Source.Count == 0)
                {
                    Cancel(job, "source gone");
                }
                else if (carrier.Destination is null && carrier.State != SettlerState.Stepping)
                {
                    if (carrier.Position == job.Source.Position)
                        BeginWork(job, TransportPhase.PickingUp);
                    else if (!Walk(job, job.Source.Position))
                        Cancel(job, "source unreachable");
                }
                break;

            case TransportPhase.PickingUp:
                if (--job.WorkTicksLeft > 0)
                    break;
                carrier.IsWorking = false;
                carrier.Activity = Activity.None;
                if (!_world.HasPile(job.Source) || job.Source.Count == 0)
                {
                    Cancel(job, "source gone");
                    break;
                }
                _world.TakeFromPile(job.Source);
                carrier.Carrying = job.Material;
                job.Phase = TransportPhase.ToTarget;
                if (!TargetUsable(job))
                    Fail(job, "target gone");
                else if (!Walk(job, job.Target.Position))
                    Fail(job, "target unreachable");
                break;

            case TransportPhase.ToTarget:
                if (!TargetUsable(job))
                {
                    Fail(job, "target gone");
                }
                else if (carrier.Destination is null && carrier.State != SettlerState.Stepping)
                {
                    if (carrier.Position == job.Target.Position)
                        BeginWork(job, TransportPhase.Dropping);
                    else if (!Walk(job, job.Target.Position))
                        Fail(job, "target unreachable");
                }
                break;

            case TransportPhase.Dropping:
                if (--job.WorkTicksLeft > 0)
                    break;
                carrier.IsWorking = false;
                carrier.Activity = Activity.None;
                if (!TargetUsable(job))
                {
                    Fail(job, "target gone");
                    break;
                }
                job.Target.Incoming--;
                job.Target.Count++;
                job.Target.Material = job.Material;
                carrier.Carrying = null;
                carrier.HasTask = false;
                _jobs.Remove(carrier.Id);
                Delivered++;
                break;
        }
    }

    /// <summary>Sends the carrier off and restarts progress tracking for the new destination.</summary>
    private bool Walk(TransportJob job, GridPoint destination)
    {
        if (!_world.WalkTo(job.Carrier, destination))
            return false;
        job.WalkingTo = destination;
        job.BestRemaining = int.MaxValue;
        job.LastProgress = _world.Tick;
        return true;
    }

    /// <summary>
    /// Progress = remaining path to the job's destination got shorter than ever before. Detours make
    /// it longer for a while, which is fine; walking somewhere else does not count at all.
    /// </summary>
    private bool Stalled(TransportJob job)
    {
        var carrier = job.Carrier;
        int remaining = carrier.Destination == job.WalkingTo ? carrier.RemainingPath.Count() : int.MaxValue;
        if (remaining < job.BestRemaining)
        {
            job.BestRemaining = remaining;
            job.LastProgress = _world.Tick;
        }
        return _world.Tick - job.LastProgress > StallTicks;
    }

    private bool TargetUsable(TransportJob job) => job.Target.Owner?.Exists != false && _world.HasPile(job.Target);

    private void BeginWork(TransportJob job, TransportPhase phase)
    {
        job.Phase = phase;
        job.WorkTicksLeft = phase == TransportPhase.PickingUp ? PickUpTicks : DropTicks;
        job.Carrier.IsWorking = true;
        job.Carrier.Activity = phase == TransportPhase.PickingUp ? Activity.PickingUp : Activity.Dropping;
        job.Carrier.ActivityTicks = 0;
    }

    /// <summary>Before pick-up: release everything, the carrier is free again.</summary>
    private void Cancel(TransportJob job, string reason)
    {
        if (job.Phase is TransportPhase.ToSource or TransportPhase.PickingUp)
            job.Source.Reserved = Math.Max(0, job.Source.Reserved - 1);
        ReleaseTarget(job);
        job.Carrier.IsWorking = false;
        job.Carrier.Activity = Activity.None;
        StopWalking(job.Carrier);
        job.Carrier.HasTask = false;
        _jobs.Remove(job.Carrier.Id);
        Cancelled++;
        _problems[reason] = _problems.GetValueOrDefault(reason) + 1;
    }

    /// <summary>Any phase: cancel, and if the carrier holds the good, put it down here.</summary>
    private void Fail(TransportJob job, string reason)
    {
        if (job.Carrier.Carrying is not { } material)
        {
            Cancel(job, reason);
            return;
        }
        ReleaseTarget(job);
        job.Carrier.IsWorking = false;
        job.Carrier.Activity = Activity.None;
        job.Carrier.Carrying = null;
        StopWalking(job.Carrier);
        _world.DropGoods(job.Carrier.Position, material);
        job.Carrier.HasTask = false;
        _jobs.Remove(job.Carrier.Id);
        DroppedOnTheWay++;
        _problems["dropped: " + reason] = _problems.GetValueOrDefault("dropped: " + reason) + 1;
    }

    private static void ReleaseTarget(TransportJob job)
    {
        if (job.Target.Incoming > 0)
            job.Target.Incoming--;
        job.Target.ResetMaterialIfEmpty();
    }

    private void StopWalking(Settler settler)
    {
        if (settler.Destination is not null)
            _world.WalkTo(settler, settler.StepTarget);
    }
}

public enum TransportPhase
{
    ToSource,
    PickingUp,
    ToTarget,
    Dropping,
}

public sealed class TransportJob
{
    internal TransportJob(Settler carrier, Pile source, Pile target, Material material, long started)
    {
        Carrier = carrier;
        Source = source;
        Target = target;
        Material = material;
        Started = started;
    }

    public Settler Carrier { get; }
    public Pile Source { get; }
    public Pile Target { get; }
    public Material Material { get; }
    public long Started { get; }
    internal GridPoint WalkingTo { get; set; }
    internal int BestRemaining { get; set; }
    internal long LastProgress { get; set; }
    public TransportPhase Phase { get; internal set; } = TransportPhase.ToSource;
    internal int WorkTicksLeft { get; set; }
}
