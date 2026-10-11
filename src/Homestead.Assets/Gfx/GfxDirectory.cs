using System.Text.RegularExpressions;

namespace Homestead.Assets.Gfx;

/// <summary>
/// The GFX folder of an installation: maps file numbers (<c>siedler3_NN</c>) to paths,
/// picking one color variant per number.
/// </summary>
public sealed partial class GfxDirectory
{
    private readonly SortedDictionary<int, string> _files = new();
    private readonly Dictionary<int, GfxFile> _opened = new();

    public GfxDirectory(string gfxDirectory, GfxColorFormat preferred = GfxColorFormat.Rgb565)
    {
        string preferredSuffix = preferred == GfxColorFormat.Rgb565 ? "f8007e01f" : "7c003e01f";
        foreach (string path in Directory.EnumerateFiles(gfxDirectory, "*.dat"))
        {
            Match m = FileNamePattern().Match(System.IO.Path.GetFileName(path));
            // Not every .dat in the folder is a GFX container (e.g. number 18 is a DLL).
            if (!m.Success || !GfxFile.HasGfxSignature(path))
                continue;
            int number = int.Parse(m.Groups[1].Value);
            bool isPreferred = m.Groups[2].Value.Equals(preferredSuffix, StringComparison.OrdinalIgnoreCase);
            if (isPreferred || !_files.ContainsKey(number))
                _files[number] = path;
        }
    }

    public IReadOnlyCollection<int> FileNumbers => _files.Keys;

    public GfxFile Open(int number)
    {
        if (!_opened.TryGetValue(number, out var file))
            _opened[number] = file = GfxFile.Open(_files[number]);
        return file;
    }

    [GeneratedRegex(@"^siedler3_(\d+)\.(7c003e01f|f8007e01f)\.dat$", RegexOptions.IgnoreCase)]
    private static partial Regex FileNamePattern();
}
