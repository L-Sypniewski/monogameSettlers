using System.Text.Json;
using Homestead.Simulation.Buildings;
using Homestead.Simulation.Pathfinding;

namespace Homestead.Simulation.Ai.Traits;

/// <summary>
/// Garrison by front (<c>frontGarrison</c> in docs/KI-TACTICS.md): military buildings within
/// <c>frontDistance</c> of an enemy military building (sites included) are fully staffed, those further
/// back get only <c>backline</c> soldiers. The soldiers that come out are free for strikes and campaigns.
/// </summary>
internal sealed class FrontGarrisonTrait(JsonElement settings) : AiTrait
{
    public const string TraitId = "frontGarrison";

    private readonly int _frontDistance = (int)Setting(settings, "frontDistance", 45);
    private readonly int _backline = (int)Setting(settings, "backline", 1);

    public override string Id => TraitId;

    internal override int? GarrisonLimit(AiPlayer ai, Building building) =>
        ai.World.Buildings.Any(b => b.Exists && ai.IsEnemy(b.Player) && b.Definition.Occupiers.Count > 0
                                    && PathFinder.Distance(b.Position, building.Position) <= _frontDistance)
            ? null
            : _backline;
}
