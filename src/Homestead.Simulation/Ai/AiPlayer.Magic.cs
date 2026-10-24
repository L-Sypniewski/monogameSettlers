using Homestead.Simulation.Magic;
using Homestead.Simulation.Pathfinding;

namespace Homestead.Simulation.Ai;

/// <summary>
/// The AI's priests: they follow the soldiers out on a strike or campaign and stay behind them (towards
/// home), otherwise they wait at home. Where own soldiers fight and the mana is there, one casts its
/// spell on the spot it hits most (see <c>magic</c> in ai.json). The mana earned in all upgrades the
/// soldiers at the small temple: swordsmen, bowmen, pikemen to level 2, then the same to level 3.
/// </summary>
public sealed partial class AiPlayer
{
    /// <summary>The next soldier upgrade in upgradeOrder, once enough mana has been earned in all.</summary>
    private void UpgradeSoldiers()
    {
        var magic = _world.Magic;
        if (!magic.CanAffordUpgrade(Player))
            return;
        foreach (var type in _config.Magic.UpgradeOrder)
            if (magic.UpgradeOrder(Player, type) == UpgradeResult.Done)
            {
                if (magic.Upgrade(Player, type) == UpgradeResult.Done)
                    Note($"upgrades {type} to level {magic.SoldierLevel(Player, type)} ({magic.ManaEarned(Player)} mana earned)");
                return;
            }
    }

    /// <summary>Own soldiers this close to an enemy count as engaged with it: the spell goes where they meet.</summary>
    private const int EngagedDistance = 12;

    private void LeadPriests()
    {
        var priests = _world.Settlers
            .Where(s => s.Player == Player && s.Profession == Profession.Priest && !s.IsDead && !s.IsInside && !_world.Magic.IsBusy(s))
            .ToList();
        if (priests.Count == 0)
            return;
        // Several priests at once, each on a part of the fight the others' spells do not reach.
        while (priests.Count > 0 && BestSpellTarget() is { } target && _world.Magic.Cast(priests, target) == CastResult.Started)
        {
            Note($"casts {_world.Magic.SpellOf(Player)!.Name} at {target.X},{target.Y}");
            priests.RemoveAll(_world.Magic.IsBusy);
        }
        FollowWithPriests(priests);
    }

    /// <summary>
    /// Where the spell hits the most soldiers it has not hit yet (enemies near own soldiers that fight or are
    /// out on a strike or campaign – their bowmen and reinforcements too –, or own fighting ones for a haste
    /// spell), if there are at least minTargets, the mana is there and no other spell is on its way there.
    /// <paramref name="near"/>: only targets this close to the point.
    /// </summary>
    private GridPoint? BestSpellTarget(GridPoint? near = null, int nearDistance = 0)
    {
        if (_world.Magic.SpellOf(Player) is not { } spell || spell.Kind == SpellKind.SummonGoods)
            return null;
        int radius = spell.Radius;
        var coming = _world.Settlers.Where(s => s.Player == Player && s.Profession == Profession.Priest && _world.Magic.IsBusy(s))
            .Select(s => _world.Magic.TargetOf(s)).OfType<GridPoint>().ToList();
        // The spells on their way pay first.
        if (_world.Magic.Mana(Player) - Enumerable.Range(0, coming.Count).Sum(i => _world.Magic.CostOf(Player, spell, i))
            < _world.Magic.CostOf(Player, spell, coming.Count))
            return null;
        var engaged = _world.Settlers
            .Where(s => s.Player == Player && s.Profession == Profession.Soldier && !s.IsInside && !s.IsDead
                        && (s.Activity is Activity.Fighting or Activity.Shooting || InStrikeOrCampaign(s)))
            .Take(60).ToList();
        if (engaged.Count == 0)
            return null;
        // Frozen ones thawing before the spell could land count as not hit.
        long thawing = _world.Tick + _world.Magic.Config.CastTicks + World.TicksPerSecond;
        var hit = _world.Settlers
            .Where(s => !s.IsInside && !s.IsDead && _world.Magic.Affects(spell, Player, s)
                        && (spell.Kind != SpellKind.Freeze || s.FrozenUntil <= thawing)
                        && (spell.Kind != SpellKind.Haste || !s.IsCharmed(SpellKind.Haste, thawing)))
            .ToList();
        var candidates = spell.Kind == SpellKind.Haste
            ? engaged.Where(f => f.Activity is Activity.Fighting or Activity.Shooting).Select(f => f.Position)
            : hit.Where(e => engaged.Any(f => PathFinder.Distance(f.Position, e.Position) <= EngagedDistance)).Select(e => e.Position);
        var best = candidates
            .Where(p => (near is null || PathFinder.Distance(p, near.Value) <= nearDistance)
                        && coming.All(c => PathFinder.Distance(c, p) > 2 * radius))
            .Select(p => (Point: p, Count: hit.Count(s => PathFinder.InCircle(p, s.Position, radius))))
            .DefaultIfEmpty().MaxBy(x => x.Count);
        return best.Count >= _config.Magic.MinTargets ? best.Point : null;
    }

    private bool InStrikeOrCampaign(Settler soldier) =>
        _campaign?.Soldiers.Contains(soldier) == true || _strikes.Any(k => k.Soldiers.Contains(soldier));

    /// <summary>
    /// Turning the fight with a spell instead of giving it up (decided at <paramref name="at"/>): while the own
    /// soldiers still have <c>magic.turnRatio</c> of what they would need, a priest casts the people's spell
    /// (freeze, convert, scatter) on the enemies there – frozen ones count nothing, so the balance tips. True
    /// while such a spell is cast or on its way there: the fight goes on for now.
    /// </summary>
    private bool TurnWithSpell(GridPoint at, double strength, double needed)
    {
        double ratio = _config.Magic.TurnRatio ?? 0.6;
        if (ratio <= 0 || strength < needed * ratio || _world.Magic.SpellOf(Player) is not { Kind: not (SpellKind.Haste or SpellKind.SummonGoods) } spell)
            return false;
        int reach = _world.Magic.Range + EngagedDistance;
        var priests = _world.Settlers.Where(s => s.Player == Player && s.Profession == Profession.Priest && !s.IsDead && !s.IsInside).ToList();
        if (priests.Any(p => _world.Magic.TargetOf(p) is { } t && PathFinder.Distance(t, at) <= EngagedDistance))
            return true;
        var free = priests.Where(p => !_world.Magic.IsBusy(p) && PathFinder.Distance(p.Position, at) <= reach).ToList();
        if (free.Count == 0 || BestSpellTarget(at, EngagedDistance) is not { } target || _world.Magic.Cast(free, target) != CastResult.Started)
            return false;
        Note($"casts {spell.Name} at {target.X},{target.Y} to turn the fight");
        return true;
    }

    /// <summary>Priests stay behind the soldiers out in the field, otherwise at home.</summary>
    private void FollowWithPriests(List<Settler> priests)
    {
        var home = HomeDoor();
        var force = _campaign?.Soldiers ?? _strikes.OrderByDescending(k => k.Soldiers.Count).FirstOrDefault()?.Soldiers;
        var spot = home;
        if (force is { Count: > 0 })
        {
            var front = new GridPoint((int)force.Average(s => s.Position.X), (int)force.Average(s => s.Position.Y));
            int distance = PathFinder.Distance(front, home);
            int back = Math.Min(_config.Magic.PriestDistance, distance);
            spot = distance == 0 ? front
                : new GridPoint(front.X + (home.X - front.X) * back / distance, front.Y + (home.Y - front.Y) * back / distance);
        }
        foreach (var priest in priests)
            if (priest.Destination is null && priest.State != SettlerState.Stepping && PathFinder.Distance(priest.Position, spot) > 4)
                _world.WalkTo(priest, spot);
    }
}
