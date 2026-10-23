using Homestead.Simulation.Buildings;
using Homestead.Simulation.Economy;
using Homestead.Simulation.Events;
using Homestead.Simulation.Map;
using Homestead.Simulation.Pathfinding;
using Homestead.Simulation.Persistence;

namespace Homestead.Simulation.Tests;

public class TradeTests
{
    private static readonly BuildingCatalog Romans = BuildingCatalog.Load(Civilization.Roman);

    /// <summary>Carriers store nothing: whatever reaches the storehouse came by donkey.</summary>
    private static readonly ProductionConfig StoreNothing = TestConfig.NoTools with { StoredMaterials = new HashSet<Material>() };

    [Fact]
    public void The_donkey_farm_breeds_donkeys_of_crop_and_water_until_enough_stand_around()
    {
        var config = TestConfig.NoTools with { Trade = (TestConfig.NoTools.Trade ?? TradeRules.Default) with { KeepIdle = 2, FarmTicks = 100 } };
        var world = new World(new GameMap(70), config);
        var farm = world.PlaceBuilding(Romans["donkey_farm"], 0, new GridPoint(30, 30))!;
        world.AddLooseGoods(new GridPoint(15, 45), Material.Crop, 6);
        world.AddLooseGoods(new GridPoint(16, 47), Material.Water, 6);
        for (int i = 0; i < 4; i++)
            world.Spawn(new GridPoint(20 + i, 20), Profession.Carrier);

        for (int t = 0; t < 20_000; t++)
            world.Step();

        var donkeys = world.Settlers.Where(s => s.Profession == Profession.Donkey).ToList();
        Assert.Equal(2, donkeys.Count);
        Assert.Equal(2, world.Production.DonkeysBred);
        Assert.All(donkeys, d => Assert.Equal(0, d.Player));
        // Donkeys are no carriers or specialists: they do not count for the shares of Siedler › Berufe.
        Assert.Equal(4, world.Recruitment.Civilians(0));
        Assert.Contains("enough donkeys", world.StatusOf(farm));
        // Two of each input used up, the rest still lies there.
        Assert.Equal(4, world.Piles.Where(p => p.Material == Material.Crop).Sum(p => p.Count));
    }

    [Fact]
    public void A_market_sends_the_chosen_goods_by_donkey_over_its_stops_to_the_destination()
    {
        var map = new GameMap(90);
        // A lake with an island in it: neither can be a destination.
        foreach (var p in PathFinder.Circle(new GridPoint(80, 15), 4))
            map.SetTerrain(p, PathFinder.Distance(p, new GridPoint(80, 15)) <= 1 ? TerrainType.Grass : TerrainType.Water);
        var world = new World(map, StoreNothing);
        var market = world.PlaceBuilding(Romans["market_place"], 0, new GridPoint(20, 20))!;
        var stock = world.PlaceBuilding(Romans["stock"], 0, new GridPoint(65, 65))!;
        Assert.Equal(6, market.Piles.Count(p => p.Role == PileRole.Trade));
        world.AddLooseGoods(new GridPoint(12, 34), Material.Plank, 5);
        world.AddLooseGoods(new GridPoint(14, 34), Material.Stone, 4);
        for (int i = 0; i < 4; i++)
            world.Spawn(new GridPoint(26 + i, 30), Profession.Carrier);
        var donkey = world.Spawn(new GridPoint(30, 12), Profession.Donkey)!;

        // Without a destination carriers bring nothing.
        world.Trade.SetRemaining(market, Material.Plank, 3);
        for (int t = 0; t < 600; t++)
            world.Step();
        Assert.Equal(0, market.Piles.Sum(p => p.Count));
        Assert.Contains("no destination", world.StatusOf(market));

        var stop = new GridPoint(15, 70);
        Assert.False(world.Trade.SetDestination(market, new GridPoint(80, 18)));
        Assert.False(world.Trade.SetDestination(market, new GridPoint(80, 15)));
        Assert.False(world.Trade.SetStop(market, 0, new GridPoint(80, 18)));
        // A click on a building: its door.
        Assert.True(world.Trade.SetDestination(market, stock.Position));
        Assert.True(world.Trade.SetStop(market, 1, stop));
        Assert.Equal([stop, stock.Door], world.Trade.Route(market));
        // A new destination drops the stops.
        Assert.True(world.Trade.SetDestination(market, stock.Position));
        Assert.Equal([stock.Door], world.Trade.Route(market));
        world.Trade.SetStop(market, 1, stop);

        bool passedStop = false;
        for (int t = 0; t < 30_000 && stock.Piles.Sum(p => p.Count) < 3; t++)
        {
            world.Step();
            passedStop |= donkey.Position == stop;
            Assert.True(world.Trade.CargoOf(donkey).Count <= 2);
        }

        Assert.Equal(3, stock.Piles.Where(p => p.Material == Material.Plank).Sum(p => p.Count));
        Assert.True(passedStop, "the donkey went round by the stop");
        Assert.Equal(0, world.Trade.Remaining(market, Material.Plank));
        Assert.Equal(3, world.Trade.Delivered);
        // Only what was asked for: two planks and all stones still lie where they were.
        Assert.Equal(2, world.Piles.Where(p => p.Role == PileRole.Loose && p.Material == Material.Plank).Sum(p => p.Count));
        Assert.Equal(4, world.Piles.Where(p => p.Material == Material.Stone).Sum(p => p.Count));

        // ∞: everything there is goes – as in the original also what already reached the destination, so it goes round and round.
        world.Trade.SetRemaining(market, Material.Stone, Trade.Unlimited);
        for (int t = 0; t < 30_000 && world.Trade.Delivered < 7; t++)
            world.Step();
        Assert.Equal(7, world.Trade.Delivered);
        Assert.Equal(Trade.Unlimited, world.Trade.Remaining(market, Material.Stone));
        for (int t = 0; t < 8000; t++)
            world.Step();
        Assert.True(world.Trade.Delivered > 7);
        Assert.Equal(4, world.Piles.Where(p => p.Material == Material.Stone).Sum(p => p.Count)
                        + world.Settlers.Count(s => s.Carrying == Material.Stone && s.Profession != Profession.Donkey)
                        + world.Trade.CargoOf(donkey).Count(m => m == Material.Stone));
    }

    [Fact]
    public void An_enemy_strikes_a_loaded_donkey_once_it_drops_its_load_and_goes_home_unharmed()
    {
        var map = new GameMap(90);
        var world = new World(map, StoreNothing);
        var market = world.PlaceBuilding(Romans["market_place"], 0, new GridPoint(20, 20))!;
        var stock = world.PlaceBuilding(Romans["stock"], 0, new GridPoint(65, 65))!;
        world.AddLooseGoods(new GridPoint(12, 34), Material.Plank, 2);
        for (int i = 0; i < 2; i++)
            world.Spawn(new GridPoint(26 + i, 30), Profession.Carrier);
        var donkey = world.Spawn(new GridPoint(30, 12), Profession.Donkey)!;
        world.Trade.SetRemaining(market, Material.Plank, 2);
        world.Trade.SetDestination(market, stock.Position);
        var enemy = world.SpawnSoldier(new GridPoint(48, 48), SoldierType.Swordsman, player: 1)!;

        for (int t = 0; t < 30_000 && world.Trade.Robbed == 0; t++)
            world.Step();

        Assert.Equal(1, world.Trade.Robbed);
        Assert.False(donkey.IsDead);
        Assert.Null(donkey.Carrying);
        Assert.Empty(world.Trade.CargoOf(donkey));
        Assert.Equal(2, world.Piles.Where(p => p.Role == PileRole.Loose && p.Material == Material.Plank
                                               && PathFinder.Distance(p.Position, donkey.Position) <= 3).Sum(p => p.Count));
        Assert.NotNull(world.Events.Latest(0, EventCategory.Trade));

        // Home again, never struck again on the way.
        for (int t = 0; t < 10_000 && world.Trade.DonkeysOf(market) > 0; t++)
            world.Step();
        Assert.Equal(0, world.Trade.DonkeysOf(market));
        Assert.Equal(1, world.Trade.Robbed);
        Assert.False(donkey.IsDead);
        Assert.True(PathFinder.Distance(donkey.Position, market.Door) <= 4);
        Assert.False(enemy.IsDead);
    }

    [Fact]
    public void Nobody_attacks_a_donkey_that_carries_nothing()
    {
        var world = new World(new GameMap(60), StoreNothing);
        var donkey = world.Spawn(new GridPoint(30, 30), Profession.Donkey)!;
        var enemy = world.SpawnSoldier(new GridPoint(33, 30), SoldierType.Swordsman, player: 1)!;
        var bowman = world.SpawnSoldier(new GridPoint(30, 37), SoldierType.Bowman, player: 1)!;

        for (int t = 0; t < 2_000; t++)
        {
            world.Step();
            Assert.Equal(Activity.None, enemy.Activity);
            Assert.Equal(Activity.None, bowman.Activity);
        }
        Assert.Equal(0, world.Trade.Robbed);
        Assert.Null(world.Events.Latest(0));
    }

    [Fact]
    public void A_donkey_on_its_way_survives_saving_and_loading()
    {
        var world = new World(new GameMap(90), StoreNothing);
        var market = world.PlaceBuilding(Romans["market_place"], 0, new GridPoint(20, 20))!;
        var stock = world.PlaceBuilding(Romans["stock"], 0, new GridPoint(65, 65))!;
        world.AddLooseGoods(new GridPoint(12, 34), Material.Plank, 2);
        world.Spawn(new GridPoint(26, 30), Profession.Carrier);
        var donkey = world.Spawn(new GridPoint(30, 12), Profession.Donkey)!;
        world.Trade.SetRemaining(market, Material.Plank, 2);
        world.Trade.SetDestination(market, stock.Position);
        world.Trade.SetStop(market, 0, new GridPoint(60, 20));
        for (int t = 0; t < 30_000 && donkey.Carrying is null; t++)
            world.Step();
        Assert.NotNull(donkey.Carrying);

        var stream = new MemoryStream();
        Snapshot.Write(stream, world);
        stream.Position = 0;
        var loaded = Snapshot.Read<World>(stream);
        var loadedStock = loaded.Buildings.Single(b => b.Id == stock.Id);
        for (int t = 0; t < 30_000 && loadedStock.Piles.Sum(p => p.Count) < 2; t++)
            loaded.Step();
        Assert.Equal(2, loadedStock.Piles.Where(p => p.Material == Material.Plank).Sum(p => p.Count));
        Assert.Equal(new GridPoint(60, 20), loaded.Trade.StopOf(loaded.Buildings.Single(b => b.Id == market.Id), 0));
    }
}
