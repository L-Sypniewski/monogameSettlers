using System.Text.Json;
using System.Text.Json.Nodes;

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

    public string MapDirectory => FindSubdirectory("Map");

    /// <summary>
    /// The background music (<c>MUSIC/TrackNN.ogg</c>, the former CD audio tracks), if the installation has it –
    /// releases playing them from the CD do not.
    /// </summary>
    public string? MusicDirectory =>
        Directory.EnumerateDirectories(Root).FirstOrDefault(d => Path.GetFileName(d).Equals("MUSIC", StringComparison.OrdinalIgnoreCase));

    /// <summary>All map files below the map folder (single player, multiplayer, user maps).</summary>
    public IEnumerable<string> MapFiles =>
        Directory.EnumerateFiles(MapDirectory, "*", SearchOption.AllDirectories)
            .Where(f => Path.GetExtension(f).Equals(".map", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase);

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

    /// <summary>
    /// The configured installation (<c>HOMESTEAD_GAME_DIR</c>, then <c>gameDirectory</c> in the settings) if it passes
    /// <see cref="InstallationCheck"/> – checked on every start. Otherwise null, and <paramref name="configured"/> is
    /// the first configured folder (to start the search from).
    /// </summary>
    public static GameInstallation? TryLocate(out string? configured)
    {
        string?[] candidates = [Environment.GetEnvironmentVariable(EnvironmentVariable), ReadSettings()];
        configured = candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));
        foreach (string? candidate in candidates)
            if (!string.IsNullOrWhiteSpace(candidate) && InstallationCheck.Check(candidate).IsValid)
                return new GameInstallation(candidate);
        return null;
    }

    /// <summary>Stores the folder as <c>gameDirectory</c> in the settings (keeping the rest) and opens it.</summary>
    public static GameInstallation Remember(string root)
    {
        SettingsFile.WriteSection("gameDirectory", JsonValue.Create(root));
        return new GameInstallation(root);
    }

    private static string? ReadSettings()
    {
        return SettingsFile.ReadSection("gameDirectory") is { ValueKind: JsonValueKind.String } directory ? directory.GetString() : null;
    }

    // Installations differ in upper/lower case of folder names; Linux file systems care.
    /// <summary>
    /// A file or folder below the installation (<c>/</c>-separated, e.g. <c>S3/ADD/001/02_03_01.DAT</c>), whatever the case
    /// of its names (CD releases spell them in capitals, others not); null if it is missing.
    /// </summary>
    public string? FindPath(string relative)
    {
        string current = Root;
        foreach (string name in relative.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Directory.Exists(current))
                return null;
            string? next = Directory.EnumerateFileSystemEntries(current)
                .FirstOrDefault(e => Path.GetFileName(e).Equals(name, StringComparison.OrdinalIgnoreCase));
            if (next is null)
                return null;
            current = next;
        }
        return current;
    }

    private string FindSubdirectory(string name) =>
        Directory.EnumerateDirectories(Root)
            .FirstOrDefault(d => Path.GetFileName(d).Equals(name, StringComparison.OrdinalIgnoreCase))
        ?? throw new DirectoryNotFoundException($"No {name} folder in {Root}");
}
