using System.Text.Json;
using Homestead.Simulation.Buildings;
using Homestead.Simulation.Pathfinding;

namespace Homestead.Simulation.Ai.Traits;

/// <summary>
/// Forward base (<c>forwardBase</c> in docs/KI-TACTICS.md): the idle soldiers gather at the own military
/// building nearest an enemy military building, close enough for guerilla strikes on the enemy. When the
/// enemy groups within <c>military.threatRange</c> of it are stronger than they (<c>fallBackRatio</c>
/// times their strength), they fall back into the own land, to the own military building nearest the
/// base that lies at least <c>fallBackDistance</c> fields further from those enemies (otherwise home). An
/// enemy moving on into the own land is then fought by defence and guerilla – from behind.
/// </summary>
internal sealed class ForwardBaseTrait(JsonElement settings) : AiTrait
{
    public const string TraitId = "forwardBase";

    private readonly double _fallBackRatio = Setting(settings, "fallBackRatio", 1.0);
    private readonly int _fallBackDistance = (int)Setting(settings, "fallBackDistance", 15);
    private Building? _post;

    public override string Id => TraitId;

    internal override bool Guard(AiPlayer ai, List<Building> military, List<Settler> idle)
    {
        var world = ai.World;
        var enemies = world.Buildings.Where(b => b.Exists && ai.IsEnemy(b.Player) && b.Definition.Occupiers.Count > 0).ToList();
        if (enemies.Count == 0)
            return false;
        var config = ai.Config.Military;
        int ToEnemy(Building b) => enemies.Min(e => PathFinder.Distance(b.Position, e.Position));
        var front = military.MinBy(ToEnemy)!;

        var threats = ai.EnemyGroups.Where(g => PathFinder.Distance(g.Center, front.Position) <= config.ThreatRange).ToList();
        double danger = threats.Sum(g => g.Strength);
        double own = idle.Sum(s => ai.Strength(s, front.Position));
        var post = front;
        if (danger > 0 && own < danger * _fallBackRatio)
        {
            int Threat(Building b) => threats.Min(g => PathFinder.Distance(g.Center, b.Position));
            post = military.Where(b => Threat(b) >= Threat(front) + _fallBackDistance)
                       .MinBy(b => PathFinder.Distance(b.Position, front.Position))
                   ?? (ai.Home is { Definition.Occupiers.Count: > 0 } home ? home : front);
        }
        if (post != _post)
        {
            ai.Note(post == front
                ? $"forwardBase: gathers at the {front.Definition.Name} at {front.Position.X},{front.Position.Y}"
                : $"forwardBase: falls back to {post.Position.X},{post.Position.Y}: {threats.Sum(g => g.Soldiers.Count)} enemies near {front.Position.X},{front.Position.Y}");
            _post = post;
        }

        var coming = idle.Where(s => PathFinder.Distance(ai.Heading(s), post.Door) > config.GuardDistance).ToList();
        if (coming.Count > 0)
            world.Military.Move(coming, post.Door, forced: post != front); // falling back: no fighting on the way
        idle.Clear();
        return true;
    }
}
