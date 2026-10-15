using Homestead.Simulation.Buildings;
using Homestead.Simulation.Pathfinding;

namespace Homestead.Simulation.Ai.Traits;

/// <summary>
/// Guarding by threat (<c>guardByThreat</c> in docs/KI-TACTICS.md): the idle soldiers guard the own
/// military buildings, shared out by how strong and close the enemy groups within
/// <c>military.threatRange</c> are; without any threat they gather at home. A soldier within
/// <c>military.guardDistance</c> of its post stays.
/// </summary>
internal sealed class GuardByThreatTrait : AiTrait
{
    public const string TraitId = "guardByThreat";

    public override string Id => TraitId;

    internal override bool Guard(AiPlayer ai, List<Building> military, List<Settler> idle)
    {
        var config = ai.Config.Military;
        var threat = military.ToDictionary(b => b, b => ai.EnemyGroups
            .Where(g => PathFinder.Distance(b.Position, g.Center) <= config.ThreatRange)
            .Sum(g => g.Strength / (1 + PathFinder.Distance(b.Position, g.Center))));
        double total = threat.Values.Sum();
        var posts = new List<Building>();
        if (total <= 0)
            posts.AddRange(Enumerable.Repeat(ai.Home is { Definition.Occupiers.Count: > 0 } home ? home : military[0], idle.Count));
        else
            foreach (var (building, value) in threat.Where(t => t.Value > 0).OrderByDescending(t => t.Value))
                posts.AddRange(Enumerable.Repeat(building, (int)Math.Ceiling(idle.Count * value / total)));
        foreach (var post in posts.Distinct())
        {
            int wanted = posts.Count(p => p == post);
            // Who is already there (or on the way) stays; the nearest of the others come.
            int present = idle.Count(s => PathFinder.Distance(ai.Heading(s), post.Door) <= config.GuardDistance);
            var coming = idle.Where(s => PathFinder.Distance(ai.Heading(s), post.Door) > config.GuardDistance)
                .OrderBy(s => PathFinder.Distance(s.Position, post.Door)).Take(wanted - present).ToList();
            idle.RemoveAll(s => PathFinder.Distance(ai.Heading(s), post.Door) <= config.GuardDistance);
            if (coming.Count == 0)
                continue;
            ai.World.Military.Move(coming, post.Door);
            idle.RemoveAll(coming.Contains);
        }
        return true;
    }
}
