using Homestead.Simulation.Pathfinding;

namespace Homestead.Simulation.Military;

/// <summary>
/// Scouts in disguise: other players take a scout for one of their own carriers, so nobody fights him,
/// until he is revealed to them – by one of their soldiers outside the buildings within
/// <see cref="ScoutRules.RevealRadius"/>, or, for a computer player, as soon as he stands on its land.
/// Recomputed every tick; once neither holds, the disguise works again.
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

        int radius = ScoutRules.RevealRadius;
        var map = _world.Map!;
        foreach (var scout in scouts)
            scout.RevealedTo = 0;
        foreach (var ai in _world.AiPlayers)
            foreach (var scout in scouts)
                if (scout.Player != ai.Player && !scout.IsInside && map.OwnerAt(scout.Position) == ai.Player)
                    scout.RevealedTo |= 1 << ai.Player;
        foreach (var soldier in _world.Settlers)
        {
            if (soldier.Profession != Profession.Soldier || soldier.IsDead || soldier.IsInside)
                continue;
            int bit = 1 << soldier.Player;
            foreach (var scout in scouts)
                if (scout.Player != soldier.Player && (scout.RevealedTo & bit) == 0
                    && PathFinder.Distance(soldier.Position, scout.Position) <= radius)
                    scout.RevealedTo |= bit;
        }
    }
}
