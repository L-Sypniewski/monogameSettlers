using Homestead.Simulation.Buildings;
using Homestead.Simulation.Pathfinding;

namespace Homestead.Simulation.Economy;

/// <summary>
/// Matches requests (input piles with space) with offers (loose and output piles) and hands the
/// transports to idle carriers.
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

    internal Logistics(World world)
    {
        _world = world;
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
        _world.KeepStorageRoom();
        var idleCarriers = _world.Settlers
            .Where(s => s.IsAvailableCarrier && !_jobs.ContainsKey(s.Id))
            .ToList();
        if (idleCarriers.Count == 0)
            return;

        var offers = _world.Piles.Where(p => p.Available > 0).ToList();
        if (offers.Count == 0)
            return;

        // Construction sites first, then production input, then the storehouse.
        var requests = _world.Piles
            .Where(p => p.Space > 0 && p.Owner?.Exists != false)
            .OrderBy(p => p.Role == PileRole.Storage ? 2 : p.Owner?.State == BuildingState.Finished ? 1 : 0)
            .ThenBy(p => p.Id)
            .ToList();

        var pathFinder = _world.PathFinder!;
        foreach (var request in requests)
        {
            if (idleCarriers.Count == 0)
                break;
            int player = request.Owner?.Player ?? 0;

            Pile? source = null;
            int sourceDistance = int.MaxValue;
            foreach (var offer in offers)
            {
                if (offer.Available <= 0 || offer.Material is not { } material || !request.Accepts(material)
                    || offer.Owner == request.Owner || !_world.BelongsTo(offer, player))
                    continue;
                // Storehouse to storehouse would only shuffle goods around.
                if (offer.Role == PileRole.Storage && request.Role == PileRole.Storage)
                    continue;
                // An empty storehouse pile starts a new material only if no pile there holds it with room
                // left; otherwise one material spreads over all piles and the storehouse is full.
                if (request is { Role: PileRole.Storage, Material: null } && request.Owner!.Piles.Any(o =>
                        o != request && o.Role == PileRole.Storage && o.Material == material && o.Space > 0))
                    continue;
                int distance = PathFinder.Distance(offer.Position, request.Position);
                if (distance < sourceDistance && pathFinder.AreConnected(offer.Position, request.Position))
                {
                    source = offer;
                    sourceDistance = distance;
                }
            }
            if (source is null)
                continue;

            Settler? carrier = null;
            int carrierDistance = int.MaxValue;
            foreach (var candidate in idleCarriers)
            {
                if (candidate.Player != player)
                    continue;
                int distance = PathFinder.Distance(candidate.Position, source.Position);
                if (distance < carrierDistance && pathFinder.AreConnected(candidate.Position, source.Position))
                {
                    carrier = candidate;
                    carrierDistance = distance;
                }
            }
            if (carrier is null)
                continue;

            idleCarriers.Remove(carrier);
            Start(carrier, source, request);
        }
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
