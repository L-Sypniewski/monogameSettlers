using Homestead.Simulation.Buildings;
using Homestead.Simulation.Economy;
using Homestead.Simulation.Map;

namespace Homestead.Simulation.Tests;

/// <summary>The map's own rules: depletion percentages, take boundaries, regeneration pacing, growth staging.</summary>
public class MapResourcesTests
{
    private static readonly BuildingCatalog Romans = BuildingCatalog.Load(Civilization.Roman);

    [Fact]
    public void At_half_depletion_only_every_second_cut_shrinks_the_rock()
    {
        var map = new GameMap(70);
        var rock = new GridPoint(36, 38);
        map.SetObjectCode(rock, MapObjectCodes.FullStone);
        var config = TestConfig.NoTools with { StonesPerCut = 1, Depletion = new Depletion(50, 100, 100) };
        var world = new World(map, config);
        world.PlaceBuilding(Romans["stonecutter"], 0, new GridPoint(30, 30));
        var stock = world.PlaceBuilding(Romans["stock"], 0, new GridPoint(50, 25))!;
        Spawn(world, 4, new GridPoint(40, 45));

        Run(world, 60_000, stopWhen: () => map.ObjectCodeAt(rock) == MapObjectCodes.StoneRubble && world.Logistics.Jobs.Count == 0);

        // Twelve sizes to rubble, two cuts per size at 50 % depletion: every cut still yields a stone.
        Assert.Equal(MapObjectCodes.StoneRubble, map.ObjectCodeAt(rock));
        Assert.Equal(24, world.Production.StonesCut);
        Assert.Equal(24, Count(stock, Material.Stone));
    }

    [Fact]
    public void A_field_loses_an_amount_step_only_on_every_perAmountth_take()
    {
        var map = new GameMap(70);
        var field = new GridPoint(31, 29);
        map.SetResource(field, ResourceType.Coal, 3);
        var world = new World(map, TestConfig.NoTools with { OrePerAmount = 2, FullOreAmount = 0, RegenerateTicks = 0, MineFreeRuns = 0 });
        TestMaps.Mountain(world.Map!, new GridPoint(30, 30));
        world.PlaceBuilding(Romans["coalmine"], 0, new GridPoint(30, 30));
        world.PlaceBuilding(Romans["stock"], 0, new GridPoint(50, 50));
        world.AddLooseGoods(new GridPoint(10, 50), Material.Bread, 8);
        Spawn(world, 4, new GridPoint(20, 20));

        Run(world, 20_000, stopWhen: () => world.Production.OreMined == 1);
        Assert.Equal(3, map.ResourceAmount(field, ResourceType.Coal)); // the first take stays within the step

        Run(world, 20_000, stopWhen: () => world.Production.OreMined == 2);
        Assert.Equal(2, map.ResourceAmount(field, ResourceType.Coal)); // only the second one lowers it
    }

    [Fact]
    public void A_depleted_field_regains_a_step_after_the_configured_ticks()
    {
        var map = new GameMap(70);
        var field = new GridPoint(31, 29);
        map.SetResource(field, ResourceType.Coal, 1);
        var world = new World(map, TestConfig.NoTools with { OrePerAmount = 1, FullOreAmount = 0, RegenerateTicks = 300, MineFreeRuns = 0 });
        TestMaps.Mountain(world.Map!, new GridPoint(30, 30));
        world.PlaceBuilding(Romans["coalmine"], 0, new GridPoint(30, 30));
        world.PlaceBuilding(Romans["stock"], 0, new GridPoint(50, 50));
        world.AddLooseGoods(new GridPoint(10, 50), Material.Bread, 8);
        Spawn(world, 4, new GridPoint(20, 20));

        int empty = Run(world, 20_000, stopWhen: () => map.ResourceAmount(field, ResourceType.Coal) == 0);
        Assert.NotEqual(0, empty);
        int back = Run(world, 20_000, stopWhen: () => map.ResourceAmount(field, ResourceType.Coal) == 1);

        // Growth and regeneration run every 10 ticks, so the step returns within one such window.
        Assert.InRange(back, 300, 310);
    }

    [Fact]
    public void Grain_advances_exactly_one_stage_per_stage_window()
    {
        var map = new GameMap(70);
        var world = new World(map, TestConfig.NoTools.WithCropGrowTicks("grain", 600));
        var crop = world.Production.Config.Crops["grain"];
        world.PlaceBuilding(Romans["farm"], 0, new GridPoint(30, 20));
        world.PlaceBuilding(Romans["stock"], 0, new GridPoint(55, 25));
        for (int i = 0; i < 3; i++)
            world.Spawn(new GridPoint(45 + i, 40), Profession.Carrier);

        GridPoint? field = null;
        for (int t = 0; t < 20_000 && field is null; t++)
        {
            world.Step();
            for (int i = 0; i < map.PointCount && field is null; i++)
                if (map.ObjectCodeAt(map.Point(i)) == crop.First)
                    field = map.Point(i);
        }
        Assert.NotNull(field);

        int stage = 600 / (crop.Ripe - crop.First); // grain has no slope or tree bonus
        for (int t = 0; t < stage + 20; t++)
            world.Step();

        Assert.Equal((byte)(crop.First + 1), map.ObjectCodeAt(field!.Value));
    }

    private static void Spawn(World world, int count, GridPoint near)
    {
        for (int i = 0; i < count; i++)
            Assert.NotNull(world.Spawn(new GridPoint(near.X + i, near.Y), Profession.Carrier));
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
