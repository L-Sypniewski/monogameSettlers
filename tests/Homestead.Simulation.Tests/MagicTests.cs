using Homestead.Simulation.Buildings;
using Homestead.Simulation.Economy;
using Homestead.Simulation.Magic;
using Homestead.Simulation.Map;
using Homestead.Simulation.Pathfinding;

namespace Homestead.Simulation.Tests;

public class MagicTests
{
    private static readonly BuildingCatalog Romans = BuildingCatalog.Load(Civilization.Roman);
    private static readonly BuildingCatalog Asians = BuildingCatalog.Load(Civilization.Asian);
    private static readonly BuildingCatalog Amazons = BuildingCatalog.Load(Civilization.Amazon);
    private static readonly BuildingCatalog Egyptians = BuildingCatalog.Load(Civilization.Egyptian);

    [Fact]
    public void The_big_temple_sends_a_priest_at_once_and_then_every_few_minutes()
    {
        var world = new World(new GameMap(80), TestConfig.NoTools);
        world.PlaceBuilding(Asians["big_temple"], 0, new GridPoint(40, 40));

        Run(world, 20);
        var priest = Assert.Single(world.Settlers, s => s.Profession == Profession.Priest);
        Assert.Equal(Civilization.Asian, priest.People);

        Run(world, world.Magic.Config.PriestTicks);
        Assert.Equal(2, world.Settlers.Count(s => s.Profession == Profession.Priest));
    }

    [Fact]
    public void Vines_give_wine_and_the_temple_turns_it_into_mana()
    {
        var world = new World(new GameMap(80), TestConfig.NoTools.WithCropGrowTicks("wine", 300));
        world.PlaceBuilding(Romans["winegrower"], 0, new GridPoint(30, 30));
        Assert.NotNull(world.PlaceBuilding(Romans["temple"], 0, new GridPoint(50, 40)));
        for (int i = 0; i < 3; i++)
            world.Spawn(new GridPoint(40 + i, 50), Profession.Carrier);

        Run(world, 10_000, () => world.Magic.Mana(0) >= 2);

        Assert.True(world.Magic.Mana(0) >= 2, $"mana {world.Magic.Mana(0)}, harvested {world.Production.CropHarvested}");
        // Vines grow again after the harvest.
        Assert.True(Enumerable.Range(0, world.Map!.PointCount).Any(i => world.Map.ObjectCodeAt(world.Map.Point(i)) is >= MapObjectCodes.FirstWine and <= MapObjectCodes.RipeWine));
    }

    [Fact]
    public void Rice_grows_only_on_swampy_ground()
    {
        var map = new GameMap(80);
        for (int y = 20; y < 40; y++)
        for (int x = 40; x < 60; x++)
            map.SetTerrain(new GridPoint(x, y), TerrainType.Mud);
        var world = new World(map, TestConfig.NoTools.WithCropGrowTicks("rice", 300));
        Assert.NotNull(world.PlaceBuilding(Asians["rice_farm"], 0, new GridPoint(30, 30)));
        world.PlaceBuilding(Asians["stock"], 0, new GridPoint(30, 55));
        for (int i = 0; i < 3; i++)
            world.Spawn(new GridPoint(30 + i, 45), Profession.Carrier);

        Run(world, 10_000, () => world.Production.CropHarvested >= 2);

        Assert.True(world.Production.CropHarvested >= 2);
        for (int i = 0; i < map.PointCount; i++)
            if (map.ObjectCodeAt(map.Point(i)) is >= MapObjectCodes.FirstRice and <= MapObjectCodes.RipeRice)
                Assert.Equal(TerrainType.Mud, map.TerrainAt(map.Point(i)));
    }

    [Fact]
    public void A_spell_is_only_cast_where_there_is_a_fight_and_costs_mana()
    {
        var world = TwoPeoples(out var priest, Romans);
        var target = new GridPoint(20, 20);
        Assert.Equal(CastResult.NoMana, world.Magic.Cast([priest], target));
        world.Magic.AddMana(0, 100);
        Assert.Equal(CastResult.NoFight, world.Magic.Cast([priest], target));
        Assert.Equal(100, world.Magic.Mana(0));
    }

    [Fact]
    public void Romans_win_enemy_soldiers_over_who_keep_their_people()
    {
        var world = TwoPeoples(out var priest, Romans);
        var (_, enemies) = Battle(world);
        world.Magic.AddMana(0, 100);
        var cost = world.Magic.SpellOf(priest)!.Cost;

        Assert.Equal(CastResult.Started, world.Magic.Cast([priest], new GridPoint(41, 40)));
        Run(world, 400, () => world.Magic.SpellsCast > 0);

        Assert.Equal(1, world.Magic.SpellsCast);
        Assert.Equal(100 - cost, world.Magic.Mana(0));
        var converted = enemies.Where(e => !e.IsDead && e.Player == 0).ToList();
        Assert.Equal(world.Magic.SpellOf(priest)!.Count, converted.Count);
        Assert.All(converted, c => Assert.Equal(Civilization.Asian, c.People));
    }

    [Fact]
    public void Amazons_freeze_enemies_on_the_spot()
    {
        var world = TwoPeoples(out var priest, Amazons);
        var (_, enemies) = Battle(world);
        world.Magic.AddMana(0, 100);

        Assert.Equal(CastResult.Started, world.Magic.Cast([priest], new GridPoint(41, 40)));
        Run(world, 400, () => world.Magic.SpellsCast > 0);
        var frozen = enemies.Where(e => !e.IsDead && e.FrozenUntil > world.Tick).ToList();
        Assert.NotEmpty(frozen);
        Run(world, World.StepTicks); // a step under way is finished
        var health = enemies.ToDictionary(e => e.Id, e => e.Health);
        var positions = frozen.ToDictionary(e => e.Id, e => e.Position);

        Run(world, 100);
        Assert.All(frozen.Where(e => !e.IsDead), e => Assert.Equal(positions[e.Id], e.Position));
        Assert.Contains(enemies, e => e.IsDead || e.Health < health[e.Id]);
    }

    [Fact]
    public void Egyptians_scatter_enemies_over_the_map()
    {
        var world = TwoPeoples(out var priest, Egyptians);
        var (_, enemies) = Battle(world);
        world.Magic.AddMana(0, 100);

        Assert.Equal(CastResult.Started, world.Magic.Cast([priest], new GridPoint(41, 40)));
        Run(world, 400, () => world.Magic.SpellsCast > 0);

        int away = enemies.Count(e => !e.IsDead && PathFinder.Distance(e.Position, new GridPoint(41, 40)) > world.Magic.Config.Radius + 2);
        Assert.True(away >= Math.Min(world.Magic.SpellOf(priest)!.Count, 5), $"{away} scattered");
    }

    [Fact]
    public void Asians_make_their_soldiers_strike_faster()
    {
        var world = TwoPeoples(out var priest, Asians);
        var (friends, _) = Battle(world);
        world.Magic.AddMana(0, 100);

        Assert.Equal(CastResult.Started, world.Magic.Cast([priest], new GridPoint(41, 40)));
        Run(world, 400, () => world.Magic.SpellsCast > 0);

        Assert.Contains(friends, f => f.HastedUntil > world.Tick);
    }

    [Fact]
    public void Soldiers_attack_enemy_priests()
    {
        var world = TwoPeoples(out var priest, Romans);
        var enemy = world.SpawnSoldier(priest.Position + new GridPoint(4, 0), SoldierType.Swordsman, level: 3, player: 1)!;

        Run(world, 2_000, () => priest.IsDead);

        Assert.True(priest.IsDead);
        Assert.False(enemy.IsDead);
    }

    [Fact]
    public void Soldier_types_are_upgraded_by_the_mana_earned_in_all_at_a_small_temple_all_to_level_2_first()
    {
        var world = new World(new GameMap(80), TestConfig.NoTools);
        var magic = world.Magic;
        var needed = magic.Config.UpgradeThresholds;
        magic.AddMana(0, needed[3]);
        Assert.Equal(UpgradeResult.NoTemple, magic.Upgrade(0, SoldierType.Swordsman));
        world.PlaceBuilding(Romans["temple"], 0, new GridPoint(40, 40));

        Assert.Equal(UpgradeResult.Done, magic.Upgrade(0, SoldierType.Bowman));
        Assert.Equal(2, magic.SoldierLevel(0, SoldierType.Bowman));
        Assert.Equal(UpgradeResult.OthersFirst, magic.Upgrade(0, SoldierType.Bowman)); // swordsmen and pikemen first
        Assert.Equal(UpgradeResult.Done, magic.Upgrade(0, SoldierType.Pikeman));
        Assert.Equal(UpgradeResult.Done, magic.Upgrade(0, SoldierType.Swordsman));
        Assert.Equal(UpgradeResult.Done, magic.Upgrade(0, SoldierType.Bowman));
        Assert.Equal(3, magic.SoldierLevel(0, SoldierType.Bowman));
        Assert.Equal(UpgradeResult.Highest, magic.Upgrade(0, SoldierType.Bowman));
        Assert.Equal(needed[3], magic.Mana(0)); // upgrades spend nothing
        Assert.Equal(1, magic.SoldierLevel(1, SoldierType.Bowman)); // per player

        // Mana spent on spells still counts as earned; the next upgrade waits for more earned mana.
        magic.AddMana(0, -magic.Mana(0));
        Assert.Equal(needed[3], magic.ManaEarned(0));
        magic.AddMana(0, needed[4] - needed[3] - 1);
        Assert.Equal(UpgradeResult.NoMana, magic.Upgrade(0, SoldierType.Swordsman));
        magic.AddMana(0, 1);
        Assert.Equal(UpgradeResult.Done, magic.Upgrade(0, SoldierType.Swordsman));
    }

    [Fact]
    public void Only_soldiers_trained_after_an_upgrade_get_the_new_level()
    {
        var world = new World(new GameMap(80), TestConfig.NoTools);
        world.PlaceBuilding(Romans["temple"], 0, new GridPoint(20, 20));
        world.PlaceBuilding(Romans["barrack"], 0, new GridPoint(40, 40));
        var veteran = world.SpawnSoldier(new GridPoint(60, 60), SoldierType.Swordsman)!;
        world.Magic.AddMana(0, 100);
        Assert.Equal(UpgradeResult.Done, world.Magic.Upgrade(0, SoldierType.Swordsman));
        world.AddLooseGoods(new GridPoint(50, 55), Material.Sword, 1);
        world.Spawn(new GridPoint(45, 55), Profession.Carrier);

        Run(world, 10_000, () => world.Military.SoldiersTrained == 1);

        var recruit = Assert.Single(world.Settlers, s => s.Profession == Profession.Soldier && s != veteran);
        Assert.Equal(2, recruit.Level);
        Assert.Equal(world.Military.Config.MaxHealth(SoldierType.Swordsman, 2), recruit.Health);
        Assert.Equal(1, veteran.Level);
    }

    /// <summary>Player 0 has the given people (and a priest of it, from a big temple), player 1 is Asian.</summary>
    private static World TwoPeoples(out Settler priest, BuildingCatalog people)
    {
        var world = new World(new GameMap(90), TestConfig.NoTools) { Peoples = [people.Civilization, Civilization.Asian] };
        world.PlaceBuilding(people["big_temple"], 0, new GridPoint(20, 40));
        Run(world, 20);
        priest = world.Settlers.Single(s => s.Profession == Profession.Priest);
        return world;
    }

    /// <summary>Two rows of strong soldiers fighting each other around x 40-41.</summary>
    private static (List<Settler> Friends, List<Settler> Enemies) Battle(World world)
    {
        var friends = new List<Settler>();
        var enemies = new List<Settler>();
        for (int y = 36; y <= 44; y++)
        {
            friends.Add(world.SpawnSoldier(new GridPoint(40, y), SoldierType.Swordsman, level: 3)!);
            enemies.Add(world.SpawnSoldier(new GridPoint(42, y), SoldierType.Swordsman, level: 3, player: 1)!);
        }
        Run(world, 40);
        Assert.Contains(friends, f => f.Activity == Activity.Fighting);
        return (friends, enemies);
    }

    private static void Run(World world, int ticks, Func<bool>? stopWhen = null)
    {
        for (int t = 0; t < ticks && stopWhen?.Invoke() != true; t++)
            world.Step();
    }
}
