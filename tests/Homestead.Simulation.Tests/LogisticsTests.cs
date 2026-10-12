using Homestead.Assets;
using Homestead.Assets.Maps;
using Homestead.Simulation.Buildings;
using Homestead.Simulation.Economy;
using Homestead.Simulation.Map;
using Xunit.Abstractions;

namespace Homestead.Simulation.Tests;

// Transport only: production is switched off, so workers do not consume the delivered goods.

public class LogisticsTests(ITestOutputHelper output)
{
    private static readonly BuildingCatalog Romans = BuildingCatalog.Load(Civilization.Roman);

    [Fact]
    public void Building_data_loads_for_all_civilizations()
    {
        foreach (var civilization in Enum.GetValues<Civilization>())
        {
            var catalog = BuildingCatalog.Load(civilization);
            Assert.NotEmpty(catalog.All);
            Assert.All(catalog.All, b => Assert.NotEmpty(b.Blocked));
        }
        Assert.Equal(Material.Trunk, Romans["sawmill"].RequestPiles.Single().Material);
        Assert.Equal(Material.Plank, Romans["sawmill"].OfferPiles.Single().Material);
    }

    [Fact]
    public void Carrier_brings_loose_goods_to_a_building()
    {
        var world = new World(new GameMap(60), ProductionConfig.None);
        var sawmill = world.PlaceBuilding(Romans["sawmill"], 0, new GridPoint(30, 30))!;
        var trunks = world.AddLooseGoods(new GridPoint(12, 14), Material.Trunk, 3)!;
        world.Spawn(new GridPoint(20, 40), Profession.Carrier);

        Run(world, 3000);

        var input = sawmill.Piles.Single(p => p.Role == PileRole.Request);
        Assert.Equal(3, input.Count);
        Assert.Null(world.PileAt(trunks.Position));
        Assert.Equal(3, world.Logistics.Delivered);
        Assert.Empty(world.Logistics.Jobs);
    }

    [Fact]
    public void Several_carriers_never_take_the_same_unit()
    {
        var world = new World(new GameMap(60), ProductionConfig.None);
        var sawmill = world.PlaceBuilding(Romans["sawmill"], 0, new GridPoint(30, 30))!;
        world.AddLooseGoods(new GridPoint(10, 10), Material.Trunk, 2);
        for (int i = 0; i < 6; i++)
            world.Spawn(new GridPoint(15 + i, 45), Profession.Carrier);

        Run(world, 3000, check: () => AssertConserved(world, Material.Trunk, 2));

        Assert.Equal(2, sawmill.Piles.Single(p => p.Role == PileRole.Request).Count);
        Assert.Equal(2, world.Logistics.Delivered);
    }

    [Fact]
    public void Specific_requests_are_served_before_the_storehouse()
    {
        var world = new World(new GameMap(70), ProductionConfig.None);
        var stock = world.PlaceBuilding(Romans["stock"], 0, new GridPoint(20, 20))!;
        var sawmill = world.PlaceBuilding(Romans["sawmill"], 0, new GridPoint(50, 50))!;
        world.AddLooseGoods(new GridPoint(22, 30), Material.Trunk, 4); // next to the storehouse
        world.Spawn(new GridPoint(25, 30), Profession.Carrier);

        Run(world, 5000);

        Assert.Equal(4, sawmill.Piles.Single(p => p.Role == PileRole.Request).Count);
        Assert.All(stock.Piles, p => Assert.Equal(0, p.Count));
    }

    [Fact]
    public void Storehouse_takes_anything_one_material_per_pile()
    {
        var world = new World(new GameMap(60), ProductionConfig.None);
        var stock = world.PlaceBuilding(Romans["stock"], 0, new GridPoint(30, 30))!;
        world.AddLooseGoods(new GridPoint(10, 10), Material.Stone, 3);
        world.AddLooseGoods(new GridPoint(12, 10), Material.Fish, 2);
        for (int i = 0; i < 3; i++)
            world.Spawn(new GridPoint(15 + i, 45), Profession.Carrier);

        Run(world, 5000);

        Assert.Equal(3, stock.Piles.Where(p => p.Material == Material.Stone).Sum(p => p.Count));
        Assert.Equal(2, stock.Piles.Where(p => p.Material == Material.Fish).Sum(p => p.Count));
        Assert.All(stock.Piles.Where(p => p.Count > 0), p => Assert.NotNull(p.Material));
    }

    [Fact]
    public void Goods_survive_when_the_target_building_disappears()
    {
        var world = new World(new GameMap(60), ProductionConfig.None);
        var sawmill = world.PlaceBuilding(Romans["sawmill"], 0, new GridPoint(40, 40))!;
        world.AddLooseGoods(new GridPoint(8, 8), Material.Trunk, 1);
        var carrier = world.Spawn(new GridPoint(10, 10), Profession.Carrier)!;

        // Wait until the trunk is on its way, then tear the building down.
        Run(world, 2000, stopWhen: () => carrier.Carrying == Material.Trunk);
        world.RemoveBuilding(sawmill);
        Run(world, 2000, check: () => AssertConserved(world, Material.Trunk, 1));

        Assert.Null(carrier.Carrying);
        Assert.Equal(1, world.Logistics.DroppedOnTheWay);
        Assert.Single(world.Piles, p => p.Role == PileRole.Loose && p.Material == Material.Trunk);
    }

    [Fact]
    public void Unreachable_goods_are_not_dispatched()
    {
        var map = new GameMap(60);
        // A lake with an island in the middle.
        for (int i = 0; i < map.PointCount; i++)
        {
            var p = map.Point(i);
            int d = Pathfinding.PathFinder.Distance(p, new GridPoint(10, 10));
            if (d is >= 3 and <= 6)
                map.SetTerrain(p, TerrainType.Water);
        }
        var world = new World(map, ProductionConfig.None);
        world.PlaceBuilding(Romans["sawmill"], 0, new GridPoint(40, 40));
        var island = world.AddLooseGoods(new GridPoint(10, 10), Material.Trunk, 2)!;
        world.Spawn(new GridPoint(30, 50), Profession.Carrier);

        Run(world, 1000);

        Assert.Equal(2, island.Count);
        Assert.Equal(0, world.Logistics.Delivered);
        Assert.Equal(0, world.Logistics.Cancelled);
    }

    [Fact]
    public void Busy_economy_conserves_goods_and_serves_requests()
    {
        var random = new Random(3);
        var world = new World(new GameMap(96), ProductionConfig.None);
        var materials = new[] { Material.Trunk, Material.Stone, Material.Crop, Material.Flour, Material.Water };
        var buildings = new List<Building>();
        foreach (var (name, x, y) in new[] { ("sawmill", 25, 25), ("mill", 70, 25), ("baker", 25, 70), ("stock", 70, 70), ("sawmill", 48, 48) })
            buildings.Add(world.PlaceBuilding(Romans[name], 0, new GridPoint(x, y))!);
        var expected = new Dictionary<Material, int>();
        for (int i = 0; i < 60; i++)
        {
            var material = materials[random.Next(materials.Length)];
            int count = random.Next(1, 9);
            if (world.AddLooseGoods(new GridPoint(random.Next(5, 91), random.Next(5, 91)), material, count) is { } pile)
                expected[material] = expected.GetValueOrDefault(material) + pile.Count;
        }
        for (int i = 0; i < 40; i++)
            world.Spawn(new GridPoint(random.Next(10, 86), random.Next(10, 86)), Profession.Carrier);

        Run(world, 30_000, check: () =>
        {
            foreach (var (material, count) in expected)
                AssertConserved(world, material, count);
        });

        // Every request pile that could be filled from the supply is full now.
        foreach (var request in world.Piles.Where(p => p.Role == PileRole.Request && p.AcceptedMaterial is { }))
        {
            int stillLoose = world.Piles.Where(p => p.Role == PileRole.Loose && p.Material == request.AcceptedMaterial).Sum(p => p.Count);
            Assert.True(request.Count == request.Capacity || stillLoose == 0,
                $"{request.Owner!.Definition.Name} wants {request.AcceptedMaterial}: has {request.Count}, {stillLoose} lying around.");
        }
        output.WriteLine(string.Join(", ", world.Logistics.Problems.Select(kv => $"{kv.Key} {kv.Value}x")));
        output.WriteLine($"delivered {world.Logistics.Delivered}, cancelled {world.Logistics.Cancelled}, " +
                         $"dropped {world.Logistics.DroppedOnTheWay}, pass-throughs {world.PassThroughs}, detours {world.Detours}");
        Assert.Equal(0, world.Logistics.DroppedOnTheWay);
    }

    [Fact]
    public void Original_maps_load_into_worlds()
    {
        GameInstallation installation;
        try { installation = GameInstallation.Locate(); }
        catch (Exception) { output.WriteLine("No installation found, skipped."); return; }

        int buildings = 0, piles = 0, settlers = 0;
        var skipped = new Dictionary<string, int>();
        foreach (string path in installation.MapFiles)
        {
            var (world, report) = OriginalScenario.Load(OriginalMap.Open(path));
            buildings += report.Buildings;
            piles += report.Piles;
            settlers += report.Settlers;
            foreach (var s in report.Skipped)
            {
                string key = s[..s.IndexOf(" at ", StringComparison.Ordinal)];
                skipped[key] = skipped.GetValueOrDefault(key) + 1;
            }
            Assert.Equal(report.Settlers, world.Settlers.Count);
        }
        output.WriteLine($"{buildings} buildings, {piles} piles, {settlers} settlers placed");
        foreach (var (key, count) in skipped.OrderByDescending(kv => kv.Value))
            output.WriteLine($"  skipped {count,5}x {key}");
    }

    private static void Run(World world, int ticks, Func<bool>? stopWhen = null, Action? check = null)
    {
        for (int t = 0; t < ticks; t++)
        {
            world.Step();
            check?.Invoke();
            if (stopWhen?.Invoke() == true)
                return;
        }
    }

    private static void AssertConserved(World world, Material material, int expected)
    {
        int inPiles = world.Piles.Where(p => p.Material == material).Sum(p => p.Count);
        int carried = world.Settlers.Count(s => s.Carrying == material);
        Assert.Equal(expected, inPiles + carried);
    }

    [Fact]
    public void A_storehouse_keeps_one_material_together()
    {
        var world = new World(new GameMap(70), ProductionConfig.None);
        var stock = world.PlaceBuilding(Romans["stock"], 0, new GridPoint(35, 35))!;
        world.AddLooseGoods(new GridPoint(15, 15), Material.Blade, 8);
        world.AddLooseGoods(new GridPoint(17, 15), Material.Blade, 4);
        for (int i = 0; i < 6; i++)
            world.Spawn(new GridPoint(20 + i, 50), Profession.Carrier);

        for (int t = 0; t < 10_000 && (world.Piles.Any(p => p.Role == PileRole.Loose && p.Count > 0) || world.Logistics.Jobs.Count > 0); t++)
            world.Step();

        var blades = stock.Piles.Where(p => p.Material == Material.Blade && p.Count > 0).Select(p => p.Count).Order().ToList();
        Assert.Equal([4, 8], blades); // one pile filled up first, not spread over all six piles
    }

    [Fact]
    public void A_building_whose_pile_place_is_blocked_cannot_be_placed()
    {
        var map = new GameMap(70);
        var world = new World(map, ProductionConfig.None);
        var at = new GridPoint(30, 30);
        Assert.True(world.CanPlace(Romans["sawmill"], at, out _));

        map.SetObject(at + Romans["sawmill"].ConstructionPiles[0].Offset, MapObjectKind.Tree);

        Assert.False(world.CanPlace(Romans["sawmill"], at, out string reason));
        Assert.Equal("door or pile place blocked", reason);
    }

    [Fact]
    public void A_full_storehouse_takes_nothing_more()
    {
        var world = new World(new GameMap(70), ProductionConfig.None);
        var stock = world.PlaceBuilding(Romans["stock"], 0, new GridPoint(35, 35))!;
        var materials = new[] { Material.Blade, Material.Axe, Material.Pick, Material.Saw, Material.Stone, Material.Plank, Material.Trunk, Material.Coal };
        for (int i = 0; i < materials.Length; i++)
            world.AddLooseGoods(new GridPoint(10 + 2 * i, 15), materials[i], 3);
        for (int i = 0; i < 8; i++)
            world.Spawn(new GridPoint(20 + i, 55), Profession.Carrier);

        for (int t = 0; t < 20_000 && (world.Piles.Any(p => p.Role == PileRole.Loose && p.Count > 0) || world.Logistics.Jobs.Count > 0); t++)
            world.Step();

        // Six piles, one material each: two materials find no room and stay where they are.
        Assert.Equal(6, stock.Piles.Count(p => p.Role == PileRole.Storage));
        Assert.Equal(6, materials.Count(m => stock.Piles.Where(p => p.Material == m).Sum(p => p.Count) == 3));
        Assert.Equal(2, world.Piles.Count(p => p.Role == PileRole.Loose && p.Count == 3));
    }

    [Fact]
    public void Only_tools_weapons_and_gold_go_to_the_storehouse()
    {
        var world = new World(new GameMap(60), ProductionConfig.Load());
        var stock = world.PlaceBuilding(Romans["stock"], 0, new GridPoint(30, 30))!;
        world.AddLooseGoods(new GridPoint(10, 10), Material.Axe, 2);
        world.AddLooseGoods(new GridPoint(12, 10), Material.Sword, 3);
        world.AddLooseGoods(new GridPoint(14, 10), Material.Plank, 4);
        world.AddLooseGoods(new GridPoint(16, 10), Material.Stone, 5);
        for (int i = 0; i < 4; i++)
            world.Spawn(new GridPoint(15 + i, 45), Profession.Carrier);

        Run(world, 5000);

        Assert.Equal(2, stock.Piles.Where(p => p.Material == Material.Axe).Sum(p => p.Count));
        Assert.Equal(3, stock.Piles.Where(p => p.Material == Material.Sword).Sum(p => p.Count));
        // Planks and stones stay where they are until somebody needs them.
        Assert.DoesNotContain(stock.Piles, p => p.Material is Material.Plank or Material.Stone);
        Assert.Equal(4, world.PileAt(new GridPoint(14, 10))!.Count);
        Assert.Equal(5, world.PileAt(new GridPoint(16, 10))!.Count);
    }

    [Fact]
    public void Food_goes_to_mines_by_the_distribution_weights()
    {
        // No ore in the ground: the mines only collect their food.
        var world = new World(new GameMap(80), ProductionConfig.Load());
        var coal = world.PlaceBuilding(Romans["coalmine"], 0, new GridPoint(25, 30))!;
        var iron = world.PlaceBuilding(Romans["ironmine"], 0, new GridPoint(55, 30))!;
        world.AddLooseGoods(new GridPoint(40, 50), Material.Bread, 8);
        for (int i = 0; i < 8; i++)
            world.Spawn(new GridPoint(36 + i, 55), Profession.Carrier);

        Run(world, 3000);

        int toCoal = coal.Piles.Where(p => p.Material == Material.Bread).Sum(p => p.Count);
        int toIron = iron.Piles.Where(p => p.Material == Material.Bread).Sum(p => p.Count);
        Assert.Equal(8, toCoal + toIron);
        Assert.True(toCoal >= 6, $"bread goes mostly to the coal mine (90:10): {toCoal} to coal, {toIron} to iron");
    }

    [Fact]
    public void Goods_go_to_the_nearest_building_of_the_drawn_type()
    {
        var world = new World(new GameMap(90), ProductionConfig.Load());
        var near = world.PlaceBuilding(Romans["coalmine"], 0, new GridPoint(25, 30))!;
        var far = world.PlaceBuilding(Romans["coalmine"], 0, new GridPoint(70, 30))!;
        world.AddLooseGoods(new GridPoint(30, 45), Material.Bread, 5);
        for (int i = 0; i < 5; i++)
            world.Spawn(new GridPoint(40 + i, 55), Profession.Carrier);

        Run(world, 3000);

        Assert.Equal(5, near.Piles.Where(p => p.Material == Material.Bread).Sum(p => p.Count));
        Assert.DoesNotContain(far.Piles, p => p.Material == Material.Bread && p.Count > 0);
    }
}
