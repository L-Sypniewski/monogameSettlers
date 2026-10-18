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

    /// <summary>Piles of random goods appear at the target (no fight needed).</summary>
    SummonGoods,
}

/// <param name="Count">Convert, Scatter: most soldiers affected.</param>
/// <param name="Ticks">Haste, Freeze: how long it lasts.</param>
/// <param name="Factor">Haste: how many times as fast the soldiers strike.</param>
public sealed record Spell(SpellKind Kind, string Name, int Cost, int Count = 0, int Ticks = 0, double Factor = 1)
{
    /// <summary>SummonGoods: how many piles (inclusive range).</summary>
    public (int Min, int Max) Piles { get; init; }

    /// <summary>SummonGoods: goods per pile (inclusive range).</summary>
    public (int Min, int Max) Amount { get; init; }

    /// <summary>SummonGoods: groups of materials; each pile takes a random group, then a random material of it.</summary>
    public IReadOnlyList<IReadOnlyList<Material>> Goods { get; init; } = [];

    /// <summary>SummonGoods: further piles of the material, each with its chance.</summary>
    public IReadOnlyList<(Material Material, double Chance)> Extra { get; init; } = [];
}

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

    /// <summary>Spells are cast from up to this many times the bowmen's range.</summary>
    public double RangeFactor { get; init; } = 1;

    /// <summary>The spells of each people: its own first, then the common ones.</summary>
    public IReadOnlyDictionary<Civilization, IReadOnlyList<Spell>> Spells { get; init; } = new Dictionary<Civilization, IReadOnlyList<Spell>>();

    /// <summary>Mana earned in all for the player's 1st, 2nd, ... soldier upgrade (whichever type); empty: no upgrades.</summary>
    public IReadOnlyList<int> UpgradeThresholds { get; init; } = [];

    public static MagicConfig None { get; } = new();

    public static MagicConfig Load(string? dataDirectory = null)
    {
        string path = Path.Combine(dataDirectory ?? BuildingCatalog.DefaultDataDirectory, "magic.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        static int Ticks(double seconds) => (int)Math.Round(seconds * World.TicksPerSecond);
        static (int, int) Range(JsonElement e) => (e[0].GetInt32(), e[1].GetInt32());
        static Spell Read(JsonElement s, Civilization people) => new(
            Enum.Parse<SpellKind>(s.GetProperty("spell").GetString()!),
            // The name shown comes from the client's texts (spells.<Kind>); "name" is optional.
            s.TryGetProperty("name", out var name) ? name.GetString()! : s.GetProperty("spell").GetString()!,
            s.GetProperty("cost").GetInt32(),
            s.TryGetProperty("count", out var count) ? count.GetInt32() : 0,
            s.TryGetProperty("seconds", out var seconds) ? Ticks(seconds.GetDouble()) : 0,
            s.TryGetProperty("factor", out var factor) ? factor.GetDouble() : 1)
        {
            Piles = s.TryGetProperty("piles", out var piles) ? Range(piles) : default,
            Amount = s.TryGetProperty("amount", out var amount) ? Range(amount) : default,
            Goods = s.TryGetProperty("goods", out var goods)
                ? goods.EnumerateArray().Select(g => (IReadOnlyList<Material>)g.EnumerateArray().Select(m => Materials.Parse(m.GetString()!)!.Value).ToArray()).ToArray()
                : [],
            Extra = s.TryGetProperty("extra", out var extra) && extra.TryGetProperty(people.ToString(), out var own)
                ? own.EnumerateArray().Select(e => (Materials.Parse(e.GetProperty("material").GetString()!)!.Value, e.GetProperty("chance").GetDouble())).ToArray()
                : [],
        };
        var common = root.TryGetProperty("commonSpells", out var commonSpells) ? commonSpells.EnumerateArray().ToList() : [];
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
            RangeFactor = root.TryGetProperty("rangeFactor", out var rangeFactor) ? rangeFactor.GetDouble() : 1,
            Spells = root.GetProperty("spells").EnumerateObject().Where(s => !s.Name.StartsWith('_')).ToDictionary(
                s => Enum.Parse<Civilization>(s.Name),
                s => (IReadOnlyList<Spell>)common.Select(c => Read(c, Enum.Parse<Civilization>(s.Name)))
                    .Prepend(Read(s.Value, Enum.Parse<Civilization>(s.Name))).ToArray()),
            UpgradeThresholds = root.TryGetProperty("upgrades", out var upgrades)
                ? upgrades.GetProperty("manaEarned").EnumerateArray().Select(c => c.GetInt32()).ToArray()
                : [],
        };
    }
}
