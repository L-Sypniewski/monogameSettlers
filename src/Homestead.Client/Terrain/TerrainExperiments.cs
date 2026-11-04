namespace Homestead.Client.Terrain;

/// <summary>
/// HOMESTEAD_TERRAIN=upscale2x,mips,depth,foam,tess2,tess4,softlight (comma-separated): landscape and water
/// rendering experiments. Without the variable everything is off, which is bit for bit the unmodified renderer.
/// Entries that match no experiment are ignored and collected in <see cref="Unknown"/> for the startup log.
/// </summary>
public sealed record TerrainExperiments(bool Upscale2x, bool Mips, bool Depth, bool Foam, int Tessellation = 1, bool SoftLight = false)
{
    public static readonly TerrainExperiments Off = new(false, false, false, false);

    /// <summary>All experiments, the water at tessellation 4.</summary>
    public static readonly TerrainExperiments All = new(true, true, true, true, 4, true);

    /// <summary>The water experiments: depth shading, shore foam, the water at tessellation 4 and softlight.</summary>
    public static readonly TerrainExperiments Water = new(false, false, true, true, 4, true);

    /// <summary>The landscape experiments: the 2x atlas and its mipmaps.</summary>
    public static readonly TerrainExperiments Terrain = new(true, true, false, false);

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
/// changes until F5 or the variable) and F5 steps it around a fixed ring of presets: off, all, water,
/// terrain, then off again. Everything that renders reads <see cref="Active"/> live, so a step applies at once.
/// </summary>
public sealed class TerrainExperimentSession
{
    /// <summary>The F5 ring, in cycle order.</summary>
    private static readonly (string Name, TerrainExperiments Set)[] Ring =
    [
        ("off", TerrainExperiments.Off),
        ("all", TerrainExperiments.All),
        ("water", TerrainExperiments.Water),
        ("terrain", TerrainExperiments.Terrain),
    ];

    private readonly IReadOnlyList<string> _unknown;
    private bool _upscale2xDropped;

    public TerrainExperimentSession(string? environment)
    {
        var parsed = TerrainExperiments.Parse(environment);
        _unknown = parsed.Unknown;
        Active = parsed;
        // The ring starts at the stop the variable's set equals, so the first F5 goes to that stop's successor.
        // A set equal to no stop is the variable's custom one: its index is -1, so the first F5 goes to off
        // (-1 + 1 = 0) and the ring continues normally from there — the first step is predictable from any start.
        NextPresetIndex = (IndexOfPreset(parsed) + 1) % Ring.Length;
    }

    /// <summary>What runs now.</summary>
    public TerrainExperiments Active { get; private set; }

    /// <summary>The ring stop the next F5 steps to.</summary>
    public int NextPresetIndex { get; private set; }

    public bool IsOn => Active.IsOn;

    public IReadOnlyList<string> Unknown => _unknown;

    /// <summary>The announcement names the ring stop whose set runs (degraded like the ring after an upscaler
    /// failure), or the set itself while the variable's custom one runs.</summary>
    public string ActiveName
    {
        get
        {
            for (int i = 0; i < Ring.Length; i++)
                if (Preset(i).Equals(Active))
                    return Ring[i].Name;
            return Active.ToString();
        }
    }

    /// <summary>F5: the next stop of the ring, wrapping around.</summary>
    public TerrainExperiments Cycle()
    {
        Active = Preset(NextPresetIndex);
        NextPresetIndex = (NextPresetIndex + 1) % Ring.Length;
        return Active;
    }

    /// <summary>The upscaler failed: upscale2x leaves the active set and every ring preset that has it for the
    /// rest of the session, the other experiments keep cycling.</summary>
    public void Upscale2xFailed()
    {
        _upscale2xDropped = true;
        Active = Active with { Upscale2x = false };
    }

    private static int IndexOfPreset(TerrainExperiments set)
    {
        for (int i = 0; i < Ring.Length; i++)
            if (Ring[i].Set.Equals(set))
                return i;
        return -1;
    }

    /// <summary>A ring stop's set, without upscale2x everywhere once the upscaler failed.</summary>
    private TerrainExperiments Preset(int index) =>
        _upscale2xDropped && Ring[index].Set.Upscale2x ? Ring[index].Set with { Upscale2x = false } : Ring[index].Set;
}
