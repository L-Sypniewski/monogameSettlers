using Homestead.Simulation.Buildings;
using Homestead.Simulation.Economy;
using Homestead.Simulation.Map;
using Xunit.Abstractions;

namespace Homestead.Simulation.Tests;

public class ProductionTests(ITestOutputHelper output)
{
    private static readonly BuildingCatalog Romans = BuildingCatalog.Load(Civilization.Roman);

    [Fact]
    public void Recipes_match_the_piles_of_the_buildings()
    {
        var config = ProductionConfig.Load();
        foreach (var civilization in Enum.GetValues<Civilization>())
        {
            var catalog = BuildingCatalog.Load(civilization);
            foreach (var (name, recipe) in config.Workshops)
            {
                if (catalog.Find(name) is not { } building)
                    continue;
                foreach (var input in recipe.Inputs)
                    Assert.True(building.RequestPiles.Any(p => p.Material == input), $"{civilization} {name} has no input pile for {input}");
                foreach (var outputMaterial in recipe.Outputs)
                    Assert.True(building.OfferPiles.Any(p => p.Material == outputMaterial), $"{civilization} {name} has no output pile for {outputMaterial}");
            }
            foreach (var (name, mine) in config.Mines)
                if (catalog.Find(name) is { } building)
                {
                    Assert.True(building.OfferPiles.Any(p => p.Material == mine.Output), $"{civilization} {name} has no output pile for {mine.Output}");
                    Assert.True(building.RequestPiles.Count > 0, $"{civilization} {name} has no food piles");
                }
            foreach (var (name, recipe) in config.Gatherers)
                if (catalog.Find(name) is { } building && recipe.Output is { } gathered)
                    Assert.True(building.OfferPiles.Any(p => p.Material == gathered), $"{civilization} {name} has no output pile for {gathered}");
        }
    }

    [Fact]
    public void A_carrier_becomes_the_worker_and_the_sawmill_turns_trunks_into_planks()
    {
        var world = new World(new GameMap(70), TestConfig.NoTools);
        var sawmill = world.PlaceBuilding(Romans["sawmill"], 0, new GridPoint(30, 30))!;
        var stock = world.PlaceBuilding(Romans["stock"], 0, new GridPoint(50, 50))!;
        world.AddLooseGoods(new GridPoint(10, 50), Material.Trunk, 5);
        Spawn(world, Profession.Carrier, 4, new GridPoint(20, 20));

        Run(world, 20_000, stopWhen: () => Count(stock, Material.Plank) == 5, check: () => AssertWood(world, 5));

        var worker = world.Production.WorkerOf(sawmill);
        Assert.NotNull(worker);
        Assert.Equal(Profession.Worker, worker.Profession);
        Assert.Equal("SAWMILLER", worker.Trade);
        Assert.Equal(5, Count(stock, Material.Plank));
        Assert.Equal(5, world.Production.Produced);
    }

    [Fact]
    public void Lumberjack_cuts_trees_in_range_and_leaves_stumps()
    {
        var map = new GameMap(70);
        var trees = new[] { new GridPoint(25, 40), new GridPoint(28, 44), new GridPoint(33, 40), new GridPoint(68, 30) };
        foreach (var tree in trees)
            map.SetObject(tree, MapObjectKind.Tree);
        var world = new World(map, TestConfig.NoTools);
        world.PlaceBuilding(Romans["lumberjack"], 0, new GridPoint(30, 30));
        var stock = world.PlaceBuilding(Romans["stock"], 0, new GridPoint(45, 25))!;
        Spawn(world, Profession.Carrier, 3, new GridPoint(40, 35));

        Run(world, 20_000, stopWhen: () => Count(stock, Material.Trunk) == 3);

        Assert.Equal(3, Count(stock, Material.Trunk));
        // A stump rots away after a while: felled trees leave a stump or, later, free ground.
        Assert.All(trees.Take(3), t => Assert.Contains(map.ObjectCodeAt(t), new[] { MapObjectCodes.TreeStump, MapObjectCodes.None }));
        Assert.All(trees.Take(3), t => Assert.True(map.IsWalkable(t)));
        Assert.True(MapObjectCodes.IsAdultTree(map.ObjectCodeAt(trees[3])), "The far tree is outside the work radius.");
    }

    [Fact]
    public void A_gatherer_with_a_full_output_pile_waits_instead_of_starting_a_new_pile()
    {
        var map = new GameMap(70);
        for (int y = 36; y < 44; y += 2)
            for (int x = 22; x < 38; x += 2)
                map.SetObject(new GridPoint(x, y), MapObjectKind.Tree);
        var world = new World(map, TestConfig.NoTools);
        var lumberjack = world.PlaceBuilding(Romans["lumberjack"], 0, new GridPoint(30, 30))!;
        Spawn(world, Profession.Carrier, 1, new GridPoint(40, 35)); // becomes the lumberjack, nobody carries
        var output = lumberjack.Piles.Single(p => p.Role == PileRole.Offer);

        Run(world, 40_000, stopWhen: () => output.Count == output.Capacity);
        Assert.Equal(output.Capacity, output.Count);
        Run(world, 3_000);

        Assert.Equal(output.Capacity, output.Count);
        Assert.DoesNotContain(world.Piles, p => p.Role == PileRole.Loose && p.Material == Material.Trunk);
        Assert.Contains("output pile is full", world.StatusOf(lumberjack));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void Stonecutter_shrinks_the_rock_until_only_rubble_is_left(int stonesPerCut)
    {
        var map = new GameMap(70);
        var rock = new GridPoint(36, 38);
        map.SetObjectCode(rock, MapObjectCodes.FullStone);
        var config = TestConfig.NoTools with { StonesPerCut = stonesPerCut };
        var world = new World(map, config);
        world.PlaceBuilding(Romans["stonecutter"], 0, new GridPoint(30, 30));
        var stock = world.PlaceBuilding(Romans["stock"], 0, new GridPoint(50, 25))!;
        Spawn(world, Profession.Carrier, 4, new GridPoint(40, 45));
        int expected = 12 * stonesPerCut;

        Run(world, 60_000, stopWhen: () => map.ObjectCodeAt(rock) == MapObjectCodes.StoneRubble && world.Logistics.Jobs.Count == 0
                                           && Count(stock, Material.Stone) == expected);

        Assert.Equal(MapObjectCodes.StoneRubble, map.ObjectCodeAt(rock));
        Assert.Equal(expected, world.Production.StonesCut);
        Assert.Equal(expected, Count(stock, Material.Stone));
        Assert.True(map.IsWalkable(rock.Neighbor(Direction.East)), "The ring around the rubble is free again.");
    }

    [Fact]
    public void Forester_plants_trees_that_grow_up()
    {
        var config = TestConfig.NoTools with { TreeGrowTicks = 200 };
        var map = new GameMap(70);
        var world = new World(map, config);
        world.PlaceBuilding(Romans["forester"], 0, new GridPoint(35, 35));
        Spawn(world, Profession.Carrier, 1, new GridPoint(40, 45));

        Run(world, 6_000, stopWhen: () => world.Production.TreesGrown >= 3);

        Assert.True(world.Production.TreesGrown >= 3, $"planted {world.Production.TreesPlanted}, grown {world.Production.TreesGrown}");
        int adult = Enumerable.Range(0, map.PointCount).Count(i => MapObjectCodes.IsAdultTree(map.ObjectCodeAt(map.Point(i))));
        Assert.Equal(world.Production.TreesGrown, adult);
    }

    [Fact]
    public void Lumberjack_cuts_in_the_moved_work_area_only()
    {
        var map = new GameMap(140);
        var lumberjack = Romans["lumberjack"];
        int radius = lumberjack.WorkRadius!.Value;
        var position = new GridPoint(70, 70);
        var moved = new GridPoint(70 + World.MaxWorkCenterDistance, 70);
        var near = position + (lumberjack.WorkCenter ?? default) + new GridPoint(-radius / 2, 0);
        var far = moved + new GridPoint(radius / 2, 0);
        map.SetObject(near, MapObjectKind.Tree);
        map.SetObject(far, MapObjectKind.Tree);
        var world = new World(map, TestConfig.NoTools);
        var building = world.PlaceBuilding(lumberjack, 0, position)!;
        Spawn(world, Profession.Carrier, 1, new GridPoint(80, 80));

        Assert.Equal(moved, world.SetWorkCenter(building, moved));
        Run(world, 10_000, stopWhen: () => world.Production.WorkerOf(building)?.Carrying == Material.Trunk);

        Assert.False(MapObjectCodes.IsAdultTree(map.ObjectCodeAt(far)), "The tree in the moved work area is cut.");
        Assert.True(MapObjectCodes.IsAdultTree(map.ObjectCodeAt(near)), "The tree only in the old work area stays.");
    }

    [Fact]
    public void Work_center_stays_within_reach_of_the_building()
    {
        var world = new World(new GameMap(140), TestConfig.NoTools);
        var building = world.PlaceBuilding(Romans["lumberjack"], 0, new GridPoint(70, 70))!;

        Assert.Equal(new GridPoint(70 + World.MaxWorkCenterDistance, 70), world.SetWorkCenter(building, new GridPoint(130, 70)));
        Assert.Equal(new GridPoint(70, 70 - World.MaxWorkCenterDistance), world.SetWorkCenter(building, new GridPoint(70, -50)));
        Assert.Equal(new GridPoint(75, 60), world.SetWorkCenter(building, new GridPoint(75, 60)));
        Assert.Equal(new GridPoint(75, 60), building.WorkCenter);
        Assert.Null(world.SetWorkCenter(world.PlaceBuilding(Romans["sawmill"], 0, new GridPoint(30, 30))!, new GridPoint(35, 35)));
    }

    [Fact]
    public void Worker_of_a_removed_building_leaves_it_and_keeps_its_trade()
    {
        var map = new GameMap(70);
        for (int i = 0; i < 6; i++)
            map.SetObject(new GridPoint(24 + i * 3, 42), MapObjectKind.Tree);
        var world = new World(map, TestConfig.NoTools);
        var lumberjack = world.PlaceBuilding(Romans["lumberjack"], 0, new GridPoint(30, 30))!;
        Spawn(world, Profession.Carrier, 1, new GridPoint(40, 45));
        Run(world, 5_000, stopWhen: () => world.Production.WorkerOf(lumberjack)?.Carrying == Material.Trunk);
        var worker = world.Production.WorkerOf(lumberjack)!;

        world.RemoveBuilding(lumberjack);
        Run(world, 100);

        Assert.Equal(Profession.Worker, worker.Profession);
        Assert.Null(worker.Workplace);
        Assert.False(worker.IsInside);
        Assert.Null(worker.Carrying);
        Assert.False(worker.HasTask);
        Assert.Contains(world.Piles, p => p.Material == Material.Trunk && p.Count > 0);
    }

    [Fact]
    public void Economy_builds_a_new_building_from_its_own_wood_and_stone()
    {
        var random = new Random(4);
        var map = new GameMap(110);
        var world = new World(map, TestConfig.NoTools);
        var anchors = new[] { ("stock", 55, 55), ("lumberjack", 28, 75), ("forester", 20, 90), ("sawmill", 40, 50), ("stonecutter", 75, 75) };
        foreach (var (name, x, y) in anchors)
            Assert.NotNull(world.PlaceBuilding(Romans[name], 0, new GridPoint(x, y)));
        // Trees and rocks only away from the buildings.
        bool NearBuilding(GridPoint p) => anchors.Any(a => Pathfinding.PathFinder.Distance(p, new GridPoint(a.Item2, a.Item3)) < 9);
        for (int planted = 0; planted < 40;)
        {
            var p = new GridPoint(random.Next(8, 50), random.Next(60, 102));
            if (!NearBuilding(p) && map.IsWalkable(p) && map.ObjectAt(p) == MapObjectKind.None)
            {
                map.SetObject(p, MapObjectKind.Tree);
                planted++;
            }
        }
        map.SetObjectCode(new GridPoint(82, 88), MapObjectCodes.FullStone);
        map.SetObjectCode(new GridPoint(88, 82), MapObjectCodes.FullStone);
        Spawn(world, Profession.Carrier, 10, new GridPoint(55, 68));
        Spawn(world, Profession.Builder, 2, new GridPoint(60, 70));
        Spawn(world, Profession.Digger, 1, new GridPoint(62, 70));

        var site = world.PlaceConstructionSite(Romans["tower"], 0, new GridPoint(75, 40))
                   ?? world.PlaceConstructionSite(Romans["sawmill"], 0, new GridPoint(75, 40))!;
        Assert.DoesNotContain(world.Piles, p => p.Role == PileRole.Loose);

        int ticks = Run(world, 120_000, stopWhen: () => site.State == BuildingState.Finished);

        output.WriteLine($"{site.Definition.Name} finished after {ticks / World.TicksPerSecond} s: trees cut {world.Production.TreesCut}, " +
                         $"planted {world.Production.TreesPlanted}, stones {world.Production.StonesCut}, planks {world.Production.Produced}; " +
                         $"logistics problems: {string.Join(", ", world.Logistics.Problems.Select(kv => $"{kv.Key} {kv.Value}x"))}; " +
                         $"production problems: {string.Join(", ", world.Production.Problems.Select(kv => $"{kv.Key} {kv.Value}x"))}");
        Assert.Equal(BuildingState.Finished, site.State);
    }

    private static void Spawn(World world, Profession profession, int count, GridPoint near)
    {
        for (int i = 0; i < count; i++)
            Assert.NotNull(world.Spawn(new GridPoint(near.X + i, near.Y), profession));
    }

    private static int Count(Building building, Material material) =>
        building.Piles.Where(p => p.Material == material).Sum(p => p.Count);

    /// <summary>Every trunk turns into exactly one plank: trunks + planks (anywhere) stay constant.</summary>
    private static void AssertWood(World world, int total)
    {
        int inPiles = world.Piles.Where(p => p.Material is Material.Trunk or Material.Plank).Sum(p => p.Count);
        int carried = world.Settlers.Count(s => s.Carrying is Material.Trunk or Material.Plank);
        Assert.InRange(inPiles + carried, total - 1, total); // one may be inside the sawmill being sawn
    }

    private static int Run(World world, int ticks, Func<bool>? stopWhen = null, Action? check = null)
    {
        for (int t = 1; t <= ticks; t++)
        {
            world.Step();
            check?.Invoke();
            if (stopWhen?.Invoke() == true)
                return t;
        }
        return ticks;
    }
}
