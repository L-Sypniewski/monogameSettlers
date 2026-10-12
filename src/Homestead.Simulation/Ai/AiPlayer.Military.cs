using Homestead.Simulation.Buildings;
using Homestead.Simulation.Pathfinding;

namespace Homestead.Simulation.Ai;

/// <summary>
/// The AI's soldiers outside buildings. It knows where every enemy soldier is (for now). Defence comes
/// first: enemies on the own land are fought at once, and enough soldiers stay back against enemies
/// close to it. Spare soldiers strike enemy groups near the land when they clearly outnumber them
/// (guerilla) and go on campaigns against enemy military buildings (see AiPlayer.Campaign). Strikes
/// and campaigns are re-checked every think (follow, reinforce, retreat); afterwards the soldiers go
/// home. The rest stand guard at the own military buildings, most where enemies are closest.
/// </summary>
public sealed partial class AiPlayer
{
    private long _nextMilitary;
    private readonly List<Strike> _strikes = new();

    /// <summary>A running guerilla attack: who strikes, whom, and where the enemies were last.</summary>
    private sealed class Strike(List<Settler> soldiers, HashSet<Settler> targets, GridPoint center)
    {
        public List<Settler> Soldiers { get; } = soldiers;
        public HashSet<Settler> Targets { get; set; } = targets;
        public GridPoint Center { get; set; } = center;
    }

    /// <summary>Enemy soldiers outside buildings, grouped (seen at the last military think).</summary>
    public IReadOnlyList<EnemyGroup> EnemyGroups { get; private set; } = [];

    public sealed record EnemyGroup(GridPoint Center, IReadOnlyList<Settler> Soldiers, double Strength);

    private void Command(List<Building> mine)
    {
        var config = _config.Military;
        EnemyGroups = FindEnemyGroups(config.GroupRadius);
        var military = mine.Where(b => IsMilitary(b) && b.State == BuildingState.Finished).ToList();
        if (military.Count == 0)
            return;
        var idle = IdleSoldiers();
        var map = _world.Map!;
        bool onOwnLand(EnemyGroup g) => map.OwnerAt(g.Center) == Player;
        bool near(EnemyGroup g, int range) => military.Any(b => PathFinder.Distance(b.Position, g.Center) <= range);
        bool untargeted(EnemyGroup g) => !_strikes.Any(k => g.Soldiers.Any(k.Targets.Contains));

        // Defence first: enemies on the own land are fought with any idle soldier.
        foreach (var group in EnemyGroups.Where(g => onOwnLand(g) && untargeted(g)).OrderByDescending(g => g.Strength))
            TryStrike(group, idle, "defends against");

        // Against enemies close to the land enough soldiers stay back; only the rest is spare.
        double danger = EnemyGroups.Where(g => !onOwnLand(g) && near(g, config.ThreatRange)).Sum(g => g.Strength) * config.StrikeRatio;
        var guards = new List<Settler>();
        double kept = 0;
        foreach (var soldier in idle.OrderBy(s => EnemyGroups.Select(g => PathFinder.Distance(s.Position, g.Center)).DefaultIfEmpty(0).Min()))
        {
            if (kept >= danger)
                break;
            guards.Add(soldier);
            kept += Strength(soldier);
        }
        var spare = idle.Except(guards).ToList();

        // Guerilla outside the own land, with spare soldiers only: the weakest worthwhile group first.
        foreach (var group in EnemyGroups.Where(g => !onOwnLand(g) && near(g, config.StrikeRange) && untargeted(g)).OrderBy(g => g.Strength))
            if (TryStrike(group, spare, "strikes"))
                break;

        // A campaign against enemy land, never while enemies stand on the own land.
        if (_campaign is null && !EnemyGroups.Any(onOwnLand))
            StartCampaign(military, spare);

        Guard(military, guards.Concat(spare).ToList());
    }

    /// <summary>
    /// Sends the nearest soldiers of <paramref name="pool"/> against the group if they get strikeRatio
    /// times the strength they will meet: the group plus enemy groups waiting on the way there.
    /// </summary>
    private bool TryStrike(EnemyGroup group, List<Settler> pool, string verb)
    {
        var config = _config.Military;
        var candidates = pool.Where(s => PathFinder.Distance(s.Position, group.Center) <= config.StrikeRange * 2)
            .OrderBy(s => PathFinder.Distance(s.Position, group.Center)).ToList();
        if (candidates.Count == 0)
            return false;
        double opposition = Opposition(group, candidates[0].Position);
        var strikers = new List<Settler>();
        double strength = 0;
        foreach (var soldier in candidates)
        {
            if (strength >= opposition * config.StrikeRatio)
                break;
            strikers.Add(soldier);
            strength += Strength(soldier);
        }
        if (strength < opposition * config.StrikeRatio)
            return false;
        _world.Military.Move(strikers, group.Center);
        _strikes.Add(new Strike(strikers, group.Soldiers.ToHashSet(), group.Center));
        Note($"{verb} {group.Soldiers.Count} enemies at {group.Center.X},{group.Center.Y} with {strikers.Count} soldiers");
        pool.RemoveAll(strikers.Contains);
        return true;
    }

    /// <summary>The soldiers guard the own military buildings, more where enemies are close; without a threat at home.</summary>
    private void Guard(List<Building> military, List<Settler> idle)
    {
        var config = _config.Military;
        var threat = military.ToDictionary(b => b, b => EnemyGroups
            .Where(g => PathFinder.Distance(b.Position, g.Center) <= config.ThreatRange)
            .Sum(g => g.Strength / (1 + PathFinder.Distance(b.Position, g.Center))));
        double total = threat.Values.Sum();
        var posts = new List<Building>();
        if (total <= 0)
            posts.AddRange(Enumerable.Repeat(_home is { } h && IsMilitary(h) ? h : military[0], idle.Count));
        else
            foreach (var (building, value) in threat.Where(t => t.Value > 0).OrderByDescending(t => t.Value))
                posts.AddRange(Enumerable.Repeat(building, (int)Math.Ceiling(idle.Count * value / total)));
        foreach (var post in posts.Distinct())
        {
            int wanted = posts.Count(p => p == post);
            // Who is already there stays; the nearest of the others come.
            int present = idle.Count(s => PathFinder.Distance(s.Position, post.Door) <= config.GuardDistance);
            var coming = idle.Where(s => PathFinder.Distance(s.Position, post.Door) > config.GuardDistance)
                .OrderBy(s => PathFinder.Distance(s.Position, post.Door)).Take(wanted - present).ToList();
            idle.RemoveAll(s => PathFinder.Distance(s.Position, post.Door) <= config.GuardDistance);
            if (coming.Count == 0)
                continue;
            _world.Military.Move(coming, post.Door);
            idle.RemoveAll(coming.Contains);
        }
    }

    /// <summary>Where soldiers go when a strike or campaign is over.</summary>
    private GridPoint HomeDoor() => _home is { Exists: true } h && h.Player == Player ? h.Door
        : _world.Buildings.Where(b => b.Exists && b.Player == Player && IsMilitary(b)).Select(b => b.Door).FirstOrDefault();

    /// <summary>The group's strength plus that of other enemy groups near the way from <paramref name="from"/> to it.</summary>
    private double Opposition(EnemyGroup group, GridPoint from)
    {
        int clearance = _config.Attack.PathClearance;
        int steps = Math.Max(1, PathFinder.Distance(from, group.Center) / 3);
        var onTheWay = EnemyGroups.Where(g => !g.Soldiers.Any(group.Soldiers.Contains) && Enumerable.Range(0, steps + 1).Any(i =>
            PathFinder.Distance(g.Center, new GridPoint(from.X + (group.Center.X - from.X) * i / steps,
                from.Y + (group.Center.Y - from.Y) * i / steps)) <= clearance));
        return group.Strength + onTheWay.Sum(g => g.Strength);
    }

    /// <summary>Own soldiers free for orders: outside, idle, not part of a running strike or campaign.</summary>
    private List<Settler> IdleSoldiers() => _world.Settlers
        .Where(s => s.Player == Player && s.Profession == Profession.Soldier && !s.HasTask && !s.IsInside && !s.IsDead
                    && !_strikes.Any(k => k.Soldiers.Contains(s)) && _campaign?.Soldiers.Contains(s) != true)
        .ToList();

    /// <summary>
    /// Checks the running strikes every think: the enemy group is found again (it may have moved or
    /// got reinforcements), the strikers follow it, idle soldiers nearby join when the odds got worse,
    /// and when they are still too weak the strike is called off and the strikers retreat home.
    /// </summary>
    private void FollowStrikes(List<Building> mine)
    {
        if (_strikes.Count == 0)
            return;
        var config = _config.Military;
        var groups = FindEnemyGroups(config.GroupRadius);
        EnemyGroups = groups;
        foreach (var strike in _strikes.ToList())
        {
            strike.Soldiers.RemoveAll(s => s.IsDead || s.IsInside);
            var group = groups.FirstOrDefault(g => g.Soldiers.Any(strike.Targets.Contains))
                        ?? groups.Where(g => PathFinder.Distance(g.Center, strike.Center) <= config.GroupRadius * 2)
                            .MinBy(g => PathFinder.Distance(g.Center, strike.Center));
            if (strike.Soldiers.Count == 0 || group is null)
            {
                // Won, or nobody left: the survivors go home.
                if (strike.Soldiers.Count > 0)
                    _world.Military.Move(strike.Soldiers, HomeDoor());
                _strikes.Remove(strike);
                continue;
            }
            strike.Targets = group.Soldiers.ToHashSet();

            double strength = strike.Soldiers.Sum(Strength);
            // What they will meet: the group and whoever waits on the way to it.
            var from = new GridPoint((int)strike.Soldiers.Average(s => s.Position.X), (int)strike.Soldiers.Average(s => s.Position.Y));
            double opposition = Opposition(group, from);
            if (strength < opposition * config.RetreatRatio)
            {
                // Reinforcements first; if there are not enough, retreat.
                var helpers = IdleSoldiers().Where(s => PathFinder.Distance(s.Position, group.Center) <= config.StrikeRange * 2)
                    .OrderBy(s => PathFinder.Distance(s.Position, group.Center)).ToList();
                var joining = new List<Settler>();
                foreach (var helper in helpers)
                {
                    if (strength >= opposition * config.StrikeRatio)
                        break;
                    joining.Add(helper);
                    strength += Strength(helper);
                }
                if (strength >= opposition * config.RetreatRatio)
                {
                    _world.Military.Move(joining, group.Center);
                    strike.Soldiers.AddRange(joining);
                    Note($"sends {joining.Count} more against {group.Soldiers.Count} enemies at {group.Center.X},{group.Center.Y}");
                }
                else
                {
                    _world.Military.Move(strike.Soldiers, HomeDoor(), forced: true);
                    Note($"calls off the strike at {group.Center.X},{group.Center.Y}: {group.Soldiers.Count} enemies are too strong");
                    _strikes.Remove(strike);
                    continue;
                }
            }

            // The enemies moved: the strikers that are not fighting follow them.
            if (PathFinder.Distance(group.Center, strike.Center) > 3)
            {
                var following = strike.Soldiers.Where(s => s.Activity is not (Activity.Fighting or Activity.Shooting)).ToList();
                if (following.Count > 0)
                    _world.Military.Move(following, group.Center);
            }
            strike.Center = group.Center;
        }
    }

    /// <summary>Enemy soldiers outside buildings, joined into groups of neighbours within <paramref name="radius"/>.</summary>
    private List<EnemyGroup> FindEnemyGroups(int radius)
    {
        var enemies = _world.Settlers.Where(s => s.Profession == Profession.Soldier && s.Player != Player && !s.IsInside && !s.IsDead).ToList();
        var groups = new List<EnemyGroup>();
        var left = new HashSet<Settler>(enemies);
        while (left.Count > 0)
        {
            var first = left.First();
            var members = new List<Settler> { first };
            left.Remove(first);
            for (int i = 0; i < members.Count; i++)
                foreach (var other in left.Where(o => PathFinder.Distance(o.Position, members[i].Position) <= radius).ToList())
                {
                    members.Add(other);
                    left.Remove(other);
                }
            var center = new GridPoint((int)members.Average(m => m.Position.X), (int)members.Average(m => m.Position.Y));
            groups.Add(new EnemyGroup(center, members, members.Sum(Strength)));
        }
        return groups;
    }

    /// <summary>Fighting value of a soldier: health times damage per second.</summary>
    private double Strength(Settler soldier)
    {
        var military = _world.Military.Config;
        var type = soldier.SoldierType!.Value;
        double perSecond = military.Damage(type, soldier.Level) * World.TicksPerSecond / military.Soldiers[type].AttackTicks;
        return soldier.Health * perSecond;
    }
}
