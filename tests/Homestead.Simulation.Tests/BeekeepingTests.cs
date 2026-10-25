using Homestead.Simulation.Buildings;
using Homestead.Simulation.Economy;
using Homestead.Simulation.Map;
using Xunit.Abstractions;

namespace Homestead.Simulation.Tests;

public class BeekeepingTests(ITestOutputHelper output)
{
    private static readonly BuildingCatalog Amazons = BuildingCatalog.Load(Civilization.Amazon);

    [Fact]
    public void A_beekeeper_keeps_up_to_twelve_hives_apart_near_the_trees()
    {
        var world = Beekeeping(Forest.Around, out var map, out _);

        Run(world, 600 * World.TicksPerSecond);

        var hives = Hives(map);
        output.WriteLine($"{hives.Count} hives: {string.Join(" ", hives)}");
        Assert.Equal(12, hives.Count);
        // Five steps apart at least.
        Assert.All(hives, h => Assert.DoesNotContain(hives, o => o != h && Pathfinding.PathFinder.Distance(o, h) < 5));
        // Every hive has the ideal ten trees close by.
        Assert.All(hives, h => Assert.True(TreesAround(map, h) >= 10, $"hive at {h}"));
    }

    [Fact]
    public void Ten_trees_around_the_hives_bring_three_honey_a_minute()
    {
        var world = Beekeeping(Forest.Around, out _, out _);
        // Twelve hives set up and full once, then measured over ten minutes (before the storehouse is full).
        Run(world, 480 * World.TicksPerSecond);
        int before = world.Production.CropHarvested;
        Run(world, 600 * World.TicksPerSecond);
        double perMinute = (world.Production.CropHarvested - before) / 10.0;
        output.WriteLine($"honey/min {perMinute:F2}");

        Assert.InRange(perMinute, 2.5, 3.2);
    }

    [Fact]
    public void Without_trees_no_hive_goes_up()
    {
        var world = Beekeeping(Forest.None, out var map, out _);

        Run(world, 300 * World.TicksPerSecond);

        Assert.Empty(Hives(map));
    }

    [Fact]
    public void Hives_whose_trees_are_gone_are_taken_home_and_go_up_at_the_trees_left()
    {
        var world = Beekeeping(Forest.East, out var map, out _);
        Run(world, 300 * World.TicksPerSecond);
        Assert.NotEmpty(Hives(map));

        // The grove east of the house is felled, a new one grows west of it.
        for (int i = 0; i < map.PointCount; i++)
            if (MapObjectCodes.IsAdultTree(map.ObjectCodeAt(map.Point(i))))
                map.SetObjectCode(map.Point(i), MapObjectCodes.None);
        Grove(map, 30, 39, 34, 60);
        Run(world, 600 * World.TicksPerSecond);

        var hives = Hives(map);
        Assert.NotEmpty(hives);
        Assert.All(hives, h => Assert.True(TreesAround(map, h) > 0, $"hive without trees at {h}"));
    }

    [Fact]
    public void Hives_go_with_their_beekeeping()
    {
        var world = Beekeeping(Forest.East, out var map, out var beekeeping);
        Run(world, 300 * World.TicksPerSecond);
        Assert.NotEmpty(Hives(map));

        world.RemoveBuilding(beekeeping);

        Assert.Empty(Hives(map));
    }

    private enum Forest { None, East, Around }

    /// <summary>
    /// A beekeeping at 44,46 with a storehouse and carriers; trees (on every other field) east of the house or all around it
    /// (the house and the way out free).
    /// </summary>
    private static World Beekeeping(Forest forest, out GameMap map, out Building beekeeping)
    {
        map = new GameMap(100);
        if (forest == Forest.East)
            Grove(map, 50, 62, 34, 60);
        if (forest == Forest.Around)
        {
            Grove(map, 28, 62, 28, 64);
            for (int y = 38; y < 64; y++)
            for (int x = 37; x < 52; x++)
                map.SetObjectCode(new GridPoint(x, y), MapObjectCodes.None);
        }
        var world = new World(map, TestConfig.NoTools);
        beekeeping = world.PlaceBuilding(Amazons["beekeeping"], 0, new GridPoint(44, 46))!;
        Assert.NotNull(beekeeping);
        Assert.NotNull(world.PlaceBuilding(Amazons["stock"], 0, new GridPoint(30, 70)));
        for (int i = 0; i < 4; i++)
            world.Spawn(new GridPoint(40 + i, 66), Profession.Carrier);
        return world;
    }

    private static void Grove(GameMap map, int fromX, int toX, int fromY, int toY)
    {
        for (int y = fromY; y < toY; y++)
        for (int x = fromX; x < toX; x++)
            if ((x + y) % 2 == 0)
                map.SetObjectCode(new GridPoint(x, y), MapObjectCodes.FirstTree);
    }

    private static int TreesAround(GameMap map, GridPoint p) =>
        Pathfinding.PathFinder.Circle(p, 5).Count(t => map.Contains(t) && MapObjectCodes.IsAdultTree(map.ObjectCodeAt(t)));

    private static List<GridPoint> Hives(GameMap map) => Enumerable.Range(0, map.PointCount).Select(map.Point)
        .Where(p => map.ObjectCodeAt(p) is >= MapObjectCodes.FirstHive and <= MapObjectCodes.FullHive).ToList();

    private static void Run(World world, int ticks)
    {
        for (int t = 0; t < ticks; t++)
            world.Step();
    }
}
