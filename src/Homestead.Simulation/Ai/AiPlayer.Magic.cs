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

    private void LeadPriests()
    {
        var priests = _world.Settlers
            .Where(s => s.Player == Player && s.Profession == Profession.Priest && !s.IsDead && !s.IsInside && !_world.Magic.IsBusy(s))
            .ToList();
        if (priests.Count == 0)
            return;
        // One spell at a time: a second priest would only hit the same fight again.
        bool casting = _world.Settlers.Any(s => s.Player == Player && s.Profession == Profession.Priest && _world.Magic.IsBusy(s));
        if (!casting && BestSpellTarget() is { } target && _world.Magic.Cast(priests, target) == CastResult.Started)
        {
            Note($"casts {_world.Magic.SpellOf(Player)!.Name} at {target.X},{target.Y}");
            priests.RemoveAll(_world.Magic.IsBusy);
        }
        FollowWithPriests(priests);
    }

    /// <summary>
    /// Where the spell hits the most soldiers (enemies, or own ones for a haste spell) next to own
    /// fighting soldiers, if there are at least minTargets and the mana is there.
    /// </summary>
    private GridPoint? BestSpellTarget()
    {
        if (_world.Magic.SpellOf(Player) is not { } spell || _world.Magic.Mana(Player) < spell.Cost)
            return null;
        var fighters = _world.Settlers
            .Where(s => s.Player == Player && s.Profession == Profession.Soldier && !s.IsInside && !s.IsDead
                        && s.Activity is Activity.Fighting or Activity.Shooting)
            .Take(30).ToList();
        if (fighters.Count == 0)
            return null;
        int radius = _world.Magic.Config.Radius;
        var hit = _world.Settlers
            .Where(s => s.Profession == Profession.Soldier && !s.IsInside && !s.IsDead
                        && (spell.Kind == SpellKind.Haste ? s.Player == Player : s.Player != Player))
            .ToList();
        var best = fighters
            .Select(f => (Point: f.Position, Count: hit.Count(s => PathFinder.InCircle(f.Position, s.Position, radius))))
            .MaxBy(x => x.Count);
        return best.Count >= _config.Magic.MinTargets && _world.Magic.IsFight(best.Point) ? best.Point : null;
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
