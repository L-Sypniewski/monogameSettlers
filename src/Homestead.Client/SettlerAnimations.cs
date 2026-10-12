using System.Text.Json;
using Homestead.Simulation;
using Homestead.Simulation.Buildings;

namespace Homestead.Client;

/// <summary>Frames <see cref="Start"/> to <see cref="Start"/> + <see cref="Duration"/> of a settler sequence (negative: backwards).</summary>
public readonly record struct SettlerAnimation(int File, int Sequence, int Start, int Duration)
{
    /// <summary>Frame at <paramref name="progress"/> (0..1) of a one-shot run, e.g. a step.</summary>
    public int Frame(float progress) => Duration >= 0
        ? Start + Math.Clamp((int)(progress * Duration), 0, Math.Max(0, Duration - 1))
        : Start + Math.Clamp((int)(progress * Duration), Duration + 1, 0);

    /// <summary>Frame <paramref name="step"/> of a looping animation.</summary>
    public int Cycle(int step) => Duration >= 0
        ? Start + step % Math.Max(1, Duration)
        : Start - step % -Duration;

    /// <summary>Highest frame index the animation uses.</summary>
    public int LastFrame => Duration >= 0 ? Start + Math.Max(0, Duration - 1) : Start;
}

/// <summary>
/// Which settler sequence shows which people's settler doing what: type (<c>BEARER</c>, <c>LUMBERJACK</c>,
/// <c>SWORDSMAN_L2</c>, ...), action (<c>WALKING</c>, <c>NO_ACTION</c>, <c>ACTION1</c>, ...), carried material and
/// direction. Rules from <c>data/settlers.json</c> (generated from JSettlers' movables tables, MIT); like there
/// the more specific rule wins: direction over material over action over type over people.
/// </summary>
public sealed class SettlerAnimations
{
    private static readonly Civilization[] Peoples = Enum.GetValues<Civilization>();

    private readonly Dictionary<Key, (SettlerAnimation Animation, int Priority)> _rules = new();
    private readonly Dictionary<Key, SettlerAnimation?> _found = new();

    private SettlerAnimations()
    {
    }

    public static SettlerAnimations Load(string? dataDirectory = null)
    {
        string path = Path.Combine(dataDirectory ?? BuildingCatalog.DefaultDataDirectory, "settlers.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var animations = new SettlerAnimations();
        foreach (var rule in document.RootElement.GetProperty("rules").EnumerateArray())
        {
            var r = rule.EnumerateArray().ToArray();
            string? type = Wildcard(r[1].GetString()), action = Wildcard(r[2].GetString());
            string? material = Wildcard(r[3].GetString()), direction = Wildcard(r[4].GetString());
            // JSettlers' catch-all (any settler: the carrier's stand frame) is left out: callers fall back themselves.
            if (type is null && action is null && material is null && direction is null)
                continue;
            int priority = 1 + (r[0].ValueKind == JsonValueKind.Null ? 0 : 10) + (type is null ? 0 : 100)
                + (action is null ? 0 : 1000) + (material is null ? 0 : 10000) + (direction is null ? 0 : 100000);
            var peoples = r[0].ValueKind == JsonValueKind.Null ? Peoples : [Enum.Parse<Civilization>(r[0].GetString()!)];
            foreach (var people in peoples)
            {
                // "cN": the people's own file N (Romans 10+N, Egyptians 20+N, Asians 30+N, Amazons 40+N).
                int file = r[5].ValueKind == JsonValueKind.String
                    ? ((int)people + 1) * 10 + int.Parse(r[5].GetString()![1..])
                    : r[5].GetInt32();
                var key = new Key(people, type, action, material, direction);
                if (!animations._rules.TryGetValue(key, out var existing) || existing.Priority < priority)
                    animations._rules[key] = (new SettlerAnimation(file, r[6].GetInt32(), r[7].GetInt32(), r[8].GetInt32()), priority);
            }
        }
        return animations;
    }

    /// <summary>The most specific animation, or null if no rule names this type.</summary>
    public SettlerAnimation? Find(Civilization people, string type, string action, string material, Direction direction)
    {
        var query = new Key(people, type, action, material, Name(direction));
        if (_found.TryGetValue(query, out var cached))
            return cached;

        (SettlerAnimation Animation, int Priority)? best = null;
        for (int mask = 0; mask < 8; mask++)
        {
            var key = query with
            {
                Action = (mask & 1) == 0 ? action : null,
                Material = (mask & 2) == 0 ? material : null,
                Direction = (mask & 4) == 0 ? query.Direction : null,
            };
            if (_rules.TryGetValue(key, out var rule) && (best is null || rule.Priority > best.Value.Priority))
                best = rule;
        }
        return _found[query] = best?.Animation;
    }

    /// <summary>Data name of a material, as in the data files (<c>GOLDORE</c>, <c>GUN_POWDER</c>).</summary>
    public static string MaterialName(Simulation.Economy.Material? material) => material switch
    {
        null => "NO_MATERIAL",
        Simulation.Economy.Material.GunPowder => "GUN_POWDER",
        { } m => m.ToString().ToUpperInvariant(),
    };

    private static string Name(Direction direction) => direction switch
    {
        Direction.East => "EAST",
        Direction.SouthEast => "SOUTH_EAST",
        Direction.SouthWest => "SOUTH_WEST",
        Direction.West => "WEST",
        Direction.NorthWest => "NORTH_WEST",
        _ => "NORTH_EAST",
    };

    private static string? Wildcard(string? value) => value == "*" ? null : value;

    private readonly record struct Key(Civilization People, string? Type, string? Action, string? Material, string? Direction);
}
