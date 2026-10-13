using System.Text.Json;
using Homestead.Simulation.Buildings;
using Homestead.Simulation.Economy;

namespace Homestead.Simulation.Magic;

public enum SpellKind
{
    /// <summary>Enemy soldiers change sides (only their color changes).</summary>
    Convert,

    /// <summary>Own soldiers strike faster for a while.</summary>
    Haste,

    /// <summary>Enemy soldiers and priests stand still for a while.</summary>
    Freeze,

    /// <summary>Enemy soldiers are put on random points of the map.</summary>
    Scatter,
}

/// <param name="Count">Convert, Scatter: most soldiers affected.</param>
/// <param name="Ticks">Haste, Freeze: how long it lasts.</param>
/// <param name="Factor">Haste: how many times as fast the soldiers strike.</param>
public sealed record Spell(SpellKind Kind, string Name, int Cost, int Count = 0, int Ticks = 0, double Factor = 1);

/// <summary>Magic balancing, loaded from <c>data/magic.json</c>.</summary>
public sealed record MagicConfig
{
    public string BigTemple { get; init; } = "big_temple";
    public int PriestTicks { get; init; } = 300 * World.TicksPerSecond;
    public string Temple { get; init; } = "temple";
    public int TempleTicks { get; init; } = 6 * World.TicksPerSecond;

    /// <summary>Mana a small temple makes from one unit of each drink.</summary>
    public IReadOnlyDictionary<Material, int> Mana { get; init; } = new Dictionary<Material, int>();
    public float PriestHealth { get; init; } = 60;

    /// <summary>A spell works on the soldiers within this radius of the point it is cast at.</summary>
    public int Radius { get; init; } = 5;
    public int CastTicks { get; init; } = 30;
    public IReadOnlyDictionary<Civilization, Spell> Spells { get; init; } = new Dictionary<Civilization, Spell>();

    /// <summary>Mana earned in all for the player's 1st, 2nd, ... soldier upgrade (whichever type); empty: no upgrades.</summary>
    public IReadOnlyList<int> UpgradeThresholds { get; init; } = [];

    public static MagicConfig None { get; } = new();

    public static MagicConfig Load(string? dataDirectory = null)
    {
        string path = Path.Combine(dataDirectory ?? BuildingCatalog.DefaultDataDirectory, "magic.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        static int Ticks(double seconds) => (int)Math.Round(seconds * World.TicksPerSecond);
        return new MagicConfig
        {
            BigTemple = root.GetProperty("bigTemple").GetString()!,
            PriestTicks = Ticks(root.GetProperty("priestSeconds").GetDouble()),
            Temple = root.GetProperty("temple").GetString()!,
            TempleTicks = Ticks(root.GetProperty("templeSeconds").GetDouble()),
            Mana = root.GetProperty("mana").EnumerateObject().ToDictionary(m => Materials.Parse(m.Name)!.Value, m => m.Value.GetInt32()),
            PriestHealth = root.GetProperty("priestHealth").GetSingle(),
            Radius = root.GetProperty("radius").GetInt32(),
            CastTicks = Ticks(root.GetProperty("castSeconds").GetDouble()),
            Spells = root.GetProperty("spells").EnumerateObject().Where(s => !s.Name.StartsWith('_')).ToDictionary(
                s => Enum.Parse<Civilization>(s.Name),
                s => new Spell(
                    Enum.Parse<SpellKind>(s.Value.GetProperty("spell").GetString()!),
                    s.Value.GetProperty("name").GetString()!,
                    s.Value.GetProperty("cost").GetInt32(),
                    s.Value.TryGetProperty("count", out var count) ? count.GetInt32() : 0,
                    s.Value.TryGetProperty("seconds", out var seconds) ? Ticks(seconds.GetDouble()) : 0,
                    s.Value.TryGetProperty("factor", out var factor) ? factor.GetDouble() : 1)),
            UpgradeThresholds = root.TryGetProperty("upgrades", out var upgrades)
                ? upgrades.GetProperty("manaEarned").EnumerateArray().Select(c => c.GetInt32()).ToArray()
                : [],
        };
    }
}
