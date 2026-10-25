using Homestead.Simulation.Buildings;
using Homestead.Simulation.Economy;
using Homestead.Simulation.Map;
using Xunit.Abstractions;

namespace Homestead.Simulation.Tests;

public class ConstructionTests(ITestOutputHelper output)
{
    private static readonly BuildingCatalog Romans = BuildingCatalog.Load(Civilization.Roman);

    [Fact]
    public void Site_on_flat_ground_is_built_from_delivered_material()
    {
        var world = new World(new GameMap(60));
        var site = world.PlaceConstructionSite(Romans["lumberjack"], 0, new GridPoint(30, 30))!;
        Assert.Equal(BuildingState.Constructing, site.State); // nothing to level
        world.AddLooseGoods(new GridPoint(10, 10), Material.Plank, 5);
        world.AddLooseGoods(new GridPoint(12, 10), Material.Stone, 5);
        Spawn(world, Profession.Carrier, 3, new GridPoint(15, 15));
        Spawn(world, Profession.Builder, 2, new GridPoint(20, 40));

        Run(world, 10_000, stopWhen: () => site.State == BuildingState.Finished);

        Assert.Equal(BuildingState.Finished, site.State);
        Assert.Equal(1f, site.ConstructionProgress);
        // Used exactly what the definition requires, the rest is still lying around.
        Assert.Equal(3, LooseCount(world, Material.Plank));
        Assert.Equal(3, LooseCount(world, Material.Stone));
        Assert.Contains(site.Piles, p => p.Role == PileRole.Offer && p.Material == Material.Trunk);
        Assert.Equal(0, world.Construction.Assigned);
    }

    [Fact]
    public void Goods_lying_on_a_pile_spot_of_a_new_building_move_aside()
    {
        // 640-16-Deepforest: the first stonecutter's offer pile came on a Roman's start saws, they were gone and no sawmill ever worked.
        var world = new World(new GameMap(60));
        var definition = Romans["lumberjack"];
        var spot = new GridPoint(30, 30) + definition.OfferPiles.First().Offset;
        Assert.NotNull(world.AddLooseGoods(spot, Material.Saw, 2));
        var site = world.PlaceConstructionSite(definition, 0, new GridPoint(30, 30))!;
        world.AddLooseGoods(new GridPoint(10, 10), Material.Plank, 5);
        world.AddLooseGoods(new GridPoint(12, 10), Material.Stone, 5);
        Spawn(world, Profession.Carrier, 3, new GridPoint(15, 15));
        Spawn(world, Profession.Builder, 2, new GridPoint(20, 40));

        Run(world, 10_000, stopWhen: () => site.State == BuildingState.Finished);

        Assert.Equal(BuildingState.Finished, site.State);
        Assert.Contains(site.Piles, p => p.Role == PileRole.Offer && p.Position == spot);
        Assert.Equal(2, LooseCount(world, Material.Saw));
    }

    [Fact]
    public void Builders_without_material_are_free_and_come_back_when_it_arrives()
    {
        var world = new World(new GameMap(60));
        var site = world.PlaceConstructionSite(Romans["lumberjack"], 0, new GridPoint(30, 30))!;
        world.AddLooseGoods(new GridPoint(10, 10), Material.Plank, 1);
        Spawn(world, Profession.Carrier, 1, new GridPoint(15, 15));
        Spawn(world, Profession.Builder, 1, new GridPoint(20, 40));
        var builder = world.Settlers.Single(s => s.Profession == Profession.Builder);

        Run(world, 10_000, stopWhen: () => site.BuildStepsDone == 1 && world.Construction.Assigned == 0);

        Assert.Equal(1, site.BuildStepsDone);
        Assert.Equal(BuildingState.Constructing, site.State);
        Assert.Equal(0, world.Construction.Assigned);
        Assert.False(builder.HasTask, "the builder is idle, free for other sites");
        Run(world, 100, stopWhen: () => builder.Destination is null && builder.State == SettlerState.Idle);
        Assert.DoesNotContain(site.Definition.BuilderSpots, spot => site.Position + spot.Offset == builder.Position);

        // Idle means it can be pushed aside: a carrier walking onto its field gets there.
        var carrier = world.Settlers.Single(s => s.Profession == Profession.Carrier);
        var field = builder.Position;
        Assert.True(world.WalkTo(carrier, field));
        Run(world, 2_000, stopWhen: () => carrier.Position == field);
        Assert.Equal(field, carrier.Position);

        world.AddLooseGoods(new GridPoint(10, 10), Material.Plank, 1);
        world.AddLooseGoods(new GridPoint(12, 10), Material.Stone, 2);
        Run(world, 10_000, stopWhen: () => site.State == BuildingState.Finished);

        Assert.Equal(BuildingState.Finished, site.State);
    }

    [Fact]
    public void Diggers_level_uneven_ground_first()
    {
        var map = new GameMap(60);
        var random = new Random(5);
        for (int i = 0; i < map.PointCount; i++)
            map.SetHeight(map.Point(i), 20 + random.Next(0, 4));
        var world = new World(map);
        var site = world.PlaceConstructionSite(Romans["forester"], 0, new GridPoint(30, 30))!;
        Assert.Equal(BuildingState.Flattening, site.State);
        Assert.True(map.IsWalkable(new GridPoint(30, 30)), "Footprint stays walkable while levelling.");
        Spawn(world, Profession.Digger, 3, new GridPoint(20, 20));

        Run(world, 10_000, stopWhen: () => site.State != BuildingState.Flattening);

        Assert.Equal(BuildingState.Constructing, site.State);
        Assert.All(site.BlockedPoints, p =>
        {
            Assert.Equal(site.TargetHeight, map.HeightAt(p));
            Assert.Equal(TerrainType.Flattened, map.TerrainAt(p));
            Assert.False(map.IsWalkable(p));
        });
    }

    [Fact]
    public void Levelling_a_hillside_cuts_into_the_slope_around_the_site_from_the_top_down()
    {
        const int Slope = 3;
        var map = new GameMap(60);
        for (int i = 0; i < map.PointCount; i++)
            map.SetHeight(map.Point(i), 20 + Slope * map.Point(i).X);
        var before = Enumerable.Range(0, map.PointCount).Select(i => map.HeightAt(map.Point(i))).ToArray();
        var world = new World(map);
        int maxSlope = world.Production.Config.LevellingMaxSlope;
        var site = world.PlaceConstructionSite(Romans["forester"], 0, new GridPoint(30, 30))!;
        Assert.Equal(BuildingState.Flattening, site.State);
        Spawn(world, Profession.Digger, 3, new GridPoint(20, 20));

        int steepest = 0;
        Run(world, 20_000, stopWhen: () =>
        {
            steepest = Math.Max(steepest, SteepestSlope(map));
            return site.State != BuildingState.Flattening;
        });

        Assert.Equal(BuildingState.Constructing, site.State);
        Assert.All(site.BlockedPoints, p => Assert.Equal(site.TargetHeight, map.HeightAt(p)));
        var footprint = site.BlockedPoints.ToHashSet();
        Assert.Contains(Enumerable.Range(0, map.PointCount),
            i => !footprint.Contains(map.Point(i)) && map.HeightAt(map.Point(i)) != before[i]);
        Assert.InRange(steepest, 0, maxSlope);
    }

    [Fact]
    public void A_site_needing_too_much_levelling_cannot_be_placed()
    {
        var map = new GameMap(60);
        for (int i = 0; i < map.PointCount; i++)
            map.SetHeight(map.Point(i), 4 * map.Point(i).X);
        var world = new World(map);

        Assert.False(world.CanPlace(Romans["castle"], new GridPoint(30, 30), out string reason));
        Assert.Equal("too steep to level", reason);
        Assert.Equal(1f, world.LevellingEffort(Romans["castle"], new GridPoint(30, 30)));
        Assert.True(world.CanPlace(Romans["tower"], new GridPoint(30, 30), out _), "a small building fits");
    }

    [Fact]
    public void Mines_stand_on_mountains_only_and_are_not_levelled()
    {
        var map = new GameMap(60);
        var world = new World(map);
        Assert.False(world.CanPlace(Romans["coalmine"], new GridPoint(30, 30), out string reason));
        Assert.Equal("cannot build on Grass", reason);

        // A steep mountain: far too much levelling for any other building.
        for (int i = 0; i < map.PointCount; i++)
        {
            map.SetTerrain(map.Point(i), TerrainType.Mountain);
            map.SetHeight(map.Point(i), 4 * map.Point(i).X);
        }
        Assert.Equal(0f, world.LevellingEffort(Romans["coalmine"], new GridPoint(30, 30)));
        var site = world.PlaceConstructionSite(Romans["coalmine"], 0, new GridPoint(30, 30))!;

        Assert.Equal(BuildingState.Constructing, site.State); // nothing to level
        foreach (var p in site.BlockedPoints)
        {
            Assert.Equal(4 * p.X, map.HeightAt(p));
            Assert.Equal(TerrainType.Mountain, map.TerrainAt(p));
        }
    }

    /// <summary>The greatest height difference between neighboring points of the map.</summary>
    private static int SteepestSlope(GameMap map)
    {
        int steepest = 0;
        for (int i = 0; i < map.PointCount; i++)
        {
            var p = map.Point(i);
            for (int d = 0; d < DirectionExtensions.Count; d++)
                if (p.Neighbor((Direction)d) is var n && map.Contains(n))
                    steepest = Math.Max(steepest, Math.Abs(map.HeightAt(n) - map.HeightAt(p)));
        }
        return steepest;
    }

    [Fact]
    public void Storehouse_supplies_construction_sites()
    {
        var world = new World(new GameMap(80), TestConfig.StoreAll);
        var stock = world.PlaceBuilding(Romans["stock"], 0, new GridPoint(20, 20))!;
        world.AddLooseGoods(new GridPoint(10, 40), Material.Plank, 6);
        world.AddLooseGoods(new GridPoint(12, 40), Material.Stone, 6);
        Spawn(world, Profession.Carrier, 4, new GridPoint(15, 30));

        // With nothing else to do, the carriers store everything.
        Run(world, 10_000, stopWhen: () => LooseCount(world, Material.Plank) + LooseCount(world, Material.Stone) == 0
                                           && world.Logistics.Jobs.Count == 0);
        Assert.Equal(6, stock.Piles.Where(p => p.Material == Material.Plank).Sum(p => p.Count));

        var site = world.PlaceConstructionSite(Romans["sawmill"], 0, new GridPoint(55, 55))!;
        Spawn(world, Profession.Builder, 3, new GridPoint(50, 40));
        Run(world, 20_000, stopWhen: () => site.State == BuildingState.Finished);

        Assert.Equal(BuildingState.Finished, site.State);
        Assert.Equal(6 - 3, stock.Piles.Where(p => p.Material == Material.Plank).Sum(p => p.Count));
        Assert.Equal(6 - 4, stock.Piles.Where(p => p.Material == Material.Stone).Sum(p => p.Count));
    }

    [Fact]
    public void Removing_a_site_releases_workers_and_keeps_the_material()
    {
        var world = new World(new GameMap(60));
        var site = world.PlaceConstructionSite(Romans["sawmill"], 0, new GridPoint(30, 30))!;
        world.AddLooseGoods(new GridPoint(10, 10), Material.Plank, 8);
        Spawn(world, Profession.Carrier, 3, new GridPoint(15, 15));
        Spawn(world, Profession.Builder, 3, new GridPoint(20, 40));

        Run(world, 10_000, stopWhen: () => site.BuildStepsDone >= 1);
        int used = site.BuildStepsDone + (world.Construction.Assigned > 0 ? 1 : 0); // a cycle may be running
        world.RemoveBuilding(site);
        Run(world, 2000);

        Assert.Equal(0, world.Construction.Assigned);
        Assert.Empty(world.Logistics.Jobs);
        Assert.All(world.Settlers, s => Assert.Null(s.Carrying));
        int planksLeft = world.Piles.Where(p => p.Material == Material.Plank).Sum(p => p.Count);
        Assert.InRange(planksLeft, 8 - used, 8 - site.BuildStepsDone);
        Assert.True(new GridPoint(30, 30) is var anchor && world.Map!.IsWalkable(anchor), "Footprint is free again.");
    }

    [Fact]
    public void The_dust_cloud_of_a_torn_down_building_goes_away()
    {
        var world = new World(new GameMap(60));
        var building = world.PlaceBuilding(Romans["lumberjack"], 0, new GridPoint(30, 30), force: true)!;

        world.RemoveBuilding(building);
        Assert.Single(world.DustClouds);
        Run(world, World.DustCloudTicks);

        Assert.Empty(world.DustClouds);
    }

    [Fact]
    public void Many_sites_all_get_finished()
    {
        var random = new Random(9);
        var map = new GameMap(120);
        for (int i = 0; i < map.PointCount; i++)
            map.SetHeight(map.Point(i), 30 + random.Next(0, 3));
        var world = new World(map);
        world.PlaceBuilding(Romans["stock"], 0, new GridPoint(60, 60));
        for (int i = 0; i < 12; i++)
        {
            world.AddLooseGoods(new GridPoint(50 + i, 75), Material.Plank, 8);
            world.AddLooseGoods(new GridPoint(50 + i, 77), Material.Stone, 8);
        }
        var names = new[] { "lumberjack", "forester", "stonecutter", "sawmill", "small_livinghouse", "fisher" };
        var sites = new List<Building>();
        for (int attempt = 0; attempt < 400 && sites.Count < 10; attempt++)
        {
            var at = new GridPoint(random.Next(10, 110), random.Next(10, 110));
            if (world.PlaceConstructionSite(Romans[names[random.Next(names.Length)]], 0, at) is { } site)
                sites.Add(site);
        }
        Spawn(world, Profession.Carrier, 15, new GridPoint(60, 70));
        Spawn(world, Profession.Digger, 4, new GridPoint(55, 70));
        Spawn(world, Profession.Builder, 5, new GridPoint(65, 70));

        int ticks = Run(world, 60_000, stopWhen: () => sites.All(s => s.State == BuildingState.Finished));

        output.WriteLine($"{sites.Count} sites finished after {ticks} ticks ({ticks / World.TicksPerSecond} s); " +
                         $"delivered {world.Logistics.Delivered}, dropped {world.Logistics.DroppedOnTheWay}, " +
                         $"pass-throughs {world.PassThroughs}; problems: " +
                         string.Join(", ", world.Logistics.Problems.Select(kv => $"{kv.Key} {kv.Value}x")));
        Assert.True(sites.Count >= 8);
        Assert.All(sites, s => Assert.Equal(BuildingState.Finished, s.State));
    }

    [Fact]
    public void A_site_with_the_highest_priority_gets_the_material_first()
    {
        var world = new World(new GameMap(70));
        var near = world.PlaceConstructionSite(Romans["lumberjack"], 0, new GridPoint(20, 30))!;
        var far = world.PlaceConstructionSite(Romans["lumberjack"], 0, new GridPoint(50, 30))!;
        world.SetPriority(far, true);
        world.AddLooseGoods(new GridPoint(10, 30), Material.Plank, 2);
        Spawn(world, Profession.Carrier, 4, new GridPoint(10, 40));

        Run(world, 10_000, stopWhen: () => Delivered(far, Material.Plank) == 2);

        Assert.Equal(2, Delivered(far, Material.Plank));
        Assert.Equal(0, Delivered(near, Material.Plank));
    }

    [Fact]
    public void Carriers_for_other_sites_put_down_only_as_much_as_the_priority_site_lacks()
    {
        var world = new World(new GameMap(70));
        var other = world.PlaceConstructionSite(Romans["lumberjack"], 0, new GridPoint(55, 30))!;
        world.AddLooseGoods(new GridPoint(10, 30), Material.Plank, 3);
        Spawn(world, Profession.Carrier, 4, new GridPoint(10, 40));
        // Both planks the other site needs are on their way there, the third still lies around.
        Run(world, 2_000, stopWhen: () => world.Logistics.Jobs.Count(j => j.Target.Owner == other && j.Phase == TransportPhase.ToTarget) == 2);
        Assert.Equal(2, world.Logistics.Jobs.Count(j => j.Target.Owner == other && j.Phase == TransportPhase.ToTarget));

        var urgent = world.PlaceConstructionSite(Romans["lumberjack"], 0, new GridPoint(30, 45))!;
        world.SetPriority(urgent, true);
        Run(world, 10_000, stopWhen: () => Delivered(urgent, Material.Plank) == 2 && world.Logistics.Jobs.Count == 0);

        Assert.Equal(2, Delivered(urgent, Material.Plank));
        Assert.Equal(1, Delivered(other, Material.Plank)); // one carrier put its plank down, the other went on
        Assert.Equal(1, world.Logistics.DroppedOnTheWay);
    }

    [Fact]
    public void A_stopped_site_gets_nothing_and_its_builders_leave_until_it_goes_on()
    {
        var world = new World(new GameMap(60));
        var site = world.PlaceConstructionSite(Romans["lumberjack"], 0, new GridPoint(30, 30))!;
        world.AddLooseGoods(new GridPoint(10, 10), Material.Plank, 1);
        Spawn(world, Profession.Carrier, 2, new GridPoint(15, 15));
        Spawn(world, Profession.Builder, 2, new GridPoint(20, 40));
        Run(world, 10_000, stopWhen: () => site.BuildStepsDone == 1);

        world.SetPaused(site, true);
        Assert.Contains("paused", world.StatusOf(site));
        world.AddLooseGoods(new GridPoint(10, 10), Material.Plank, 1);
        world.AddLooseGoods(new GridPoint(12, 10), Material.Stone, 2);
        Run(world, 3_000);
        Assert.Equal(1, site.BuildStepsDone);
        Assert.Equal(0, world.Construction.Assigned);
        Assert.Equal(1, LooseCount(world, Material.Plank));

        world.SetPaused(site, false);
        Run(world, 10_000, stopWhen: () => site.State == BuildingState.Finished);
        Assert.Equal(BuildingState.Finished, site.State);
    }

    /// <summary>Units of the material lying at the site (none built in: no builders).</summary>
    private static int Delivered(Building site, Material material) =>
        site.Piles.Where(p => p.Role == PileRole.Request && p.AcceptedMaterial == material).Sum(p => p.Count);

    private static void Spawn(World world, Profession profession, int count, GridPoint near)
    {
        for (int i = 0; i < count; i++)
            Assert.NotNull(world.Spawn(new GridPoint(near.X + i, near.Y), profession));
    }

    private static int LooseCount(World world, Material material) =>
        world.Piles.Where(p => p.Role == PileRole.Loose && p.Material == material).Sum(p => p.Count);

    private static int Run(World world, int ticks, Func<bool>? stopWhen = null)
    {
        for (int t = 1; t <= ticks; t++)
        {
            world.Step();
            if (stopWhen?.Invoke() == true)
                return t;
        }
        return ticks;
    }
}
