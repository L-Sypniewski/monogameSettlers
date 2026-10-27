using Homestead.Simulation.Buildings;
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
            // Revealed to the soldier's whole team.
            int bit = TeamBits(soldier.Player);
            foreach (var scout in scouts)
                if (scout.Player != soldier.Player && !scout.IsInside && (scout.RevealedTo & bit) != bit
                    && PathFinder.Distance(soldier.Position, scout.Position) <= radius)
                    scout.RevealedTo |= bit;
        }
    }

    /// <summary>Bits of the player and its allies (as in <see cref="Settler.RevealedTo"/>).</summary>
    private int TeamBits(int player)
    {
        int bits = 1 << player;
        for (int other = 0; other < Math.Min(32, _world.Teams.Count); other++)
            if (other != player && _world.AreAllies(player, other))
                bits |= 1 << other;
        return bits;
    }

    /// <summary>
    /// Whether <paramref name="player"/> sees the point: within sight of an own or allied settler outside the buildings
    /// or of such a building with a sight (<see cref="SightOfBuilding"/>; as the fog of war, <see cref="Vision"/>).
    /// The own land itself sees nothing.
    /// </summary>
    public bool Sees(int player, GridPoint p)
    {
        foreach (var settler in _world.Settlers)
            if (_world.AreAllies(settler.Player, player) && !settler.IsDead && !settler.IsInside
                && PathFinder.InCircle(settler.Position, p, SightOf(settler)))
                return true;
        foreach (var building in _world.Buildings)
            if (_world.AreAllies(building.Player, player) && SightOfBuilding(building) is { } sight && PathFinder.InCircle(building.Position, p, sight))
                return true;
        return _world.Magic.SeesBySpell(player, p);
    }

    /// <summary>
    /// How far a building sees in the fog of war: finished ones with a <see cref="MilitaryConfig.BuildingSight"/>
    /// (lookout tower) that far, occupied military buildings <see cref="MilitaryConfig.MilitarySightMargin"/> beyond
    /// their land; null for the others.
    /// </summary>
    public int? SightOfBuilding(Building building)
    {
        if (building is not { Exists: true, State: Buildings.BuildingState.Finished })
            return null;
        if (_config.BuildingSight.TryGetValue(building.Definition.Name, out int sight))
            return sight;
        if (building.Definition.Occupiers.Count > 0)
            return IsOccupied(building) && TerritoryRadius(building) is var radius and > 0
                ? (int)Math.Ceiling(radius) + _config.MilitarySightMargin
                : null;
        // Every other building (houses and storehouses too).
        return _config.BuildingSightRadius ?? 5;
    }

    /// <summary>
    /// How far a settler sees (soldiers as far as they notice enemies, scouts further, donkeys hardly, others
    /// <see cref="Vision.SettlerSight"/>).
    /// </summary>
    internal int SightOf(Settler settler) =>
        settler.SoldierType is { } type ? _config.Sight(type)
        : settler.Profession == Profession.Scout ? ScoutRules.Sight
        : settler.Profession == Profession.Donkey ? (_world.Production.Config.Trade ?? Economy.TradeRules.Default).Sight
        : Vision.SettlerSight;
}
