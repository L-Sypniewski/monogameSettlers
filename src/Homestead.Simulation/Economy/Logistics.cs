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
/// <item>A job that takes far longer than its path suggests is aborted the same way.</item>
/// </list>
/// </remarks>
public sealed class Logistics
{
    public const int PickUpTicks = 8;
    public const int DropTicks = 8;

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
            .Where(s => s.IsCarrier && !_jobs.ContainsKey(s.Id) && s.Destination is null && s.Carrying is null
                        && s.State != SettlerState.Stepping)
            .ToList();
        if (idleCarriers.Count == 0)
            return;

        var offers = _world.Piles.Where(p => p.Available > 0).ToList();
        if (offers.Count == 0)
            return;

        // Specific requests (production input) before "anything" requests (storehouse).
        var requests = _world.Piles
            .Where(p => p.Role == PileRole.Request && p.Space > 0 && p.Owner?.Exists != false)
            .OrderBy(p => p.AcceptedMaterial is null ? 1 : 0)
            .ThenBy(p => p.Id)
            .ToList();

        var pathFinder = _world.PathFinder!;
        foreach (var request in requests)
        {
            if (idleCarriers.Count == 0)
                break;

            Pile? source = null;
            int sourceDistance = int.MaxValue;
            foreach (var offer in offers)
            {
                if (offer.Available <= 0 || offer.Material is not { } material || !request.Accepts(material)
                    || offer.Owner == request.Owner)
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
            Start(carrier, source, request, carrierDistance + sourceDistance);
        }
    }

    private void Start(Settler carrier, Pile source, Pile target, int estimatedSteps)
    {
        var material = source.Material!.Value;
        source.Reserved++;
        target.Incoming++;
        target.Material ??= material;

        var job = new TransportJob(carrier, source, target, material, _world.Tick,
            deadline: _world.Tick + 3L * estimatedSteps * World.StepTicks + 400);
        _jobs[carrier.Id] = job;
        if (!_world.WalkTo(carrier, source.Position))
            Cancel(job);
    }

    private void Advance(TransportJob job)
    {
        var carrier = job.Carrier;
        if (_world.Tick > job.Deadline)
        {
            Fail(job);
            return;
        }

        switch (job.Phase)
        {
            case TransportPhase.ToSource:
                if (!_world.HasPile(job.Source) || job.Source.Count == 0)
                {
                    Cancel(job);
                }
                else if (carrier.Destination is null && carrier.State != SettlerState.Stepping)
                {
                    if (carrier.Position == job.Source.Position)
                        BeginWork(job, TransportPhase.PickingUp);
                    else if (!_world.WalkTo(carrier, job.Source.Position))
                        Cancel(job);
                }
                break;

            case TransportPhase.PickingUp:
                if (--job.WorkTicksLeft > 0)
                    break;
                carrier.IsWorking = false;
                if (!_world.HasPile(job.Source) || job.Source.Count == 0)
                {
                    Cancel(job);
                    break;
                }
                _world.TakeFromPile(job.Source);
                carrier.Carrying = job.Material;
                job.Phase = TransportPhase.ToTarget;
                if (!TargetUsable(job) || !_world.WalkTo(carrier, job.Target.Position))
                    Fail(job);
                break;

            case TransportPhase.ToTarget:
                if (!TargetUsable(job))
                {
                    Fail(job);
                }
                else if (carrier.Destination is null && carrier.State != SettlerState.Stepping)
                {
                    if (carrier.Position == job.Target.Position)
                        BeginWork(job, TransportPhase.Dropping);
                    else if (!_world.WalkTo(carrier, job.Target.Position))
                        Fail(job);
                }
                break;

            case TransportPhase.Dropping:
                if (--job.WorkTicksLeft > 0)
                    break;
                carrier.IsWorking = false;
                if (!TargetUsable(job))
                {
                    Fail(job);
                    break;
                }
                job.Target.Incoming--;
                job.Target.Count++;
                job.Target.Material = job.Material;
                carrier.Carrying = null;
                _jobs.Remove(carrier.Id);
                Delivered++;
                break;
        }
    }

    private bool TargetUsable(TransportJob job) => job.Target.Owner?.Exists != false && _world.HasPile(job.Target);

    private void BeginWork(TransportJob job, TransportPhase phase)
    {
        job.Phase = phase;
        job.WorkTicksLeft = phase == TransportPhase.PickingUp ? PickUpTicks : DropTicks;
        job.Carrier.IsWorking = true;
    }

    /// <summary>Before pick-up: release everything, the carrier is free again.</summary>
    private void Cancel(TransportJob job)
    {
        if (job.Phase is TransportPhase.ToSource or TransportPhase.PickingUp)
            job.Source.Reserved = Math.Max(0, job.Source.Reserved - 1);
        ReleaseTarget(job);
        job.Carrier.IsWorking = false;
        StopWalking(job.Carrier);
        _jobs.Remove(job.Carrier.Id);
        Cancelled++;
    }

    /// <summary>Any phase: cancel, and if the carrier holds the good, put it down here.</summary>
    private void Fail(TransportJob job)
    {
        if (job.Carrier.Carrying is not { } material)
        {
            Cancel(job);
            return;
        }
        ReleaseTarget(job);
        job.Carrier.IsWorking = false;
        job.Carrier.Carrying = null;
        StopWalking(job.Carrier);
        _world.DropGoods(job.Carrier.Position, material);
        _jobs.Remove(job.Carrier.Id);
        DroppedOnTheWay++;
    }

    private static void ReleaseTarget(TransportJob job)
    {
        if (job.Target.Incoming > 0)
            job.Target.Incoming--;
        if (job.Target.Role == PileRole.Request && job.Target.Count == 0 && job.Target.Incoming == 0)
            job.Target.Material = job.Target.AcceptedMaterial;
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
    internal TransportJob(Settler carrier, Pile source, Pile target, Material material, long started, long deadline)
    {
        Carrier = carrier;
        Source = source;
        Target = target;
        Material = material;
        Started = started;
        Deadline = deadline;
    }

    public Settler Carrier { get; }
    public Pile Source { get; }
    public Pile Target { get; }
    public Material Material { get; }
    public long Started { get; }
    public long Deadline { get; }
    public TransportPhase Phase { get; internal set; } = TransportPhase.ToSource;
    internal int WorkTicksLeft { get; set; }
}
