using Homestead.Assets;
using Homestead.Simulation.Ai;
using Homestead.Simulation.Buildings;
using Homestead.Simulation.Economy;
using Homestead.Simulation.Map;
using Homestead.Simulation.Pathfinding;
using Xunit.Abstractions;

namespace Homestead.Simulation.Tests;

/// <summary>The AI in the fight: wounded pulled out and back, bowmen ahead of the melee, the late army.</summary>
public class AiTacticsTests(ITestOutputHelper output)
{
    private static readonly BuildingCatalog Romans = BuildingCatalog.Load(Civilization.Roman);

    /// <summary>An AI tower at 40,80 with its garrison; enemies are placed near 40,125 (nobody's land).</summary>
    private static (World World, GameMap Map) Battlefield()
    {
        var map = new GameMap(160);
        var world = new World(map, TestConfig.NoTools) { TerritoryRules = true, PlayerCount = 2 };
        var tower = world.PlaceBuilding(Romans["tower"], 0, new GridPoint(40, 80), force: true)!;
        world.SpawnSoldier(tower.Door, SoldierType.Swordsman); // the garrison
        world.Step();
        return (world, map);
    }

    [Fact]
    public void A_badly_wounded_striker_is_pulled_out_of_a_small_fight_and_brought_back_healed()
    {
        var (world, _) = Battlefield();
        var wounded = world.SpawnSoldier(new GridPoint(42, 92), SoldierType.Swordsman)!;
        for (int i = 0; i < 4; i++)
            world.SpawnSoldier(new GridPoint(44 + i, 84), SoldierType.Swordsman);
        var enemies = Enumerable.Range(0, 2).Select(i => world.SpawnSoldier(new GridPoint(40 + i, 125), SoldierType.Swordsman, player: 1)!).ToList();
        var ai = new AiPlayer(world, Romans, 0, character: "standard");
        world.AiPlayers.Add(ai);

        // In the fight it is hurt badly.
        Run(world, 60 * World.TicksPerSecond, () => wounded.Activity == Activity.Fighting);
        Assert.Equal(Activity.Fighting, wounded.Activity);
        SetHealth(wounded, world.MaxHealth(0, SoldierType.Swordsman, 1) * 0.3f);
        Run(world, 3 * World.TicksPerSecond, () => ai.Log.Any(l => l.Contains("pulls 1 wounded")));
        Assert.Contains(ai.Log, l => l.Contains("pulls 1 wounded"));
        Assert.NotNull(world.Military.HoldTarget(wounded));
        // An enemy may run after it, but once it has turned away it is not hit any more.
        Run(world, World.TicksPerSecond);
        float health = wounded.Health;
        Run(world, 5 * World.TicksPerSecond);
        Assert.Equal(health, wounded.Health);

        // Healed, it comes back.
        SetHealth(wounded, world.MaxHealth(0, SoldierType.Swordsman, 1));
        Run(world, 3 * World.TicksPerSecond, () => ai.Log.Any(l => l.Contains("back into the fight")));
        foreach (string line in ai.Log)
            output.WriteLine(line);
        Assert.Contains(ai.Log, l => l.Contains("brings 1 withdrawn soldiers back into the fight") && l.Contains("healed"));
    }

    [Theory]
    [InlineData(0, true)]  // no teams: everybody against everybody
    [InlineData(2, true)]  // other team
    [InlineData(1, false)] // same team: allies are left alone
    public void The_ai_strikes_soldiers_near_its_land_unless_they_are_allies(int otherTeam, bool strikes)
    {
        var (world, _) = Battlefield();
        world.Teams.AddRange([otherTeam == 0 ? 0 : 1, otherTeam]);
        for (int i = 0; i < 4; i++)
            world.SpawnSoldier(new GridPoint(44 + i, 84), SoldierType.Swordsman);
        for (int i = 0; i < 2; i++)
            world.SpawnSoldier(new GridPoint(40 + i, 125), SoldierType.Swordsman, player: 1);
        var ai = new AiPlayer(world, Romans, 0, character: "standard");
        world.AiPlayers.Add(ai);

        Run(world, 20 * World.TicksPerSecond, () => ai.Log.Any(l => l.Contains("enemies at")));
        Assert.Equal(strikes, ai.Log.Any(l => l.Contains("enemies at")));
        Assert.Equal(otherTeam == 1, world.AreAllies(0, 1));
    }

    [Fact]
    public void With_a_hospital_in_call_range_the_wounded_go_there_instead_of_holding_a_position()
    {
        var (world, _) = Battlefield();
        var hospital = world.PlaceBuilding(Romans["hospital"], 0, new GridPoint(52, 96), force: true)!;
        world.Spawn(new GridPoint(50, 100), Profession.Carrier); // becomes the healer
        var wounded = world.SpawnSoldier(new GridPoint(42, 92), SoldierType.Swordsman)!;
        for (int i = 0; i < 4; i++)
            world.SpawnSoldier(new GridPoint(44 + i, 84), SoldierType.Swordsman);
        for (int i = 0; i < 2; i++)
            world.SpawnSoldier(new GridPoint(40 + i, 125), SoldierType.Swordsman, player: 1);
        var ai = new AiPlayer(world, Romans, 0, character: "standard");
        world.AiPlayers.Add(ai);

        Run(world, 60 * World.TicksPerSecond, () => wounded.Activity == Activity.Fighting);
        SetHealth(wounded, world.MaxHealth(0, SoldierType.Swordsman, 1) * 0.3f);
        Run(world, 3 * World.TicksPerSecond, () => ai.Log.Any(l => l.Contains("pulls 1 wounded")));
        Assert.Null(world.Military.HoldTarget(wounded));
        Assert.True(PathFinder.Distance(world.Military.ForcedMoveTarget(wounded)!.Value, hospital.Door) <= 1);

        // It arrives free (not holding), where the healer takes it.
        Run(world, 30 * World.TicksPerSecond, () => PathFinder.Distance(wounded.Position, hospital.Door) <= 1 && !wounded.HasTask);
        foreach (string line in ai.Log)
            output.WriteLine(line);
        Assert.False(wounded.IsDead);
        Assert.True(PathFinder.Distance(wounded.Position, hospital.Door) <= 1);
        Assert.False(wounded.HasTask);
    }

    [Theory]
    [InlineData("standard", true)]
    [InlineData("standard@hospital.wounded=20", false)]
    public void More_than_five_wounded_soldiers_get_a_hospital_at_the_military_building_nearest_them(string character, bool builds)
    {
        var (world, _) = Battlefield();
        for (int i = 0; i < 4; i++)
        {
            world.AddLooseGoods(new GridPoint(48 + i * 2, 74), Material.Plank, 8);
            world.AddLooseGoods(new GridPoint(48 + i * 2, 70), Material.Stone, 8);
        }
        for (int i = 0; i < 20; i++)
            world.Spawn(new GridPoint(45, 88), Profession.Carrier);
        // Wounded swordsmen: the tower's one infantry place is taken, none of them goes in.
        for (int i = 0; i < 6; i++)
            SetHealth(world.SpawnSoldier(new GridPoint(50 + i, 90), SoldierType.Swordsman)!, 50);
        var ai = new AiPlayer(world, Romans, 0, character: character);
        world.AiPlayers.Add(ai);

        Run(world, 10 * World.TicksPerSecond, () => ai.Log.Any(l => l.Contains("builds hospital")));

        foreach (string line in ai.Log)
            output.WriteLine(line);
        var hospital = world.Buildings.FirstOrDefault(b => b.Exists && b.Definition.Name == "hospital");
        Assert.Equal(builds, hospital is not null);
        if (hospital is not null)
            Assert.True(PathFinder.Distance(hospital.Position, new GridPoint(40, 80)) <= 30, "near the tower");
    }

    [Fact]
    public void Idle_wounded_soldiers_walk_to_the_hospital_door_and_are_healed_there()
    {
        var (world, _) = Battlefield();
        var hospital = world.PlaceBuilding(Romans["hospital"], 0, new GridPoint(52, 96), force: true)!;
        world.Spawn(new GridPoint(50, 100), Profession.Carrier); // becomes the healer
        // Far beyond the call range of the healer, idle.
        var wounded = world.SpawnSoldier(new GridPoint(hospital.Door.X - 45, hospital.Door.Y), SoldierType.Swordsman)!;
        SetHealth(wounded, 40);
        Assert.True(PathFinder.Distance(wounded.Position, hospital.Door) > world.Military.Config.HospitalCallRadius);
        var ai = new AiPlayer(world, Romans, 0, character: "standard");
        world.AiPlayers.Add(ai);

        Run(world, 90 * World.TicksPerSecond, () => world.Production.HealsDone > 0);

        Assert.Equal(1, world.Production.HealsDone);
        Run(world, world.Military.Config.HealTicks + 5);
        Assert.Equal(world.MaxHealth(0, SoldierType.Swordsman, 1), wounded.Health);
    }

    [Fact]
    public void Many_wounded_queue_in_front_of_the_hospital_and_are_healed_one_after_another()
    {
        // Seen in a game: all of them were sent to the door, pushed each other aside (idle soldiers keep a field
        // apart) and nobody ever stood still in front of it – the healer never came out.
        var (world, _) = Battlefield();
        var hospital = world.PlaceBuilding(Romans["hospital"], 0, new GridPoint(52, 96), force: true)!;
        world.Spawn(new GridPoint(50, 100), Profession.Carrier); // becomes the healer
        var wounded = Enumerable.Range(0, 8).Select(i => world.SpawnSoldier(new GridPoint(30 + i % 4, 100 + i / 4), SoldierType.Swordsman)!).ToList();
        for (int i = 0; i < wounded.Count; i++)
            SetHealth(wounded[i], 10 + i * 5);
        var ai = new AiPlayer(world, Romans, 0, character: "standard");
        world.AiPlayers.Add(ai);

        var healed = new List<Settler>();
        Run(world, 4 * 60 * World.TicksPerSecond, () =>
        {
            healed.AddRange(wounded.Where(s => s.Health >= 100 && !healed.Contains(s)));
            return healed.Count == wounded.Count;
        });

        Assert.Equal(wounded.Count, healed.Count);
        Assert.Equal(wounded, healed); // the weakest first
    }

    [Theory]
    [InlineData("standard@military.wounded.withdrawHealth=0")]
    [InlineData("standard@military.wounded.maxSoldiers=2")]
    public void Wounded_stay_in_the_fight_when_it_is_too_big_or_the_character_never_pulls_out(string character)
    {
        var (world, _) = Battlefield();
        var wounded = world.SpawnSoldier(new GridPoint(42, 92), SoldierType.Swordsman)!;
        for (int i = 0; i < 4; i++)
            world.SpawnSoldier(new GridPoint(44 + i, 84), SoldierType.Swordsman);
        for (int i = 0; i < 2; i++)
            world.SpawnSoldier(new GridPoint(40 + i, 125), SoldierType.Swordsman, player: 1);
        var ai = new AiPlayer(world, Romans, 0, character: character);
        world.AiPlayers.Add(ai);

        Run(world, 60 * World.TicksPerSecond, () => wounded.Activity == Activity.Fighting);
        SetHealth(wounded, world.MaxHealth(0, SoldierType.Swordsman, 1) * 0.3f);
        Run(world, 3 * World.TicksPerSecond);

        Assert.DoesNotContain(ai.Log, l => l.Contains("wounded"));
    }

    [Theory]
    [InlineData("formation")]
    [InlineData("formation@formation.parkDistance=14")]
    public void Bowmen_go_ahead_the_melee_waits_behind_and_goes_in_when_the_enemy_comes_close(string character)
    {
        var (world, _) = Battlefield();
        var bowmen = Enumerable.Range(0, 2).Select(i => world.SpawnSoldier(new GridPoint(44 + i, 88), SoldierType.Bowman)!).ToList();
        var melee = Enumerable.Range(0, 3).Select(i => world.SpawnSoldier(new GridPoint(44 + i, 84), SoldierType.Swordsman)!).ToList();
        var enemies = Enumerable.Range(0, 2).Select(i => world.SpawnSoldier(new GridPoint(40 + i, 125), SoldierType.Swordsman, player: 1)!).ToList();
        var ai = new AiPlayer(world, Romans, 0, character: character);
        world.AiPlayers.Add(ai);
        var enemy = new GridPoint(40, 125);

        Run(world, 10 * World.TicksPerSecond, () => ai.Log.Any(l => l.Contains("formation: sends")));
        Assert.Contains(ai.Log, l => l.Contains("formation: sends 2 bowmen ahead"));
        // On the way the bowmen lead, the melee soldiers keep behind them.
        Run(world, 40 * World.TicksPerSecond, () => bowmen.All(b => b.Activity == Activity.Shooting));
        Assert.All(bowmen, b => Assert.Equal(Activity.Shooting, b.Activity));
        Assert.True(PathFinder.Distance(Center(melee), enemy) >= PathFinder.Distance(Center(bowmen), enemy) + 3,
            "the melee soldiers wait behind the bowmen");
        Assert.DoesNotContain(melee, m => m.Activity == Activity.Fighting);

        Run(world, 60 * World.TicksPerSecond, () => enemies.All(e => e.IsDead));
        foreach (string line in ai.Log)
            output.WriteLine(line);
        Assert.Contains(ai.Log, l => l.Contains("formation: 3 soldiers go in") && l.Contains("the bowmen fall back behind them"));
        Assert.All(enemies, e => Assert.True(e.IsDead));
        Assert.All(bowmen, b => Assert.False(b.IsDead));
    }

    [Fact]
    public void Without_the_trait_or_against_bowmen_everybody_goes_in_at_once()
    {
        foreach (var (character, enemyType) in new[] { ("standard", SoldierType.Swordsman), ("formation", SoldierType.Bowman) })
        {
            var (world, _) = Battlefield();
            for (int i = 0; i < 2; i++)
                world.SpawnSoldier(new GridPoint(44 + i, 88), SoldierType.Bowman);
            for (int i = 0; i < 4; i++)
                world.SpawnSoldier(new GridPoint(44 + i, 84), SoldierType.Swordsman);
            for (int i = 0; i < 2; i++)
                world.SpawnSoldier(new GridPoint(40 + i, 125), enemyType, player: 1);
            var ai = new AiPlayer(world, Romans, 0, character: character);
            world.AiPlayers.Add(ai);

            Run(world, 10 * World.TicksPerSecond, () => ai.Log.Any(l => l.Contains("strikes")));
            Assert.Contains(ai.Log, l => l.Contains("strikes"));
            Assert.DoesNotContain(ai.Log, l => l.Contains("formation:"));
        }
    }

    [Fact]
    public void With_a_big_army_the_weaponsmiths_make_mostly_spears_and_bows_and_with_a_small_one_again_as_before()
    {
        // No military building that would call soldiers in: a sawmill as the AI's home.
        var world = new World(new GameMap(160), TestConfig.NoTools) { PlayerCount = 2 };
        world.PlaceBuilding(Romans["sawmill"], 0, new GridPoint(40, 80), force: true);
        var production = world.Production;
        double sword = production.ShareOf(0, Material.Sword), spear = production.ShareOf(0, Material.Spear);
        var army = Enumerable.Range(0, 19).Select(i => world.SpawnSoldier(new GridPoint(50 + i % 10, 90 + i / 10), SoldierType.Swordsman)!).ToList();
        var ai = new AiPlayer(world, Romans, 0, character: "standard");
        world.AiPlayers.Add(ai);
        bool Late() => production.ShareOf(0, Material.Spear) > production.ShareOf(0, Material.Sword)
                       && production.ShareOf(0, Material.Bow) > production.ShareOf(0, Material.Sword);

        Run(world, 2 * World.TicksPerSecond);
        Assert.False(Late(), "19 soldiers are no big army yet");

        army.Add(world.SpawnSoldier(new GridPoint(50, 92), SoldierType.Swordsman)!);
        Run(world, 2 * World.TicksPerSecond);
        Assert.True(Late());
        Assert.Contains(ai.Log, l => l.Contains("late game, 20 soldiers in the field"));

        // Fewer, but not much fewer: still late. Below 15: as before.
        var killer = world.SpawnSoldier(new GridPoint(140, 140), SoldierType.Swordsman, player: 1)!;
        foreach (var soldier in army.Take(5))
            Kill(world, soldier, killer);
        Run(world, 2 * World.TicksPerSecond);
        Assert.True(Late(), "15 left");
        Kill(world, army[5], killer);
        Run(world, 2 * World.TicksPerSecond);
        Assert.False(Late());
        Assert.Equal(sword, production.ShareOf(0, Material.Sword));
        Assert.Equal(spear, production.ShareOf(0, Material.Spear));
        Assert.Contains(ai.Log, l => l.Contains("only 14 soldiers left in the field"));
    }

    private static GridPoint Center(List<Settler> soldiers) =>
        new((int)soldiers.Average(s => s.Position.X), (int)soldiers.Average(s => s.Position.Y));

    /// <summary>The soldier falls as if struck down by <paramref name="attacker"/>.</summary>
    private static void Kill(World world, Settler soldier, Settler attacker) =>
        typeof(Simulation.Military.Military).GetMethod("Kill", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(world.Military, [soldier, attacker]);

    /// <summary>Wounds or heals a soldier at once (the health is the simulation's own).</summary>
    [Fact]
    public void An_assault_throws_everything_at_the_player_without_falling_back()
    {
        var (world, _) = Battlefield();
        var own = world.BuildingAt(new GridPoint(40, 80))!;
        world.SpawnSoldier(own.Door, SoldierType.Swordsman); // a second one in the tower: it comes out too
        var enemyTower = world.PlaceBuilding(Romans["tower"], 1, new GridPoint(40, 130), force: true)!;
        for (int i = 0; i < 3; i++)
            world.SpawnSoldier(enemyTower.Door, SoldierType.Swordsman, player: 1);
        var army = Enumerable.Range(0, 2).Select(i => world.SpawnSoldier(new GridPoint(44 + i, 84), SoldierType.Swordsman)!).ToList();
        var bowman = world.SpawnSoldier(new GridPoint(46, 86), SoldierType.Bowman)!;
        var ai = new AiPlayer(world, Romans, 0, character: "standard");
        world.AiPlayers.Add(ai);
        Run(world, 10 * World.TicksPerSecond);
        // Far too weak for a campaign of its own.
        Assert.DoesNotContain(ai.Log, l => l.Contains("gathers"));

        ai.Assault(1);
        Run(world, 10 * World.TicksPerSecond);
        foreach (string line in ai.Log)
            output.WriteLine(line);
        Assert.All(army, s => Assert.Same(enemyTower, world.Military.AttackTarget(s)));
        Assert.Equal(1, world.Military.Occupants(own).Count(o => o is not null));
        Assert.Contains(world.Settlers, s => s.Player == 0 && !s.IsInside && !army.Contains(s) && s != bowman
                                             && world.Military.AttackTarget(s) == enemyTower);
        Assert.True(PathFinder.Distance(bowman.Position, enemyTower.Door) < PathFinder.Distance(new GridPoint(46, 86), enemyTower.Door));
        Run(world, 120 * World.TicksPerSecond, () => army.All(s => s.IsDead));
        Assert.DoesNotContain(ai.Log, l => l.Contains("retreats") || l.Contains("gives up") || l.Contains("pulls"));
    }

    [Fact]
    public void An_ally_is_not_assaulted()
    {
        var (world, _) = Battlefield();
        world.Teams.AddRange([1, 1]);
        var ai = new AiPlayer(world, Romans, 0, character: "standard");
        ai.Assault(1);
        Assert.Null(ai.AssaultOn);
    }

    private static void SetHealth(Settler soldier, float health) => typeof(Settler).GetProperty(nameof(Settler.Health))!.SetValue(soldier, health);

    private static void Run(World world, int ticks, Func<bool>? stopWhen = null)
    {
        for (int t = 0; t < ticks && stopWhen?.Invoke() != true; t++)
            world.Step();
    }
}
