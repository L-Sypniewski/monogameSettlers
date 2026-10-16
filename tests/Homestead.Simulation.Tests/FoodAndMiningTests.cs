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
    public void Mine_digs_until_the_ground_is_empty_and_one_bread_lasts_a_coal_miner_eight_trips()
    {
        var map = new GameMap(70);
        map.SetResource(new GridPoint(31, 29), ResourceType.Coal, 2);
        map.SetResource(new GridPoint(29, 31), ResourceType.IronOre, 5); // wrong kind for a coal mine
        var world = new World(map, TestConfig.NoTools with { OrePerAmount = 2, FullOreAmount = 0 });
        TestMaps.Mountain(world.Map!, new GridPoint(30, 30));
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
        Assert.Equal(7, Total(world, Material.Bread)); // one bread for four trips; an exhausted mine eats nothing

        Run(world, 2_000);
        Assert.Equal(7, Total(world, Material.Bread));
    }

    [Fact]
    public void Other_food_lasts_a_miner_only_two_trips()
    {
        var map = new GameMap(70);
        map.SetResource(new GridPoint(31, 29), ResourceType.Coal, 2);
        var world = new World(map, TestConfig.NoTools with { OrePerAmount = 2, FullOreAmount = 0 });
        TestMaps.Mountain(world.Map!, new GridPoint(30, 30));
        var mine = world.PlaceBuilding(Romans["coalmine"], 0, new GridPoint(30, 30))!;
        Assert.True(mine.Piles.Any(p => p.Material == Material.Fish), "the Roman coal mine takes fish");
        world.PlaceBuilding(Romans["stock"], 0, new GridPoint(50, 50));
        world.AddLooseGoods(new GridPoint(10, 50), Material.Fish, 8);
        Spawn(world, Profession.Carrier, 4, new GridPoint(20, 20));

        Run(world, 20_000, stopWhen: () => world.Production.IsExhausted(mine) && world.Logistics.Jobs.Count == 0);

        Assert.Equal(4, world.Production.OreMined);
        Assert.Equal(8 - 2, Total(world, Material.Fish));
    }

    [Fact]
    public void A_mines_chance_is_the_ore_left_against_full_fields_and_every_find_lowers_it()
    {
        var map = new GameMap(70);
        var center = new GridPoint(30, 30);
        var area = PathFinder.Circle(center, 3).ToList();
        // Half the fields full of coal, one with iron ore (a miss for a coal mine), the rest empty.
        foreach (var p in area.Take(area.Count / 2))
            map.SetResource(p, ResourceType.Coal, 8);
        map.SetResource(area[^1], ResourceType.IronOre, 8);
        var world = new World(map, TestConfig.NoTools with { OrePerAmount = 4, FullOreAmount = 8 });
        TestMaps.Mountain(world.Map!, center);
        var mine = world.PlaceBuilding(Romans["coalmine"], 0, center)!;
        var stock = world.PlaceBuilding(Romans["stock"], 0, new GridPoint(50, 50))!;
        int full = area.Count * 8 * 4;
        Assert.Equal((double)(area.Count / 2) * 8 * 4 / full, world.Production.MineChance(mine)!.Value, 6);

        world.AddLooseGoods(new GridPoint(10, 50), Material.Bread, 8);
        Spawn(world, Profession.Carrier, 4, new GridPoint(20, 20));
        Run(world, 30_000, stopWhen: () => world.Production.OreMined >= 10 && world.Production.MineMisses >= 5);

        var production = world.Production;
        output.WriteLine($"hits {production.OreMined}, misses {production.MineMisses}");
        Assert.True(production.MineMisses >= 5, "about half the runs find nothing");
        Assert.Equal((double)((area.Count / 2) * 8 * 4 - production.OreMined) / full, production.MineChance(mine)!.Value, 6);
        // Misses eat too: one bread lasts eight runs, hit or miss.
        int runs = production.OreMined + production.MineMisses;
        Assert.True(Total(world, Material.Bread) <= 8 - runs / 8, $"bread {Total(world, Material.Bread)} after {runs} runs");
    }

    [Fact]
    public void A_mine_without_its_resource_has_no_chance_and_counts_as_exhausted()
    {
        var map = new GameMap(70);
        map.SetResource(new GridPoint(31, 29), ResourceType.IronOre, 8);
        var world = new World(map, TestConfig.NoTools with { FullOreAmount = 8 });
        TestMaps.Mountain(world.Map!, new GridPoint(30, 30));
        var mine = world.PlaceBuilding(Romans["coalmine"], 0, new GridPoint(30, 30))!;

        Assert.Equal(0, world.Production.MineChance(mine));
        Assert.Null(world.Production.MineChance(world.PlaceBuilding(Romans["stock"], 0, new GridPoint(50, 50))!));
    }

    [Fact]
    public void Depleted_ground_regenerates_when_configured()
    {
        var map = new GameMap(70);
        map.SetResource(new GridPoint(31, 29), ResourceType.Coal, 1);
        var config = TestConfig.NoTools with { OrePerAmount = 2, RegenerateTicks = 300, FullOreAmount = 0 };
        var world = new World(map, config);
        TestMaps.Mountain(world.Map!, new GridPoint(30, 30));
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
    public void A_mine_takes_at_most_four_units_of_each_food()
    {
        var map = new GameMap(70);
        map.SetResource(new GridPoint(31, 29), ResourceType.Coal, 8);
        var world = new World(map, TestConfig.NoTools with { MineFoodPileCapacity = 4 });
        TestMaps.Mountain(world.Map!, new GridPoint(30, 30));
        var mine = world.PlaceBuilding(Romans["coalmine"], 0, new GridPoint(30, 30))!;
        world.AddLooseGoods(new GridPoint(10, 50), Material.Bread, 8);
        Spawn(world, Profession.Carrier, 4, new GridPoint(20, 20));

        Run(world, 3_000);

        var bread = mine.Piles.Single(p => p.Material == Material.Bread);
        Assert.Equal(4, bread.Capacity);
        Assert.True(bread.Count <= 4);
        Assert.True(Total(world, Material.Bread) - bread.Count >= 3, "the rest stays outside the mine (the miner eats one)");
    }

    [Fact]
    public void Waterworks_draws_water_from_the_river_not_the_lake()
    {
        var map = LakeMap(70, fishAmount: 0);
        var world = new World(map, TestConfig.NoTools);
        world.PlaceBuilding(Romans["waterworks"], 0, new GridPoint(40, 45));
        world.PlaceBuilding(Romans["stock"], 0, new GridPoint(50, 25));
        Spawn(world, Profession.Carrier, 3, new GridPoint(45, 40));

        Run(world, 3_000);

        Assert.Equal(0, world.Production.WaterDrawn);
    }

    [Fact]
    public void Waterworks_draws_water()
    {
        var map = LakeMap(70, fishAmount: 0, river: true);
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
        var world = new World(map, TestConfig.NoTools.WithCropGrowTicks("grain", 700));
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
        var map = LakeMap(110, fishAmount: 0, river: true);
        for (int dy = -3; dy <= 3; dy++)
        for (int dx = -3; dx <= 3; dx++)
            map.SetResource(new GridPoint(80 + dx, 80 + dy), ResourceType.Coal, 8);
        TestMaps.Mountain(map, new GridPoint(80, 80));
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

    /// <summary>
    /// Grass map with a lake (x 5-24, y 30-69) holding fish of the given amount; with
    /// <paramref name="river"/> a river runs along its east shore (x 25-26).
    /// </summary>
    private static GameMap LakeMap(int size, int fishAmount, bool river = false)
    {
        var map = new GameMap(size);
        for (int y = 30; y < 70; y++)
        for (int x = 5; x < (river ? 27 : 25); x++)
        {
            var p = new GridPoint(x + y / 2 - 15, y); // keep the lake upright on screen
            if (x >= 25)
            {
                map.SetTerrain(p, TerrainType.River);
                continue;
            }
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
