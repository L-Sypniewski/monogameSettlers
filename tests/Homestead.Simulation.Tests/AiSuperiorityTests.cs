using Homestead.Simulation.Ai;
using Homestead.Simulation.Buildings;
using Homestead.Simulation.Map;
using Xunit.Abstractions;

namespace Homestead.Simulation.Tests;

/// <summary>Superiority: a clearly bigger army attacks an enemy out of a campaign's reach (big maps).</summary>
public class AiSuperiorityTests(ITestOutputHelper output)
{
    private static readonly BuildingCatalog Romans = BuildingCatalog.Load(Civilization.Roman);

    /// <summary>
    /// The AI (a skirmisher: campaigns only within 60 fields of its land) with a tower at 40,20, one swordsman in it and
    /// <paramref name="idle"/> outside; the enemy's tower 130 fields away, held by one, and <paramref name="enemyArmy"/> more far off.
    /// </summary>
    private static (World World, AiPlayer Ai) FarApart(string character, int idle, int enemyArmy = 0)
    {
        var map = new GameMap(160);
        var world = new World(map, TestConfig.NoTools) { TerritoryRules = true, PlayerCount = 2 };
        var tower = world.PlaceBuilding(Romans["tower"], 0, new GridPoint(40, 20), force: true)!;
        world.SpawnSoldier(tower.Door, SoldierType.Swordsman);
        for (int i = 0; i < idle; i++)
            world.SpawnSoldier(new GridPoint(42 + i, 26), SoldierType.Swordsman);
        var enemyTower = world.PlaceBuilding(Romans["tower"], 1, new GridPoint(40, 150), force: true)!;
        world.SpawnSoldier(enemyTower.Door, SoldierType.Swordsman, player: 1);
        for (int i = 0; i < enemyArmy; i++)
            world.SpawnSoldier(new GridPoint(130 + i % 6, 140 + i / 6), SoldierType.Swordsman, player: 1);
        var ai = new AiPlayer(world, Romans, 0, character: character);
        world.AiPlayers.Add(ai);
        return (world, ai);
    }

    [Fact]
    public void An_army_twice_the_enemys_attacks_it_beyond_reach_but_only_from_the_start_time_on()
    {
        // Before superiority.startMinutes (60 by default) nothing happens: the target is too far for the skirmisher.
        var (early, earlyAi) = FarApart("skirmisher", idle: 8);
        Run(early, 20 * World.TicksPerSecond);
        Assert.Null(earlyAi.CampaignAgainst);

        var (world, ai) = FarApart("skirmisher@superiority.startMinutes=0", idle: 8);
        Run(world, 5 * World.TicksPerSecond, () => ai.CampaignAgainst is not null);
        foreach (string line in ai.Log)
            output.WriteLine(line);
        Assert.Equal(1, ai.CampaignAgainst);
        Assert.Contains(ai.Log, l => l.Contains("outnumbers player 2"));
    }

    [Fact]
    public void No_attack_below_two_to_one()
    {
        // 6 against 4: not enough.
        var (world, ai) = FarApart("skirmisher@superiority.startMinutes=0", idle: 5, enemyArmy: 3);
        Run(world, 20 * World.TicksPerSecond);
        Assert.Null(ai.CampaignAgainst);
        Assert.DoesNotContain(ai.Log, l => l.Contains("outnumbers"));
    }

    private static void Run(World world, int ticks, Func<bool>? stopWhen = null)
    {
        for (int t = 0; t < ticks && stopWhen?.Invoke() != true; t++)
            world.Step();
    }
}
