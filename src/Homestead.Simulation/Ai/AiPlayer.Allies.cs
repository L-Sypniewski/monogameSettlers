using Homestead.Simulation.Buildings;
using Homestead.Simulation.Pathfinding;

namespace Homestead.Simulation.Ai;

/// <summary>
/// Allies in every weighing of strength: the enemy side always counts every enemy (all players but the allies); the own side
/// counts allied soldiers outside near the fight and allied bowmen in military buildings reaching it as well. So an AI joins a
/// fight an ally has started (the defence it needs is partly met already), and armies are compared team against team.
/// </summary>
public sealed partial class AiPlayer
{
    [NonSerialized] private (List<Settler> Soldiers, List<Building> Military)? _allied;
    [NonSerialized] private long _alliedTick = -1;

    /// <summary>Soldiers outside and occupied military buildings of the other players allied with it, once per tick.</summary>
    private (List<Settler> Soldiers, List<Building> Military) AlliedNow()
    {
        if (_allied is { } known && _alliedTick == _world.Tick)
            return known;
        bool ally(int player) => player != Player && !IsEnemy(player);
        var allied = (_world.Settlers.Where(s => s.Profession == Profession.Soldier && ally(s.Player) && !s.IsInside && !s.IsDead).ToList(),
            _world.Buildings.Where(b => b.Exists && ally(b.Player) && IsMilitary(b) && b.State == BuildingState.Finished).ToList());
        (_allied, _alliedTick) = (allied, _world.Tick);
        return allied;
    }

    /// <summary>
    /// Strength of the allied soldiers outside within <paramref name="radius"/> of <paramref name="at"/> (they fight there too)
    /// and of the bowmen in allied military buildings reaching it.
    /// </summary>
    internal double AlliedStrength(GridPoint at, int radius)
    {
        var (soldiers, military) = AlliedNow();
        if (soldiers.Count == 0 && military.Count == 0)
            return 0;
        int range = _world.Military.Config.TowerRange;
        return soldiers.Where(s => PathFinder.Distance(s.Position, at) <= radius).Sum(s => Strength(s, at))
               + military.Where(b => PathFinder.Distance(b.Position, at) <= range)
                   .Sum(b => _world.Military.Occupants(b).Where(s => s is { SoldierType: SoldierType.Bowman }).Sum(s => Strength(s!, b.Position)));
    }

    /// <summary>Allied soldiers within this distance of a strike's target fight there too (as enemy reinforcements do).</summary>
    private int StrikeSupportRadius => Math.Max(_config.Military.ReinforceRadius, _config.Military.GroupRadius);

    /// <summary>The campaign's soldiers and the allies at its target, against <see cref="Defense"/>.</summary>
    internal double CampaignStrength(Campaign campaign) =>
        campaign.Soldiers.Sum(s => Strength(s, campaign.Target.Position)) + AlliedStrength(campaign.Target.Position, _config.Attack.DefenseRadius);

    /// <summary>The whole armies (<see cref="Armies"/>, with each player's combat strength) of <paramref name="player"/> and its allies.</summary>
    private double TeamArmy(Dictionary<int, double> armies, int player) =>
        armies.Where(a => _world.AreAllies(player, a.Key)).Sum(a => a.Value * _world.Military.CombatStrength(a.Key));
}
