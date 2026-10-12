using Homestead.Simulation.Buildings;
using Homestead.Simulation.Economy;
using Homestead.Simulation.Map;
using Xunit.Abstractions;

namespace Homestead.Simulation.Tests;

public class ConstructionTests(ITestOutputHelper output)
{
    private static readonly BuildingCatalog Romans = BuildingCatalog.Load(Civilization.Roman);

    [Fact]
    public void Site_on_flat_ground_is_built_from_delivered_material()
    {
        var world = new World(new GameMap(60));
        var site = world.PlaceConstructionSite(Romans["lumberjack"], 0, new GridPoint(30, 30))!;
        Assert.Equal(BuildingState.Constructing, site.State); // nothing to level
        world.AddLooseGoods(new GridPoint(10, 10), Material.Plank, 5);
        world.AddLooseGoods(new GridPoint(12, 10), Material.Stone, 5);
        Spawn(world, Profession.Carrier, 3, new GridPoint(15, 15));
        Spawn(world, Profession.Builder, 2, new GridPoint(20, 40));

        Run(world, 10_000, stopWhen: () => site.State == BuildingState.Finished);

        Assert.Equal(BuildingState.Finished, site.State);
        Assert.Equal(1f, site.ConstructionProgress);
        // Used exactly what the definition requires, the rest is still lying around.
        Assert.Equal(3, LooseCount(world, Material.Plank));
        Assert.Equal(3, LooseCount(world, Material.Stone));
        Assert.Contains(site.Piles, p => p.Role == PileRole.Offer && p.Material == Material.Trunk);
        Assert.Equal(0, world.Construction.Assigned);
    }

    [Fact]
    public void Diggers_level_uneven_ground_first()
    {
        var map = new GameMap(60);
        var random = new Random(5);
        for (int i = 0; i < map.PointCount; i++)
            map.SetHeight(map.Point(i), 20 + random.Next(0, 4));
        var world = new World(map);
        var site = world.PlaceConstructionSite(Romans["forester"], 0, new GridPoint(30, 30))!;
        Assert.Equal(BuildingState.Flattening, site.State);
        Assert.True(map.IsWalkable(new GridPoint(30, 30)), "Footprint stays walkable while levelling.");
        Spawn(world, Profession.Digger, 3, new GridPoint(20, 20));

        Run(world, 10_000, stopWhen: () => site.State != BuildingState.Flattening);

        Assert.Equal(BuildingState.Constructing, site.State);
        Assert.All(site.BlockedPoints, p =>
        {
            Assert.Equal(site.TargetHeight, map.HeightAt(p));
            Assert.Equal(TerrainType.Flattened, map.TerrainAt(p));
            Assert.False(map.IsWalkable(p));
        });
    }

    [Fact]
    public void Storehouse_supplies_construction_sites()
    {
        var world = new World(new GameMap(80), TestConfig.StoreAll);
        var stock = world.PlaceBuilding(Romans["stock"], 0, new GridPoint(20, 20))!;
        world.AddLooseGoods(new GridPoint(10, 40), Material.Plank, 6);
        world.AddLooseGoods(new GridPoint(12, 40), Material.Stone, 6);
        Spawn(world, Profession.Carrier, 4, new GridPoint(15, 30));

        // With nothing else to do, the carriers store everything.
        Run(world, 10_000, stopWhen: () => LooseCount(world, Material.Plank) + LooseCount(world, Material.Stone) == 0
                                           && world.Logistics.Jobs.Count == 0);
        Assert.Equal(6, stock.Piles.Where(p => p.Material == Material.Plank).Sum(p => p.Count));

        var site = world.PlaceConstructionSite(Romans["sawmill"], 0, new GridPoint(55, 55))!;
        Spawn(world, Profession.Builder, 3, new GridPoint(50, 40));
        Run(world, 20_000, stopWhen: () => site.State == BuildingState.Finished);

        Assert.Equal(BuildingState.Finished, site.State);
        Assert.Equal(6 - 3, stock.Piles.Where(p => p.Material == Material.Plank).Sum(p => p.Count));
        Assert.Equal(6 - 4, stock.Piles.Where(p => p.Material == Material.Stone).Sum(p => p.Count));
    }

    [Fact]
    public void Removing_a_site_releases_workers_and_keeps_the_material()
    {
        var world = new World(new GameMap(60));
        var site = world.PlaceConstructionSite(Romans["sawmill"], 0, new GridPoint(30, 30))!;
        world.AddLooseGoods(new GridPoint(10, 10), Material.Plank, 8);
        Spawn(world, Profession.Carrier, 3, new GridPoint(15, 15));
        Spawn(world, Profession.Builder, 3, new GridPoint(20, 40));

        Run(world, 10_000, stopWhen: () => site.BuildStepsDone >= 1);
        int used = site.BuildStepsDone + (world.Construction.Assigned > 0 ? 1 : 0); // a cycle may be running
        world.RemoveBuilding(site);
        Run(world, 2000);

        Assert.Equal(0, world.Construction.Assigned);
        Assert.Empty(world.Logistics.Jobs);
        Assert.All(world.Settlers, s => Assert.Null(s.Carrying));
        int planksLeft = world.Piles.Where(p => p.Material == Material.Plank).Sum(p => p.Count);
        Assert.InRange(planksLeft, 8 - used, 8 - site.BuildStepsDone);
        Assert.True(new GridPoint(30, 30) is var anchor && world.Map!.IsWalkable(anchor), "Footprint is free again.");
    }

    [Fact]
    public void Many_sites_all_get_finished()
    {
        var random = new Random(9);
        var map = new GameMap(120);
        for (int i = 0; i < map.PointCount; i++)
            map.SetHeight(map.Point(i), 30 + random.Next(0, 3));
        var world = new World(map);
        world.PlaceBuilding(Romans["stock"], 0, new GridPoint(60, 60));
        for (int i = 0; i < 12; i++)
        {
            world.AddLooseGoods(new GridPoint(50 + i, 75), Material.Plank, 8);
            world.AddLooseGoods(new GridPoint(50 + i, 77), Material.Stone, 8);
        }
        var names = new[] { "lumberjack", "forester", "stonecutter", "sawmill", "small_livinghouse", "fisher" };
        var sites = new List<Building>();
        for (int attempt = 0; attempt < 400 && sites.Count < 10; attempt++)
        {
            var at = new GridPoint(random.Next(10, 110), random.Next(10, 110));
            if (world.PlaceConstructionSite(Romans[names[random.Next(names.Length)]], 0, at) is { } site)
                sites.Add(site);
        }
        Spawn(world, Profession.Carrier, 15, new GridPoint(60, 70));
        Spawn(world, Profession.Digger, 4, new GridPoint(55, 70));
        Spawn(world, Profession.Builder, 5, new GridPoint(65, 70));

        int ticks = Run(world, 60_000, stopWhen: () => sites.All(s => s.State == BuildingState.Finished));

        output.WriteLine($"{sites.Count} sites finished after {ticks} ticks ({ticks / World.TicksPerSecond} s); " +
                         $"delivered {world.Logistics.Delivered}, dropped {world.Logistics.DroppedOnTheWay}, " +
                         $"pass-throughs {world.PassThroughs}; problems: " +
                         string.Join(", ", world.Logistics.Problems.Select(kv => $"{kv.Key} {kv.Value}x")));
        Assert.True(sites.Count >= 8);
        Assert.All(sites, s => Assert.Equal(BuildingState.Finished, s.State));
    }

    private static void Spawn(World world, Profession profession, int count, GridPoint near)
    {
        for (int i = 0; i < count; i++)
            Assert.NotNull(world.Spawn(new GridPoint(near.X + i, near.Y), profession));
    }

    private static int LooseCount(World world, Material material) =>
        world.Piles.Where(p => p.Role == PileRole.Loose && p.Material == material).Sum(p => p.Count);

    private static int Run(World world, int ticks, Func<bool>? stopWhen = null)
    {
        for (int t = 1; t <= ticks; t++)
        {
            world.Step();
            if (stopWhen?.Invoke() == true)
                return t;
        }
        return ticks;
    }
}
