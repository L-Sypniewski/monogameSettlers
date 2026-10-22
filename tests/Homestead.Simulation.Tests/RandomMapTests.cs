using Homestead.Assets.Maps;
using Homestead.Simulation.Economy;
using Homestead.Simulation.Map;
using Homestead.Simulation.Map.Generation;
using Homestead.Simulation.Pathfinding;
using Xunit.Abstractions;

namespace Homestead.Simulation.Tests;

public class RandomMapTests(ITestOutputHelper output)
{
    [Fact]
    public void The_same_settings_and_seed_make_the_same_map()
    {
        var settings = new RandomMapSettings(3, MapSymmetry.None, Abundance.Many);
        var a = RandomMapGenerator.Generate(settings, 42).Area;
        var b = RandomMapGenerator.Generate(settings, 42).Area;
        var c = RandomMapGenerator.Generate(settings, 43).Area;
        Assert.Equal(a.Landscape, b.Landscape);
        Assert.Equal(a.Height, b.Height);
        Assert.Equal(a.Object, b.Object);
        Assert.Equal(a.Resources, b.Resources);
        Assert.NotEqual(a.Landscape, c.Landscape);
    }

    [Theory]
    [InlineData(2, MapSymmetry.None)]
    [InlineData(3, MapSymmetry.Mirror)]
    [InlineData(4, MapSymmetry.Repeat)]
    [InlineData(5, MapSymmetry.Mirror)]
    [InlineData(6, MapSymmetry.None)]
    public void Every_player_starts_with_a_tower_on_land_that_reaches_the_others(int players, MapSymmetry symmetry)
    {
        var original = RandomMapGenerator.Generate(new RandomMapSettings(players, symmetry), 7);
        Assert.Equal(players, original.Players.Count);
        Assert.False(original.IsSinglePlayer);

        var (world, report) = OriginalScenario.Load(original);
        output.WriteLine($"{original.Name}: {original.Area.Size}², {report.Buildings} towers, {report.Piles} piles; skipped: {string.Join("; ", report.Skipped)}");
        Assert.Equal(players, world.Buildings.Count(b => b.Definition.Name == "tower"));

        // Every start reaches every other on foot.
        var map = world.Map!;
        var finder = new PathFinder(map);
        var starts = original.Players.Select(p => new GridPoint(p.StartX, p.StartY)).ToArray();
        for (int i = 1; i < starts.Length; i++)
            Assert.True(finder.FindPath(Walkable(map, starts[0]), Walkable(map, starts[i])) is not null, $"start 1 reaches start {i + 1}");
    }

    /// <summary>A walkable field next to the start (the tower stands on the start point).</summary>
    private static GridPoint Walkable(GameMap map, GridPoint start) =>
        Enumerable.Range(0, 400).Select(i => new GridPoint(start.X + i % 20 - 10, start.Y + i / 20 - 10))
            .Where(map.IsWalkable).OrderBy(p => PathFinder.Distance(p, start)).First();

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    public void Mirrored_maps_give_every_player_the_same_surroundings(int players)
    {
        var map = RandomMapGenerator.Generate(new RandomMapSettings(players, MapSymmetry.Mirror), 11);
        var surroundings = map.Players.Select(p => Surroundings(map.Area, p.StartX, p.StartY, 30)).ToList();
        foreach (var other in surroundings.Skip(1))
            Assert.Equal(surroundings[0], other);
    }

    [Fact]
    public void Repeated_maps_copy_the_start_tile()
    {
        var map = RandomMapGenerator.Generate(new RandomMapSettings(4, MapSymmetry.Repeat), 3);
        // The copied tile reaches homeRadius (36); water depth looks up to 8 fields further for land, so
        // near its edge the depth may differ with what lies outside the tile.
        var surroundings = map.Players.Select(p => Surroundings(map.Area, p.StartX, p.StartY, 36 - 8)).ToList();
        foreach (var other in surroundings.Skip(1))
            Assert.Equal(surroundings[0], other);
    }

    [Fact]
    public void Every_home_has_a_mountain_with_ore_stones_trees_and_a_river()
    {
        var original = RandomMapGenerator.Generate(new RandomMapSettings(6, MapSymmetry.None, Resources: Abundance.Few), 5);
        var area = original.Area;
        foreach (var p in original.Players)
        {
            int ore = 0, stones = 0, trees = 0, river = 0;
            foreach (int i in Around(area, p.StartX, p.StartY, 36))
            {
                if (area.Landscape[i] is 32 or 33 && area.Resources[i] >> 4 is >= 1 and <= 3)
                    ore++;
                byte code = area.Object[i];
                if (code is >= MapObjectCodes.FullStone and < MapObjectCodes.StoneRubble)
                    stones++;
                if (MapObjectCodes.IsAdultTree(code))
                    trees++;
                if (area.Landscape[i] is >= 96 and <= 99)
                    river++;
            }
            output.WriteLine($"start {p.StartX},{p.StartY}: ore {ore}, stones {stones}, trees {trees}, river {river}");
            Assert.True(ore >= 10 && stones >= 5 && trees >= 20 && river >= 4, $"home at {p.StartX},{p.StartY}");
        }
    }

    [Theory]
    [InlineData(2, MapSymmetry.None)]
    [InlineData(3, MapSymmetry.Mirror)]
    [InlineData(4, MapSymmetry.Repeat)]
    public void Many_treasures_put_a_hoard_of_gold_and_weapons_in_the_middle(int players, MapSymmetry symmetry)
    {
        var map = RandomMapGenerator.Generate(new RandomMapSettings(players, symmetry, Treasures: TreasureLevel.Many), 4);
        int middle = (map.Area.Size - 1) / 2;
        var hoard = map.Stacks.Where(s => PathFinder.Distance(new GridPoint(s.X, s.Y), new GridPoint(middle, middle)) <= 2).ToList();
        Assert.Equal(19, hoard.Count);
        Assert.All(hoard, s => Assert.Equal(8, s.Count));
        var goods = hoard.Select(s => Materials.FromOriginalStack(s.Type)).ToHashSet();
        Assert.Contains(Material.Gold, goods);
        Assert.Contains(Material.Sword, goods);

        var normal = RandomMapGenerator.Generate(new RandomMapSettings(players, symmetry), 4);
        Assert.DoesNotContain(normal.Stacks, s => PathFinder.Distance(new GridPoint(s.X, s.Y), new GridPoint(middle, middle)) <= 2 && s.Count == 8);
    }

    [Theory]
    [InlineData(2, MapSymmetry.None, 1)]
    [InlineData(5, MapSymmetry.None, 9)]
    [InlineData(6, MapSymmetry.Repeat, 3)]
    public void Free_maps_are_one_landmass(int players, MapSymmetry symmetry, int seed)
    {
        var area = RandomMapGenerator.Generate(new RandomMapSettings(players, symmetry), seed).Area;
        bool IsLand(int i) => area.Landscape[i] > 7;
        int first = Enumerable.Range(0, area.Size * area.Size).First(IsLand);
        var reached = new HashSet<int> { first };
        var queue = new Queue<int>([first]);
        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            int x = i % area.Size, y = i / area.Size;
            foreach (var (dx, dy) in new[] { (1, 0), (1, 1), (0, 1), (-1, 0), (-1, -1), (0, -1) })
                if ((uint)(x + dx) < (uint)area.Size && (uint)(y + dy) < (uint)area.Size
                    && IsLand(area.Index(x + dx, y + dy)) && reached.Add(area.Index(x + dx, y + dy)))
                    queue.Enqueue(area.Index(x + dx, y + dy));
        }
        Assert.Equal(Enumerable.Range(0, area.Size * area.Size).Count(IsLand), reached.Count);
    }

    [Fact]
    public void Random_map_names_parse_with_defaults()
    {
        Assert.Null(RandomMapSettings.ParseName("448-4-Utopia"));
        var (settings, seed) = RandomMapSettings.ParseName("random:4:mirror:17")!.Value;
        Assert.Equal(new RandomMapSettings(4, MapSymmetry.Mirror), settings);
        Assert.Equal(17, seed);
        Assert.Equal((new RandomMapSettings(), 5), RandomMapSettings.ParseName("random", defaultSeed: 5));
    }

    private static IEnumerable<int> Around(MapArea area, int x, int y, int radius)
    {
        for (int dy = -radius; dy <= radius; dy++)
        for (int dx = -radius; dx <= radius; dx++)
            if (PathFinder.Distance(new GridPoint(0, 0), new GridPoint(dx, dy)) <= radius
                && (uint)(x + dx) < (uint)area.Size && (uint)(y + dy) < (uint)area.Size)
                yield return area.Index(x + dx, y + dy);
    }

    /// <summary>What lies around a start, independent of direction: counts of landscape, objects and ore per type.</summary>
    private static string Surroundings(MapArea area, int x, int y, int radius)
    {
        var counts = new SortedDictionary<string, int>();
        foreach (int i in Around(area, x, y, radius))
        {
            void Add(string key) => counts[key] = counts.GetValueOrDefault(key) + 1;
            Add($"land {area.Landscape[i]}");
            Add($"object {area.Object[i]}");
            Add($"resource {area.Resources[i]}");
        }
        return string.Join(", ", counts.Select(c => $"{c.Key}: {c.Value}"));
    }
}
