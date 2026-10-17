using Homestead.Simulation.Military;
using Homestead.Simulation.Pathfinding;

namespace Homestead.Simulation.Economy;

/// <summary>
/// Scouts stealing goods. Told to steal, a scout takes the nearest good of somebody else within
/// <see cref="ScoutRules.StealRadius"/> (any pile with a unit nobody is coming for), walks there and
/// pockets one unit. Loaded, he sneaks at <see cref="ScoutRules.LoadedSpeed"/> and takes nothing more;
/// as soon as he stands still on his own land he drops it there – where he dies too. Sent off with
/// the order to steal (<see cref="StealAt"/>), he walks there first and starts looking on arrival.
/// </summary>
public sealed class Theft
{
    /// <summary>A walk that makes no progress for this long gives up the pile.</summary>
    private const int StallTicks = 200;

    private readonly World _world;
    private readonly Dictionary<int, Job> _jobs = new();

    internal Theft(World world) => _world = world;

    private ScoutRules Rules => _world.Military.Config.Scouts ?? ScoutRules.Default;

    /// <summary>Whether the scout is on his way to steal (walking there or to the goods).</summary>
    public bool IsStealing(Settler scout) => _jobs.ContainsKey(scout.Id);

    /// <summary>
    /// The scouts among <paramref name="settlers"/> that carry nothing go for the nearest goods around
    /// them. Returns how many found some.
    /// </summary>
    public int Steal(IEnumerable<Settler> settlers)
    {
        int started = 0;
        foreach (var scout in Scouts(settlers))
        {
            _world.Geology.Stop(scout);
            Stop(scout);
            if (scout.Carrying is not null)
                continue;
            var job = new Job(scout);
            _jobs[scout.Id] = job;
            scout.HasTask = true;
            scout.IsStrolling = false;
            if (Next(job))
                started++;
            else
                End(job);
        }
        return started;
    }

    /// <summary>
    /// Sends the scouts to <paramref name="target"/>; there they steal (see <see cref="Steal"/>). With
    /// <paramref name="ranks"/> they take the best ranked good around them (lowest rank first, goods
    /// without one last), nearest among equals. Returns how many set off.
    /// </summary>
    public int StealAt(IEnumerable<Settler> settlers, GridPoint target, IReadOnlyDictionary<Material, int>? ranks = null)
    {
        var scouts = Scouts(settlers).Where(s => s.Carrying is null).ToList();
        int sent = _world.Geology.Send(scouts, target);
        foreach (var scout in scouts.Where(s => s.HasTask))
            _jobs[scout.Id] = new Job(scout) { Approaching = true, Ranks = ranks };
        return sent;
    }

    private static IEnumerable<Settler> Scouts(IEnumerable<Settler> settlers) =>
        settlers.Where(s => s.Profession == Profession.Scout && !s.IsDead && !s.IsInside).Distinct().ToList();

    /// <summary>Ends the scout's theft (he keeps what he carries).</summary>
    internal void Stop(Settler scout)
    {
        if (_jobs.TryGetValue(scout.Id, out var job))
            End(job);
    }

    internal void Tick()
    {
        foreach (var job in _jobs.Values.ToList())
        {
            var scout = job.Scout;
            if (scout.IsDead || scout.Profession != Profession.Scout)
            {
                _jobs.Remove(scout.Id);
                continue;
            }
            if (job.Approaching)
            {
                // Arrived (the walk there is over): look for goods.
                if (!_world.Geology.IsSent(scout))
                {
                    job.Approaching = false;
                    job.Skipped.Clear();
                    if (!Next(job))
                        End(job);
                }
                continue;
            }
            if (scout.Position == job.Pile!.Position && scout.State != SettlerState.Stepping)
            {
                if (_world.StealFromPile(job.Pile))
                {
                    scout.Carrying = job.Material;
                    End(job);
                }
                else if (!Next(job))
                    End(job);
                continue;
            }
            if (scout.Position != job.LastPosition)
            {
                job.LastPosition = scout.Position;
                job.LastProgress = _world.Tick;
            }
            bool lost = scout.Destination is null && scout.State != SettlerState.Stepping;
            if (lost || _world.Tick - job.LastProgress > StallTicks || job.Pile.Count - job.Pile.Reserved <= 0 || !_world.HasPile(job.Pile))
            {
                job.Skipped.Add(job.Pile.Id);
                if (!Next(job))
                    End(job);
            }
        }

        // Loaded scouts standing still on their own land drop the goods.
        var map = _world.Map!;
        foreach (var scout in _world.Settlers)
            if (scout is { Profession: Profession.Scout, Carrying: { } stolen, IsInside: false } && scout.State != SettlerState.Stepping
                && scout.Destination is null && map.OwnerAt(scout.Position) == scout.Player)
            {
                scout.Carrying = null;
                _world.DropGoods(scout.Position, stolen);
            }
    }

    /// <summary>Walks to the nearest (best ranked) pile around the scout with a unit to take. False if there is none.</summary>
    private bool Next(Job job)
    {
        var scout = job.Scout;
        int radius = Rules.StealRadius;
        foreach (var pile in _world.Piles
                     .Where(p => p.Material is not null && p.Count - p.Reserved > 0 && !job.Skipped.Contains(p.Id)
                                 && PathFinder.Distance(scout.Position, p.Position) <= radius && !IsOwn(p, scout.Player))
                     .OrderBy(p => job.Ranks?.GetValueOrDefault(p.Material!.Value, int.MaxValue) ?? 0)
                     .ThenBy(p => PathFinder.Distance(scout.Position, p.Position)).ThenBy(p => p.Id).ToList())
        {
            if (pile.Position != scout.Position && !_world.WalkTo(scout, pile.Position))
            {
                job.Skipped.Add(pile.Id);
                continue;
            }
            job.Pile = pile;
            job.Material = pile.Material!.Value;
            job.LastPosition = scout.Position;
            job.LastProgress = _world.Tick;
            return true;
        }
        return false;
    }

    /// <summary>Piles of the own buildings and loose goods on the own land are not stolen.</summary>
    private bool IsOwn(Pile pile, int player) =>
        pile.Owner is { } owner ? owner.Player == player : _world.Map!.OwnerAt(pile.Position) == player;

    private void End(Job job)
    {
        _jobs.Remove(job.Scout.Id);
        job.Scout.HasTask = false;
    }

    private sealed class Job(Settler scout)
    {
        public Settler Scout { get; } = scout;

        /// <summary>Still walking to where he was sent; he looks for goods on arrival.</summary>
        public bool Approaching { get; set; }

        /// <summary>Which goods he prefers (lowest rank first), or null for the nearest.</summary>
        public IReadOnlyDictionary<Material, int>? Ranks { get; init; }

        public Pile? Pile { get; set; }
        public Material Material { get; set; }
        public GridPoint LastPosition { get; set; }
        public long LastProgress { get; set; }

        /// <summary>Piles he could not reach.</summary>
        public HashSet<int> Skipped { get; } = new();
    }
}
