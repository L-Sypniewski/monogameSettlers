using Homestead.Simulation.Buildings;
using Homestead.Simulation.Pathfinding;

namespace Homestead.Simulation.Ai;

/// <summary>
/// Campaigns against enemy land: the AI picks an occupied enemy military building near its own land,
/// weakly defended and reachable without running into enemy groups, gathers enough spare soldiers
/// at its own military building nearest to it, and attacks once they are together (see <c>attack</c>
/// in ai.json). A trait of the character may lead the campaign instead (<see cref="AiTrait.LeadCampaign"/>).
/// </summary>
public sealed partial class AiPlayer
{
    private Campaign? _campaign;

    internal sealed class Campaign(Building target, List<Settler> soldiers, GridPoint rally, long since)
    {
        public Building Target { get; } = target;
        public List<Settler> Soldiers { get; } = soldiers;

        /// <summary>Door of the own military building nearest the target, where the soldiers gather.</summary>
        public GridPoint Rally { get; } = rally;
        public long Since { get; } = since;
        public bool Attacking { get; set; }

        /// <summary>The trait leading it, or null for the built-in gather-and-attack.</summary>
        public CampaignLeader? Leader { get; set; }
    }

    private void StartCampaign(List<Building> military, List<Settler> spare)
    {
        var rules = _config.Attack;
        if (spare.Count < rules.MinSoldiers)
            return;
        // Decision point "timing": a trait may hold campaigns back.
        if (Character.Traits.Any(t => !t.AllowCampaign(this)))
            return;
        var pathFinder = _world.PathFinder!;
        // The own army fights on foreign land, the enemy's at home.
        double ownArmy = Army(Player) * _world.Military.CombatStrength(Player);
        (Building Target, Building Rally, List<Settler> Force, double Score)? best = null;
        foreach (var target in _world.Buildings.Where(b => b.Exists && b.Player != Player && IsMilitary(b)
                                                           && b.State == BuildingState.Finished && _world.Military.IsOccupied(b)))
        {
            // Clearly stronger than that enemy as a whole.
            if (ownArmy < Army(target.Player) * rules.ArmyRatio)
                continue;
            var rally = military.MinBy(b => PathFinder.Distance(b.Position, target.Position))!;
            // An outpost far away could not be held.
            if (rules.MaxDistance > 0 && PathFinder.Distance(rally.Position, target.Position) > rules.MaxDistance)
                continue;
            if (!pathFinder.AreConnected(rally.Door, target.Door) || !WayIsClear(rally.Door, target))
                continue;
            double defense = Defense(target);
            var force = new List<Settler>();
            double strength = 0;
            foreach (var soldier in spare.OrderBy(s => PathFinder.Distance(s.Position, rally.Door)))
            {
                if (strength >= defense * rules.Ratio && force.Count >= rules.MinSoldiers)
                    break;
                force.Add(soldier);
                strength += Strength(soldier, target.Position);
            }
            if (strength < defense * rules.Ratio || force.Count < rules.MinSoldiers)
                continue;
            // Weakly defended and close to the own land first.
            double score = (defense + 1) * (1 + PathFinder.Distance(rally.Position, target.Position) / 30.0);
            if (best is null || score < best.Value.Score)
                best = (target, rally, force, score);
        }
        if (best is not { } chosen)
            return;
        spare.RemoveAll(chosen.Force.Contains);
        // A share of the other spare soldiers comes along (all of them: full strength).
        var along = spare.OrderBy(s => PathFinder.Distance(s.Position, chosen.Rally.Door))
            .Take((int)Math.Ceiling(spare.Count * rules.Commit)).ToList();
        chosen.Force.AddRange(along);
        spare.RemoveAll(along.Contains);
        var campaign = new Campaign(chosen.Target, chosen.Force, chosen.Rally.Door, _world.Tick);
        _campaign = campaign;
        foreach (var trait in Character.Traits)
            if ((campaign.Leader = trait.LeadCampaign(this, campaign, spare)) is not null)
                return;
        _world.Military.Move(chosen.Force, chosen.Rally.Door);
        Note($"gathers {chosen.Force.Count} soldiers at {chosen.Rally.Position.X},{chosen.Rally.Position.Y} " +
             $"against the {chosen.Target.Definition.Name} of player {chosen.Target.Player + 1} at {chosen.Target.Position.X},{chosen.Target.Position.Y}");
    }

    /// <summary>Every think: wait for the gathering, attack, retreat when too weak, go home when it is over.</summary>
    private void FollowCampaign()
    {
        if (_campaign is not { } c)
            return;
        var rules = _config.Attack;
        c.Soldiers.RemoveAll(s => s.IsDead || s.IsInside || s.Player != Player); // also won over by a spell
        var target = c.Target;
        if (!target.Exists || target.Player == Player || c.Soldiers.Count == 0)
        {
            Note(target.Player == Player ? $"took the {target.Definition.Name} at {target.Position.X},{target.Position.Y}"
                : c.Soldiers.Count == 0 ? $"lost the campaign against {target.Position.X},{target.Position.Y}"
                : $"the {target.Definition.Name} at {target.Position.X},{target.Position.Y} is gone");
            if (c.Soldiers.Count > 0)
                _world.Military.Move(c.Soldiers, HomeDoor());
            _campaign = null;
            return;
        }
        if (c.Leader is { } leader)
        {
            if (!leader.Follow(this, c))
                _campaign = null;
            return;
        }

        double strength = c.Soldiers.Sum(s => Strength(s, target.Position)), defense = Defense(target);
        if (!c.Attacking)
        {
            // Weighed again every think, also on the way to the gathering point.
            bool gathered = c.Soldiers.All(s => PathFinder.Distance(s.Position, c.Rally) <= _config.Military.GuardDistance);
            if (strength < defense * rules.AbortRatio)
            {
                Note($"gives up the campaign against {target.Position.X},{target.Position.Y}: the defence got stronger");
                _world.Military.Move(c.Soldiers, HomeDoor());
                _campaign = null;
                return;
            }
            if (!gathered && _world.Tick - c.Since < rules.GatherTicks)
                return;
            _world.Military.Attack(c.Soldiers, target);
            c.Attacking = true;
            Note($"attacks the {target.Definition.Name} at {target.Position.X},{target.Position.Y} with {c.Soldiers.Count} soldiers");
            return;
        }
        if (strength < defense * _config.Military.RetreatRatio)
        {
            Note($"retreats from {target.Position.X},{target.Position.Y}");
            _world.Military.Move(c.Soldiers, HomeDoor(), forced: true);
            _campaign = null;
        }
    }

    /// <summary>
    /// The target's garrison (weighted by <c>attack.garrisonWeight</c>: a castle's is worth more) plus
    /// enemy soldiers outside near it, as they fight at the target.
    /// </summary>
    internal double Defense(Building target) =>
        _world.Military.Occupants(target).Where(s => s is not null).Sum(s => Strength(s!, target.Position))
        * (_config.Attack.GarrisonWeight?.GetValueOrDefault(target.Definition.Name, 1.0) ?? 1.0)
        + _world.Settlers.Where(s => s.Profession == Profession.Soldier && s.Player != Player && !s.IsInside && !s.IsDead
                                     && PathFinder.Distance(s.Position, target.Position) <= _config.Attack.DefenseRadius)
            .Sum(s => Strength(s, target.Position));

    /// <summary>All soldiers of a player, in buildings and outside, without the combat strength.</summary>
    private double Army(int player) =>
        _world.Settlers.Where(s => s.Player == player && s.Profession == Profession.Soldier && !s.IsDead).Sum(Power);

    /// <summary>No enemy group near the way (groups at the target count as its defence).</summary>
    internal bool WayIsClear(GridPoint from, Building target)
    {
        var rules = _config.Attack;
        int steps = Math.Max(1, PathFinder.Distance(from, target.Position) / 3);
        for (int i = 0; i <= steps; i++)
        {
            var p = new GridPoint(from.X + (target.Position.X - from.X) * i / steps, from.Y + (target.Position.Y - from.Y) * i / steps);
            if (EnemyGroups.Any(g => g.Armed && PathFinder.Distance(g.Center, target.Position) > rules.DefenseRadius
                                     && PathFinder.Distance(g.Center, p) <= rules.PathClearance))
                return false;
        }
        return true;
    }
}
