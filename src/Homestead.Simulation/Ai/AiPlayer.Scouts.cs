using Homestead.Simulation.Buildings;
using Homestead.Simulation.Pathfinding;

namespace Homestead.Simulation.Ai;

/// <summary>
/// Suspicion: a stranger's carrier on the own land (an enemy scout in disguise) is looked at. The AI sends the
/// nearest idle soldier towards him (a plain move order, following him); once within
/// <c>scouts.revealRadius</c> the scout is revealed and fought like any enemy. Without an idle soldier the
/// nearest military building with more than one soldier sends one out (<see cref="_releasing"/>).
/// </summary>
public sealed partial class AiPlayer
{
    /// <summary>The soldier looking at each suspected scout, by scout id.</summary>
    private readonly Dictionary<int, Settler> _inspectors = new();

    /// <summary>Military buildings asked to send a soldier out for a suspect (one fewer in <see cref="Staff"/>).</summary>
    private readonly HashSet<Building> _releasing = new();

    /// <summary>Sends soldiers to the suspects; they leave <paramref name="idle"/> while they go.</summary>
    private void InspectScouts(List<Building> military, List<Settler> idle)
    {
        var map = _world.Map!;
        var suspects = _world.Settlers
            .Where(s => s.Profession == Profession.Scout && IsEnemy(s.Player) && !s.IsDead && !s.IsInside
                        && map.OwnerAt(s.Position) == Player && !s.IsRevealedTo(Player))
            .ToList();
        foreach (var (id, soldier) in _inspectors.ToList())
            if (!suspects.Any(s => s.Id == id) || soldier.IsDead || soldier.IsInside || soldier.Player != Player)
                _inspectors.Remove(id); // revealed (fought now), gone, or the soldier is lost
        _releasing.RemoveWhere(b => !b.Exists || b.Player != Player);
        if (suspects.Count == 0)
            _releasing.Clear();

        var m = _world.Military;
        foreach (var suspect in suspects.OrderBy(s => s.Id))
        {
            if (_inspectors.TryGetValue(suspect.Id, out var inspector))
            {
                // Follow him; a fight on the way (with him revealed) is left alone.
                if (m.MoveTarget(inspector) is { } to && PathFinder.Distance(to, suspect.Position) > 2
                    || !inspector.HasTask)
                    m.Move([inspector], suspect.Position);
                idle.Remove(inspector);
                continue;
            }
            var free = idle.Where(s => m.ForcedMoveTarget(s) is null).MinBy(s => PathFinder.Distance(s.Position, suspect.Position));
            if (free is not null)
            {
                _inspectors[suspect.Id] = free;
                idle.Remove(free);
                m.Move([free], suspect.Position);
                _releasing.Clear();
                Note($"suspects a carrier at {suspect.Position.X},{suspect.Position.Y}, sends a soldier to look");
            }
            else if (_releasing.Count == 0
                     && military.Where(b => m.Occupants(b).Count(o => o is not null) > 1)
                         .MinBy(b => PathFinder.Distance(b.Door, suspect.Position)) is { } post)
            {
                _releasing.Add(post);
                Note($"suspects a carrier at {suspect.Position.X},{suspect.Position.Y}, calls a soldier out of {post.Position.X},{post.Position.Y}");
            }
        }
    }
}
