using System.Text.Json;
using System.Text.Json.Nodes;
using Homestead.Assets;
using Homestead.Simulation.Economy;

namespace Homestead.Simulation;

/// <summary>
/// The player's game settings: the <c>game</c> section of <c>settings.json</c> (<see cref="GameInstallation.SettingsPath"/>,
/// beside <c>gameDirectory</c>), written by the settings screen. Whatever it leaves out keeps the value of
/// <c>data/production.json</c>. New games start with them (<see cref="Apply"/>); saved games keep their own.
/// </summary>
public sealed record GameSettings
{
    /// <summary>Percent of the units taken that use up the ground (<c>depletion</c> in <c>production.json</c>).</summary>
    public required Depletion Depletion { get; init; }

    /// <summary>The values of <paramref name="production"/>, as without a settings file.</summary>
    public static GameSettings Defaults(ProductionConfig production) => new() { Depletion = production.Depletion };

    /// <summary>The settings in the file (default: the user's), the rest from <paramref name="production"/>; an unreadable file counts as none.</summary>
    public static GameSettings Load(ProductionConfig production, string? path = null)
    {
        path ??= GameInstallation.SettingsPath;
        var settings = Defaults(production);
        if (!File.Exists(path))
            return settings;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("game", out var game))
                return settings;
            if (game.TryGetProperty("depletion", out var depletion))
            {
                int Percent(string name, int fallback) =>
                    depletion.TryGetProperty(name, out var value) && value.TryGetInt32(out int percent) ? Math.Clamp(percent, 0, 100) : fallback;
                var d = settings.Depletion;
                settings = settings with { Depletion = new Depletion(Percent("stone", d.Stone), Percent("mountain", d.Mountain), Percent("fish", d.Fish)) };
            }
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
        }
        return settings;
    }

    /// <summary><paramref name="production"/> with these settings.</summary>
    public ProductionConfig Apply(ProductionConfig production) => production with { Depletion = Depletion };

    /// <summary>
    /// Writes the <c>game</c> section into the file (default: the user's), keeping everything else in it. A file that
    /// is no JSON object is left alone (<see cref="JsonException"/>) – it may hold the game directory.
    /// </summary>
    public void Save(string? path = null)
    {
        path ??= GameInstallation.SettingsPath;
        var root = File.Exists(path)
            ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? throw new JsonException($"{path} is no JSON object.")
            : new JsonObject();
        root["game"] = new JsonObject
        {
            ["depletion"] = new JsonObject { ["stone"] = Depletion.Stone, ["mountain"] = Depletion.Mountain, ["fish"] = Depletion.Fish },
        };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, overwrite: true);
    }
}
