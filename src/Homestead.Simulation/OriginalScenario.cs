using Homestead.Assets.Maps;
using Homestead.Simulation.Buildings;
using Homestead.Simulation.Economy;
using Homestead.Simulation.Map;

namespace Homestead.Simulation;

/// <summary>Builds a world from an original map: terrain, buildings, piles of goods and settlers.</summary>
public static class OriginalScenario
{
    /// <summary>Building type byte of original maps (chunk 8). Order from JSettlers' EOriginalMapBuildingType (MIT).</summary>
    private static readonly string?[] BuildingNames =
    [
        null, "stock", "lumberjack", "stonecutter", "sawmill", "forester", "lookout_tower", "coalmine", "goldmine",
        "ironmine", "goldmelt", "toolsmith", "weaponsmith", "winegrower", "tower", "big_tower", "castle", "barrack",
        "baker", "pig_farm", "farm", "fisher", "small_livinghouse", "medium_livinghouse", "big_livinghouse",
        "sulfurmine", "waterworks", "dockyard", "harbor", "hospital", "gemsmine", "charcoal_burner", "big_temple",
        "temple", "donkey_farm", "beekeeping", "sulfurmine",
    ];

    /// <summary>Settler type 0 in chunk 7 is the carrier; other professions are placed as plain settlers for now.</summary>
    private const int CarrierType = 0;

    public sealed record LoadReport(int Buildings, int Piles, int Settlers, IReadOnlyList<string> Skipped);

    public static (World World, LoadReport Report) Load(OriginalMap original, string? dataDirectory = null)
    {
        var map = GameMap.FromOriginal(original.Area);
        var world = new World(map);
        var catalogs = new Dictionary<Civilization, BuildingCatalog>();
        var skipped = new List<string>();

        BuildingCatalog CatalogOf(int player)
        {
            var civilization = player < original.Players.Count && original.Players[player].Tribe is >= 0 and <= 3
                ? (Civilization)original.Players[player].Tribe
                : Civilization.Roman;
            if (!catalogs.TryGetValue(civilization, out var catalog))
                catalogs[civilization] = catalog = BuildingCatalog.Load(civilization, dataDirectory);
            return catalog;
        }

        int buildings = 0;
        foreach (var b in original.Buildings)
        {
            string? name = b.Type < BuildingNames.Length ? BuildingNames[b.Type] : null;
            var definition = name is null ? null : CatalogOf(b.Player).Find(name);
            if (definition is null)
            {
                skipped.Add($"building type {b.Type} ({name ?? "unknown"}) at {b.X},{b.Y}");
                continue;
            }
            if (world.PlaceBuilding(definition, b.Player, new GridPoint(b.X, b.Y), force: true) is not null)
                buildings++;
        }

        int piles = 0;
        foreach (var s in original.Stacks)
        {
            if (Materials.FromOriginalStack(s.Type) is not { } material)
            {
                skipped.Add($"pile type {s.Type} at {s.X},{s.Y}");
                continue;
            }
            if (world.AddLooseGoods(new GridPoint(s.X, s.Y), material, s.Count) is not null)
                piles++;
        }

        int settlers = 0;
        foreach (var s in original.Settlers)
        {
            if (world.Spawn(new GridPoint(s.X, s.Y), carrier: s.Type == CarrierType) is not null)
                settlers++;
        }

        return (world, new LoadReport(buildings, piles, settlers, skipped));
    }
}
