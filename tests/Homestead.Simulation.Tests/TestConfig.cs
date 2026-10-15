using Homestead.Simulation.Economy;
using Homestead.Simulation.Map;
using Homestead.Simulation.Pathfinding;

namespace Homestead.Simulation.Tests;

internal static class TestConfig
{
    /// <summary>
    /// The real balancing, but storehouses take every material (tests use a storehouse to collect and
    /// count what was produced) and no carriers are kept back (tests work with few carriers).
    /// </summary>
    public static ProductionConfig StoreAll { get; } = ProductionConfig.Load() with { StoredMaterials = null, KeptCarriers = 0 };

    /// <summary>Like <see cref="StoreAll"/>, and every trade works without a tool (tests that are not about tools).</summary>
    public static ProductionConfig NoTools { get; } = StoreAll with { Tools = new Dictionary<string, Material>() };
}

internal static class TestMaps
{
    /// <summary>Turns the ground around <paramref name="center"/> into mountain, where mines may stand.</summary>
    public static void Mountain(GameMap map, GridPoint center, int radius = 6)
    {
        foreach (var p in PathFinder.Circle(center, radius))
            if (map.Contains(p))
                map.SetTerrain(p, TerrainType.Mountain);
    }
}
