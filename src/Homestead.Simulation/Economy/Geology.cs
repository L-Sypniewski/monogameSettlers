using Homestead.Simulation.Map;
using Homestead.Simulation.Military;
using Homestead.Simulation.Pathfinding;

namespace Homestead.Simulation.Economy;

/// <summary>What a geologist found on a field: a resource (null: nothing) and its amount (0-15).</summary>
public sealed record ResourceSign(int Player, ResourceType? Resource, int Amount, long ExpiresAt);

/// <summary>
/// Geologists. The player turns carriers into geologists (as far as the carrier reserve allows) and
/// sends them to a mountain. There a geologist examines field after field, every second one: next the
/// nearest field with no sign on it or next to it (a random one among equally near ones), always within
/// <see cref="GeologistRules.SearchRadius"/> of the point he was sent to. Each search leaves a sign with
/// what lies below; signs fade after a few minutes. When no field is left he is done. Sent anywhere
/// else, geologists just walk there like soldiers, spread over the free fields around the point.
/// Scouts are made and sent the same way, but never search: they always just walk there.
/// </summary>
public sealed class Geology
{
    /// <summary>A walk that makes no progress for this long gives up the field.</summary>
    private const int StallTicks = 200;

    private readonly World _world;
    private readonly GeologistRules _rules;
    private readonly Dictionary<int, Job> _jobs = new();
    private readonly Dictionary<GridPoint, ResourceSign> _signs = new();

    /// <summary>Carriers that become geologists or scouts once their current delivery is done.</summary>
    private readonly Dictionary<Settler, Profession> _conversions = new();
    private readonly Random _random;

    internal Geology(World world, ProductionConfig config)
    {
        _world = world;
        _random = new Random(4711 + world.Seed);
        _rules = config.Geologists;
    }

    public IReadOnlyDictionary<GridPoint, ResourceSign> Signs => _signs;

    /// <summary>Whether the geologist (or scout) is on an errand: walking where he was sent or searching.</summary>
    public bool IsSent(Settler settler) => _jobs.ContainsKey(settler.Id);

    /// <summary>Whether a geologist is searching (sent to a mountain and not done yet).</summary>
    public bool IsSearching(Settler geologist) => _jobs.TryGetValue(geologist.Id, out var job) && job.Phase != Phase.Moving;

    /// <summary>
    /// Turns the carriers among <paramref name="settlers"/> into geologists (or scouts, pioneers), one after another
    /// while the carrier reserve allows: free ones at once, busy ones after their delivery. Returns how many
    /// converted now and how many will follow.
    /// </summary>
    public (int Now, int Later) Convert(IEnumerable<Settler> settlers, Profession profession = Profession.Geologist)
    {
        int now = 0, later = 0;
        foreach (var settler in settlers.Where(s => s.IsCarrier && !s.IsDead).ToList())
        {
            // Busy carriers keep their place in the reserve count: they convert only if it still allows then.
            if (!_world.Recruitment.MayConvert(settler.Player, reserved: _conversions.Keys.Count(p => p.Player == settler.Player)))
                break;
            if (settler.IsAvailableCarrier)
            {
                Make(settler, profession);
                now++;
            }
            else if (_conversions.TryAdd(settler, profession))
            {
                settler.ConversionPending = true;
                later++;
            }
        }
        return (now, later);
    }

    /// <summary>
    /// Turns up to <paramref name="count"/> carriers of the player into geologists or scouts (Siedler › Berufe):
    /// idle ones first, then busy ones after their delivery, as far as the carrier reserve allows.
    /// </summary>
    public (int Now, int Later) ConvertCarriers(int player, int count, Profession profession = Profession.Geologist) =>
        Convert(_world.Settlers
            .Where(s => s.Player == player && s.IsCarrier && !s.IsDead && !s.ConversionPending && !_world.Recruitment.Active.Any(r => r.Settler == s))
            .OrderBy(s => s.IsAvailableCarrier ? 0 : 1)
            .ThenBy(s => s.Id)
            .Take(count), profession);

    private void Make(Settler settler, Profession profession)
    {
        settler.Profession = profession;
        settler.Trade = null;
        settler.Health = HealthOf(_world.Military.Config, profession);
    }

    /// <summary>Health of the professions soldiers can fight without them striking back.</summary>
    internal static float HealthOf(MilitaryConfig config, Profession profession) => profession switch
    {
        Profession.Scout => (config.Scouts ?? ScoutRules.Default).Health,
        Profession.Pioneer => (config.Pioneers ?? PioneerRules.Default).Health,
        _ => config.GeologistHealth,
    };

    /// <summary>
    /// Turns up to <paramref name="count"/> pioneers of the player back into carriers (Siedler › Berufe), idle
    /// ones first. Returns how many.
    /// </summary>
    public int PioneersToCarriers(int player, int count)
    {
        var pioneers = _world.Settlers
            .Where(s => s.Player == player && s.Profession == Profession.Pioneer && !s.IsDead && !s.IsInside)
            .OrderBy(s => s.HasTask ? 1 : 0).ThenBy(s => s.Id).Take(count).ToList();
        foreach (var pioneer in pioneers)
        {
            Stop(pioneer);
            _world.Pioneering.Stop(pioneer);
            pioneer.Profession = Profession.Carrier;
            pioneer.Health = 0;
        }
        return pioneers.Count;
    }

    /// <summary>Fields geologists examine: mountain ground.</summary>
    public bool IsSearchable(GridPoint p) =>
        _world.Map!.Contains(p) && _world.Map.TerrainAt(p) is TerrainType.Mountain or TerrainType.MountainBorder;

    /// <summary>
    /// Sends geologists, scouts and pioneers to <paramref name="target"/>: on a mountain geologists search around it,
    /// elsewhere (and scouts and pioneers always) they walk there and spread over the free fields around it. Returns how
    /// many set off.
    /// </summary>
    public int Send(IEnumerable<Settler> geologists, GridPoint target)
    {
        var list = geologists.Where(g => g.Profession is Profession.Geologist or Profession.Scout or Profession.Pioneer && !g.IsDead && !g.IsInside).Distinct().ToList();
        foreach (var geologist in list)
        {
            Stop(geologist);
            _world.Theft.Stop(geologist);
            _world.Pioneering.Stop(geologist);
        }
        if (!IsSearchable(target))
            return Move(list, target);
        int sent = Move(list.Where(s => s.Profession != Profession.Geologist).ToList(), target);
        foreach (var geologist in list.Where(s => s.Profession == Profession.Geologist))
        {
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

    /// <summary>Walks the geologists (or scouts) to the walkable fields nearest to <paramref name="target"/>, one each.</summary>
    private int Move(List<Settler> geologists, GridPoint target)
    {
        var map = _world.Map!;
        var spots = new List<GridPoint>();
        for (int r = 0; r <= 12 && spots.Count < geologists.Count; r++)
            for (int dy = -r; dy <= r && spots.Count < geologists.Count; dy++)
            for (int dx = -r; dx <= r && spots.Count < geologists.Count; dx++)
            {
                var p = new GridPoint(target.X + dx, target.Y + dy);
                if (PathFinder.Distance(target, p) == r && map.Contains(p) && map.IsWalkable(p))
                    spots.Add(p);
            }
        int sent = 0;
        for (int i = 0; i < geologists.Count; i++)
        {
            var geologist = geologists[i];
            var spot = spots.Count > i ? spots[i] : target;
            if (spot != geologist.Position && !_world.WalkTo(geologist, spot))
                continue;
            var job = new Job(geologist, spot) { Phase = Phase.Moving, LastPosition = geologist.Position, LastProgress = _world.Tick };
            _jobs[geologist.Id] = job;
            geologist.HasTask = true;
            geologist.IsStrolling = false;
            sent++;
        }
        return sent;
    }

    /// <summary>Ends what the geologist (or scout) was sent to do.</summary>
    internal void Stop(Settler settler)
    {
        if (_jobs.TryGetValue(settler.Id, out var job))
            End(job);
    }

    internal void Tick()
    {
        foreach (var (settler, profession) in _conversions.ToList())
        {
            if (settler.IsDead || !settler.IsCarrier)
            {
                _conversions.Remove(settler);
                settler.ConversionPending = false;
            }
            else if (!settler.HasTask && settler.Carrying is null)
            {
                _conversions.Remove(settler);
                settler.ConversionPending = false;
                if (_world.Recruitment.MayConvert(settler.Player))
                    Make(settler, profession);
            }
        }

        foreach (var (point, sign) in _signs.Where(s => s.Value.ExpiresAt <= _world.Tick).ToList())
            _signs.Remove(point);

        foreach (var job in _jobs.Values.ToList())
        {
            var geologist = job.Geologist;
            if (geologist.IsDead || geologist.Profession is not (Profession.Geologist or Profession.Scout or Profession.Pioneer))
            {
                _jobs.Remove(geologist.Id);
                continue;
            }
            switch (job.Phase)
            {
                case Phase.Moving:
                    // Done at the field, without a way, or stuck: he stands there idle.
                    if (geologist.Position != job.LastPosition)
                    {
                        job.LastPosition = geologist.Position;
                        job.LastProgress = _world.Tick;
                    }
                    if (geologist.State != SettlerState.Stepping && geologist.Destination is null
                        || _world.Tick - job.LastProgress > StallTicks)
                        End(job);
                    break;
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
        var taken = _jobs.Values.Where(j => j != job && j.Phase != Phase.Moving).Select(j => j.Field).ToHashSet();
        // Only every second field: none next to a sign or to a field another geologist goes to.
        bool Spaced(GridPoint p)
        {
            if (_signs.ContainsKey(p) || taken.Contains(p))
                return false;
            for (int d = 0; d < DirectionExtensions.Count; d++)
            {
                var n = p.Neighbor((Direction)d);
                if (_signs.ContainsKey(n) || taken.Contains(n))
                    return false;
            }
            return true;
        }
        bool Free(GridPoint p) =>
            IsSearchable(p) && Spaced(p) && !job.Skipped.Contains(p)
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

        /// <summary>Only walking to a point off the mountains (no search).</summary>
        Moving,
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
