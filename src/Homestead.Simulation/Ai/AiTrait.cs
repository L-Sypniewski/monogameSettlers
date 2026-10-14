using System.Text.Json;
using Homestead.Simulation.Ai.Traits;
using Homestead.Simulation.Buildings;

namespace Homestead.Simulation.Ai;

/// <summary>
/// A trait of an AI character (Charakterzug, see docs/KI-TACTICS.md). A trait hooks into fixed decision points of
/// <see cref="AiPlayer"/>; where it returns nothing, the built-in behavior decides. Traits are combined
/// into characters in <c>data/ai.json</c>.
/// </summary>
public abstract class AiTrait
{
    /// <summary>The trait's id in ai.json (English); its German name is in ui.json.</summary>
    public abstract string Id { get; }

    /// <summary>
    /// Decision point "leading troops": takes over a campaign the AI has decided on (target and soldiers
    /// are chosen). The first trait of the character returning a leader leads it; null leaves it to the
    /// built-in gather-and-attack. The leader may take more of the <paramref name="spare"/> soldiers
    /// (removing them there, adding them to the campaign).
    /// </summary>
    internal virtual CampaignLeader? LeadCampaign(AiPlayer ai, AiPlayer.Campaign campaign, List<Settler> spare) => null;

    /// <summary>
    /// Decision point "guarding": places the idle soldiers left after defence, strikes and campaign
    /// (<paramref name="military"/>: the own finished military buildings). True if the trait did; the
    /// first one that does decides. Without such a trait idle soldiers stay where they are.
    /// </summary>
    internal virtual bool Guard(AiPlayer ai, List<Building> military, List<Settler> idle) => false;

    private static readonly Dictionary<string, Func<JsonElement, AiTrait>> Factories = new()
    {
        [PincerTrait.TraitId] = settings => new PincerTrait(settings),
        [GuardByThreatTrait.TraitId] = _ => new GuardByThreatTrait(),
    };

    public static IEnumerable<string> Known => Factories.Keys;

    public static AiTrait Create(string id, JsonElement settings) =>
        Factories.TryGetValue(id, out var create) ? create(settings)
            : throw new InvalidDataException($"unknown AI trait '{id}' (known: {string.Join(", ", Known)})");

    protected static double Setting(JsonElement settings, string name, double fallback) =>
        settings.ValueKind == JsonValueKind.Object && settings.TryGetProperty(name, out var value) ? value.GetDouble() : fallback;
}

/// <summary>Leads a campaign in place of the built-in gather-and-attack.</summary>
internal abstract class CampaignLeader
{
    /// <summary>
    /// Every think while the target is still standing and soldiers are left. False once the campaign is
    /// over (the leader has sent the soldiers home).
    /// </summary>
    public abstract bool Follow(AiPlayer ai, AiPlayer.Campaign campaign);
}
