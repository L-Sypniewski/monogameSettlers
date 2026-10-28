using Homestead.Simulation.Ai;
using Homestead.Simulation.Buildings;
using Homestead.Simulation.Map;
using Xunit.Abstractions;

namespace Homestead.Simulation.Tests;

/// <summary>Allies count in every weighing of strength: at the fight and army against army.</summary>
public class AiAlliesTests(ITestOutputHelper output)
{
    private static readonly BuildingCatalog Romans = BuildingCatalog.Load(Civilization.Roman);

    /// <summary>
    /// The AI (player 1) with a tower at 40,20, one swordsman in it and <paramref name="idle"/> outside; the enemy (player 2) with a
    /// tower at 40,60 held by one and <paramref name="enemyArmy"/> more far off; player 3, allied with the AI, with <paramref name="allied"/> swordsmen at <paramref name="alliesAt"/>.
    /// </summary>
    private static (World World, AiPlayer Ai) Setup(string character, int idle, int allied, GridPoint alliesAt, int enemyArmy = 0)
    {
        var map = new GameMap(160);
        var world = new World(map, TestConfig.NoTools) { TerritoryRules = true, PlayerCount = 3 };
        world.Teams.AddRange([1, 0, 1]);
        var tower = world.PlaceBuilding(Romans["tower"], 0, new GridPoint(40, 20), force: true)!;
        world.SpawnSoldier(tower.Door, SoldierType.Swordsman);
        for (int i = 0; i < idle; i++)
            world.SpawnSoldier(new GridPoint(42 + i, 26), SoldierType.Swordsman);
        var enemyTower = world.PlaceBuilding(Romans["tower"], 1, new GridPoint(40, 60), force: true)!;
        world.SpawnSoldier(enemyTower.Door, SoldierType.Swordsman, player: 1);
        for (int i = 0; i < enemyArmy; i++)
            world.SpawnSoldier(new GridPoint(130 + i, 140), SoldierType.Swordsman, player: 1);
        for (int i = 0; i < allied; i++)
            world.SpawnSoldier(new GridPoint(alliesAt.X + i % 4, alliesAt.Y + i / 4), SoldierType.Swordsman, player: 2);
        var ai = new AiPlayer(world, Romans, 0, character: character);
        world.AiPlayers.Add(ai);
        return (world, ai);
    }

    [Fact]
    public void Allies_at_the_target_make_a_campaign_worth_it()
    {
        const string character = "standard@attack.minSoldiers=1@attack.armyRatio=0";
        // One against the garrison of one is short of attack.ratio (1.5).
        var (alone, aloneAi) = Setup(character, idle: 1, allied: 0, new GridPoint(0, 0));
        Run(alone, 20 * World.TicksPerSecond);
        Assert.Null(aloneAi.CampaignAgainst);

        var (world, ai) = Setup(character, idle: 1, allied: 4, new GridPoint(38, 50));
        Run(world, 5 * World.TicksPerSecond, () => ai.CampaignAgainst is not null);
        foreach (string line in ai.Log)
            output.WriteLine(line);
        Assert.Equal(1, ai.CampaignAgainst);
    }

    [Fact]
    public void Superiority_compares_team_against_team()
    {
        // 6 against 4 alone; with the ally's 6 far away 12 against 4.
        const string character = "standard@superiority.startMinutes=0";
        var (alone, aloneAi) = Setup(character, idle: 5, allied: 0, new GridPoint(0, 0), enemyArmy: 3);
        Run(alone, 20 * World.TicksPerSecond);
        Assert.DoesNotContain(aloneAi.Log, l => l.Contains("outnumbers"));

        var (world, ai) = Setup(character, idle: 5, allied: 6, new GridPoint(120, 20), enemyArmy: 3);
        Run(world, 5 * World.TicksPerSecond, () => ai.CampaignAgainst is not null);
        foreach (string line in ai.Log)
            output.WriteLine(line);
        Assert.Contains(ai.Log, l => l.Contains("outnumbers player 2"));
    }

    private static void Run(World world, int ticks, Func<bool>? stopWhen = null)
    {
        for (int t = 0; t < ticks && stopWhen?.Invoke() != true; t++)
            world.Step();
    }
}
