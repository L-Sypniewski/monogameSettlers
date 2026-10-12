using System.Text.Json;
using Homestead.Simulation.Economy;

namespace Homestead.Simulation.Military;

/// <summary>Military balancing, loaded from <c>data/military.json</c>.</summary>
public sealed record MilitaryConfig
{
    /// <summary>Radius of the land an occupied military building claims (by building name).</summary>
    public required IReadOnlyDictionary<string, double> Territory { get; init; }

    /// <summary>Which soldier a weapon makes in the barracks.</summary>
    public required IReadOnlyDictionary<Material, SoldierType> Weapons { get; init; }

    public required int TrainingTicks { get; init; }

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
        };
    }

    /// <summary>The weapon a soldier type is trained with.</summary>
    public Material WeaponOf(SoldierType type) => Weapons.First(w => w.Value == type).Key;
}
