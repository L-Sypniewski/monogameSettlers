using Homestead.Assets.Gfx;

namespace Homestead.Assets;

/// <summary>What <see cref="InstallationCheck.Check"/> found in a folder.</summary>
/// <param name="GfxFiles">GFX file numbers found (one per number, either color variant).</param>
/// <param name="MissingGfx">Numbers of <see cref="InstallationCheck.RequiredGfx"/> not found.</param>
/// <param name="MissingSounds">Files of <see cref="InstallationCheck.RequiredSounds"/> not found.</param>
/// <param name="Maps">Map files below the map folder.</param>
public sealed record InstallationReport(
    string Root, bool Exists, bool HasGfx, int GfxFiles, IReadOnlyList<int> MissingGfx,
    bool HasSounds, IReadOnlyList<string> MissingSounds, bool HasMaps, int Maps)
{
    public bool GfxOk => HasGfx && MissingGfx.Count == 0;
    public bool SoundsOk => HasSounds && MissingSounds.Count == 0;
    public bool MapsOk => HasMaps && Maps > 0;
    public bool IsValid => Exists && GfxOk && SoundsOk && MapsOk;
}

/// <summary>
/// Checks whether a folder holds a usable installation of the original game (graphics, sounds, maps), finds the
/// installation near a folder the player picked, and looks for installations in the usual places.
/// </summary>
public static class InstallationCheck
{
    /// <summary>
    /// The GFX files the game reads: landscape, settlers, objects, GUI of the panel and the menus (0–17), the four
    /// peoples (20–27, 30–37, 40–48; 10–17 for the Romans) and the Gold Edition's main menu (61). 18 is no GFX file.
    /// </summary>
    public static readonly IReadOnlyList<int> RequiredGfx =
        [.. Enumerable.Range(0, 18), .. Enumerable.Range(20, 8), .. Enumerable.Range(30, 8), .. Enumerable.Range(40, 9), 61];

    /// <summary>The sound files (sound effects).</summary>
    public static readonly IReadOnlyList<string> RequiredSounds = ["siedler3_00.dat", "siedler3_01.dat"];

    public static InstallationReport Check(string root)
    {
        bool exists = Directory.Exists(root);
        string? gfx = exists ? Subdirectory(root, "GFX") : null;
        string? snd = exists ? Subdirectory(root, "SND") : null;
        string? map = exists ? Subdirectory(root, "Map") : null;

        IReadOnlyCollection<int> found = [];
        if (gfx is not null)
            try
            {
                found = new GfxDirectory(gfx).FileNumbers;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        var sounds = snd is null
            ? []
            : Directory.EnumerateFiles(snd).Select(f => Path.GetFileName(f).ToLowerInvariant()).ToHashSet();
        int maps = 0;
        if (map is not null)
            try
            {
                maps = Directory.EnumerateFiles(map, "*", SearchOption.AllDirectories)
                    .Count(f => Path.GetExtension(f).Equals(".map", StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        return new InstallationReport(root, exists, gfx is not null, found.Count, RequiredGfx.Except(found).ToArray(),
            snd is not null, RequiredSounds.Where(s => !sounds.Contains(s)).ToArray(), map is not null, maps);
    }

    /// <summary>
    /// The installation the player meant with <paramref name="path"/>: the folder itself, the one above if it is
    /// one of the installation's folders (GFX, SND, Map …), or the first valid one below (e.g. a Wine prefix or the
    /// launcher's game folder; at most <paramref name="depth"/> levels). Null if there is none.
    /// </summary>
    public static string? FindRoot(string path, int depth = 4)
    {
        if (!Directory.Exists(path))
            return null;
        path = Path.GetFullPath(path);
        if (Check(path).IsValid)
            return path;
        if (Path.GetDirectoryName(path.TrimEnd(Path.DirectorySeparatorChar)) is { } parent && Check(parent).IsValid)
            return parent;
        return Below(path, depth, new Budget(4000)).FirstOrDefault();
    }

    /// <summary>
    /// Valid installations in the usual places (GOG, Steam, Ubisoft, Wine prefixes, Lutris, …); stops after
    /// <paramref name="maxFolders"/> folders looked at.
    /// </summary>
    public static IEnumerable<string> Detect(int maxFolders = 20000)
    {
        var budget = new Budget(maxFolders);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (start, depth) in SearchStarts())
            if (Directory.Exists(start))
                foreach (string root in Below(start, depth, budget))
                    if (seen.Add(root))
                        yield return root;
    }

    private sealed class Budget(int folders)
    {
        public int Left = folders;
    }

    private static IEnumerable<(string Path, int Depth)> SearchStarts()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsWindows())
        {
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed))
            {
                yield return (Path.Combine(drive.RootDirectory.FullName, "GOG Games"), 2);
                yield return (Path.Combine(drive.RootDirectory.FullName, "Games"), 3);
            }
            foreach (var folder in new[] { Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.ProgramFiles })
                if (Environment.GetFolderPath(folder) is { Length: > 0 } programs)
                {
                    yield return (Path.Combine(programs, "GOG Galaxy", "Games"), 2);
                    yield return (Path.Combine(programs, "Steam", "steamapps", "common"), 2);
                    yield return (Path.Combine(programs, "Ubisoft"), 3);
                    yield return (programs, 2);
                }
            yield break;
        }
        // Wine prefixes of launchers: the game sits in drive_c/<GOG Games|Program Files…>/<game>.
        yield return (Path.Combine(home, "Games"), 6);
        yield return (Path.Combine(home, ".wine", "drive_c"), 3);
        yield return (Path.Combine(home, ".local", "share", "Steam", "steamapps", "common"), 2);
        yield return (Path.Combine(home, ".steam", "steam", "steamapps", "common"), 2);
        yield return (Path.Combine(home, ".local", "share", "lutris"), 5);
        yield return (Path.Combine(home, ".var", "app", "com.usebottles.bottles", "data", "bottles", "bottles"), 5);
        yield return (Path.Combine(home, "GOG Games"), 2);
        yield return (home, 2);
    }

    /// <summary>Valid installations below <paramref name="start"/>, breadth first; hidden folders and links are skipped.</summary>
    private static IEnumerable<string> Below(string start, int depth, Budget budget)
    {
        var queue = new Queue<(string Path, int Depth)>();
        queue.Enqueue((start, 0));
        while (queue.Count > 0 && budget.Left > 0)
        {
            var (folder, level) = queue.Dequeue();
            budget.Left--;
            if (Subdirectory(folder, "GFX") is not null && Subdirectory(folder, "Map") is not null && Check(folder).IsValid)
            {
                yield return folder;
                continue;
            }
            if (level >= depth)
                continue;
            string[] children;
            try
            {
                children = Directory.GetDirectories(folder);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            foreach (string child in children.Order(StringComparer.OrdinalIgnoreCase))
            {
                var info = new DirectoryInfo(child);
                if (info.Name.StartsWith('.') || info.LinkTarget is not null)
                    continue;
                queue.Enqueue((child, level + 1));
            }
        }
    }

    /// <summary>The subfolder of that name in any upper/lower case (Linux file systems care), or null.</summary>
    public static string? Subdirectory(string folder, string name)
    {
        try
        {
            return Directory.EnumerateDirectories(folder)
                .FirstOrDefault(d => Path.GetFileName(d).Equals(name, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
