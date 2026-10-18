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
    /// Replaces the section, keeping everything else in the file (written through a temporary file). An empty file
    /// counts as none; one that is no JSON object is kept as <c>settings.json.broken</c> (it may hold something the
    /// player wants back) and a new one started.
    /// </summary>
    public static void WriteSection(string name, JsonNode section, string? path = null)
    {
        path ??= GameInstallation.SettingsPath;
        var root = new JsonObject();
        if (File.Exists(path) && File.ReadAllText(path) is var text && !string.IsNullOrWhiteSpace(text))
        {
            try
            {
                root = JsonNode.Parse(text) as JsonObject ?? throw new JsonException($"{path} is no JSON object.");
            }
            catch (JsonException)
            {
                File.Copy(path, path + ".broken", overwrite: true);
            }
        }
        root[name] = section;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, overwrite: true);
    }
}
