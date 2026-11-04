namespace Homestead.Client.Terrain;

/// <summary>
/// The five water lab proposals, combined freely. A mutable session record: the F5 popup replaces
/// <see cref="WaterLab.Current"/> while the game runs, every renderer reads it per frame/draw.
/// </summary>
public sealed record WaterLabState
{
    /// <summary>Still water: no lifted wave pass, no crest/glint shine; glitter keeps running (the original's look).</summary>
    public bool FlatWater { get; set; }

    /// <summary>Per-pixel water shading through a custom effect instead of the mesh-lit BasicEffect pass.</summary>
    public bool Pixelsurf { get; set; }

    /// <summary>Runtime normal map with lit frames from the original water texels, sampled by the wave pass.</summary>
    public bool Normals { get; set; }

    /// <summary>Shoreline foam, washed in and out, drawn as an extra shine-style pass.</summary>
    public bool Foam { get; set; }

    /// <summary>Depth-graded water color: a tint ramp over the water rings Water1..Water8.</summary>
    public bool Rings { get; set; }

    public bool None => !FlatWater && !Pixelsurf && !Normals && !Foam && !Rings;

    /// <summary>The active flags as in HOMESTEAD_WATER (lower case, comma separated), or "(none)".</summary>
    public override string ToString() => None ? "(none)"
        : string.Join(", ", new[]
        {
            FlatWater ? "flatwater" : "", Pixelsurf ? "pixelsurf" : "", Normals ? "normals" : "",
            Foam ? "foam" : "", Rings ? "rings" : "",
        }.Where(f => f.Length > 0));
}

/// <summary>
/// The water lab's session state: the initial set comes from HOMESTEAD_WATER (comma list, case-insensitive;
/// unknown entries are logged), everything reads it live so the F5 popup toggles take effect immediately.
/// Default (variable unset) is all off, byte-identical to the plain game.
/// </summary>
public static class WaterLab
{
    public const string Variable = "HOMESTEAD_WATER";

    /// <summary>Ordered as in the env var; index 0..4 maps to the five flags.</summary>
    public static readonly string[] Names = ["flatwater", "pixelsurf", "normals", "foam", "rings"];

    public static WaterLabState Current { get; private set; } = Parse(Environment.GetEnvironmentVariable(Variable)).State;

    // Live reads (per frame/per draw): never cache across frames.
    public static bool FlatWater => Current.FlatWater;
    public static bool Pixelsurf => Current.Pixelsurf;
    public static bool Normals => Current.Normals;
    public static bool Foam => Current.Foam;
    public static bool Rings => Current.Rings;

    /// <summary>Parses a comma list (case-insensitive, blanks tolerated); unknown entries come back separately.</summary>
    public static (WaterLabState State, string[] Unknown) Parse(string? value)
    {
        var state = new WaterLabState();
        List<string>? unknown = null;
        if (!string.IsNullOrWhiteSpace(value))
            foreach (var entry in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                switch (entry.ToLowerInvariant())
                {
                    case "flatwater": state.FlatWater = true; break;
                    case "pixelsurf": state.Pixelsurf = true; break;
                    case "normals": state.Normals = true; break;
                    case "foam": state.Foam = true; break;
                    case "rings": state.Rings = true; break;
                    default: (unknown ??= []).Add(entry); break;
                }
        }
        return (state, unknown?.ToArray() ?? []);
    }

    public static void Set(WaterLabState next) => Current = next;

    /// <summary>
    /// Startup log of the preselected set. Only when HOMESTEAD_WATER is set at all: default-off runs must not
    /// print a single line mentioning the variable.
    /// </summary>
    public static void LogPreselection()
    {
        if (Environment.GetEnvironmentVariable(Variable) is not { } value)
            return;
        var (state, unknown) = Parse(value);
        Console.WriteLine($"{Variable}={value.Trim()} -> {state}"
            + (unknown.Length > 0 ? $" (unknown: {string.Join(", ", unknown)})" : ""));
    }
}
