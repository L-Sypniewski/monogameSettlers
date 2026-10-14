using System.Text.Json;
using Homestead.Simulation.Buildings;
using Homestead.Simulation.Economy;

namespace Homestead.Simulation.Military;

public sealed record SoldierStats(float Health, float Damage, int AttackTicks, int Range);

/// <summary>Military balancing, loaded from <c>data/military.json</c>.</summary>
public sealed record MilitaryConfig
{
    /// <summary>Radius of the land an occupied military building claims (by building name).</summary>
    public required IReadOnlyDictionary<string, double> Territory { get; init; }

    /// <summary>Which soldier a weapon makes in the barracks.</summary>
    public required IReadOnlyDictionary<Material, SoldierType> Weapons { get; init; }

    public required int TrainingTicks { get; init; }

    public required IReadOnlyDictionary<SoldierType, SoldierStats> Soldiers { get; init; }

    /// <summary>Health and damage factor per level (index 0 = level 1).</summary>
    public required IReadOnlyList<float> LevelBonus { get; init; }

    /// <summary>Health of a military building's door, which melee attackers must break first.</summary>
    public float DoorHealth { get; init; } = 130;

    /// <summary>A broken or damaged door is whole again after this many ticks without an attack.</summary>
    public int DoorRegenTicks { get; init; } = 30 * World.TicksPerSecond;

    /// <summary>How many melee attackers fit at the door of a building at once (by building name; 2 elsewhere).</summary>
    public IReadOnlyDictionary<string, int> DoorFighters { get; init; } = new Dictionary<string, int>();

    /// <summary>Soldiers react to enemies within this distance.</summary>
    public required int SightRange { get; init; }

    /// <summary>Fog of war: how far finished buildings of these names see (by building name).</summary>
    public IReadOnlyDictionary<string, int> BuildingSight { get; init; } = new Dictionary<string, int>();

    /// <summary>Near the enemy, ways are planned locally within this radius around everyone standing.</summary>
    public required int CombatPathRadius { get; init; }

    /// <summary>An attack takes soldiers from own buildings within this distance of the target.</summary>
    public required int AttackRange { get; init; }

    /// <summary>Combat strength away from own land: how much the goods on own land add.</summary>
    public CombatStrengthConfig CombatStrength { get; init; } = CombatStrengthConfig.None;

    /// <summary>How far a soldier of the type notices enemies: the sight range, or its shooting range if further.</summary>
    public int Sight(SoldierType type) => Math.Max(SightRange, Soldiers[type].Range);

    public float MaxHealth(SoldierType type, int level) => Soldiers[type].Health * Bonus(level);

    public float Damage(SoldierType type, int level) => Soldiers[type].Damage * Bonus(level);

    private float Bonus(int level) => LevelBonus[Math.Clamp(level, 1, LevelBonus.Count) - 1];

    public static MilitaryConfig Load(string? dataDirectory = null)
    {
        string path = Path.Combine(dataDirectory ?? Buildings.BuildingCatalog.DefaultDataDirectory, "military.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        var soldiers = root.GetProperty("soldiers").EnumerateObject().ToDictionary(
            s => Enum.Parse<SoldierType>(s.Name),
            s => new SoldierStats(s.Value.GetProperty("health").GetSingle(), s.Value.GetProperty("damage").GetSingle(),
                (int)Math.Round(s.Value.GetProperty("attackSeconds").GetDouble() * World.TicksPerSecond), s.Value.GetProperty("range").GetInt32()));
        var door = root.TryGetProperty("door", out var d) ? d : default;
        return new MilitaryConfig
        {
            Territory = root.GetProperty("territory").EnumerateObject().ToDictionary(t => t.Name, t => t.Value.GetDouble()),
            Weapons = root.GetProperty("weapons").EnumerateObject()
                .ToDictionary(w => Materials.Parse(w.Name)!.Value, w => Enum.Parse<SoldierType>(w.Value.GetString()!)),
            TrainingTicks = (int)Math.Round(root.GetProperty("trainingSeconds").GetDouble() * World.TicksPerSecond),
            Soldiers = soldiers,
            DoorHealth = door.ValueKind == JsonValueKind.Object
                ? soldiers[Enum.Parse<SoldierType>(door.GetProperty("healthOf").GetString()!)].Health
                : soldiers[SoldierType.Pikeman].Health,
            DoorFighters = door.ValueKind == JsonValueKind.Object && door.TryGetProperty("fighters", out var fighters)
                ? fighters.EnumerateObject().ToDictionary(f => f.Name, f => f.Value.GetInt32())
                : new Dictionary<string, int>(),
            DoorRegenTicks = (int)Math.Round((door.ValueKind == JsonValueKind.Object ? door.GetProperty("regenSeconds").GetDouble() : 30) * World.TicksPerSecond),
            LevelBonus = root.GetProperty("levelBonus").EnumerateArray().Select(v => v.GetSingle()).ToArray(),
            SightRange = root.GetProperty("sightRange").GetInt32(),
            BuildingSight = root.TryGetProperty("buildingSight", out var buildingSight)
                ? buildingSight.EnumerateObject().ToDictionary(b => b.Name, b => b.Value.GetInt32())
                : new Dictionary<string, int>(),
            CombatPathRadius = root.GetProperty("combatPathRadius").GetInt32(),
            AttackRange = root.GetProperty("attackRange").GetInt32(),
            CombatStrength = root.TryGetProperty("combatStrength", out var strength) ? CombatStrengthConfig.Parse(strength) : CombatStrengthConfig.None,
        };
    }

    /// <summary>The weapon a soldier type is trained with.</summary>
    public Material WeaponOf(SoldierType type) => Weapons.First(w => w.Value == type).Key;
}

/// <summary>
/// Combat strength (Kampfkraft): away from own land it starts at 1 / number of players; goods on own
/// land (weighted per people) raise it, at most to 1. Each <see cref="StepPercent"/> points of bonus
/// cost <see cref="UnitsPerPercent"/>[i] units per point, the last value going on.
/// </summary>
public sealed record CombatStrengthConfig(float StepPercent, IReadOnlyList<float> UnitsPerPercent, IReadOnlyDictionary<Material, float> Goods,
    IReadOnlyDictionary<Civilization, IReadOnlyDictionary<Material, float>> Civilizations)
{
    public static CombatStrengthConfig None { get; } = new(10, [], new Dictionary<Material, float>(), new Dictionary<Civilization, IReadOnlyDictionary<Material, float>>());

    /// <summary>What each unit of the goods counts for a player of the people.</summary>
    public IReadOnlyDictionary<Material, float> GoodsOf(Civilization people) => Civilizations.GetValueOrDefault(people) ?? Goods;

    /// <summary>The bonus (1 = 100 percentage points) the units buy, up to <paramref name="max"/>.</summary>
    public float Bonus(float units, float max)
    {
        if (UnitsPerPercent.Count == 0)
            return 0;
        float percent = 0, maxPercent = max * 100;
        for (int step = 0; units > 0 && percent < maxPercent; step++)
        {
            float cost = UnitsPerPercent[Math.Min(step, UnitsPerPercent.Count - 1)];
            float bought = Math.Min(units / cost, StepPercent);
            percent += bought;
            units -= bought * cost;
        }
        return Math.Min(percent, maxPercent) / 100;
    }

    internal static CombatStrengthConfig Parse(JsonElement element)
    {
        static IReadOnlyDictionary<Material, float> Weights(JsonElement goods) =>
            goods.EnumerateObject().ToDictionary(g => Materials.Parse(g.Name)!.Value, g => g.Value.GetSingle());
        return new CombatStrengthConfig(element.GetProperty("stepPercent").GetSingle(),
            element.GetProperty("unitsPerPercent").EnumerateArray().Select(v => v.GetSingle()).ToArray(),
            Weights(element.GetProperty("goods")),
            element.TryGetProperty("civilizations", out var civilizations)
                ? civilizations.EnumerateObject().ToDictionary(c => Enum.Parse<Civilization>(c.Name), c => Weights(c.Value))
                : new Dictionary<Civilization, IReadOnlyDictionary<Material, float>>());
    }
}
