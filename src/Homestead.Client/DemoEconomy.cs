using Homestead.Simulation;
using Homestead.Simulation.Buildings;
using Homestead.Simulation.Economy;
using Homestead.Simulation.Pathfinding;

namespace Homestead.Client;

/// <summary>
/// Development helper: sets up a small economy (storehouse, a few workshops, goods lying around,
/// carriers) to watch the logistics at work. Not the game's start equipment.
/// </summary>
public static class DemoEconomy
{
    private static readonly string[] BuildingNames = ["stock", "sawmill", "mill", "baker"];
    private static readonly Material[] Goods = [Material.Trunk, Material.Crop, Material.Flour, Material.Water, Material.Stone, Material.Plank];

    public static string Create(World world, BuildingCatalog catalog, GridPoint near, Random random, int carriers = 12,
        int diggers = 3, int builders = 4)
    {
        int placed = 0;
        foreach (string name in BuildingNames)
            if (PlaceNear(world, catalog[name], near, random) is not null)
                placed++;

        int piles = 0;
        for (int attempt = 0; attempt < 200 && piles < 10; attempt++)
        {
            var at = new GridPoint(near.X + random.Next(-25, 26), near.Y + random.Next(-25, 26));
            if (world.Map!.Contains(at) && world.AddLooseGoods(at, Goods[random.Next(Goods.Length)], random.Next(3, 9)) is not null)
                piles++;
        }

        // Construction material, so placed sites can be built.
        foreach (var material in new[] { Material.Plank, Material.Stone })
            for (int i = 0, added = 0; i < 100 && added < 4; i++)
                if (world.AddLooseGoods(new GridPoint(near.X + random.Next(-12, 13), near.Y + random.Next(-12, 13)), material, 8) is not null)
                    added++;

        int spawned = 0;
        foreach (var (profession, count) in new[] { (Profession.Carrier, carriers), (Profession.Digger, diggers), (Profession.Builder, builders) })
            for (int i = 0; i < count; i++)
                if (world.Spawn(new GridPoint(near.X + random.Next(-6, 7), near.Y + random.Next(-6, 7)), profession) is not null)
                    spawned++;

        return $"demo economy: {placed} buildings, {piles} piles, {spawned} workers";
    }

    /// <summary>Tries positions in growing rings around <paramref name="near"/> until the building fits.</summary>
    private static Building? PlaceNear(World world, BuildingDefinition definition, GridPoint near, Random random)
    {
        for (int r = 6; r < 40; r += 2)
        for (int attempt = 0; attempt < 12; attempt++)
        {
            var at = new GridPoint(near.X + random.Next(-r, r + 1), near.Y + random.Next(-r, r + 1));
            if (PathFinder.Distance(at, near) < r - 2)
                continue;
            // Keep the protected ring free as well, so piles and doors stay reachable.
            if (definition.Protected.Any(o => !world.Map!.IsWalkable(at + o)))
                continue;
            if (world.PlaceBuilding(definition, 0, at) is { } building)
                return building;
        }
        return null;
    }
}
