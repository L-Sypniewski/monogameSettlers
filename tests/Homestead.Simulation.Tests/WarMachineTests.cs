using Homestead.Simulation.Buildings;
using Homestead.Simulation.Economy;
using Homestead.Simulation.Map;
using Homestead.Simulation.Military;
using Homestead.Simulation.Pathfinding;
using Homestead.Simulation.Persistence;

namespace Homestead.Simulation.Tests;

public class WarMachineTests
{
    private static readonly BuildingCatalog Romans = BuildingCatalog.Load(Civilization.Roman);

    private static void Run(World world, int ticks, Func<bool>? stopWhen = null)
    {
        for (int t = 0; t < ticks && stopWhen?.Invoke() != true; t++)
            world.Step();
    }

    /// <summary>An enemy (player 1) tower at (70, 50) with a swordsman inside.</summary>
    private static World WithEnemyTower(out Building tower, int size = 120)
    {
        var world = new World(new GameMap(size), TestConfig.NoTools);
        tower = world.PlaceBuilding(Romans["tower"], 1, new GridPoint(70, 50))!;
        world.SpawnSoldier(tower.Door, SoldierType.Swordsman, player: 1);
        var t = tower;
        Run(world, 2_000, () => world.Military.IsOccupied(t));
        Assert.True(world.Military.IsOccupied(tower));
        return world;
    }

    [Fact]
    public void Every_people_has_its_works_and_the_asians_their_gunpowder_maker()
    {
        var rules = MilitaryConfig.Load().WarMachines;
        foreach (var (people, works) in new[] { (Civilization.Roman, "catapult_works"), (Civilization.Egyptian, "ballista_works"),
                     (Civilization.Asian, "cannon_works"), (Civilization.Amazon, "gong_works") })
        {
            var catalog = BuildingCatalog.Load(people);
            var definition = catalog[works];
            Assert.DoesNotContain(definition.Door, definition.Blocked);
            Assert.Equal(3, definition.RequestPiles.Count);
            Assert.Equal(works, rules.Kinds[people].Works);
            Assert.All(rules.Kinds[people].Cost.Keys,
                m => Assert.Contains(definition.RequestPiles, p => p.Material == m));
        }
        Assert.NotNull(BuildingCatalog.Load(Civilization.Asian).Find("gunpowder_maker"));
        Assert.Null(Romans.Find("gunpowder_maker"));
    }

    [Fact]
    public void The_gunpowder_maker_makes_gunpowder_of_coal_and_sulfur()
    {
        var world = new World(new GameMap(100), TestConfig.NoTools);
        var maker = world.PlaceBuilding(BuildingCatalog.Load(Civilization.Asian)["gunpowder_maker"], 0, new GridPoint(40, 40))!;
        world.AddLooseGoods(new GridPoint(30, 55), Material.Coal, 3);
        world.AddLooseGoods(new GridPoint(33, 55), Material.Sulfur, 3);
        for (int i = 0; i < 3; i++)
            world.Spawn(new GridPoint(25 + i, 50), Profession.Carrier);
        Run(world, 20_000, () => world.Piles.Where(p => p.Material == Material.GunPowder).Sum(p => p.Count) >= 2);
        Assert.True(world.Piles.Where(p => p.Material == Material.GunPowder).Sum(p => p.Count) >= 2, world.StatusOf(maker));
    }

    [Fact]
    public void Ordered_machines_and_loads_are_hammered_together_in_front_of_the_works()
    {
        var world = new World(new GameMap(100), TestConfig.NoTools);
        var works = world.PlaceBuilding(Romans["catapult_works"], 0, new GridPoint(40, 40))!;
        world.AddLooseGoods(new GridPoint(30, 55), Material.Plank, 8);
        world.AddLooseGoods(new GridPoint(32, 55), Material.Stone, 8);
        world.AddLooseGoods(new GridPoint(34, 55), Material.Stone, 8);
        world.AddLooseGoods(new GridPoint(36, 55), Material.Iron, 4);
        for (int i = 0; i < 5; i++)
            world.Spawn(new GridPoint(25 + i, 50), Profession.Carrier);

        // Nothing ordered: nobody brings anything.
        Run(world, 1_500);
        Assert.Equal(0, works.Piles.Where(p => p.Role == PileRole.Request).Sum(p => p.Count));
        Assert.Contains("nothing ordered", world.StatusOf(works));

        Assert.True(world.WarMachines.Order(works, WarWork.Machine));
        Assert.True(world.WarMachines.Order(works, WarWork.Ammo));
        Assert.Equal(WarWork.Machine, world.WarMachines.Current(works));
        bool hammering = false;
        Run(world, 60_000, () =>
        {
            hammering |= world.Production.WorkerOf(works) is { IsInside: false, Activity: Activity.Building };
            return world.WarMachines.Loads(works) == 1;
        });
        Assert.True(hammering);
        var machine = Assert.Single(world.WarMachines.Of(0));
        Assert.Equal(Civilization.Roman, machine.People);
        Assert.Equal(6, machine.Ammo);
        Assert.Equal(1, world.WarMachines.Loads(works));
        Assert.Null(world.WarMachines.Current(works));
        // Exactly the cost went in: machine 6 planks, 6 stones, 3 iron; the load 6 stones.
        Assert.Equal(2, world.Piles.Where(p => p.Material == Material.Plank).Sum(p => p.Count));
        Assert.Equal(4, world.Piles.Where(p => p.Material == Material.Stone).Sum(p => p.Count));
        Assert.Equal(1, world.Piles.Where(p => p.Material == Material.Iron).Sum(p => p.Count));
        // It does not count among the people, nor does it take a job.
        Assert.False(machine.HasTask);
        Assert.Equal(5, world.Recruitment.Civilians(0)); // the carriers, one of them the works' worker now
    }

    [Fact]
    public void Machines_fire_at_an_enemy_tower_until_it_falls_and_its_soldier_walks_out_alive()
    {
        var world = WithEnemyTower(out var tower);
        var machines = Enumerable.Range(0, 2)
            .Select(i => world.WarMachines.Spawn(new GridPoint(35 + i, 50), 0, Civilization.Asian)!).ToList();
        int hits = world.Military.Config.WarMachines.HitsOf("tower");
        Assert.Equal(2, world.WarMachines.Attack(machines, tower));

        long firstShot = -1;
        Run(world, 30_000, () =>
        {
            if (firstShot < 0 && machines.Any(m => m.Ammo < 6))
                firstShot = world.Tick;
            return !tower.Exists;
        });
        Assert.False(tower.Exists);
        Run(world, 2);
        // They drove into range and stopped there.
        Assert.All(machines, m => Assert.InRange(PathFinder.Distance(m.Position, tower.Position), 1, world.WarMachines.KindOf(m)!.Range));
        // At least the building's hits were fired, the cannon misses one in ten.
        Assert.InRange(machines.Sum(m => 6 - m.Ammo), hits, 12);
        var soldier = Assert.Single(world.Settlers, s => s.Player == 1 && s.Profession == Profession.Soldier);
        Assert.False(soldier.IsDead);
        Assert.False(soldier.IsInside);
        Assert.Contains(world.Events.Since(0, 1), e => e.Definition.Id == "buildingDestroyed");
    }

    [Fact]
    public void A_machine_fires_only_every_few_seconds_and_stops_without_shots()
    {
        var world = WithEnemyTower(out var tower);
        var machine = world.WarMachines.Spawn(new GridPoint(60, 50), 0, Civilization.Roman, ammo: 2)!;
        // Idle within range: it fires on its own.
        Run(world, world.Military.Config.WarMachines.CooldownTicks - 1);
        Assert.Equal(1, machine.Ammo);
        Run(world, world.Military.Config.WarMachines.CooldownTicks);
        Assert.Equal(0, machine.Ammo);
        Run(world, 3 * world.Military.Config.WarMachines.CooldownTicks);
        Assert.Equal(0, machine.Ammo);
        // Ordered at the tower without shots: it gives up at once.
        world.WarMachines.Attack([machine], tower);
        Run(world, 1);
        Assert.Null(world.WarMachines.TargetOf(machine));
    }

    [Fact]
    public void A_hit_tower_mends_one_hit_after_another_over_time()
    {
        var world = WithEnemyTower(out var tower);
        var cannon = world.WarMachines.Spawn(new GridPoint(60, 50), 0, Civilization.Asian, ammo: 3)!;
        Run(world, 3_000, () => world.WarMachines.HitsTaken(tower) >= 2);
        Assert.Equal(2, world.WarMachines.HitsTaken(tower));
        Assert.Contains(world.WarMachines.Impacts, i => i.Building == tower);
        Assert.True(world.WarMachines.DamageOf(tower) > 0);
        Run(world, 3 * world.Military.Config.WarMachines.CooldownTicks, () => cannon.Ammo == 0 && world.WarMachines.Shots.Count == 0);
        // Out of shots now: the tower mends one hit per repairSeconds.
        int left = world.WarMachines.HitsTaken(tower);
        Run(world, world.Military.Config.WarMachines.RepairTicks + World.TicksPerSecond + 2 * world.Military.Config.WarMachines.CooldownTicks);
        Assert.Equal(left - 1, world.WarMachines.HitsTaken(tower));
        Run(world, left * (world.Military.Config.WarMachines.RepairTicks + World.TicksPerSecond));
        Assert.Equal(0, world.WarMachines.HitsTaken(tower));
        Assert.Equal(0, world.WarMachines.DamageOf(tower));
        Assert.True(tower.Exists);
        Assert.Empty(world.WarMachines.Impacts);
    }

    [Fact]
    public void The_gong_needs_no_ammunition()
    {
        var world = WithEnemyTower(out var tower);
        var amazons = BuildingCatalog.Load(Civilization.Amazon);
        var works = world.PlaceBuilding(amazons["gong_works"], 0, new GridPoint(20, 80))!;
        Assert.False(world.WarMachines.Order(works, WarWork.Ammo));
        Assert.True(world.WarMachines.Order(works, WarWork.Machine));

        var gong = world.WarMachines.Spawn(new GridPoint(62, 50), 0, Civilization.Amazon)!;
        Assert.Equal(0, gong.Ammo);
        Run(world, 40 * World.TicksPerSecond);
        Assert.True(world.WarMachines.HitsTaken(tower) > 0 || !tower.Exists, "it struck at the tower");
        Run(world, 200 * World.TicksPerSecond, () => !tower.Exists);
        Assert.False(tower.Exists);
        Assert.Equal(0, gong.Ammo);
    }

    [Fact]
    public void Swordsmen_destroy_a_machine_but_bowmen_leave_it_alone()
    {
        var world = new World(new GameMap(80), TestConfig.NoTools);
        var machine = world.WarMachines.Spawn(new GridPoint(40, 40), 0, Civilization.Roman)!;
        var bowman = world.SpawnSoldier(new GridPoint(48, 40), SoldierType.Bowman, player: 1)!;
        Run(world, 1_000);
        Assert.Equal(world.Military.Config.WarMachines.Kinds[Civilization.Roman].Health, machine.Health);
        Assert.False(bowman.HasTask);

        world.SpawnSoldier(new GridPoint(46, 42), SoldierType.Swordsman, level: 3, player: 1);
        Run(world, 5_000, () => machine.IsDead);
        Assert.True(machine.IsDead);
        Assert.Empty(world.WarMachines.Of(0));
    }

    [Fact]
    public void A_machine_reloads_at_its_works()
    {
        var world = new World(new GameMap(100), TestConfig.NoTools);
        var works = world.PlaceBuilding(Romans["catapult_works"], 0, new GridPoint(40, 40))!;
        var machine = world.WarMachines.Spawn(new GridPoint(60, 60), 0, Civilization.Roman, ammo: 0)!;
        Assert.Equal(0, world.WarMachines.Reload([machine])); // no load lying there
        Assert.True(world.WarMachines.Finish(works, WarWork.Ammo));
        Assert.Equal(1, world.WarMachines.Loads(works));

        Assert.Equal(1, world.WarMachines.Reload([machine]));
        Run(world, 5_000, () => machine.Ammo > 0);
        Assert.Equal(6, machine.Ammo);
        Assert.Equal(0, world.WarMachines.Loads(works));
        Assert.InRange(PathFinder.Distance(machine.Position, works.Door), 0, world.Military.Config.WarMachines.ReloadDistance);
    }

    [Fact]
    public void Machines_on_the_way_and_shots_in_the_air_are_saved()
    {
        var world = WithEnemyTower(out var tower);
        var machine = world.WarMachines.Spawn(new GridPoint(30, 50), 0, Civilization.Roman)!;
        world.WarMachines.Attack([machine], tower);
        Run(world, 3_000, () => world.WarMachines.Shots.Count > 0);
        Assert.NotEmpty(world.WarMachines.Shots);

        var stream = new MemoryStream();
        Snapshot.Write(stream, world);
        stream.Position = 0;
        var loaded = Snapshot.Read<World>(stream);
        var loadedTower = loaded.Buildings.Single(b => b.Id == tower.Id);
        var loadedMachine = loaded.Settlers.Single(s => s.Id == machine.Id);
        Assert.Equal(loadedTower, loaded.WarMachines.TargetOf(loadedMachine));
        Assert.Equal(world.WarMachines.Shots.Count, loaded.WarMachines.Shots.Count);
        Run(world, 20_000, () => !tower.Exists);
        Run(loaded, 20_000, () => !loadedTower.Exists);
        Assert.Equal(tower.Exists, loadedTower.Exists);
        Assert.Equal(machine.Ammo, loadedMachine.Ammo);
    }

    /// <summary>A map with a wall of water at x = 50, open at y = 50 and the <paramref name="gap"/> - 1 fields below.</summary>
    private static World WithWall(int gap)
    {
        var map = new GameMap(100);
        for (int y = 0; y < 100; y++)
            if (y < 50 || y >= 50 + gap)
                map.SetTerrain(new GridPoint(50, y), TerrainType.Water);
        map.RefreshWalkable();
        return new World(map, TestConfig.NoTools);
    }

    [Fact]
    public void A_machine_covers_its_field_and_the_six_around_it_where_it_stands_and_drives()
    {
        var world = new World(new GameMap(100), TestConfig.NoTools);
        var machine = world.WarMachines.Spawn(new GridPoint(30, 30), 0, Civilization.Roman)!;
        var body = machine.Body(machine.Position).ToList();
        Assert.Equal(7, body.Distinct().Count());
        Assert.All(body, p => Assert.True(world.IsOccupied(p), $"{p} free"));

        world.WarMachines.Move([machine], new GridPoint(45, 30));
        Run(world, 2_000, () => machine.Position == new GridPoint(45, 30) && machine.State != SettlerState.Stepping);
        Assert.Equal(new GridPoint(45, 30), machine.Position);
        Assert.All(machine.Body(machine.Position), p => Assert.True(world.IsOccupied(p), $"{p} free"));
        // Nothing left behind on the way.
        int occupied = Enumerable.Range(0, world.Map!.PointCount).Count(i => world.IsOccupied(world.Map.Point(i)));
        Assert.Equal(7, occupied);
    }

    [Fact]
    public void A_machine_does_not_fit_through_a_gap_a_carrier_walks_through()
    {
        var narrow = WithWall(gap: 2);
        var machine = narrow.WarMachines.Spawn(new GridPoint(40, 50), 0, Civilization.Roman)!;
        var carrier = narrow.Spawn(new GridPoint(40, 60))!;
        narrow.WarMachines.Move([machine], new GridPoint(60, 50));
        Assert.True(narrow.WalkTo(carrier, new GridPoint(60, 60)));
        Run(narrow, 3_000);
        Assert.Equal(new GridPoint(60, 60), carrier.Position);
        // As near as it gets: before the wall.
        Assert.True(machine.Position.X < 50, $"{machine.Position}");
        Assert.True(machine.Position.X >= 47, $"{machine.Position}");

        var wide = WithWall(gap: 3);
        machine = wide.WarMachines.Spawn(new GridPoint(40, 50), 0, Civilization.Roman)!;
        wide.WarMachines.Move([machine], new GridPoint(60, 50));
        Run(wide, 3_000, () => machine.Position == new GridPoint(60, 50));
        Assert.Equal(new GridPoint(60, 50), machine.Position);
    }

    [Fact]
    public void Idle_settlers_make_way_for_a_machine_and_walkers_go_around_it()
    {
        var world = new World(new GameMap(100), TestConfig.NoTools);
        var machine = world.WarMachines.Spawn(new GridPoint(30, 30), 0, Civilization.Roman)!;
        // Idle carriers on and beside its way.
        for (int x = 34; x <= 40; x += 2)
        {
            world.Spawn(new GridPoint(x, 30));
            world.Spawn(new GridPoint(x, 31));
        }
        world.WarMachines.Move([machine], new GridPoint(45, 30));
        Run(world, 2_000, () => machine.Position == new GridPoint(45, 30));
        Assert.Equal(new GridPoint(45, 30), machine.Position);
        Assert.Equal(0, world.PassThroughs);

        // Across a standing machine: around it, not through.
        var walker = world.Spawn(new GridPoint(41, 30))!;
        Assert.True(world.WalkTo(walker, new GridPoint(49, 30)));
        var body = machine.Body(machine.Position).ToHashSet();
        bool crossed = false;
        Run(world, 1_000, () =>
        {
            crossed |= body.Contains(walker.Position);
            return walker.Position == new GridPoint(49, 30);
        });
        Assert.Equal(new GridPoint(49, 30), walker.Position);
        Assert.False(crossed);
    }
}
