using Homestead.Simulation.Buildings;
using Homestead.Simulation.Economy;
using Homestead.Simulation.Map;

namespace Homestead.Simulation.Tests;

public class ToolTests
{
    private static readonly BuildingCatalog Romans = BuildingCatalog.Load(Civilization.Roman);

    [Fact]
    public void A_carrier_becomes_a_woodcutter_only_with_an_axe()
    {
        var map = new GameMap(70);
        map.SetObject(new GridPoint(25, 42), MapObjectKind.Tree);
        var world = new World(map, TestConfig.StoreAll);
        var lumberjack = world.PlaceBuilding(Romans["lumberjack"], 0, new GridPoint(30, 30))!;
        var stock = world.PlaceBuilding(Romans["stock"], 0, new GridPoint(50, 50))!;
        Spawn(world, 3, new GridPoint(40, 40));

        Run(world, 1_000);
        Assert.Null(world.Production.WorkerOf(lumberjack));
        Assert.All(world.Settlers, s => Assert.Equal(Profession.Carrier, s.Profession));
        Assert.Equal("no worker: tool missing (Axe)", world.StatusOf(lumberjack));
        Assert.False(world.Production.HasWorker(lumberjack));

        world.AddLooseGoods(new GridPoint(15, 55), Material.Axe, 1);
        Run(world, 20_000, stopWhen: () => Count(stock, Material.Trunk) == 1);

        var worker = world.Production.WorkerOf(lumberjack);
        Assert.NotNull(worker);
        Assert.Equal(Material.Axe, worker.Tool);
        Assert.True(world.Production.HasWorker(lumberjack));
        Assert.Same(lumberjack, world.BuildingAt(lumberjack.Position));
        Assert.Equal(1, Count(stock, Material.Trunk));
        Assert.DoesNotContain(world.Piles, p => p.Material == Material.Axe && p.Count > 0);
    }

    [Fact]
    public void A_dismissed_worker_keeps_trade_and_tool_and_takes_the_next_job_of_its_trade()
    {
        var world = new World(new GameMap(70));
        var sawmill = world.PlaceBuilding(Romans["sawmill"], 0, new GridPoint(30, 30))!;
        world.AddLooseGoods(new GridPoint(40, 45), Material.Saw, 1);
        Spawn(world, 2, new GridPoint(40, 40));
        Run(world, 5_000, stopWhen: () => world.Production.WorkerOf(sawmill)?.IsInside == true);
        var worker = world.Production.WorkerOf(sawmill)!;

        world.RemoveBuilding(sawmill);
        Run(world, 20);

        Assert.Equal(Profession.Worker, worker.Profession);
        Assert.Equal(Material.Saw, worker.Tool);
        Assert.Null(worker.Workplace);
        Assert.False(worker.HasTask);
        Assert.DoesNotContain(world.Piles, p => p.Material == Material.Saw && p.Count > 0);

        // No saw left: the new sawmill can only get the old sawmiller.
        var second = world.PlaceBuilding(Romans["sawmill"], 0, new GridPoint(50, 30))!;
        Run(world, 3_000, stopWhen: () => world.Production.WorkerOf(second)?.IsInside == true);
        Assert.Same(worker, world.Production.WorkerOf(second));
    }

    [Fact]
    public void Construction_sites_recruit_diggers_and_builders_with_shovels_and_hammers()
    {
        var map = new GameMap(70);
        for (int y = 30; y < 50; y++)
        for (int x = 20; x < 45; x++)
            map.SetHeight(new GridPoint(x, y), (x * 7 + y * 3) % 5); // uneven: diggers needed
        var world = new World(map);
        var site = world.PlaceConstructionSite(Romans["sawmill"], 0, new GridPoint(30, 40))!;
        world.AddLooseGoods(new GridPoint(10, 20), Material.Blade, 2);
        world.AddLooseGoods(new GridPoint(12, 20), Material.Hammer, 1);
        world.AddLooseGoods(new GridPoint(50, 20), Material.Plank, 8);
        world.AddLooseGoods(new GridPoint(52, 20), Material.Stone, 8);
        Spawn(world, 8, new GridPoint(45, 55));

        Run(world, 20_000, stopWhen: () => site.State == BuildingState.Finished);

        Assert.Equal(BuildingState.Finished, site.State);
        Assert.Equal(2, world.Settlers.Count(s => s.Profession == Profession.Digger));
        Assert.Equal(1, world.Settlers.Count(s => s.Profession == Profession.Builder));
        Assert.All(world.Settlers.Where(s => s.Profession == Profession.Digger), s => Assert.Equal(Material.Blade, s.Tool));
    }

    [Fact]
    public void Diggers_stay_within_the_players_share()
    {
        var map = new GameMap(70);
        for (int y = 30; y < 50; y++)
        for (int x = 20; x < 45; x++)
            map.SetHeight(new GridPoint(x, y), (x * 7 + y * 3) % 5);
        var world = new World(map);
        world.PlaceConstructionSite(Romans["sawmill"], 0, new GridPoint(30, 40));
        world.AddLooseGoods(new GridPoint(10, 20), Material.Blade, 3);
        Spawn(world, 8, new GridPoint(45, 55));
        world.Recruitment.SetShare(0, Share.Diggers, 0.15);

        Run(world, 2_000);

        Assert.Equal(1, world.Recruitment.MaxOf(0, Share.Diggers));
        Assert.Equal(1, world.Settlers.Count(s => s.Profession == Profession.Digger)
                        + world.Recruitment.Active.Count(r => r.Profession == Profession.Digger));
    }

    [Fact]
    public void The_kept_carrier_share_limits_conversions_and_carriers_become_geologists_by_count()
    {
        var world = new World(new GameMap(70));
        Spawn(world, 10, new GridPoint(40, 40));
        Assert.Equal(7, world.Recruitment.Convertible(0)); // 30 % stay carriers

        world.Recruitment.SetShare(0, Share.Carriers, 0.5);
        Assert.Equal(0.5, world.Recruitment.ShareOf(0, Share.Carriers));
        Assert.Equal(5, world.Recruitment.Convertible(0));

        Assert.Equal((1, 0), world.Geology.ConvertCarriers(0, 1));
        Assert.Equal((4, 0), world.Geology.ConvertCarriers(0, 5)); // the fifth would undercut the half kept
        Assert.Equal(5, world.Settlers.Count(s => s.Profession == Profession.Geologist));
    }

    [Fact]
    public void Idle_diggers_of_another_player_do_not_keep_a_site_from_recruiting()
    {
        var map = new GameMap(70);
        for (int y = 30; y < 50; y++)
        for (int x = 20; x < 45; x++)
            map.SetHeight(new GridPoint(x, y), (x * 7 + y * 3) % 5);
        var world = new World(map);
        var site = world.PlaceConstructionSite(Romans["sawmill"], 0, new GridPoint(30, 40))!;
        world.Spawn(new GridPoint(60, 60), Profession.Digger, player: 1);
        world.AddLooseGoods(new GridPoint(10, 20), Material.Blade, 1);
        Spawn(world, 2, new GridPoint(45, 55));

        Run(world, 5_000, stopWhen: () => site.State != BuildingState.Flattening);

        Assert.NotEqual(BuildingState.Flattening, site.State);
        Assert.Contains(world.Settlers, s => s.Profession == Profession.Digger && s.Player == 0);
    }

    [Fact]
    public void Toolsmith_makes_the_missing_axe_first()
    {
        var map = new GameMap(80);
        map.SetObject(new GridPoint(25, 62), MapObjectKind.Tree);
        var world = new World(map);
        var lumberjack = world.PlaceBuilding(Romans["lumberjack"], 0, new GridPoint(30, 50))!;
        world.PlaceBuilding(Romans["toolsmith"], 0, new GridPoint(50, 30));
        world.PlaceBuilding(Romans["stock"], 0, new GridPoint(60, 55));
        world.AddLooseGoods(new GridPoint(40, 20), Material.Hammer, 1);
        world.AddLooseGoods(new GridPoint(20, 20), Material.Iron, 2);
        world.AddLooseGoods(new GridPoint(22, 20), Material.Coal, 2);
        Spawn(world, 5, new GridPoint(40, 40));

        Run(world, 20_000, stopWhen: () => world.Production.WorkerOf(lumberjack)?.Tool == Material.Axe);

        Assert.Equal(Material.Axe, world.Production.WorkerOf(lumberjack)?.Tool);
        Assert.Equal(1, world.Production.ToolsMade);
    }

    [Fact]
    public void Smith_rests_when_everything_is_in_stock()
    {
        var config = ProductionConfig.Load() with
        {
            Smiths = new Dictionary<string, SmithRecipe>
            {
                ["toolsmith"] = new([Material.Iron, Material.Coal], [Material.Axe, Material.Saw], 100, KeepInStock: 1),
            },
        };
        var world = new World(new GameMap(70), config);
        world.PlaceBuilding(Romans["toolsmith"], 0, new GridPoint(30, 30));
        world.PlaceBuilding(Romans["stock"], 0, new GridPoint(50, 50));
        world.AddLooseGoods(new GridPoint(40, 20), Material.Hammer, 1);
        world.AddLooseGoods(new GridPoint(20, 50), Material.Iron, 5);
        world.AddLooseGoods(new GridPoint(22, 50), Material.Coal, 5);
        Spawn(world, 4, new GridPoint(40, 40));

        Run(world, 20_000, stopWhen: () => world.Production.ToolsMade == 2);
        Run(world, 3_000);

        Assert.Equal(2, world.Production.ToolsMade);
        Assert.Contains(world.Piles, p => p.Material == Material.Axe && p.Count == 1);
        Assert.Contains(world.Piles, p => p.Material == Material.Saw && p.Count == 1);
    }

    [Fact]
    public void Toolsmith_makes_only_what_is_needed_right_now()
    {
        var map = new GameMap(80);
        map.SetObject(new GridPoint(60, 62), MapObjectKind.Tree);
        var world = new World(map, ProductionConfig.Load());
        world.PlaceBuilding(Romans["toolsmith"], 0, new GridPoint(30, 30));
        world.AddLooseGoods(new GridPoint(40, 20), Material.Hammer, 1);
        world.AddLooseGoods(new GridPoint(20, 50), Material.Iron, 5);
        world.AddLooseGoods(new GridPoint(22, 50), Material.Coal, 5);
        Spawn(world, 4, new GridPoint(40, 40));

        Run(world, 6_000);
        Assert.Equal(0, world.Production.ToolsMade); // nobody needs anything

        world.PlaceBuilding(Romans["lumberjack"], 0, new GridPoint(60, 55));
        Run(world, 20_000, stopWhen: () => world.Production.ToolsMade == 1);
        Run(world, 4_000);

        Assert.Equal(1, world.Production.ToolsMade); // one axe for the woodcutter, then rest again
    }

    private static void Spawn(World world, int count, GridPoint near)
    {
        for (int i = 0; i < count; i++)
            Assert.NotNull(world.Spawn(new GridPoint(near.X + i, near.Y), Profession.Carrier));
    }

    private static int Count(Building building, Material material) =>
        building.Piles.Where(p => p.Material == material).Sum(p => p.Count);

    private static void Run(World world, int ticks, Func<bool>? stopWhen = null)
    {
        for (int t = 1; t <= ticks; t++)
        {
            world.Step();
            if (stopWhen?.Invoke() == true)
                return;
        }
    }
}
