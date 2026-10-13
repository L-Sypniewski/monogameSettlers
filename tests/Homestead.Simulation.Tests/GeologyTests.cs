using Homestead.Simulation.Map;

namespace Homestead.Simulation.Tests;

public class GeologyTests
{
    [Fact]
    public void Geologists_sent_off_the_mountains_walk_there_and_spread_out()
    {
        var world = new World(new GameMap(60), TestConfig.NoTools);
        var geologists = Enumerable.Range(0, 3).Select(i => world.Spawn(new GridPoint(10 + i, 10), Profession.Geologist)!).ToList();
        var target = new GridPoint(40, 40);

        Assert.Equal(3, world.Geology.Send(geologists, target));
        Assert.All(geologists, g => Assert.False(world.Geology.IsSearching(g)));
        for (int t = 0; t < 2_000 && geologists.Any(g => g.HasTask); t++)
            world.Step();

        Assert.All(geologists, g => Assert.False(g.HasTask));
        Assert.All(geologists, g => Assert.True(Pathfinding.PathFinder.Distance(g.Position, target) <= 1));
        Assert.Equal(3, geologists.Select(g => g.Position).Distinct().Count());
        Assert.Empty(world.Geology.Signs);
    }

    [Fact]
    public void Geologists_sent_to_a_mountain_search_it()
    {
        var map = new GameMap(60);
        for (int y = 35; y <= 45; y++)
        for (int x = 35; x <= 45; x++)
            map.SetTerrain(new GridPoint(x, y), TerrainType.Mountain);
        var world = new World(map, TestConfig.NoTools);
        var geologist = world.Spawn(new GridPoint(10, 10), Profession.Geologist)!;

        Assert.Equal(1, world.Geology.Send([geologist], new GridPoint(40, 40)));
        Assert.True(world.Geology.IsSearching(geologist));
        for (int t = 0; t < 3_000 && world.Geology.Signs.Count == 0; t++)
            world.Step();

        Assert.NotEmpty(world.Geology.Signs);
    }
}
