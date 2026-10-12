using System.Text.Json;
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

    /// <summary>Soldiers react to enemies within this distance.</summary>
    public required int SightRange { get; init; }

    /// <summary>An attack takes soldiers from own buildings within this distance of the target.</summary>
    public required int AttackRange { get; init; }

    public float MaxHealth(SoldierType type, int level) => Soldiers[type].Health * Bonus(level);

    public float Damage(SoldierType type, int level) => Soldiers[type].Damage * Bonus(level);

    private float Bonus(int level) => LevelBonus[Math.Clamp(level, 1, LevelBonus.Count) - 1];

    public static MilitaryConfig Load(string? dataDirectory = null)
    {
        string path = Path.Combine(dataDirectory ?? Buildings.BuildingCatalog.DefaultDataDirectory, "military.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        return new MilitaryConfig
        {
            Territory = root.GetProperty("territory").EnumerateObject().ToDictionary(t => t.Name, t => t.Value.GetDouble()),
            Weapons = root.GetProperty("weapons").EnumerateObject()
                .ToDictionary(w => Materials.Parse(w.Name)!.Value, w => Enum.Parse<SoldierType>(w.Value.GetString()!)),
            TrainingTicks = (int)Math.Round(root.GetProperty("trainingSeconds").GetDouble() * World.TicksPerSecond),
            Soldiers = root.GetProperty("soldiers").EnumerateObject().ToDictionary(
                s => Enum.Parse<SoldierType>(s.Name),
                s => new SoldierStats(s.Value.GetProperty("health").GetSingle(), s.Value.GetProperty("damage").GetSingle(),
                    (int)Math.Round(s.Value.GetProperty("attackSeconds").GetDouble() * World.TicksPerSecond), s.Value.GetProperty("range").GetInt32())),
            LevelBonus = root.GetProperty("levelBonus").EnumerateArray().Select(v => v.GetSingle()).ToArray(),
            SightRange = root.GetProperty("sightRange").GetInt32(),
            AttackRange = root.GetProperty("attackRange").GetInt32(),
        };
    }

    /// <summary>The weapon a soldier type is trained with.</summary>
    public Material WeaponOf(SoldierType type) => Weapons.First(w => w.Value == type).Key;
}
