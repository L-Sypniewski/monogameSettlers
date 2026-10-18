using System.Text.Json;
using System.Text.Json.Nodes;
using Homestead.Assets;
using Homestead.Simulation.Economy;

namespace Homestead.Simulation;

/// <summary>
/// The player's game settings: the <c>game</c> section of <c>settings.json</c> (<see cref="SettingsFile"/>), written by
/// the settings screen. Whatever it leaves out keeps the value of <c>data/production.json</c>. New games start with
/// them (<see cref="Apply"/>); saved games keep their own.
/// </summary>
public sealed record GameSettings
{
    private const string Section = "game";

    /// <summary>Percent of the units taken that use up the ground (<c>depletion</c> in <c>production.json</c>).</summary>
    public required Depletion Depletion { get; init; }

    /// <summary>The values of <paramref name="production"/>, as without a settings file.</summary>
    public static GameSettings Defaults(ProductionConfig production) => new() { Depletion = production.Depletion };

    /// <summary>The settings in the file (default: the user's), the rest from <paramref name="production"/>; an unreadable file counts as none.</summary>
    public static GameSettings Load(ProductionConfig production, string? path = null)
    {
        var settings = Defaults(production);
        if (SettingsFile.ReadSection(Section, path) is not { ValueKind: JsonValueKind.Object } game)
            return settings;
        if (game.TryGetProperty("depletion", out var depletion) && depletion.ValueKind == JsonValueKind.Object)
        {
            int Percent(string name, int fallback) =>
                depletion.TryGetProperty(name, out var value) && value.TryGetInt32(out int percent) ? Math.Clamp(percent, 0, 100) : fallback;
            var d = settings.Depletion;
            settings = settings with { Depletion = new Depletion(Percent("stone", d.Stone), Percent("mountain", d.Mountain), Percent("fish", d.Fish)) };
        }
        return settings;
    }

    /// <summary><paramref name="production"/> with these settings.</summary>
    public ProductionConfig Apply(ProductionConfig production) => production with { Depletion = Depletion };

    /// <summary>Writes the <c>game</c> section into the file (default: the user's), see <see cref="SettingsFile.WriteSection"/>.</summary>
    public void Save(string? path = null) =>
        SettingsFile.WriteSection(Section, new JsonObject
        {
            ["depletion"] = new JsonObject { ["stone"] = Depletion.Stone, ["mountain"] = Depletion.Mountain, ["fish"] = Depletion.Fish },
        }, path);
}
