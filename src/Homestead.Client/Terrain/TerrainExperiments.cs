namespace Homestead.Client.Terrain;

/// <summary>
/// HOMESTEAD_TERRAIN=upscale2x,mips,depth,foam,tess2,tess4,softlight (comma-separated): landscape and water
/// rendering experiments. Without the variable everything is off, which is bit for bit the unmodified renderer.
/// Entries that match no experiment are ignored and collected in <see cref="Unknown"/> for the startup log.
/// </summary>
public sealed record TerrainExperiments(bool Upscale2x, bool Mips, bool Depth, bool Foam, int Tessellation = 1, bool SoftLight = false)
{
    public static readonly TerrainExperiments Off = new(false, false, false, false);

    /// <summary>All experiments, the water at tessellation 4: what F5 turns on when the variable selected none.</summary>
    public static readonly TerrainExperiments All = new(true, true, true, true, 4, true);

    /// <summary>Entries of the variable that no experiment matched.</summary>
    public IReadOnlyList<string> Unknown { get; private init; } = [];

    public bool IsOn => Upscale2x || Mips || Depth || Foam || Tessellation > 1 || SoftLight;

    /// <summary>Equality is the flags only; <see cref="Unknown"/> is parse diagnostics for the log.</summary>
    public bool Equals(TerrainExperiments? other) =>
        other is not null && Upscale2x == other.Upscale2x && Mips == other.Mips
        && Depth == other.Depth && Foam == other.Foam && Tessellation == other.Tessellation && SoftLight == other.SoftLight;

    public override int GetHashCode() => HashCode.Combine(Upscale2x, Mips, Depth, Foam, Tessellation, SoftLight);

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
        if (Tessellation > 1)
            active.Add($"tess{Tessellation}");
        if (SoftLight)
            active.Add("softlight");
        return active.Count == 0 ? "off" : string.Join(", ", active);
    }

    public static TerrainExperiments Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Off;
        bool upscale2x = false, mips = false, depth = false, foam = false, tess2 = false, tess4 = false, softLight = false;
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
                case "tess2":
                    tess2 = true;
                    break;
                case "tess4":
                    tess4 = true;
                    break;
                case "softlight":
                    softLight = true;
                    break;
                default:
                    unknown.Add(entry);
                    break;
            }
        }
        // One knob: tess4 wins when both are given.
        return new TerrainExperiments(upscale2x, mips, depth, foam, tess4 ? 4 : tess2 ? 2 : 1, softLight) { Unknown = unknown };
    }
}

/// <summary>
/// What runs this session: <see cref="Active"/> starts as the variable's set (off without one, so nothing
/// changes until F5 or the variable) and F5 flips it to <see cref="Selected"/> and back. Selected is the
/// variable's set, or all experiments when it selected none (the packaged app has no variable, F5 is
/// the only way in). Everything that renders reads <see cref="Active"/> live, so a flip applies at once.
/// </summary>
public sealed class TerrainExperimentSession
{
    private readonly IReadOnlyList<string> _unknown;

    public TerrainExperimentSession(string? environment)
    {
        var parsed = TerrainExperiments.Parse(environment);
        _unknown = parsed.Unknown;
        Selected = parsed.IsOn ? parsed : TerrainExperiments.All;
        Active = parsed;
    }

    /// <summary>The set F5 turns on.</summary>
    public TerrainExperiments Selected { get; private set; }

    /// <summary>What runs now.</summary>
    public TerrainExperiments Active { get; private set; }

    public bool IsOn => Active.IsOn;

    public IReadOnlyList<string> Unknown => _unknown;

    /// <summary>F5: on → off, off → the selected set.</summary>
    public TerrainExperiments Toggle()
    {
        Active = Active.IsOn ? TerrainExperiments.Off : Selected;
        return Active;
    }

    /// <summary>The upscaler failed: upscale2x leaves both sets for the rest of the session, the other
    /// experiments keep toggling.</summary>
    public void Upscale2xFailed()
    {
        Selected = Selected with { Upscale2x = false };
        Active = Active with { Upscale2x = false };
    }
}
