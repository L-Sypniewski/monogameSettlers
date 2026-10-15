using Homestead.Simulation.Buildings;
using Homestead.Simulation.Pathfinding;

namespace Homestead.Simulation.Ai;

/// <summary>
/// The AI's soldiers outside buildings. It knows where every enemy soldier is (for now). Defence comes
/// first: enemies on the own land are fought at once, and enough soldiers stay back against enemies
/// close to it. Idle soldiers strike enemy groups (soldiers, also geologists) near the land when they clearly outnumber them
/// (guerilla) – also those held back against them, as long as enough stay against the others – and spare ones go on campaigns against enemy military buildings (see AiPlayer.Campaign). Strikes
/// and campaigns are re-checked every think (follow, reinforce, retreat); afterwards the soldiers go
/// home. The rest are placed by the character's traits (decision point "guarding"). Soldiers walking on a
/// plain move order of the AI (to a post, home) count as idle: their order is weighed again every think.
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

    /// <summary>Enemy soldiers and geologists outside buildings, grouped (seen at the last military think).</summary>
    public IReadOnlyList<EnemyGroup> EnemyGroups { get; private set; } = [];

    /// <summary>
    /// Enemy soldiers (and geologists, which do not fight) close together; <see cref="Strength"/> as they would
    /// fight at <see cref="Center"/>.
    /// </summary>
    public sealed record EnemyGroup(GridPoint Center, IReadOnlyList<Settler> Soldiers, double Strength)
    {
        /// <summary>Whether there are soldiers among them (not just geologists).</summary>
        public bool Armed => Soldiers.Any(s => s.Profession == Profession.Soldier);
    }

    private void Command(List<Building> mine)
    {
        var config = _config.Military;
        EnemyGroups = FindEnemyGroups(config.GroupRadius);
        var military = mine.Where(b => IsMilitary(b) && b.State == BuildingState.Finished).ToList();
        if (military.Count == 0)
            return;
        var idle = IdleSoldiers();
        Staff(military, idle);
        var map = _world.Map!;
        bool onOwnLand(EnemyGroup g) => map.OwnerAt(g.Center) == Player;
        bool near(EnemyGroup g, int range) => military.Any(b => PathFinder.Distance(b.Position, g.Center) <= range);
        bool untargeted(EnemyGroup g) => !_strikes.Any(k => g.Soldiers.Any(k.Targets.Contains));

        // Defence first: enemies on the own land are fought with any idle soldier.
        foreach (var group in EnemyGroups.Where(g => onOwnLand(g) && untargeted(g)).OrderByDescending(g => g.Strength))
            TryStrike(group, idle, "defends against");

        bool threatening(EnemyGroup g) => !onOwnLand(g) && near(g, config.ThreatRange) && untargeted(g);

        // Guerilla outside the own land, the weakest worthwhile group first. It comes before holding
        // soldiers back: the defenders may strike the enemies they stand against themselves; only
        // those needed against the other groups close to the land stay out of it. An attack on the land
        // (enemies under way or fighting) is met anywhere; enemies just standing around not deep in
        // their own land (military.hinterlandDepth).
        foreach (var group in EnemyGroups.Where(g => !onOwnLand(g) && near(g, config.StrikeRange) && untargeted(g)).OrderBy(g => g.Strength))
        {
            bool standing = IsStanding(group);
            if (standing && InHinterland(group))
                continue;
            var pool = idle.Except(Reserve(idle, EnemyGroups.Where(g => g != group && threatening(g)))).ToList();
            if (!TryStrike(group, pool, "strikes", standing ? "standing around" : "attacking the land"))
                continue;
            idle.RemoveAll(_strikes[^1].Soldiers.Contains);
            break;
        }

        // Against the enemies still close to the land enough soldiers stay back; only the rest is spare.
        var guards = Reserve(idle, EnemyGroups.Where(threatening));
        var spare = idle.Except(guards).ToList();

        // A campaign against enemy land, never while enemy soldiers stand on the own land.
        if (_campaign is null && !EnemyGroups.Any(g => g.Armed && onOwnLand(g)))
            StartCampaign(military, spare);

        // Decision point "guarding": the first trait that takes it places the idle soldiers; without one they stay where they are.
        var rest = guards.Concat(spare).ToList();
        foreach (var trait in Character.Traits)
            if (trait.Guard(this, military, rest))
                break;
    }

    /// <summary>
    /// The AI wants its military buildings full: bowmen on the bowman places; the infantry there stays,
    /// free infantry places go to idle swordsmen, then idle pikemen, the rest waits for swordsmen.
    /// Decision point "garrison": a trait may limit a building to fewer soldiers (infantry first, at
    /// least one); the others come out.
    /// </summary>
    private void Staff(List<Building> military, List<Settler> idle)
    {
        var m = _world.Military;
        int freeSwordsmen = idle.Count(s => s.SoldierType == SoldierType.Swordsman);
        int freePikemen = idle.Count(s => s.SoldierType == SoldierType.Pikeman);
        foreach (var building in military)
        {
            int places = building.Definition.Occupiers.Count;
            int limit = Math.Max(1, Character.Traits.Select(t => t.GarrisonLimit(this, building)).Min() ?? places);
            int infantryPlaces = building.Definition.Occupiers.Count(o => !o.ForBowman);
            int infantry = Math.Min(infantryPlaces, limit);
            int swordsmen = m.Assigned(building, SoldierType.Swordsman), pikemen = m.Assigned(building, SoldierType.Pikeman);
            // Over the limit: pikemen leave before swordsmen.
            int over = Math.Max(0, swordsmen + pikemen - infantry);
            int pikemenLeaving = Math.Min(over, pikemen);
            pikemen -= pikemenLeaving;
            swordsmen -= over - pikemenLeaving;
            int room = Math.Max(0, infantry - swordsmen - pikemen);
            int moreSwordsmen = Math.Min(room, freeSwordsmen), morePikemen = Math.Min(room - moreSwordsmen, freePikemen);
            freeSwordsmen -= moreSwordsmen;
            freePikemen -= morePikemen;
            var target = new Dictionary<SoldierType, int>
            {
                [SoldierType.Swordsman] = swordsmen + room - morePikemen,
                [SoldierType.Pikeman] = pikemen + morePikemen,
                [SoldierType.Bowman] = Math.Min(places - infantryPlaces, limit - infantry),
            };
            // Fewer first (frees places), then more, then fewer again (a building is never left wanting none).
            for (int pass = 0; pass < 3; pass++)
                foreach (var (type, count) in target)
                {
                    int sign = pass == 1 ? 1 : -1;
                    while (sign * (count - m.Wanted(building, type)) > 0 && m.ChangeWanted(building, type, sign))
                    {
                    }
                }
        }
    }

    /// <summary>Enemies just standing around: none of them walks or fights (otherwise they attack the land).</summary>
    private static bool IsStanding(EnemyGroup group) =>
        group.Soldiers.All(s => s.Destination is null && s.State != SettlerState.Stepping && s.Activity is not (Activity.Fighting or Activity.Shooting));

    /// <summary>
    /// Whether the group stands deeper than <see cref="AiConfig.MilitaryRules.HinterlandDepth"/> in the land of
    /// the player it stands on (other than this one): no point of other land within that distance.
    /// </summary>
    private bool InHinterland(EnemyGroup group)
    {
        int depth = _config.Military.HinterlandDepth;
        var map = _world.Map!;
        if (depth <= 0 || map.OwnerAt(group.Center) is not { } owner || owner == Player)
            return false;
        foreach (var p in PathFinder.Circle(group.Center, depth))
            if (map.Contains(p) && map.OwnerAt(p) != owner)
                return false;
        return true;
    }

    /// <summary>
    /// Sends the nearest soldiers of <paramref name="pool"/> against the group if they get strikeRatio
    /// times the strength they will meet: the group plus enemy groups waiting on the way there.
    /// </summary>
    private bool TryStrike(EnemyGroup group, List<Settler> pool, string verb, string? remark = null)
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
            // Geologists alone do not fight back: one soldier will do.
            if (strength >= opposition * config.StrikeRatio && strikers.Count > 0)
                break;
            strikers.Add(soldier);
            strength += Strength(soldier, group.Center);
        }
        if (strength < opposition * config.StrikeRatio)
            return false;
        _world.Military.Move(strikers, group.Center);
        _strikes.Add(new Strike(strikers, group.Soldiers.ToHashSet(), group.Center));
        Note($"{verb} {group.Soldiers.Count} enemies at {group.Center.X},{group.Center.Y} with {strikers.Count} soldiers{(remark is null ? "" : $" ({remark})")}");
        pool.RemoveAll(strikers.Contains);
        return true;
    }

    /// <summary>The idle soldiers that stay back against <paramref name="threats"/>: strikeRatio times their strength, nearest first.</summary>
    private List<Settler> Reserve(List<Settler> idle, IEnumerable<EnemyGroup> threats)
    {
        var groups = threats.ToList();
        // They would fight on the own land: there the guards have their full strength, the enemies their reduced one.
        double danger = groups.SelectMany(g => g.Soldiers).Sum(s => Power(s) * _world.Military.CombatStrength(s.Player)) * _config.Military.StrikeRatio;
        var guards = new List<Settler>();
        double kept = 0;
        foreach (var soldier in idle.OrderBy(s => groups.Select(g => PathFinder.Distance(s.Position, g.Center)).DefaultIfEmpty(0).Min()))
        {
            if (kept >= danger)
                break;
            guards.Add(soldier);
            kept += Power(soldier);
        }
        return guards;
    }

    /// <summary>Whether enemy soldiers stand within <c>military.threatRange</c> of an own military building.</summary>
    internal bool Threatened(List<Building> military) =>
        EnemyGroups.Any(g => g.Armed && military.Any(b => PathFinder.Distance(b.Position, g.Center) <= _config.Military.ThreatRange));

    /// <summary>Occupied military buildings of other players (where the enemy is).</summary>
    internal List<Building> EnemyMilitary() =>
        _world.Buildings.Where(b => b.Exists && b.Player != Player && IsMilitary(b) && b.State == BuildingState.Finished).ToList();

    /// <summary>
    /// Shares the idle soldiers out evenly among the posts (the first ones get one more where it does not
    /// come out even). Who is already at a post or on the way to it (within <c>military.guardDistance</c>)
    /// stays there while it has room; the others go to the nearest post with room left.
    /// </summary>
    internal void Distribute(IReadOnlyList<Building> posts, List<Settler> idle)
    {
        if (posts.Count == 0)
            return;
        int guardDistance = _config.Military.GuardDistance;
        var room = posts.Select((_, i) => idle.Count / posts.Count + (i < idle.Count % posts.Count ? 1 : 0)).ToArray();
        var left = new List<Settler>();
        foreach (var soldier in idle)
        {
            int at = Enumerable.Range(0, posts.Count).Where(i => room[i] > 0)
                .FirstOrDefault(i => PathFinder.Distance(Heading(soldier), posts[i].Door) <= guardDistance, -1);
            if (at >= 0)
                room[at]--;
            else
                left.Add(soldier);
        }
        var sent = new Dictionary<int, List<Settler>>();
        foreach (var soldier in left)
        {
            int to = Enumerable.Range(0, posts.Count).Where(i => room[i] > 0).MinBy(i => PathFinder.Distance(soldier.Position, posts[i].Door));
            room[to]--;
            (sent.TryGetValue(to, out var list) ? list : sent[to] = new List<Settler>()).Add(soldier);
        }
        foreach (var (i, soldiers) in sent)
            _world.Military.Move(soldiers, posts[i].Door);
        idle.Clear();
    }

    /// <summary>Where soldiers go when a strike or campaign is over.</summary>
    internal GridPoint HomeDoor() => _home is { Exists: true } h && h.Player == Player ? h.Door
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

    /// <summary>
    /// Own soldiers free for orders: outside, idle or walking on a plain move order (to a post, home), not part
    /// of a running strike or campaign.
    /// </summary>
    private List<Settler> IdleSoldiers() => _world.Settlers
        .Where(s => s.Player == Player && s.Profession == Profession.Soldier && !s.IsInside && !s.IsDead
                    && (!s.HasTask || _world.Military.MoveTarget(s) is not null)
                    && !_strikes.Any(k => k.Soldiers.Contains(s)) && _campaign?.Soldiers.Contains(s) != true)
        .ToList();

    /// <summary>Where an idle soldier will stand: where it walks to on a move order, otherwise where it is.</summary>
    internal GridPoint Heading(Settler soldier) => _world.Military.MoveTarget(soldier) ?? soldier.Position;

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
            strike.Soldiers.RemoveAll(s => s.IsDead || s.IsInside || s.Player != Player); // also won over by a spell
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

            double strength = strike.Soldiers.Sum(s => Strength(s, group.Center));
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
                    strength += Strength(helper, group.Center);
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
        var enemies = _world.Settlers.Where(s => s.Profession is Profession.Soldier or Profession.Geologist && s.Player != Player && !s.IsInside && !s.IsDead).ToList();
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
            groups.Add(new EnemyGroup(center, members, members.Sum(m => Strength(m, center))));
        }
        return groups;
    }

    /// <summary>
    /// Fighting value of a soldier (own or enemy) fighting at <paramref name="at"/>: health times damage
    /// per second times its player's combat strength there – full on its own land, reduced on neutral
    /// and foreign land. The AI knows every player's combat strength.
    /// </summary>
    internal double Strength(Settler soldier, GridPoint at) => Power(soldier) * _world.Military.CombatStrengthAt(soldier.Player, at);

    /// <summary>Health times damage per second, without the combat strength (0 for geologists: they do not fight).</summary>
    private double Power(Settler soldier)
    {
        var military = _world.Military.Config;
        if (soldier.SoldierType is not { } type)
            return 0;
        double perSecond = military.Damage(type, soldier.Level) * World.TicksPerSecond / military.Soldiers[type].AttackTicks;
        return soldier.Health * perSecond;
    }
}
