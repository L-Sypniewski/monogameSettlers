using System.Text.Json;

namespace Homestead.Assets;

/// <summary>
/// Locates the user's local installation of the original game. Assets are only ever read
/// from there at runtime and never copied into the repository or build output.
/// </summary>
/// <remarks>
/// Lookup order: explicit argument, environment variable <c>HOMESTEAD_GAME_DIR</c>,
/// <c>gameDirectory</c> in <c>~/.config/homestead/settings.json</c> (Windows: <c>%APPDATA%</c>).
/// </remarks>
public sealed class GameInstallation
{
    public const string EnvironmentVariable = "HOMESTEAD_GAME_DIR";

    private GameInstallation(string root)
    {
        Root = root;
    }

    public string Root { get; }

    public string GfxDirectory => FindSubdirectory("GFX");

    public string SndDirectory => FindSubdirectory("SND");

    // Not XDG_CONFIG_HOME on purpose: sandboxed launchers (Flatpak IDEs) redirect it,
    // and the same file should be found no matter how the program was started.
    public static string SettingsPath => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "homestead", "settings.json")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "homestead", "settings.json");

    public static GameInstallation Locate(string? explicitPath = null)
    {
        string? root = explicitPath
            ?? Environment.GetEnvironmentVariable(EnvironmentVariable)
            ?? ReadSettings();

        if (string.IsNullOrWhiteSpace(root))
            throw new InvalidOperationException(
                $"Game directory not configured. Set {EnvironmentVariable} or \"gameDirectory\" in {SettingsPath}.");
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Game directory not found: {root}");

        return new GameInstallation(root);
    }

    private static string? ReadSettings()
    {
        if (!File.Exists(SettingsPath))
            return null;
        using var doc = JsonDocument.Parse(File.ReadAllText(SettingsPath));
        return doc.RootElement.TryGetProperty("gameDirectory", out var dir) ? dir.GetString() : null;
    }

    // Installations differ in upper/lower case of folder names; Linux file systems care.
    private string FindSubdirectory(string name) =>
        Directory.EnumerateDirectories(Root)
            .FirstOrDefault(d => Path.GetFileName(d).Equals(name, StringComparison.OrdinalIgnoreCase))
        ?? throw new DirectoryNotFoundException($"No {name} folder in {Root}");
}
