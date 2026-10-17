using Homestead.Simulation.Map;
using Homestead.Simulation.Pathfinding;

namespace Homestead.Simulation.Military;

/// <summary>
/// Scouts in disguise: other players take a scout for one of their own carriers, so nobody fights him,
/// until one of their soldiers outside the buildings comes within <see cref="ScoutRules.RevealRadius"/>.
/// Once revealed to a player he stays revealed while he is in that player's sight (<see cref="Sees"/>:
/// the own land, the sight of the own settlers outside and of buildings with a sight); out of it, the
/// disguise works again. Computer players grow suspicious of scouts on their land and send a soldier to
/// look at them (<c>AiPlayer.InspectScouts</c>).
/// </summary>
public sealed partial class Military
{
    private ScoutRules ScoutRules => _config.Scouts ?? ScoutRules.Default;

    private void RevealScouts()
    {
        List<Settler>? scouts = null;
        foreach (var settler in _world.Settlers)
            if (settler.Profession == Profession.Scout && !settler.IsDead)
                (scouts ??= new()).Add(settler);
        if (scouts is null)
            return;

        // Revealed ones stay so while in sight.
        foreach (var scout in scouts)
            if (scout.RevealedTo != 0)
                for (int player = 0; player < 32; player++)
                    if ((scout.RevealedTo & (1 << player)) != 0 && (scout.IsInside || !Sees(player, scout.Position)))
                        scout.RevealedTo &= ~(1 << player);

        int radius = ScoutRules.RevealRadius;
        foreach (var soldier in _world.Settlers)
        {
            if (soldier.Profession != Profession.Soldier || soldier.IsDead || soldier.IsInside)
                continue;
            int bit = 1 << soldier.Player;
            foreach (var scout in scouts)
                if (scout.Player != soldier.Player && !scout.IsInside && (scout.RevealedTo & bit) == 0
                    && PathFinder.Distance(soldier.Position, scout.Position) <= radius)
                    scout.RevealedTo |= bit;
        }
    }

    /// <summary>
    /// Whether <paramref name="player"/> sees the point: on the own land, within sight of an own settler outside
    /// the buildings or of a finished own building with a sight (as the fog of war, <see cref="Vision"/>).
    /// </summary>
    public bool Sees(int player, GridPoint p)
    {
        if (_world.Map!.OwnerAt(p) == player)
            return true;
        foreach (var settler in _world.Settlers)
            if (settler.Player == player && !settler.IsDead && !settler.IsInside
                && PathFinder.InCircle(settler.Position, p, SightOf(settler)))
                return true;
        foreach (var building in _world.Buildings)
            if (building is { Exists: true, State: Buildings.BuildingState.Finished } && building.Player == player
                && _config.BuildingSight.TryGetValue(building.Definition.Name, out int sight) && PathFinder.InCircle(building.Position, p, sight))
                return true;
        return false;
    }

    /// <summary>How far a settler sees (soldiers as far as they notice enemies, scouts further, others <see cref="Vision.SettlerSight"/>).</summary>
    internal int SightOf(Settler settler) =>
        settler.SoldierType is { } type ? _config.Sight(type)
        : settler.Profession == Profession.Scout ? ScoutRules.Sight
        : Vision.SettlerSight;
}
