using System.Text.Json;

namespace Homestead.Simulation.Ai.Traits;

/// <summary>
/// Early army (<c>earlyArmy</c> in docs/KI-TACTICS.md): the first build order entries of
/// <c>buildings</c> (iron melt, weaponsmith, barrack) move together, in that order, right behind the
/// first <c>after</c> entries (the basic wood and stone production). The start goods (iron ore, coal on
/// the medium and high start levels) then become soldiers instead of waiting for mines and food.
/// </summary>
internal sealed class EarlyArmyTrait(JsonElement settings) : AiTrait
{
    public const string TraitId = "earlyArmy";

    private readonly int _after = (int)Setting(settings, "after", 4);
    private readonly string[] _buildings = Setting(settings, "buildings", ["ironmelt", "weaponsmith", "barrack"]);

    public override string Id => TraitId;

    internal override IReadOnlyList<(string Building, int Count)> ArrangeBuildOrder(IReadOnlyList<(string Building, int Count)> order)
    {
        var result = order.ToList();
        var moved = new List<(string Building, int Count)>();
        foreach (string building in _buildings)
        {
            int index = result.FindIndex(e => e.Building == building);
            if (index < 0)
                continue;
            moved.Add(result[index]);
            result.RemoveAt(index);
        }
        result.InsertRange(Math.Min(_after, result.Count), moved);
        return result;
    }
}
