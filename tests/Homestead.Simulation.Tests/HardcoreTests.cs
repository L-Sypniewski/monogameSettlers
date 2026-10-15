using Homestead.Simulation.Buildings;
using Homestead.Simulation.Economy;
using Homestead.Simulation.Map;
using Homestead.Simulation.Military;
using Homestead.Simulation.Pathfinding;

namespace Homestead.Simulation.Tests;

public class HardcoreTests
{
    private static readonly BuildingCatalog Romans = BuildingCatalog.Load(Civilization.Roman);

    [Fact]
    public void Hardcore_adds_a_flat_tenth_to_the_combat_strength()
    {
        var map = new GameMap(60);
        var world = new World(map, TestConfig.NoTools) { PlayerCount = 2, Peoples = [Civilization.Roman, Civilization.Roman] };
        world.HardcorePlayers.Add(1);
        for (int y = 0; y < 30; y++)
            for (int x = 0; x < 60; x++)
                map.SetOwner(new GridPoint(x, y), y < 15 ? 0 : 1);

        Assert.Equal(0.5f, world.Military.CombatStrength(0));
        Assert.Equal(0.6f, world.Military.CombatStrength(1), 4);
        Assert.Equal(1.1f, world.Military.CombatStrengthAt(1, new GridPoint(20, 20)), 4); // own land
        Assert.Equal(0.6f, world.Military.CombatStrengthAt(1, new GridPoint(20, 5)), 4);  // enemy land
    }

    [Fact]
    public void Hardcore_houses_release_thirty_percent_more_residents()
    {
        var world = new World(new GameMap(90));
        world.HardcorePlayers.Add(1);
        var house = world.PlaceBuilding(Romans["small_livinghouse"], 1, new GridPoint(45, 45))!;

        Assert.Equal(13, world.Housing.ResidentsLeft(house));
        for (int i = 0; i < World.TicksPerSecond * 25; i++)
            world.Step();
        Assert.Equal(13, world.Settlers.Count);
        Assert.Equal(13, world.Housing.RoomOf(house));
    }

    [Fact]
    public void Hardcore_soldiers_have_a_fifth_more_hit_points()
    {
        var world = new World(new GameMap(40));
        world.HardcorePlayers.Add(1);
        var normal = world.SpawnSoldier(new GridPoint(10, 10), SoldierType.Swordsman, 2, player: 0)!;
        var hard = world.SpawnSoldier(new GridPoint(20, 20), SoldierType.Swordsman, 2, player: 1)!;

        Assert.Equal(world.Military.Config.MaxHealth(SoldierType.Swordsman, 2), normal.Health);
        Assert.Equal(normal.Health * 1.2f, hard.Health, 3);
    }

    [Fact]
    public void Hardcore_construction_sites_ask_less_material()
    {
        var world = new World(new GameMap(90));
        world.HardcorePlayers.Add(1);
        var definition = Romans["castle"];
        var normal = world.PlaceConstructionSite(definition, 0, new GridPoint(30, 30))!;
        var hard = world.PlaceConstructionSite(definition, 1, new GridPoint(60, 60))!;

        int full = definition.ConstructionPiles.Where(p => p.Material is not null).Sum(p => p.Required);
        Assert.Equal(full, normal.BuildStepsTotal);
        Assert.InRange(hard.BuildStepsTotal, full * Hardcore.CostFactor - 2, full * Hardcore.CostFactor + 2);
        Assert.Equal(hard.BuildStepsTotal, hard.Piles.Where(p => p.Role == PileRole.Request).Sum(p => p.Capacity));
    }

    [Fact]
    public void Hardcore_players_start_with_two_more_swordsmen_and_planks_and_trunks()
    {
        var world = new World(new GameMap(140));
        world.HardcorePlayers.Add(1);
        var setup = StartSetup.Load();
        Assert.NotNull(setup.Apply(world, Romans, 0, new GridPoint(35, 70), 1));
        Assert.NotNull(setup.Apply(world, Romans, 1, new GridPoint(105, 70), 1));

        int Soldiers(int player) => world.Settlers.Count(s => s.Player == player && s.Profession == Profession.Soldier);
        int Goods(int player, Material material) => world.Piles
            .Where(p => p.Material == material && PathFinder.Distance(p.Position, new GridPoint(player == 0 ? 35 : 105, 70)) <= 20)
            .Sum(p => p.Count);
        Assert.Equal(Soldiers(0) + Hardcore.StartSoldiers, Soldiers(1));
        Assert.Equal(Goods(0, Material.Plank) + 5, Goods(1, Material.Plank));
        Assert.Equal(Goods(0, Material.Trunk) + 5, Goods(1, Material.Trunk));
    }

    [Theory]
    [InlineData(10, 9)]
    [InlineData(100, 90)]
    [InlineData(1, 1)]
    [InlineData(0, 0)]
    public void Work_times_are_a_tenth_shorter(int ticks, int expected) =>
        Assert.Equal(expected, Hardcore.WorkTicks(ticks));

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 2)]
    [InlineData(4, 3)]
    [InlineData(8, 6)]
    public void Construction_cost_is_rounded_and_never_zero(int required, int expected) =>
        Assert.Equal(expected, Hardcore.Cost(required));
}
