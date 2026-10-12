using Homestead.Simulation.Buildings;
using Homestead.Simulation.Pathfinding;

namespace Homestead.Simulation.Ai;

/// <summary>
/// The AI's soldiers outside buildings. It knows where every enemy soldier is (for now). Idle soldiers
/// strike enemy groups close to its land when they clearly outnumber them (guerilla; re-checked every
/// think: follow the group, reinforce, or retreat), the rest stand guard at the own military
/// buildings, most where enemies are closest.
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
        var idle = IdleSoldiers();
        if (idle.Count == 0)
            return;
        var military = mine.Where(b => IsMilitary(b) && b.State == BuildingState.Finished).ToList();
        if (military.Count == 0)
            return;

        // Guerilla: the most worthwhile enemy group near the own land that idle soldiers nearby beat clearly.
        foreach (var group in EnemyGroups
                     .Where(g => military.Any(b => PathFinder.Distance(b.Position, g.Center) <= config.StrikeRange)
                                 && !_strikes.Any(k => g.Soldiers.Any(k.Targets.Contains)))
                     .OrderBy(g => g.Strength))
        {
            var near = idle.Where(s => PathFinder.Distance(s.Position, group.Center) <= config.StrikeRange * 2)
                .OrderBy(s => PathFinder.Distance(s.Position, group.Center)).ToList();
            var strikers = new List<Settler>();
            double strength = 0;
            foreach (var soldier in near)
            {
                if (strength >= group.Strength * config.StrikeRatio)
                    break;
                strikers.Add(soldier);
                strength += Strength(soldier);
            }
            if (strength < group.Strength * config.StrikeRatio)
                continue;
            _world.Military.Move(strikers, group.Center);
            _strikes.Add(new Strike(strikers, group.Soldiers.ToHashSet(), group.Center));
            Note($"strikes {group.Soldiers.Count} enemies at {group.Center.X},{group.Center.Y} with {strikers.Count} soldiers");
            idle.RemoveAll(strikers.Contains);
            break;
        }

        // Guard: the rest goes to the own military buildings, more where enemies are close.
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

    /// <summary>Own soldiers free for orders: outside, idle, not part of a running strike.</summary>
    private List<Settler> IdleSoldiers() => _world.Settlers
        .Where(s => s.Player == Player && s.Profession == Profession.Soldier && !s.HasTask && !s.IsInside && !s.IsDead
                    && !_strikes.Any(k => k.Soldiers.Contains(s)))
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
        foreach (var strike in _strikes.ToList())
        {
            strike.Soldiers.RemoveAll(s => s.IsDead || s.IsInside);
            var group = groups.FirstOrDefault(g => g.Soldiers.Any(strike.Targets.Contains))
                        ?? groups.Where(g => PathFinder.Distance(g.Center, strike.Center) <= config.GroupRadius * 2)
                            .MinBy(g => PathFinder.Distance(g.Center, strike.Center));
            if (strike.Soldiers.Count == 0 || group is null)
            {
                _strikes.Remove(strike); // won, or nobody left
                continue;
            }
            strike.Targets = group.Soldiers.ToHashSet();

            double strength = strike.Soldiers.Sum(Strength);
            if (strength < group.Strength * config.RetreatRatio)
            {
                // Reinforcements first; if there are not enough, retreat.
                var helpers = IdleSoldiers().Where(s => PathFinder.Distance(s.Position, group.Center) <= config.StrikeRange * 2)
                    .OrderBy(s => PathFinder.Distance(s.Position, group.Center)).ToList();
                var joining = new List<Settler>();
                foreach (var helper in helpers)
                {
                    if (strength >= group.Strength * config.StrikeRatio)
                        break;
                    joining.Add(helper);
                    strength += Strength(helper);
                }
                if (strength >= group.Strength * config.RetreatRatio)
                {
                    _world.Military.Move(joining, group.Center);
                    strike.Soldiers.AddRange(joining);
                    Note($"sends {joining.Count} more against {group.Soldiers.Count} enemies at {group.Center.X},{group.Center.Y}");
                }
                else
                {
                    var home = _home is { } h && IsMilitary(h) ? h.Door : mine.Where(IsMilitary).Select(b => b.Door).FirstOrDefault();
                    _world.Military.Move(strike.Soldiers, home, retreat: true);
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
