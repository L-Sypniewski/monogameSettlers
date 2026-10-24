using System.Text.Json;
using Homestead.Simulation.Buildings;
using Homestead.Simulation.Economy;

namespace Homestead.Simulation.Magic;

/// <summary>The spells of all peoples (values: <c>spells</c> in <c>data/magic.json</c>, from the original).</summary>
public enum SpellKind
{
    /// <summary>Enemy units change sides (only their color changes).</summary>
    Convert,

    /// <summary>Own swordsmen strike faster and harder for a while (Samurai sword).</summary>
    Haste,

    /// <summary>Enemy soldiers stand still for a while.</summary>
    Freeze,

    /// <summary>Enemy units are put on random points of the map.</summary>
    Scatter,

    /// <summary>Piles of random goods appear at the target (heavenly gifts).</summary>
    SummonGoods,

    /// <summary>The resources under the ground in the area shrink (halved, rounded down).</summary>
    ShrinkResources,

    /// <summary>Iron bars lying in the area become gold (Midas touch).</summary>
    IronToGold,

    /// <summary>Enemy soldiers on the caster's land strike weaker for a while.</summary>
    Fear,

    /// <summary>Grain, rice and wine in the area ripen sooner.</summary>
    Growth,

    /// <summary>Desert in the area turns into grass.</summary>
    Grassland,

    /// <summary>The caster sees the area for a while.</summary>
    MagicEye,

    /// <summary>Enemy bowmen become pioneers.</summary>
    BowsToPioneers,

    /// <summary>Own soldiers strike harder away from own land for a while.</summary>
    Strengthen,

    /// <summary>The trees in the area burn down, the fire passes to trees next to them.</summary>
    ForestFire,

    /// <summary>Fish lying in the area becomes meat.</summary>
    FishToMeat,

    /// <summary>Swamp in the area turns into grass.</summary>
    DrainSwamp,

    /// <summary>Grass in the area turns into desert (Horus' heat).</summary>
    Desertify,

    /// <summary>Own units in the area are moved to the priest.</summary>
    Reinforce,

    /// <summary>Own soldiers take less damage for a while.</summary>
    Shield,

    /// <summary>Stones lying in the area become iron.</summary>
    StoneToIron,

    /// <summary>Fish is added to every water point of the area.</summary>
    FishFlood,

    /// <summary>Stones appear on the free points of the area.</summary>
    StoneCurse,

    /// <summary>Snow in the area melts.</summary>
    MeltSnow,

    /// <summary>Own soldiers take no damage from arrows for a while.</summary>
    CursedArrows,

    /// <summary>Goods lying in the area on own land are moved to the priest.</summary>
    CallGoods,

    /// <summary>Gold bars lying in the area become stones.</summary>
    GoldToStone,

    /// <summary>Goods lying in the area are moved to a random point of the map.</summary>
    SendGoods,

    /// <summary>Trees grow on the free points of the area.</summary>
    GrowForest,

    /// <summary>The caster sees the whole map for a while.</summary>
    RevealMap,
}

/// <summary>
/// A spell as a people's priests know it. <paramref name="Radius"/>: the area it works on around the target (0: everywhere).
/// </summary>
/// <param name="Count">Most units (or goods) affected.</param>
/// <param name="Ticks">How long it lasts (effects on units, sight).</param>
/// <param name="Factor">Damage dealt or taken, attack speed (Haste), growth time left (Growth).</param>
public sealed record Spell(SpellKind Kind, int Cost, int Radius, int Count = 0, int Ticks = 0, double Factor = 1)
{
    /// <summary>The name in notes and logs (the client shows <c>spells.&lt;Kind&gt;</c>).</summary>
    public string Name => Kind.ToString();

    /// <summary>Haste: the damage factor that comes with the faster strikes.</summary>
    public double Damage { get; init; } = 1;

    /// <summary>Goods spells: what becomes what.</summary>
    public Material? From { get; init; }

    public Material? To { get; init; }

    /// <summary>Cast at any point of the map, without walking there.</summary>
    public bool Anywhere { get; init; }

    /// <summary>Not implemented yet: shown in the menu with its cost and charge, cannot be cast.</summary>
    public bool Placeholder { get; init; }

    /// <summary>SummonGoods: how many piles (inclusive range); FishFlood: fish added per point.</summary>
    public (int Min, int Max) Piles { get; init; }

    /// <summary>SummonGoods: goods per pile (inclusive range).</summary>
    public (int Min, int Max) Amount { get; init; }

    /// <summary>SummonGoods: groups of materials; each pile takes a random group, then a random material of it.</summary>
    public IReadOnlyList<IReadOnlyList<Material>> Goods { get; init; } = [];

    /// <summary>SummonGoods: further piles of the material, each with its chance.</summary>
    public IReadOnlyList<(Material Material, double Chance)> Extra { get; init; } = [];

    /// <summary>Whether the spell needs a point to be cast at (all but those working on the whole map).</summary>
    public bool Targeted => Radius > 0 || Kind == SpellKind.SummonGoods;
}

/// <summary>
/// A line of the priests' spell menu (<paramref name="Spell"/> null: a placeholder, cannot be cast) with what it
/// costs. <paramref name="Id"/> names its text (<c>spells.&lt;Id&gt;</c>).
/// </summary>
public sealed record MenuSpell(string Id, int Cost, Spell? Spell);

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

    /// <summary>How far from a priest a spell can be cast (spells working <see cref="Spell.Anywhere"/> excepted).</summary>
    public int Range { get; init; } = 24;
    public int CastTicks { get; init; } = 30;

    /// <summary>Percent each cast of a spell makes the player's next one of its kind dearer (rounded up); 0: constant costs.</summary>
    public int CostIncrease { get; init; }

    /// <summary>The spells of each people, in the order of its menu (top to bottom, like the original).</summary>
    public IReadOnlyDictionary<Civilization, IReadOnlyList<Spell>> Spells { get; init; } = new Dictionary<Civilization, IReadOnlyList<Spell>>();

    /// <summary>The people's own spell, the one the AI casts in fights.</summary>
    public IReadOnlyDictionary<Civilization, SpellKind> Own { get; init; } = new Dictionary<Civilization, SpellKind>();

    /// <summary>Mana earned in all for the player's 1st, 2nd, ... soldier upgrade (whichever type); empty: no upgrades.</summary>
    public IReadOnlyList<int> UpgradeThresholds { get; init; } = [];

    public static MagicConfig None { get; } = new();

    /// <summary>The spell of the kind as any people knows it (values that do not differ between peoples).</summary>
    public Spell? Find(SpellKind kind) => Spells.Values.SelectMany(s => s).FirstOrDefault(s => s.Kind == kind);

    public static MagicConfig Load(string? dataDirectory = null)
    {
        string path = Path.Combine(dataDirectory ?? BuildingCatalog.DefaultDataDirectory, "magic.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        static int Ticks(double seconds) => (int)Math.Round(seconds * World.TicksPerSecond);
        static (int, int) Range(JsonElement e) => (e[0].GetInt32(), e[1].GetInt32());
        static Material Good(JsonElement e) => Materials.Parse(e.GetString()!)!.Value;
        static Spell Read(SpellKind kind, JsonElement s, Civilization people) => new(
            kind,
            s.GetProperty("cost").GetInt32(),
            s.TryGetProperty("radius", out var radius) ? radius.GetInt32() : 0,
            s.TryGetProperty("count", out var count) ? count.GetInt32() : 0,
            s.TryGetProperty("seconds", out var seconds) ? Ticks(seconds.GetDouble()) : 0,
            s.TryGetProperty("factor", out var factor) ? factor.GetDouble() : 1)
        {
            Damage = s.TryGetProperty("damage", out var damage) ? damage.GetDouble() : 1,
            From = s.TryGetProperty("from", out var from) ? Good(from) : null,
            To = s.TryGetProperty("to", out var to) ? Good(to) : null,
            Anywhere = s.TryGetProperty("anywhere", out var anywhere) && anywhere.GetBoolean(),
            Placeholder = s.TryGetProperty("placeholder", out var placeholder) && placeholder.GetBoolean(),
            Piles = s.TryGetProperty("piles", out var piles) ? Range(piles) : default,
            Amount = s.TryGetProperty("amount", out var amount) ? Range(amount) : default,
            Goods = s.TryGetProperty("goods", out var goods)
                ? goods.EnumerateArray().Select(g => (IReadOnlyList<Material>)g.EnumerateArray().Select(Good).ToArray()).ToArray()
                : [],
            Extra = s.TryGetProperty("extra", out var extra) && extra.TryGetProperty(people.ToString(), out var own)
                ? own.EnumerateArray().Select(e => (Good(e.GetProperty("material")), e.GetProperty("chance").GetDouble())).ToArray()
                : [],
        };
        var definitions = root.GetProperty("spells").EnumerateObject().Where(s => !s.Name.StartsWith('_'))
            .ToDictionary(s => Enum.Parse<SpellKind>(s.Name), s => s.Value.Clone());
        var spells = root.GetProperty("menu").EnumerateObject().Where(m => !m.Name.StartsWith('_')).ToDictionary(
            m => Enum.Parse<Civilization>(m.Name),
            m => (IReadOnlyList<Spell>)m.Value.EnumerateArray()
                .Select(k => Enum.Parse<SpellKind>(k.GetString()!))
                .Select(k => Read(k, definitions[k], Enum.Parse<Civilization>(m.Name))).ToArray());
        return new MagicConfig
        {
            BigTemple = root.GetProperty("bigTemple").GetString()!,
            PriestTicks = Ticks(root.GetProperty("priestSeconds").GetDouble()),
            Temple = root.GetProperty("temple").GetString()!,
            TempleTicks = Ticks(root.GetProperty("templeSeconds").GetDouble()),
            Mana = root.GetProperty("mana").EnumerateObject().ToDictionary(m => Materials.Parse(m.Name)!.Value, m => m.Value.GetInt32()),
            PriestHealth = root.GetProperty("priestHealth").GetSingle(),
            Range = root.GetProperty("range").GetInt32(),
            CastTicks = Ticks(root.GetProperty("castSeconds").GetDouble()),
            CostIncrease = root.TryGetProperty("costIncrease", out var costIncrease) ? costIncrease.GetInt32() : 0,
            Spells = spells,
            Own = root.GetProperty("own").EnumerateObject().Where(o => !o.Name.StartsWith('_'))
                .ToDictionary(o => Enum.Parse<Civilization>(o.Name), o => Enum.Parse<SpellKind>(o.Value.GetString()!)),
            UpgradeThresholds = root.TryGetProperty("upgrades", out var upgrades)
                ? upgrades.GetProperty("manaEarned").EnumerateArray().Select(c => c.GetInt32()).ToArray()
                : [],
        };
    }
}
