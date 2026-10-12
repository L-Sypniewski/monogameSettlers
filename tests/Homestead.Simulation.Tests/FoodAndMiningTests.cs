using Homestead.Simulation.Buildings;
using Homestead.Simulation.Economy;
using Homestead.Simulation.Map;
using Homestead.Simulation.Pathfinding;
using Xunit.Abstractions;

namespace Homestead.Simulation.Tests;

public class FoodAndMiningTests(ITestOutputHelper output)
{
    private static readonly BuildingCatalog Romans = BuildingCatalog.Load(Civilization.Roman);

    [Fact]
    public void Mine_turns_one_food_into_one_ore_until_the_ground_is_empty()
    {
        var map = new GameMap(70);
        map.SetResource(new GridPoint(31, 29), ResourceType.Coal, 2);
        map.SetResource(new GridPoint(29, 31), ResourceType.IronOre, 5); // wrong kind for a coal mine
        var world = new World(map, TestConfig.NoTools with { OrePerAmount = 2 });
        var mine = world.PlaceBuilding(Romans["coalmine"], 0, new GridPoint(30, 30))!;
        var stock = world.PlaceBuilding(Romans["stock"], 0, new GridPoint(50, 50))!;
        world.AddLooseGoods(new GridPoint(10, 50), Material.Bread, 8);
        Spawn(world, Profession.Carrier, 4, new GridPoint(20, 20));
        Assert.Equal(4, world.Production.OreLeft(mine));

        Run(world, 20_000, stopWhen: () => Count(stock, Material.Coal) == 4 && world.Production.IsExhausted(mine)
                                           && world.Logistics.Jobs.Count == 0);

        Assert.Equal(4, Count(stock, Material.Coal));
        Assert.Equal(4, world.Production.OreMined);
        Assert.Equal(0, world.Production.OreLeft(mine));
        Assert.True(world.Production.IsExhausted(mine));
        Assert.Equal(0, map.ResourceAmount(new GridPoint(31, 29), ResourceType.Coal));
        Assert.Equal(5, map.ResourceAmount(new GridPoint(29, 31), ResourceType.IronOre));
        Assert.Equal(4, Total(world, Material.Bread)); // the rest waits, an exhausted mine eats nothing

        Run(world, 2_000);
        Assert.Equal(4, Total(world, Material.Bread));
    }

    [Fact]
    public void Depleted_ground_regenerates_when_configured()
    {
        var map = new GameMap(70);
        map.SetResource(new GridPoint(31, 29), ResourceType.Coal, 1);
        var config = TestConfig.NoTools with { OrePerAmount = 2, RegenerateTicks = 300 };
        var world = new World(map, config);
        var mine = world.PlaceBuilding(Romans["coalmine"], 0, new GridPoint(30, 30))!;
        var stock = world.PlaceBuilding(Romans["stock"], 0, new GridPoint(50, 50))!;
        world.AddLooseGoods(new GridPoint(10, 50), Material.Bread, 8);
        Spawn(world, Profession.Carrier, 4, new GridPoint(20, 20));

        // One amount step holds 2 ore; regeneration brings the step back again and again.
        Run(world, 30_000, stopWhen: () => Count(stock, Material.Coal) >= 6);

        Assert.True(Count(stock, Material.Coal) >= 6, $"coal {Count(stock, Material.Coal)}");
        Assert.True(map.ResourceAmount(new GridPoint(31, 29), ResourceType.Coal) <= 1, "never above the start amount");
    }

    [Fact]
    public void Fisher_catches_fish_from_the_lake_and_empties_it()
    {
        var map = LakeMap(70, fishAmount: 1);
        var world = new World(map, TestConfig.NoTools with { FishPerAmount = 1 });
        world.PlaceBuilding(Romans["fisher"], 0, new GridPoint(40, 45));
        var stock = world.PlaceBuilding(Romans["stock"], 0, new GridPoint(50, 25))!;
        Spawn(world, Profession.Carrier, 3, new GridPoint(45, 40));
        int fishBefore = FishIn(map);

        Run(world, 20_000, stopWhen: () => Count(stock, Material.Fish) >= 4);

        Assert.True(Count(stock, Material.Fish) >= 4);
        Assert.Equal(fishBefore - world.Production.FishCaught, FishIn(map));
    }

    [Fact]
    public void Waterworks_draws_water()
    {
        var map = LakeMap(70, fishAmount: 0);
        var world = new World(map, TestConfig.NoTools);
        world.PlaceBuilding(Romans["waterworks"], 0, new GridPoint(40, 45));
        var stock = world.PlaceBuilding(Romans["stock"], 0, new GridPoint(50, 25))!;
        Spawn(world, Profession.Carrier, 3, new GridPoint(45, 40));

        Run(world, 10_000, stopWhen: () => Count(stock, Material.Water) >= 4);

        Assert.True(Count(stock, Material.Water) >= 4);
    }

    [Fact]
    public void Farmer_sows_grain_that_ripens_and_harvests_it()
    {
        var map = new GameMap(70);
        var world = new World(map, TestConfig.NoTools with { GrainGrowTicks = 700 });
        var farm = world.PlaceBuilding(Romans["farm"], 0, new GridPoint(30, 20))!;
        var stock = world.PlaceBuilding(Romans["stock"], 0, new GridPoint(55, 25))!;
        Spawn(world, Profession.Carrier, 3, new GridPoint(45, 40));

        Run(world, 20_000, stopWhen: () => Count(stock, Material.Crop) >= 4);

        Assert.True(Count(stock, Material.Crop) >= 4, $"sown {world.Production.FieldsSown}, harvested {world.Production.CropHarvested}");
        Assert.True(world.Production.FieldsSown >= world.Production.CropHarvested);
        // Fields lie around the work center in front of the farm.
        var center = farm.Position + farm.Definition.WorkCenter!.Value;
        for (int i = 0; i < map.PointCount; i++)
            if (map.ObjectAt(map.Point(i)) == MapObjectKind.Field)
                Assert.True(PathFinder.InCircle(center, map.Point(i), farm.Definition.WorkRadius!.Value));
    }

    [Fact]
    public void Food_chain_feeds_a_coal_mine()
    {
        var map = LakeMap(110, fishAmount: 0);
        for (int dy = -3; dy <= 3; dy++)
        for (int dx = -3; dx <= 3; dx++)
            map.SetResource(new GridPoint(80 + dx, 80 + dy), ResourceType.Coal, 8);
        var world = new World(map, TestConfig.NoTools);
        var anchors = new[] { ("stock", 60, 55), ("farm", 60, 15), ("waterworks", 40, 45), ("mill", 50, 80), ("baker", 65, 95), ("coalmine", 80, 80) };
        foreach (var (name, x, y) in anchors)
            Assert.True(world.PlaceBuilding(Romans[name], 0, new GridPoint(x, y)) is not null, $"{name} does not fit");
        var stock = world.Buildings[0];
        Spawn(world, Profession.Carrier, 12, new GridPoint(55, 65));

        int ticks = Run(world, 60_000, stopWhen: () => Count(stock, Material.Coal) >= 3);

        output.WriteLine($"3 coal after {ticks / World.TicksPerSecond} s: sown {world.Production.FieldsSown}, harvested " +
                         $"{world.Production.CropHarvested}, water {world.Production.WaterDrawn}, workshop products {world.Production.Produced}; " +
                         $"logistics problems: {string.Join(", ", world.Logistics.Problems.Select(kv => $"{kv.Key} {kv.Value}x"))}; " +
                         $"production problems: {string.Join(", ", world.Production.Problems.Select(kv => $"{kv.Key} {kv.Value}x"))}");
        Assert.True(Count(stock, Material.Coal) >= 3);
    }

    /// <summary>Grass map with a lake (x 5-24, y 30-69) holding fish of the given amount.</summary>
    private static GameMap LakeMap(int size, int fishAmount)
    {
        var map = new GameMap(size);
        for (int y = 30; y < 70; y++)
        for (int x = 5; x < 25; x++)
        {
            var p = new GridPoint(x + y / 2 - 15, y); // keep the lake upright on screen
            map.SetTerrain(p, TerrainType.Water);
            map.SetResource(p, ResourceType.Fish, fishAmount);
        }
        return map;
    }

    private static int FishIn(GameMap map) =>
        Enumerable.Range(0, map.PointCount).Sum(i => map.ResourceAmount(map.Point(i), ResourceType.Fish));

    private static int Total(World world, Material material) =>
        world.Piles.Where(p => p.Material == material).Sum(p => p.Count) + world.Settlers.Count(s => s.Carrying == material);

    private static void Spawn(World world, Profession profession, int count, GridPoint near)
    {
        for (int i = 0; i < count; i++)
            Assert.NotNull(world.Spawn(new GridPoint(near.X + i, near.Y), profession));
    }

    private static int Count(Building building, Material material) =>
        building.Piles.Where(p => p.Material == material).Sum(p => p.Count);

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
