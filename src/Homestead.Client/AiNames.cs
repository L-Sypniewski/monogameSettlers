using System.Text.Json;
using Homestead.Simulation.Buildings;

namespace Homestead.Client;

/// <summary>German names of the AI characters and traits (<c>aiCharacters</c>, <c>aiTraits</c> in <c>data/ui.json</c>); unknown ids stay as they are.</summary>
public sealed class AiNames
{
    private readonly Dictionary<string, string> _characters = new();
    private readonly Dictionary<string, string> _traits = new();

    public AiNames()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(BuildingCatalog.DefaultDataDirectory, "ui.json")));
        var root = document.RootElement;
        if (root.TryGetProperty("aiCharacters", out var characters))
            foreach (var name in characters.EnumerateObject())
                _characters[name.Name] = name.Value.GetString()!;
        if (root.TryGetProperty("aiTraits", out var traits))
            foreach (var name in traits.EnumerateObject())
                _traits[name.Name] = name.Value.GetString()!;
    }

    public string Character(string id) => _characters.GetValueOrDefault(id, id);

    public string Trait(string id) => _traits.GetValueOrDefault(id, id);
}
