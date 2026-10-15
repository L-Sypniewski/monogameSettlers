using Homestead.Simulation.Buildings;
using Homestead.Simulation.Pathfinding;

namespace Homestead.Simulation.Ai.Traits;

/// <summary>
/// Every other tower (<c>everyOtherTower</c> in docs/KI-TACTICS.md): without a threat, the idle soldiers are
/// shared out evenly among every other own military building – bigger groups that are quickly at the
/// neighbors left out. Front first: going by the distance to the enemy, a building is taken unless a
/// neighbor of it (no further than <c>neighborFactor</c> times the distance to its nearest one) already is.
/// With a threat it leaves them to the next trait (guardByThreat).
/// </summary>
internal sealed class EveryOtherTowerTrait(System.Text.Json.JsonElement settings) : AiTrait
{
    public const string TraitId = "everyOtherTower";

    private readonly double _neighborFactor = Setting(settings, "neighborFactor", 1.5);
    private List<Building> _posts = [];

    public override string Id => TraitId;

    internal override bool Guard(AiPlayer ai, List<Building> military, List<Settler> idle)
    {
        if (ai.Threatened(military))
            return false;
        var enemies = ai.EnemyMilitary();
        int ToEnemy(Building b) => enemies.Count == 0 ? 0 : enemies.Min(e => PathFinder.Distance(b.Position, e.Position));
        int Nearest(Building b) => military.Where(o => o != b).Select(o => PathFinder.Distance(o.Position, b.Position)).DefaultIfEmpty(0).Min();
        bool Neighbors(Building a, Building b) => PathFinder.Distance(a.Position, b.Position) <= _neighborFactor * Math.Max(Nearest(a), Nearest(b));
        var posts = new List<Building>();
        foreach (var building in military.OrderBy(ToEnemy).ThenBy(b => b.Id))
            if (!posts.Any(p => Neighbors(p, building)))
                posts.Add(building);
        if (!posts.SequenceEqual(_posts))
            ai.Note($"{TraitId}: guards {posts.Count} of {military.Count} military buildings");
        _posts = posts;
        ai.Distribute(posts, idle);
        return true;
    }
}
