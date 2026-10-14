namespace Homestead.Simulation.Ai;

/// <summary>What an AI plays like: its traits, in order of priority.</summary>
public sealed class AiCharacter(string id, IReadOnlyList<AiTrait> traits)
{
    /// <summary>The character's id in ai.json (English); its German name is in ui.json.</summary>
    public string Id { get; } = id;

    public IReadOnlyList<AiTrait> Traits { get; } = traits;

    public static AiCharacter Create(AiConfig config, string id) =>
        config.Characters.TryGetValue(id, out var traits)
            ? new AiCharacter(id, traits.Select(t => AiTrait.Create(t.Trait, t.Settings)).ToArray())
            : throw new ArgumentException($"unknown AI character '{id}' (known: {string.Join(", ", config.Characters.Keys)})");

    /// <summary>A character drawn by the people's weights.</summary>
    public static AiCharacter Draw(AiConfig config, Random random)
    {
        double total = config.CharacterWeights.Sum(w => w.Weight);
        double pick = random.NextDouble() * total;
        foreach (var (character, weight) in config.CharacterWeights)
        {
            if (pick < weight)
                return Create(config, character);
            pick -= weight;
        }
        return Create(config, config.CharacterWeights.Count > 0 ? config.CharacterWeights[^1].Character : AiConfig.StandardCharacter);
    }
}
