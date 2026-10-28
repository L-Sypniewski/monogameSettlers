using Homestead.Simulation.Buildings;

namespace Homestead.Simulation.Ai;

/// <summary>
/// Superiority (see <c>superiority</c> in ai.json): on big maps the enemies are often out of a campaign's reach and never
/// attacked. From a game time on, the whole armies are compared plainly every few minutes; against an enemy the own army
/// clearly outnumbers, a campaign starts like a challenge – whatever the distance, the way and the character's timing say.
/// </summary>
public sealed partial class AiPlayer
{
    /// <summary>No comparison before this tick.</summary>
    private long _nextSuperiority;

    private void CheckSuperiority(List<Building> military, List<Settler> spare)
    {
        var rules = _config.Superiority ?? AiConfig.SuperiorityRules.Default;
        if (rules.ArmyRatio <= 0 || _world.Tick < Math.Max(rules.StartTicks, _nextSuperiority))
            return;
        _nextSuperiority = _world.Tick + rules.IntervalTicks;
        // Team against team.
        var armies = Armies();
        double own = TeamArmy(armies, Player);
        // The weakest enemy first; a stronger one outnumbered as well if that one has no target within reach.
        foreach (var (enemy, army) in armies.Keys.Where(p => IsEnemy(p) && !InTruce(p))
                     .Select(p => (Player: p, Army: TeamArmy(armies, p)))
                     .Where(a => own >= a.Army * rules.ArmyRatio).OrderBy(a => a.Army))
        {
            StartCampaign(military, spare, only: enemy, ratio: rules.Ratio);
            if (_campaign is null)
                continue;
            Note($"outnumbers player {enemy + 1} ({own:0} against {army:0}) and attacks");
            return;
        }
    }
}
