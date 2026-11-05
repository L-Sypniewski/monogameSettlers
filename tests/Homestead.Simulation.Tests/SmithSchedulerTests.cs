using Homestead.Simulation.Buildings;
using Homestead.Simulation.Economy;
using Homestead.Simulation.Map;

namespace Homestead.Simulation.Tests;

/// <summary>What a smith makes next: the round robin over the shares, and each smith's own default share.</summary>
public class SmithSchedulerTests
{
    private static readonly BuildingCatalog Romans = BuildingCatalog.Load(Civilization.Roman);

    [Fact]
    public void The_smith_round_robins_products_exactly_by_their_shares()
    {
        var config = ProductionConfig.Load() with
        {
            Smiths = new Dictionary<string, SmithRecipe>
            {
                ["toolsmith"] = new([Material.Iron, Material.Coal], [Material.Axe, Material.Saw], 100,
                    KeepInStock: 0, Shares: new Dictionary<Material, double> { [Material.Axe] = 1, [Material.Saw] = 0.5 }),
            },
        };
        var world = new World(new GameMap(70), config);
        world.PlaceBuilding(Romans["toolsmith"], 0, new GridPoint(30, 30));
        world.PlaceBuilding(Romans["stock"], 0, new GridPoint(50, 50));
        world.AddLooseGoods(new GridPoint(40, 20), Material.Hammer, 1);
        world.AddLooseGoods(new GridPoint(20, 50), Material.Iron, 8);
        world.AddLooseGoods(new GridPoint(20, 46), Material.Iron, 5);
        world.AddLooseGoods(new GridPoint(22, 50), Material.Coal, 8);
        world.AddLooseGoods(new GridPoint(22, 46), Material.Coal, 5);
        Spawn(world, 4, new GridPoint(40, 40));

        Run(world, 120_000, stopWhen: () => world.Production.ToolsMade == 12);

        Assert.Equal(12, world.Production.ToolsMade);
        // Shares 1 and 0.5: every candidate earns its share, the chosen one pays the sum back,
        // so the picks repeat axe, saw, axe and twelve of them split exactly 8 to 4.
        Assert.Equal(8, Made(world, Material.Axe));
        Assert.Equal(4, Made(world, Material.Saw));
    }

    [Fact]
    public void Two_smiths_sharing_a_product_fall_back_to_their_own_default_share()
    {
        var world = new World(new GameMap(70), TwoSmithsSharingAxe());
        world.PlaceBuilding(Romans["weaponsmith"], 0, new GridPoint(30, 30));
        world.PlaceBuilding(Romans["stock"], 0, new GridPoint(50, 50));
        world.AddLooseGoods(new GridPoint(40, 20), Material.Hammer, 1);
        world.AddLooseGoods(new GridPoint(20, 50), Material.Iron, 7);
        world.AddLooseGoods(new GridPoint(22, 50), Material.Coal, 7);
        Spawn(world, 4, new GridPoint(40, 40));

        Run(world, 60_000, stopWhen: () => world.Production.ToolsMade == 6);

        Assert.Equal(6, world.Production.ToolsMade);
        // Only the weaponsmith stands: its own Axe default (0.5) applies, not the toolsmith's 0
        // that comes first in the data, so shares 1 and 0.5 split six picks 4 to 2.
        Assert.Equal(4, Made(world, Material.Sword));
        Assert.Equal(2, Made(world, Material.Axe));
        // The public bar shows the first smith's default in data order - kept that way on purpose.
        Assert.Equal(0, world.Production.ShareOf(0, Material.Axe));
    }

    [Fact]
    public void The_players_share_overrides_both_defaults()
    {
        var world = new World(new GameMap(70), TwoSmithsSharingAxe());
        world.Production.SetShare(0, Material.Axe, 0);
        world.PlaceBuilding(Romans["weaponsmith"], 0, new GridPoint(30, 30));
        world.PlaceBuilding(Romans["stock"], 0, new GridPoint(50, 50));
        world.AddLooseGoods(new GridPoint(40, 20), Material.Hammer, 1);
        world.AddLooseGoods(new GridPoint(20, 50), Material.Iron, 4);
        world.AddLooseGoods(new GridPoint(22, 50), Material.Coal, 4);
        Spawn(world, 4, new GridPoint(40, 40));

        Run(world, 60_000, stopWhen: () => world.Production.ToolsMade == 3);

        Assert.Equal(3, world.Production.ToolsMade);
        Assert.Equal(3, Made(world, Material.Sword));
        Assert.Equal(0, Made(world, Material.Axe));
    }

    /// <summary>The toolsmith comes first in the data and shares the Axe with the weaponsmith.</summary>
    private static ProductionConfig TwoSmithsSharingAxe() => ProductionConfig.Load() with
    {
        Smiths = new Dictionary<string, SmithRecipe>
        {
            ["toolsmith"] = new([Material.Iron, Material.Coal], [Material.Axe, Material.Saw], 100,
                KeepInStock: 0, Shares: new Dictionary<Material, double> { [Material.Axe] = 0, [Material.Saw] = 0 }),
            ["weaponsmith"] = new([Material.Iron, Material.Coal], [Material.Sword, Material.Axe], 100,
                KeepInStock: 0, Shares: new Dictionary<Material, double> { [Material.Sword] = 1, [Material.Axe] = 0.5 }),
        },
    };

    private static int Made(World world, Material material) =>
        world.Piles.Where(p => p.Material == material).Sum(p => p.Count) + world.Settlers.Count(s => s.Carrying == material);

    private static void Spawn(World world, int count, GridPoint near)
    {
        for (int i = 0; i < count; i++)
            Assert.NotNull(world.Spawn(new GridPoint(near.X + i, near.Y), Profession.Carrier));
    }

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
