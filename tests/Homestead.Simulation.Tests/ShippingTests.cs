using Homestead.Simulation.Buildings;
using Homestead.Simulation.Economy;
using Homestead.Simulation.Map;
using Homestead.Simulation.Pathfinding;
using Homestead.Simulation.Persistence;

namespace Homestead.Simulation.Tests;

public class ShippingTests
{
    private static readonly BuildingCatalog Romans = BuildingCatalog.Load(Civilization.Roman);

    /// <summary>Land up to x = 49, the sea from x = 50 on.</summary>
    private static World Coast(out Building dockyard)
    {
        var map = new GameMap(80);
        for (int y = 0; y < 80; y++)
        for (int x = 50; x < 80; x++)
            map.SetTerrain(new GridPoint(x, y), TerrainType.Water);
        var world = new World(map, TestConfig.NoTools);
        dockyard = world.PlaceBuilding(Romans["dockyard"], 0, new GridPoint(38, 40))!;
        return world;
    }

    [Fact]
    public void A_click_into_the_water_puts_the_dock_at_once_where_its_end_is_nearest()
    {
        var world = Coast(out var dockyard);
        Assert.Null(world.Shipping.DockOf(dockyard));
        Assert.Contains("no dock", world.StatusOf(dockyard));

        // Past the reach of a pier: the end as near as it gets (the rows are shifted, so south-east is nearer than east).
        Assert.True(world.Shipping.PlaceDock(dockyard, new GridPoint(60, 42)));
        var dock = world.Shipping.DockOf(dockyard)!.Value;
        Assert.Equal(new GridPoint(49, 34), dock.Land);
        Assert.Equal(Direction.SouthEast, dock.Direction);
        Assert.Equal([new(50, 35), new(51, 36), new(52, 37), new(53, 38)], dock.Pier);
        // Straight out from a shore field, over open water.
        Assert.True(world.Shipping.PlaceDock(dockyard, new GridPoint(53, 42)));
        dock = world.Shipping.DockOf(dockyard)!.Value;
        Assert.Equal(new GridPoint(53, 42), dock.End);
        Assert.Equal(49, dock.Land.X);
        Assert.All(dock.Pier, p => Assert.Equal(MapObjectCodes.Dock, world.Map!.ObjectCodeAt(p)));
        Assert.DoesNotContain("no dock", world.StatusOf(dockyard));

        // Set again elsewhere: the old pier goes.
        Assert.True(world.Shipping.PlaceDock(dockyard, new GridPoint(55, 30)));
        var moved = world.Shipping.DockOf(dockyard)!.Value;
        Assert.NotEqual(dock.Land, moved.Land);
        Assert.All(dock.Pier.Except(moved.Pier), p => Assert.Equal(MapObjectCodes.None, world.Map!.ObjectCodeAt(p)));

        // It goes with the shipyard.
        world.RemoveBuilding(dockyard);
        Assert.All(moved.Pier, p => Assert.Equal(MapObjectCodes.None, world.Map!.ObjectCodeAt(p)));
        Assert.Null(world.Shipping.DockOf(dockyard));
    }

    [Fact]
    public void No_dock_into_a_narrow_river_nor_far_from_the_shipyard()
    {
        var map = new GameMap(80);
        for (int y = 0; y < 80; y++)
        for (int x = 50; x < 53; x++)
            map.SetTerrain(new GridPoint(x, y), TerrainType.Water);
        var world = new World(map, TestConfig.NoTools);
        var dockyard = world.PlaceBuilding(Romans["dockyard"], 0, new GridPoint(38, 40))!;
        Assert.False(world.Shipping.PlaceDock(dockyard, new GridPoint(51, 40)));

        // The sea, but more than dockDistance away.
        var far = Coast(out var near);
        var distant = far.PlaceBuilding(Romans["dockyard"], 0, new GridPoint(12, 40))!;
        Assert.False(far.Shipping.PlaceDock(distant, new GridPoint(60, 40)));
        Assert.True(far.Shipping.PlaceDock(near, new GridPoint(60, 40)));
    }

    [Fact]
    public void The_dock_is_saved()
    {
        var world = Coast(out var dockyard);
        world.Shipping.PlaceDock(dockyard, new GridPoint(53, 42));

        var stream = new MemoryStream();
        Snapshot.Write(stream, world);
        stream.Position = 0;
        var loaded = Snapshot.Read<World>(stream);
        Assert.Equal(world.Shipping.DockOf(dockyard), loaded.Shipping.DockOf(loaded.Buildings.Single(b => b.Id == dockyard.Id)));
        Assert.Equal(MapObjectCodes.Dock, loaded.Map!.ObjectCodeAt(new GridPoint(53, 42)));
        // The pier can be walked on.
        Assert.True(loaded.Map.IsWalkable(new GridPoint(53, 42)));

        // A ship on its way sails on after loading, a ship on the stocks keeps what is built in.
        world.Shipping.Launch(dockyard, ShipKind.Ferry);
        world.Shipping.Order(dockyard, ShipKind.TradeShip);
        var ship = world.Shipping.Ships[0];
        world.Shipping.Move([ship], new GridPoint(70, 20));
        for (int t = 0; t < 30; t++)
            world.Step();
        stream = new MemoryStream();
        Snapshot.Write(stream, world);
        stream.Position = 0;
        loaded = Snapshot.Read<World>(stream);
        var sailing = Assert.Single(loaded.Shipping.Ships);
        Assert.Equal(ship.Position, sailing.Position);
        Assert.Equal(ShipKind.TradeShip, loaded.Shipping.Building(loaded.Buildings.Single(b => b.Id == dockyard.Id)));
        for (int t = 0; t < 2000 && sailing.IsMoving; t++)
            loaded.Step();
        Assert.Equal(new GridPoint(70, 20), sailing.Position);
    }

    [Fact]
    public void An_ordered_ship_is_built_unit_by_unit_on_the_pier_and_then_sails_where_it_is_sent()
    {
        var world = Coast(out var dockyard);
        world.AddLooseGoods(new GridPoint(30, 50), Material.Plank, 8);
        world.AddLooseGoods(new GridPoint(31, 52), Material.Iron, 4);
        for (int i = 0; i < 4; i++)
            world.Spawn(new GridPoint(25 + i, 45), Profession.Carrier);
        Assert.False(world.Shipping.Order(dockyard, ShipKind.Ferry)); // no dock yet
        Assert.True(world.Shipping.PlaceDock(dockyard, new GridPoint(53, 42)));
        var dock = world.Shipping.DockOf(dockyard)!.Value;

        // Without an order nobody brings anything.
        for (int t = 0; t < 1500; t++)
            world.Step();
        Assert.Equal(0, dockyard.Piles.Sum(p => p.Count));
        Assert.Contains("no ship ordered", world.StatusOf(dockyard));

        Assert.True(world.Shipping.Order(dockyard, ShipKind.Ferry));
        Assert.False(world.Shipping.Order(dockyard, ShipKind.TradeShip)); // one at a time
        Assert.False(world.Shipping.PlaceDock(dockyard, new GridPoint(55, 30))); // the dock stays while a ship is built
        var cost = world.Shipping.CostOf(ShipKind.Ferry);
        double last = 0;
        bool onPier = false;
        for (int t = 0; t < 40_000 && world.Shipping.Ships.Count == 0; t++)
        {
            world.Step();
            var worker = world.Production.WorkerOf(dockyard);
            onPier |= worker is { IsInside: false } && dock.Pier.Contains(worker.Position) && worker.Activity == Activity.Building;
            if (world.Shipping.Progress(dockyard) is { } progress)
            {
                Assert.True(progress >= last);
                last = progress;
            }
        }
        Assert.True(onPier);
        var ship = Assert.Single(world.Shipping.Ships);
        Assert.Equal(ShipKind.Ferry, ship.Kind);
        Assert.Equal(dock.Berth, ship.Position);
        Assert.Null(world.Shipping.Building(dockyard));
        // Exactly the cost went in: the rest is still lying around.
        Assert.Equal(8 - cost[Material.Plank], world.Piles.Where(p => p.Material == Material.Plank).Sum(p => p.Count));
        Assert.Equal(4 - cost[Material.Iron], world.Piles.Where(p => p.Material == Material.Iron).Sum(p => p.Count));

        // On the water only: a click on land sends it to the nearest open water.
        Assert.Equal(1, world.Shipping.Move([ship], new GridPoint(70, 20)));
        for (int t = 0; t < 2000 && ship.IsMoving; t++)
            world.Step();
        Assert.Equal(new GridPoint(70, 20), ship.Position);
        world.Shipping.Move([ship], new GridPoint(20, 20));
        for (int t = 0; t < 2000 && ship.IsMoving; t++)
            world.Step();
        Assert.Equal(50 + Shipping.ShoreClearance, ship.Position.X);
        Assert.True(world.Shipping.IsNavigable(ship.Position));

        // A second ship keeps its distance: sent to the same point, they lie side by side.
        Assert.True(world.Shipping.Launch(dockyard, ShipKind.TradeShip));
        var second = world.Shipping.Ships[1];
        Assert.True(PathFinder.Distance(ship.Position, second.Position) >= Shipping.Spacing);
        Assert.Equal(2, world.Shipping.Move([ship, second], new GridPoint(65, 60)));
        int closest = int.MaxValue;
        for (int t = 0; t < 3000 && (ship.IsMoving || second.IsMoving); t++)
        {
            world.Step();
            closest = Math.Min(closest, PathFinder.Distance(ship.Position, second.Position));
        }
        Assert.False(ship.IsMoving || second.IsMoving);
        Assert.True(closest >= Shipping.Spacing - 1);
        Assert.Contains(new GridPoint(65, 60), new[] { ship.Position, second.Position });
    }
}
