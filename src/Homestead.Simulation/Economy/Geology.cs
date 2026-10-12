using Homestead.Simulation.Map;
using Homestead.Simulation.Pathfinding;

namespace Homestead.Simulation.Economy;

/// <summary>What a geologist found on a field: a resource (null: nothing) and its amount (0-15).</summary>
public sealed record ResourceSign(int Player, ResourceType? Resource, int Amount, long ExpiresAt);

/// <summary>
/// Geologists. The player turns carriers into geologists (as far as the carrier reserve allows) and
/// sends them to a mountain. There a geologist examines field after field: next the nearest field
/// without a sign (a random one among equally near ones), always within
/// <see cref="GeologistRules.SearchRadius"/> of the point he was sent to. Each search leaves a sign with
/// what lies below; signs fade after a few minutes. When no field is left he is done.
/// </summary>
public sealed class Geology
{
    /// <summary>A walk that makes no progress for this long gives up the field.</summary>
    private const int StallTicks = 200;

    private readonly World _world;
    private readonly GeologistRules _rules;
    private readonly Dictionary<int, Job> _jobs = new();
    private readonly Dictionary<GridPoint, ResourceSign> _signs = new();

    /// <summary>Carriers that become geologists once their current delivery is done.</summary>
    private readonly HashSet<Settler> _pending = new();
    private readonly Random _random = new(4711);

    internal Geology(World world, ProductionConfig config)
    {
        _world = world;
        _rules = config.Geologists;
    }

    public IReadOnlyDictionary<GridPoint, ResourceSign> Signs => _signs;

    /// <summary>Whether a geologist is searching (sent to a mountain and not done yet).</summary>
    public bool IsSearching(Settler geologist) => _jobs.ContainsKey(geologist.Id);

    /// <summary>
    /// Turns the carriers among <paramref name="settlers"/> into geologists, one after another while the
    /// carrier reserve allows: free ones at once, busy ones after their delivery. Returns how many became
    /// geologists now and how many will follow.
    /// </summary>
    public (int Now, int Later) Convert(IEnumerable<Settler> settlers)
    {
        int now = 0, later = 0;
        foreach (var settler in settlers.Where(s => s.IsCarrier && !s.IsDead).ToList())
        {
            // Busy carriers keep their place in the reserve count: they convert only if it still allows then.
            if (!_world.Recruitment.MayConvert(settler.Player, reserved: _pending.Count(p => p.Player == settler.Player)))
                break;
            if (settler.IsAvailableCarrier)
            {
                MakeGeologist(settler);
                now++;
            }
            else if (_pending.Add(settler))
            {
                settler.ConversionPending = true;
                later++;
            }
        }
        return (now, later);
    }

    private static void MakeGeologist(Settler settler)
    {
        settler.Profession = Profession.Geologist;
        settler.Trade = null;
    }

    /// <summary>Fields geologists examine: mountain ground.</summary>
    public bool IsSearchable(GridPoint p) =>
        _world.Map!.Contains(p) && _world.Map.TerrainAt(p) is TerrainType.Mountain or TerrainType.MountainBorder;

    /// <summary>
    /// Sends geologists to search the mountain around <paramref name="target"/>. Returns how many set
    /// off (none if the point is no mountain or nothing there can be reached).
    /// </summary>
    public int Send(IEnumerable<Settler> geologists, GridPoint target)
    {
        if (!IsSearchable(target))
            return 0;
        int sent = 0;
        foreach (var geologist in geologists.Where(g => g.Profession == Profession.Geologist && !g.IsDead && !g.IsInside).ToList())
        {
            if (_jobs.TryGetValue(geologist.Id, out var old))
                End(old);
            var job = new Job(geologist, target);
            _jobs[geologist.Id] = job;
            geologist.HasTask = true;
            geologist.IsStrolling = false;
            if (Next(job, preferred: target))
                sent++;
            else
                End(job);
        }
        return sent;
    }

    internal void Tick()
    {
        foreach (var settler in _pending.ToList())
        {
            if (settler.IsDead || !settler.IsCarrier)
            {
                _pending.Remove(settler);
                settler.ConversionPending = false;
            }
            else if (!settler.HasTask && settler.Carrying is null)
            {
                _pending.Remove(settler);
                settler.ConversionPending = false;
                if (_world.Recruitment.MayConvert(settler.Player))
                    MakeGeologist(settler);
            }
        }

        foreach (var (point, sign) in _signs.Where(s => s.Value.ExpiresAt <= _world.Tick).ToList())
            _signs.Remove(point);

        foreach (var job in _jobs.Values.ToList())
        {
            var geologist = job.Geologist;
            if (geologist.IsDead || geologist.Profession != Profession.Geologist)
            {
                _jobs.Remove(geologist.Id);
                continue;
            }
            switch (job.Phase)
            {
                case Phase.Walking when geologist.Position == job.Field && geologist.State != SettlerState.Stepping:
                    Begin(job, Phase.Surveying, Activity.Surveying, _rules.SurveyTicks);
                    break;
                case Phase.Walking:
                    // Stuck or no way: give up this field, take the next.
                    if (geologist.Position != job.LastPosition)
                    {
                        job.LastPosition = geologist.Position;
                        job.LastProgress = _world.Tick;
                    }
                    bool lost = geologist.Destination is null && geologist.State != SettlerState.Stepping;
                    if (lost || _world.Tick - job.LastProgress > StallTicks)
                    {
                        job.Skipped.Add(job.Field);
                        if (!Next(job))
                            End(job);
                    }
                    break;
                case Phase.Surveying when --job.TicksLeft <= 0:
                    Begin(job, Phase.Signing, Activity.Signing, _rules.SignTicks);
                    break;
                case Phase.Signing when --job.TicksLeft <= 0:
                    PlaceSign(job.Field, geologist.Player);
                    geologist.IsWorking = false;
                    geologist.Activity = Activity.None;
                    if (!Next(job))
                        End(job);
                    break;
            }
        }
    }

    private void Begin(Job job, Phase phase, Activity activity, int ticks)
    {
        job.Phase = phase;
        job.TicksLeft = Math.Max(1, ticks);
        job.Geologist.IsWorking = true;
        job.Geologist.Activity = activity;
        job.Geologist.ActivityTicks = 0;
    }

    private void PlaceSign(GridPoint p, int player)
    {
        var (type, amount) = _world.Map!.ResourceAt(p);
        bool found = type != ResourceType.Fish && amount > 0;
        long lifetime = _rules.SignMinTicks + (long)(_random.NextDouble() * Math.Max(0, _rules.SignMaxTicks - _rules.SignMinTicks));
        _signs[p] = new ResourceSign(player, found ? type : null, found ? amount : 0, _world.Tick + lifetime);
    }

    /// <summary>
    /// Walks to the next field: <paramref name="preferred"/> if it is free, else the nearest free one
    /// (random among equally near ones). False if none is left.
    /// </summary>
    private bool Next(Job job, GridPoint? preferred = null)
    {
        var geologist = job.Geologist;
        var taken = _jobs.Values.Where(j => j != job).Select(j => j.Field).ToHashSet();
        bool Free(GridPoint p) =>
            IsSearchable(p) && !_signs.ContainsKey(p) && !taken.Contains(p) && !job.Skipped.Contains(p)
            && _world.Map!.IsWalkable(p) && (!_world.IsOccupied(p) || p == geologist.Position);

        var candidates = new List<GridPoint>();
        if (preferred is { } first && Free(first))
            candidates.Add(first);
        else
        {
            int r = _rules.SearchRadius;
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                var p = new GridPoint(job.Center.X + dx, job.Center.Y + dy);
                if (PathFinder.Distance(job.Center, p) <= r && Free(p))
                    candidates.Add(p);
            }
        }

        // Nearest first; equally near ones in random order.
        foreach (var field in candidates.GroupBy(p => PathFinder.Distance(geologist.Position, p)).OrderBy(g => g.Key)
                     .SelectMany(g => g.OrderBy(_ => _random.Next())))
        {
            if (!_world.Reachable(geologist, field))
            {
                job.Skipped.Add(field);
                continue;
            }
            if (field != geologist.Position && !_world.WalkTo(geologist, field))
            {
                job.Skipped.Add(field);
                continue;
            }
            job.Field = field;
            job.Phase = Phase.Walking;
            job.LastPosition = geologist.Position;
            job.LastProgress = _world.Tick;
            return true;
        }
        return false;
    }

    private void End(Job job)
    {
        _jobs.Remove(job.Geologist.Id);
        job.Geologist.HasTask = false;
        job.Geologist.IsWorking = false;
        job.Geologist.Activity = Activity.None;
    }

    private enum Phase
    {
        Walking,
        Surveying,
        Signing,
    }

    private sealed class Job(Settler geologist, GridPoint center)
    {
        public Settler Geologist { get; } = geologist;

        /// <summary>The point the geologist was sent to: he stays within the search radius of it.</summary>
        public GridPoint Center { get; } = center;

        public GridPoint Field { get; set; } = center;
        public Phase Phase { get; set; }
        public int TicksLeft { get; set; }
        public GridPoint LastPosition { get; set; }
        public long LastProgress { get; set; }

        /// <summary>Fields this geologist could not reach.</summary>
        public HashSet<GridPoint> Skipped { get; } = new();
    }
}
