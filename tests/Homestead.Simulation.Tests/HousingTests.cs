using Homestead.Simulation.Buildings;
using Homestead.Simulation.Economy;
using Homestead.Simulation.Map;

namespace Homestead.Simulation.Tests;

public class HousingTests
{
    private static readonly BuildingCatalog Romans = BuildingCatalog.Load(Civilization.Roman);

    [Theory]
    [InlineData("small_livinghouse", 10)]
    [InlineData("medium_livinghouse", 30)]
    public void A_finished_house_releases_one_carrier_per_second_through_its_door(string name, int residents)
    {
        var world = new World(new GameMap(90));
        var house = world.PlaceBuilding(Romans[name], 0, new GridPoint(45, 45))!;

        Run(world, World.TicksPerSecond * 3 + 1);
        Assert.Equal(4, world.Settlers.Count); // at ticks 0, 20, 40, 60
        Assert.All(world.Settlers, s => Assert.Equal(Profession.Carrier, s.Profession));

        Run(world, World.TicksPerSecond * (residents + 10));
        Assert.Equal(residents, world.Settlers.Count);
        Assert.Equal(0, world.Housing.ResidentsLeft(house));
        Assert.False(world.IsOccupied(house.Door), "newcomers leave the doorstep");
    }

    [Fact]
    public void A_house_built_on_a_construction_site_releases_its_residents_when_finished()
    {
        var world = new World(new GameMap(70), ProductionConfig.Load() with { Tools = new Dictionary<string, Material>() });
        var site = world.PlaceConstructionSite(Romans["small_livinghouse"], 0, new GridPoint(35, 35))!;
        world.AddLooseGoods(new GridPoint(20, 20), Material.Plank, 8);
        world.AddLooseGoods(new GridPoint(22, 20), Material.Stone, 8);
        for (int i = 0; i < 4; i++)
            world.Spawn(new GridPoint(50 + i, 50), i < 2 ? Profession.Carrier : Profession.Builder);
        world.Spawn(new GridPoint(55, 50), Profession.Digger);

        Run(world, 200);
        Assert.Equal(5, world.Settlers.Count);

        Run(world, 20_000, stopWhen: () => world.Housing.ResidentsLeft(site) == 0);
        Assert.Equal(15, world.Settlers.Count);
    }

    [Fact]
    public void Houses_of_a_loaded_map_do_not_release_anyone()
    {
        var world = new World(new GameMap(70));
        var house = world.PlaceBuilding(Romans["big_livinghouse"], 0, new GridPoint(35, 35))!;
        world.Housing.MarkPopulated(house);

        Run(world, 200);

        Assert.Empty(world.Settlers);
    }

    private static void Run(World world, int ticks, Func<bool>? stopWhen = null)
    {
        for (int t = 0; t < ticks && stopWhen?.Invoke() != true; t++)
            world.Step();
    }
}
