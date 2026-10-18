using System.Text.Json;
using System.Text.Json.Nodes;

namespace Homestead.Assets;

/// <summary>
/// Sections of the user's <c>settings.json</c> (<see cref="GameInstallation.SettingsPath"/>): <c>gameDirectory</c>,
/// <c>game</c>, <c>display</c>, … Each part of the program reads and writes its own section and keeps the others.
/// </summary>
public static class SettingsFile
{
    /// <summary>The section (a copy), or null without one or with an unreadable file.</summary>
    public static JsonElement? ReadSection(string name, string? path = null)
    {
        path ??= GameInstallation.SettingsPath;
        if (!File.Exists(path))
            return null;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty(name, out var section)
                ? section.Clone()
                : null;
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Replaces the section, keeping everything else in the file (written through a temporary file). A file that is
    /// no JSON object is left alone (<see cref="JsonException"/>) – it may hold the game directory.
    /// </summary>
    public static void WriteSection(string name, JsonNode section, string? path = null)
    {
        path ??= GameInstallation.SettingsPath;
        var root = File.Exists(path)
            ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? throw new JsonException($"{path} is no JSON object.")
            : new JsonObject();
        root[name] = section;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, overwrite: true);
    }
}
