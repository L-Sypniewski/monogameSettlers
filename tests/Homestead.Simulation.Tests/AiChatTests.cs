using System.Text.Json;
using Homestead.Simulation.Ai;
using Homestead.Simulation.Buildings;
using Homestead.Simulation.Map;
using Xunit.Abstractions;

namespace Homestead.Simulation.Tests;

/// <summary>What the chat with the computer players rests on: the situation it judges by and the challenge it may take up.</summary>
public class AiChatTests(ITestOutputHelper output)
{
    private static readonly BuildingCatalog Romans = BuildingCatalog.Load(Civilization.Roman);

    /// <summary>
    /// Player 1 (index 0, the AI) with a tower at 40,80 and five swordsmen outside; player 2 with a tower at 40,130 held by one
    /// and a big army far off at 140,20 – too strong as a whole for a campaign of the AI's own.
    /// </summary>
    private static (World World, AiPlayer Ai, Building EnemyTower) Standoff(int idle = 5)
    {
        var map = new GameMap(160);
        var world = new World(map, TestConfig.NoTools) { TerritoryRules = true, PlayerCount = 2 };
        var tower = world.PlaceBuilding(Romans["tower"], 0, new GridPoint(40, 80), force: true)!;
        world.SpawnSoldier(tower.Door, SoldierType.Swordsman);
        for (int i = 0; i < idle; i++)
            world.SpawnSoldier(new GridPoint(44 + i, 84), SoldierType.Swordsman);
        var enemyTower = world.PlaceBuilding(Romans["tower"], 1, new GridPoint(40, 130), force: true)!;
        world.SpawnSoldier(enemyTower.Door, SoldierType.Swordsman, player: 1);
        for (int i = 0; i < 12; i++)
            world.SpawnSoldier(new GridPoint(130 + i % 6, 20 + i / 6), SoldierType.Swordsman, player: 1);
        var ai = new AiPlayer(world, Romans, 0, character: "standard");
        world.AiPlayers.Add(ai);
        return (world, ai, enemyTower);
    }

    [Fact]
    public void A_challenge_starts_a_campaign_against_the_challenger_though_its_whole_army_is_stronger()
    {
        var (world, ai, enemyTower) = Standoff();
        Run(world, 20 * World.TicksPerSecond);
        Assert.Null(ai.CampaignAgainst);

        Assert.True(ai.Challenge(1));
        Assert.Equal(1, ai.ChallengedBy);
        Run(world, 2 * World.TicksPerSecond);
        foreach (string line in ai.Log)
            output.WriteLine(line);
        Assert.Null(ai.ChallengedBy);
        Assert.Equal(1, ai.CampaignAgainst);
        Assert.Contains(ai.Log, l => l.Contains("takes up the challenge of player 2"));
        // An ordinary campaign from here: it gathers, then attacks the tower.
        Run(world, 90 * World.TicksPerSecond, () => ai.CampaignAttacking);
        Assert.True(ai.CampaignAttacking);
        Assert.Contains(world.Settlers, s => s.Player == 0 && world.Military.AttackTarget(s) == enemyTower);
        // No second challenge while it runs.
        Assert.False(ai.Challenge(1));
    }

    [Fact]
    public void A_skirmisher_takes_up_a_challenge_far_beyond_its_reach_with_half_its_idle_fighters()
    {
        // The skirmisher only campaigns within 60 fields of its land (attack.maxDistance); a challenge goes anyway (a save:
        // the Amazons promised an attack 422 fields away twice and sent nobody).
        var map = new GameMap(160);
        var world = new World(map, TestConfig.NoTools) { TerritoryRules = true, PlayerCount = 2 };
        var tower = world.PlaceBuilding(Romans["tower"], 0, new GridPoint(40, 20), force: true)!;
        world.SpawnSoldier(tower.Door, SoldierType.Swordsman);
        for (int i = 0; i < 8; i++)
            world.SpawnSoldier(new GridPoint(42 + i, 26), SoldierType.Swordsman);
        var enemyTower = world.PlaceBuilding(Romans["tower"], 1, new GridPoint(40, 150), force: true)!;
        world.SpawnSoldier(enemyTower.Door, SoldierType.Swordsman, player: 1);
        var ai = new AiPlayer(world, Romans, 0, character: "skirmisher");
        world.AiPlayers.Add(ai);
        Run(world, 20 * World.TicksPerSecond);
        Assert.Null(ai.CampaignAgainst);
        Assert.True(ai.CanTakeUpChallenge(1));

        Assert.True(ai.Challenge(1));
        Run(world, 2 * World.TicksPerSecond);
        foreach (string line in ai.Log)
            output.WriteLine(line);
        Assert.Equal(1, ai.CampaignAgainst);
        string gathers = Assert.Single(ai.Log, l => l.Contains("gathers"));
        int count = int.Parse(gathers.Split("gathers ")[1].Split(' ')[0]);
        Assert.InRange(count, 4, 8);
        Assert.False(ai.CanTakeUpChallenge(1));
    }

    [Fact]
    public void The_situation_tells_when_a_computer_player_cannot_attack_and_keeps_umlauts()
    {
        var (armed, _, _) = Standoff();
        Run(armed, 2 * World.TicksPerSecond);
        Assert.Null(AiSituation.Build(armed, 1).Players.Single(p => p.Player == 1).CannotAttackSpeaker);
        // Nobody but the garrison: nothing to attack with.
        var (world, ai, _) = Standoff(idle: 0);
        Run(world, 2 * World.TicksPerSecond);
        world.Say(1, "Ätsch, du traust dich nicht!");
        Assert.False(ai.CanTakeUpChallenge(1));
        Assert.True(AiSituation.Build(world, 1).Players.Single(p => p.Player == 1).CannotAttackSpeaker);
        Assert.Contains("Ätsch", AiSituation.Describe(world, 1));
    }

    [Fact]
    public void An_ally_cannot_challenge()
    {
        var (world, ai, _) = Standoff();
        world.Teams.AddRange([1, 1]);
        Assert.False(ai.Challenge(1));
        Assert.Null(ai.ChallengedBy);
    }

    [Fact]
    public void The_situation_tells_strength_against_the_speaker_distance_and_what_the_ai_is_up_to()
    {
        var (world, ai, _) = Standoff();
        Run(world, 2 * World.TicksPerSecond);
        world.Say(1, "Come and get me, coward!");
        ai.Challenge(1);

        var report = AiSituation.Build(world, speaker: 1);
        Assert.Equal(2, report.Speaker);
        var computer = report.Players.Single(p => p.Player == 1);
        var human = report.Players.Single(p => p.Player == 2);
        Assert.Equal("standard", computer.Character);
        Assert.Null(human.Character);
        Assert.Equal(6, computer.Soldiers.Total);
        Assert.Equal(13, human.Soldiers.Swordsmen);
        Assert.Equal(0.5, computer.PowerVsSpeaker!.Value, 2);
        Assert.Null(computer.Soldiers.Bowmen);
        Assert.Null(computer.Team);
        Assert.Null(computer.Defeated);
        Assert.Null(human.PowerVsSpeaker);
        Assert.InRange(computer.DistanceToSpeaker!.Value, 40, 60);
        Assert.Equal("about to attack player 2", computer.Doing);
        Assert.Equal(1, computer.MilitaryBuildings);
        Assert.True(computer.Land > 0);
        Assert.Equal([new AiSituation.ChatLine(0, 2, "Come and get me, coward!")], report.Chat);

        string json = AiSituation.Describe(world, 1);
        output.WriteLine(json);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(2, document.RootElement.GetProperty("speaker").GetInt32());
        Assert.Equal(2, document.RootElement.GetProperty("players").GetArrayLength());
    }

    private static void Run(World world, int ticks, Func<bool>? stopWhen = null)
    {
        for (int t = 0; t < ticks && stopWhen?.Invoke() != true; t++)
            world.Step();
    }
}
