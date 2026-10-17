using Homestead.Simulation.Military;
using Homestead.Simulation.Pathfinding;

namespace Homestead.Simulation.Economy;

/// <summary>
/// Pioneers taking land („Land einnehmen“). Told to, a pioneer walks to the nearest field within
/// <see cref="PioneerRules.SearchRadius"/> that he may take – free land, or land of another player that
/// no occupied military building of theirs covers (<see cref="Military.Military.IsProtected"/>) – sets a
/// border post there (<see cref="PioneerRules.ClaimTicks"/>) and the field, with the unwalkable ones
/// around it, becomes his player's; then on to the next, so the land grows piece by piece around where he
/// was sent (ties go to the field nearest to it). Standing in enemy land he heads for free land nearby;
/// with none within reach he stops. Sent off with the order (<see cref="ClaimAt"/>), he walks there first.
/// The land is unprotected: an enemy military building claiming it takes it back.
/// </summary>
public sealed class Pioneering
{
    /// <summary>A walk that makes no progress for this long gives up the field.</summary>
    private const int StallTicks = 200;

    private readonly World _world;
    private readonly Dictionary<int, Job> _jobs = new();
    private readonly Random _random;

    internal Pioneering(World world)
    {
        _world = world;
        _random = new Random(1913 + world.Seed);
    }

    private PioneerRules Rules => _world.Military.Config.Pioneers ?? PioneerRules.Default;

    /// <summary>Whether the pioneer is taking land (or on his way to).</summary>
    public bool IsClaiming(Settler pioneer) => _jobs.ContainsKey(pioneer.Id);

    /// <summary>Whether the player's pioneers may take the point: free land, or another player's land no military building of theirs protects.</summary>
    public bool IsClaimable(GridPoint p, int player) =>
        _world.Map!.Contains(p) && _world.Map.OwnerAt(p) is var owner && owner != player
        && (owner is null || !_world.Military.IsProtected(p));

    /// <summary>The pioneers among <paramref name="settlers"/> take land around them. Returns how many found some.</summary>
    public int Claim(IEnumerable<Settler> settlers)
    {
        int started = 0;
        foreach (var pioneer in Pioneers(settlers))
        {
            _world.Geology.Stop(pioneer);
            Stop(pioneer);
            var job = new Job(pioneer, pioneer.Position);
            _jobs[pioneer.Id] = job;
            pioneer.HasTask = true;
            pioneer.IsStrolling = false;
            if (Next(job))
                started++;
            else
                End(job);
        }
        return started;
    }

    /// <summary>Sends the pioneers to <paramref name="target"/>; there they take land (see <see cref="Claim"/>). Returns how many set off.</summary>
    public int ClaimAt(IEnumerable<Settler> settlers, GridPoint target)
    {
        var pioneers = Pioneers(settlers);
        int sent = _world.Geology.Send(pioneers, target);
        foreach (var pioneer in pioneers.Where(s => s.HasTask))
            _jobs[pioneer.Id] = new Job(pioneer, target) { Approaching = true };
        return sent;
    }

    private static List<Settler> Pioneers(IEnumerable<Settler> settlers) =>
        settlers.Where(s => s.Profession == Profession.Pioneer && !s.IsDead && !s.IsInside).Distinct().ToList();

    /// <summary>Ends what the pioneer is doing.</summary>
    internal void Stop(Settler pioneer)
    {
        if (_jobs.TryGetValue(pioneer.Id, out var job))
            End(job);
    }

    internal void Tick()
    {
        foreach (var job in _jobs.Values.ToList())
        {
            var pioneer = job.Pioneer;
            if (pioneer.IsDead || pioneer.Profession != Profession.Pioneer)
            {
                _jobs.Remove(pioneer.Id);
                continue;
            }
            if (job.Approaching)
            {
                // Arrived (the walk there is over): take land around here.
                if (!_world.Geology.IsSent(pioneer))
                {
                    job.Approaching = false;
                    if (!Next(job))
                        End(job);
                }
                continue;
            }
            if (job.TicksLeft > 0)
            {
                if (--job.TicksLeft > 0)
                    continue;
                pioneer.IsWorking = false;
                pioneer.Activity = Activity.None;
                Take(job.Field, pioneer.Player);
                if (!Next(job))
                    End(job);
                continue;
            }
            if (pioneer.Position == job.Field && pioneer.State != SettlerState.Stepping)
            {
                // Someone else may have taken it meanwhile.
                if (IsClaimable(job.Field, pioneer.Player))
                {
                    job.TicksLeft = Math.Max(1, Rules.ClaimTicks);
                    pioneer.IsWorking = true;
                    pioneer.Activity = Activity.Claiming;
                    pioneer.ActivityTicks = 0;
                }
                else if (!Next(job))
                    End(job);
                continue;
            }
            if (pioneer.Position != job.LastPosition)
            {
                job.LastPosition = pioneer.Position;
                job.LastProgress = _world.Tick;
            }
            bool lost = pioneer.Destination is null && pioneer.State != SettlerState.Stepping;
            if (lost || _world.Tick - job.LastProgress > StallTicks || !IsClaimable(job.Field, pioneer.Player))
            {
                job.Skipped.Add(job.Field);
                if (!Next(job))
                    End(job);
            }
        }
    }

    /// <summary>The field and the unwalkable ones around it (trees, rocks, water he cannot stand on) become the player's.</summary>
    private void Take(GridPoint field, int player)
    {
        var map = _world.Map!;
        if (!IsClaimable(field, player))
            return;
        _world.Military.TakeLand(field, player);
        for (int d = 0; d < DirectionExtensions.Count; d++)
        {
            var n = field.Neighbor((Direction)d);
            if (map.Contains(n) && !map.IsWalkable(n) && IsClaimable(n, player))
                _world.Military.TakeLand(n, player);
        }
    }

    /// <summary>
    /// Walks to the nearest field around the pioneer he may take (ties: nearest to where he was sent,
    /// then at random), not one another pioneer goes to. False if there is none.
    /// </summary>
    private bool Next(Job job)
    {
        var pioneer = job.Pioneer;
        var map = _world.Map!;
        var taken = _jobs.Values.Where(j => j != job && !j.Approaching).Select(j => j.Field).ToHashSet();
        var candidates = new List<GridPoint>();
        int r = Rules.SearchRadius;
        foreach (var p in PathFinder.Circle(pioneer.Position, r))
            if (map.Contains(p) && map.IsWalkable(p) && !taken.Contains(p) && !job.Skipped.Contains(p)
                && (!_world.IsOccupied(p) || p == pioneer.Position) && IsClaimable(p, pioneer.Player))
                candidates.Add(p);

        foreach (var field in candidates
                     .GroupBy(p => (PathFinder.Distance(pioneer.Position, p), PathFinder.Distance(job.Center, p)))
                     .OrderBy(g => g.Key.Item1).ThenBy(g => g.Key.Item2)
                     .SelectMany(g => g.OrderBy(_ => _random.Next())))
        {
            if (field != pioneer.Position && (!_world.Reachable(pioneer, field) || !_world.WalkTo(pioneer, field)))
            {
                job.Skipped.Add(field);
                continue;
            }
            job.Field = field;
            job.LastPosition = pioneer.Position;
            job.LastProgress = _world.Tick;
            return true;
        }
        return false;
    }

    private void End(Job job)
    {
        _jobs.Remove(job.Pioneer.Id);
        job.Pioneer.HasTask = false;
        job.Pioneer.IsWorking = false;
        job.Pioneer.Activity = Activity.None;
    }

    private sealed class Job(Settler pioneer, GridPoint center)
    {
        public Settler Pioneer { get; } = pioneer;

        /// <summary>Where he was sent: the land grows around it.</summary>
        public GridPoint Center { get; } = center;

        /// <summary>Still walking to where he was sent; he starts on arrival.</summary>
        public bool Approaching { get; set; }

        public GridPoint Field { get; set; } = center;

        /// <summary>Setting the border post: ticks left (0: walking to <see cref="Field"/>).</summary>
        public int TicksLeft { get; set; }

        public GridPoint LastPosition { get; set; }
        public long LastProgress { get; set; }

        /// <summary>Fields he could not reach.</summary>
        public HashSet<GridPoint> Skipped { get; } = new();
    }
}
