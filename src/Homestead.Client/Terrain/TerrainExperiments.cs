namespace Homestead.Client.Terrain;

/// <summary>
/// HOMESTEAD_TERRAIN=upscale2x,mips,depth,foam (comma-separated): landscape and water rendering experiments.
/// Without the variable everything is off, which is bit for bit the unmodified renderer. Entries that match no
/// experiment are ignored and collected in <see cref="Unknown"/> for the startup log.
/// </summary>
public sealed record TerrainExperiments(bool Upscale2x, bool Mips, bool Depth, bool Foam)
{
    public static readonly TerrainExperiments Off = new(false, false, false, false);

    /// <summary>Entries of the variable that no experiment matched.</summary>
    public IReadOnlyList<string> Unknown { get; private init; } = [];

    public bool IsOn => Upscale2x || Mips || Depth || Foam;

    public override string ToString()
    {
        var active = new List<string>();
        if (Upscale2x)
            active.Add("upscale2x");
        if (Mips)
            active.Add("mips");
        if (Depth)
            active.Add("depth");
        if (Foam)
            active.Add("foam");
        return active.Count == 0 ? "off" : string.Join(", ", active);
    }

    public static TerrainExperiments Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Off;
        bool upscale2x = false, mips = false, depth = false, foam = false;
        var unknown = new List<string>();
        foreach (string entry in value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (entry.ToLowerInvariant())
            {
                case "upscale2x":
                    upscale2x = true;
                    break;
                case "mips":
                    mips = true;
                    break;
                case "depth":
                    depth = true;
                    break;
                case "foam":
                    foam = true;
                    break;
                default:
                    unknown.Add(entry);
                    break;
            }
        }
        return new TerrainExperiments(upscale2x, mips, depth, foam) { Unknown = unknown };
    }
}
