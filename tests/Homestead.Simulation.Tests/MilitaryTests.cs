using Homestead.Assets;
using Homestead.Assets.Maps;
using Homestead.Simulation.Buildings;
using Homestead.Simulation.Economy;
using Homestead.Simulation.Map;
using Homestead.Simulation.Pathfinding;
using Xunit.Abstractions;

namespace Homestead.Simulation.Tests;

public class MilitaryTests(ITestOutputHelper output)
{
    private static readonly BuildingCatalog Romans = BuildingCatalog.Load(Civilization.Roman);

    [Fact]
    public void Barracks_turn_carriers_and_weapons_into_soldiers()
    {
        var world = new World(new GameMap(70), TestConfig.NoTools);
        world.PlaceBuilding(Romans["barrack"], 0, new GridPoint(30, 30));
        world.AddLooseGoods(new GridPoint(50, 50), Material.Sword, 2);
        world.AddLooseGoods(new GridPoint(52, 50), Material.Bow, 1);
        Spawn(world, 5, new GridPoint(40, 45));

        Run(world, 10_000, stopWhen: () => world.Military.SoldiersTrained == 3);

        var soldiers = world.Settlers.Where(s => s.Profession == Profession.Soldier).ToList();
        Assert.Equal(3, soldiers.Count);
        Assert.Equal(2, soldiers.Count(s => s.SoldierType == SoldierType.Swordsman));
        Assert.Equal(1, soldiers.Count(s => s.SoldierType == SoldierType.Bowman));
        Assert.DoesNotContain(world.Piles, p => p.Material is Material.Sword or Material.Bow && p.Count > 0);
    }

    [Fact]
    public void Soldiers_occupy_a_tower_and_it_claims_the_land_around_it()
    {
        var map = new GameMap(100);
        var world = new World(map, TestConfig.NoTools);
        var tower = world.PlaceBuilding(Romans["tower"], 0, new GridPoint(50, 50))!;
        SpawnSoldiers(world, SoldierType.Swordsman, 1, new GridPoint(70, 70));
        SpawnSoldiers(world, SoldierType.Bowman, 3, new GridPoint(70, 75));
        Assert.False(world.Military.IsOccupied(tower));
        Assert.Null(map.OwnerAt(tower.Position));

        Run(world, 5_000, stopWhen: () => world.Military.IsOccupied(tower) && world.Military.Occupants(tower).All(s => s is not null));

        var occupants = world.Military.Occupants(tower);
        Assert.Equal(3, occupants.Count);
        Assert.All(occupants, s => Assert.True(s!.IsInside));
        Assert.Equal(2, occupants.Count(s => s!.SoldierType == SoldierType.Bowman)); // two bowman places only
        Assert.Contains(world.Settlers, s => s.SoldierType == SoldierType.Bowman && !s.IsInside);
        Assert.Equal(0, map.OwnerAt(new GridPoint(50 + 30, 50)));
        Assert.Null(map.OwnerAt(new GridPoint(50 + 31, 50)));
        Assert.Equal("3/3 soldiers", world.StatusOf(tower));
    }

    [Fact]
    public void A_lost_tower_gives_up_its_land_except_what_another_tower_covers()
    {
        var map = new GameMap(120);
        var world = new World(map, TestConfig.NoTools);
        var west = world.PlaceBuilding(Romans["tower"], 0, new GridPoint(40, 60))!;
        var east = world.PlaceBuilding(Romans["tower"], 0, new GridPoint(80, 60))!;
        SpawnSoldiers(world, SoldierType.Swordsman, 2, new GridPoint(60, 80));
        Run(world, 5_000, stopWhen: () => world.Military.IsOccupied(west) && world.Military.IsOccupied(east));

        world.RemoveBuilding(east);
        Run(world, 20);

        Assert.Equal(0, map.OwnerAt(new GridPoint(60, 60)));   // still covered by the western tower
        Assert.Null(map.OwnerAt(new GridPoint(100, 60)));      // only the eastern tower covered this
        Assert.Equal(1, world.Settlers.Count(s => s.Profession == Profession.Soldier && !s.IsInside && !s.HasTask)); // came out
    }

    [Fact]
    public void Buildings_need_own_land_when_territory_rules_apply()
    {
        var map = new GameMap(70);
        foreach (var p in PathFinder.Circle(new GridPoint(20, 20), 15).Where(map.Contains))
            map.SetOwner(p, 0);
        var world = new World(map, TestConfig.NoTools) { TerritoryRules = true };

        Assert.True(world.CanPlace(Romans["sawmill"], new GridPoint(20, 20), out _, player: 0));
        Assert.False(world.CanPlace(Romans["sawmill"], new GridPoint(20, 20), out string reason, player: 1));
        Assert.Equal("not on own land", reason);
        Assert.False(world.CanPlace(Romans["sawmill"], new GridPoint(50, 50), out _, player: 0));
    }

    [Fact]
    public void Carriers_only_serve_their_own_player()
    {
        var world = new World(new GameMap(80), ProductionConfig.None);
        var site = world.PlaceConstructionSite(Romans["sawmill"], 1, new GridPoint(30, 30))!;
        world.AddLooseGoods(new GridPoint(40, 40), Material.Plank, 8);
        Spawn(world, 3, new GridPoint(45, 45)); // player 0

        Run(world, 1_000);
        Assert.Empty(world.Logistics.Jobs);

        world.Spawn(new GridPoint(50, 50), Profession.Carrier, player: 1);
        Run(world, 100);
        Assert.Single(world.Logistics.Jobs);
        Assert.Equal(1, world.Logistics.Jobs.First().Carrier.Player);
        Assert.NotNull(site);
    }

    [Fact]
    public void Territory_radii_reproduce_the_land_of_the_original_maps()
    {
        GameInstallation installation;
        try { installation = GameInstallation.Locate(); }
        catch (Exception) { output.WriteLine("No installation found, skipped."); return; }

        // Recompute the land from the military buildings alone (one soldier each) and compare with the owner bytes.
        long agree = 0, total = 0, missing = 0, extra = 0;
        int occupied = 0, military = 0;
        var maps = installation.MapFiles.Select(OriginalMap.Open)
            .Where(m => m.Buildings.Any(b => b.Type is 14 or 15 or 16)).Take(40).ToList();
        Assert.NotEmpty(maps);
        foreach (var original in maps)
        {
            var area = original.Area;
            var map = new GameMap(area.Size);
            var world = new World(map, ProductionConfig.None);
            foreach (var b in original.Buildings.Where(b => b.Type is 14 or 15 or 16))
            {
                string name = b.Type switch { 14 => "tower", 15 => "big_tower", _ => "castle" };
                var building = world.PlaceBuilding(Romans[name], b.Player, new GridPoint(b.X, b.Y), force: true);
                if (building is null)
                    continue;
                world.SpawnSoldier(building.Door, SoldierType.Swordsman, player: b.Player);
            }
            Run(world, 3_000, stopWhen: () => world.Buildings.All(world.Military.IsOccupied));
            military += world.Buildings.Count;
            occupied += world.Buildings.Count(world.Military.IsOccupied);
            for (int i = 0; i < map.PointCount; i++)
            {
                int stored = area.Owner[i];
                if (stored == GameMap.NoOwner && map.OwnerAt(map.Point(i)) is null)
                    continue;
                total++;
                var computed = map.OwnerAt(map.Point(i));
                if (computed == (stored == GameMap.NoOwner ? null : stored))
                    agree++;
                else if (computed is null)
                    missing++;
                else if (stored == GameMap.NoOwner)
                    extra++;
            }
        }
        output.WriteLine($"owned points agreeing: {agree}/{total} = {100.0 * agree / total:F1}%; not claimed {missing}, " +
                         $"claimed but not in the map {extra}, other owner {total - agree - missing - extra}; occupied {occupied}/{military}");
        // Land we claim must be the map's land (precision). Some maps also give players land that no
        // military building covers (set in the editor), so not every owned point is claimed.
        long claimed = total - missing;
        output.WriteLine($"precision {100.0 * agree / claimed:F1}%");
        Assert.Equal(military, occupied);
        Assert.True(agree >= claimed * 0.98);
    }

    [Fact]
    public void Two_soldiers_of_different_players_fight_until_one_is_dead()
    {
        var world = new World(new GameMap(60), TestConfig.NoTools);
        var red = world.SpawnSoldier(new GridPoint(20, 30), SoldierType.Swordsman, level: 3)!;
        var blue = world.SpawnSoldier(new GridPoint(26, 30), SoldierType.Swordsman, level: 1, player: 1)!;

        Run(world, 3_000, stopWhen: () => red.IsDead || blue.IsDead);

        Assert.True(blue.IsDead, "the level 3 swordsman wins");
        Assert.False(red.IsDead);
        Assert.True(red.Health < world.Military.Config.MaxHealth(SoldierType.Swordsman, 3));
        Assert.Equal(1, world.Military.Kills);
        Assert.DoesNotContain(blue, world.Settlers);
        Run(world, 50);
        Assert.False(red.HasTask, "back to idle after the fight");
    }

    [Fact]
    public void Bowmen_in_a_tower_shoot_at_enemies_in_range()
    {
        var world = new World(new GameMap(80), TestConfig.NoTools);
        var tower = world.PlaceBuilding(Romans["tower"], 0, new GridPoint(40, 40))!;
        SpawnSoldiers(world, SoldierType.Bowman, 2, new GridPoint(45, 50));
        Run(world, 3_000, stopWhen: () => world.Military.Occupants(tower).Count(s => s is not null) == 2);
        var enemy = world.SpawnSoldier(new GridPoint(46, 40), SoldierType.Pikeman, player: 1)!; // 6 fields away

        Run(world, 3_000, stopWhen: () => enemy.IsDead);

        Assert.True(enemy.IsDead);
        Assert.Equal(2, world.Military.Occupants(tower).Count(s => s is not null));
    }

    [Fact]
    public void Attackers_take_an_enemy_tower_with_its_land()
    {
        var map = new GameMap(120);
        var world = new World(map, TestConfig.NoTools);
        var enemyTower = world.PlaceBuilding(Romans["tower"], 1, new GridPoint(80, 60))!;
        var enemyMill = world.PlaceBuilding(Romans["mill"], 1, new GridPoint(95, 70))!;
        world.SpawnSoldier(enemyTower.Door, SoldierType.Swordsman, player: 1);
        Run(world, 2_000, stopWhen: () => world.Military.IsOccupied(enemyTower));
        Assert.Equal(1, map.OwnerAt(new GridPoint(100, 60)));

        var ownTower = world.PlaceBuilding(Romans["tower"], 0, new GridPoint(45, 60), force: true)!;
        SpawnSoldiers(world, SoldierType.Swordsman, 5, new GridPoint(52, 68));
        Run(world, 3_000, stopWhen: () => world.Military.Occupants(ownTower).Count(s => s is not null) == 1 && world.Settlers.Count(s => s.Player == 0 && !s.HasTask && !s.IsInside) == 4);
        int sent = world.Military.Attack(0, enemyTower);
        Assert.Equal(4, sent); // the one in the own tower stays

        Run(world, 20_000, stopWhen: () => enemyTower.Player == 0);

        Assert.Equal(0, enemyTower.Player);
        Assert.True(world.Military.IsOccupied(enemyTower));
        Assert.Equal(1, world.Military.Captures);
        Assert.Equal(0, map.OwnerAt(new GridPoint(100, 60)));
        Assert.False(enemyMill.Exists, "the enemy mill stood on land that changed sides");
        Assert.DoesNotContain(world.Settlers, s => s.Player == 1 && s.Profession == Profession.Soldier);
    }

    [Fact]
    public void A_defender_goes_back_into_its_place_after_winning()
    {
        var world = new World(new GameMap(80), TestConfig.NoTools);
        var tower = world.PlaceBuilding(Romans["tower"], 0, new GridPoint(40, 40))!;
        world.SpawnSoldier(new GridPoint(50, 50), SoldierType.Swordsman, level: 3);
        Run(world, 3_000, stopWhen: () => world.Military.IsOccupied(tower));
        var attacker = world.SpawnSoldier(new GridPoint(60, 60), SoldierType.Swordsman, player: 1)!;
        var enemyTower = world.PlaceBuilding(Romans["tower"], 1, new GridPoint(65, 25), force: true)!;
        world.Military.Attack(1, tower);

        Run(world, 10_000, stopWhen: () => attacker.IsDead && world.Military.Occupants(tower).Any(s => s is not null));

        Assert.True(attacker.IsDead);
        Assert.Equal(0, tower.Player);
        Assert.Single(world.Military.Occupants(tower), s => s is not null);
        Assert.NotNull(enemyTower);
    }

    private static void Spawn(World world, int count, GridPoint near)
    {
        for (int i = 0; i < count; i++)
            Assert.NotNull(world.Spawn(new GridPoint(near.X + i, near.Y), Profession.Carrier));
    }

    private static void SpawnSoldiers(World world, SoldierType type, int count, GridPoint near)
    {
        for (int i = 0; i < count; i++)
            Assert.NotNull(world.SpawnSoldier(new GridPoint(near.X + i, near.Y), type));
    }

    private static void Run(World world, int ticks, Func<bool>? stopWhen = null)
    {
        for (int t = 0; t < ticks && stopWhen?.Invoke() != true; t++)
            world.Step();
    }
}
