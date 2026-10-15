using System.Text.Json;
using Homestead.Simulation.Buildings;
using Homestead.Simulation.Pathfinding;

namespace Homestead.Simulation.Ai.Traits;

/// <summary>
/// Front towers (<c>frontTowers</c> in docs/KI-TACTICS.md): without a threat, the idle soldiers are shared out
/// evenly among the own military buildings facing the enemy – those from which the nearest enemy military
/// building can be reached without a detour (the way at most <c>detour</c> times the straight distance plus a
/// few fields) and without passing another own military building on the way. With a threat it leaves them to
/// the next trait (guardByThreat). Rechecked every <c>checkSeconds</c>.
/// </summary>
internal sealed class FrontTowersTrait(JsonElement settings) : AiTrait
{
    public const string TraitId = "frontTowers";

    private readonly double _detour = Setting(settings, "detour", 1.4);
    private readonly int _checkTicks = (int)(Setting(settings, "checkSeconds", 10) * World.TicksPerSecond);
    private long _nextCheck;
    private List<Building> _posts = [];

    public override string Id => TraitId;

    internal override bool Guard(AiPlayer ai, List<Building> military, List<Settler> idle)
    {
        if (ai.Threatened(military))
            return false;
        var enemies = ai.EnemyMilitary();
        if (enemies.Count == 0)
            return false;
        if (ai.World.Tick >= _nextCheck || _posts.Any(b => !b.Exists || b.Player != ai.Player))
        {
            var posts = Front(ai, military, enemies);
            if (!posts.SequenceEqual(_posts))
                ai.Note($"{TraitId}: {posts.Count} of {military.Count} military buildings face the enemy");
            _posts = posts;
            _nextCheck = ai.World.Tick + _checkTicks;
        }
        ai.Distribute(_posts, idle);
        return true;
    }

    private List<Building> Front(AiPlayer ai, List<Building> military, List<Building> enemies)
    {
        var pathFinder = ai.World.PathFinder!;
        var front = new List<Building>();
        foreach (var building in military)
        {
            var enemy = enemies.MinBy(e => PathFinder.Distance(building.Position, e.Position))!;
            int straight = PathFinder.Distance(building.Position, enemy.Position);
            // Another own one on the way stands in front of it.
            bool covered = military.Any(m => m != building && PathFinder.Distance(m.Position, enemy.Position) < straight
                                             && PathFinder.Distance(building.Position, m.Position) + PathFinder.Distance(m.Position, enemy.Position) <= straight * 1.15);
            if (covered)
                continue;
            if (pathFinder.FindPath(building.Door, enemy.Door) is not { } path || path.Count > straight * _detour + 5)
                continue;
            front.Add(building);
        }
        // Nothing faces the enemy directly (water, mountains between): the one nearest it.
        if (front.Count == 0)
            front.Add(military.MinBy(b => enemies.Min(e => PathFinder.Distance(b.Position, e.Position)))!);
        return front;
    }
}
