using System.Text.Json;
using System.Text.Json.Serialization;
using Homestead.Simulation.Buildings;
using Homestead.Simulation.Pathfinding;

namespace Homestead.Simulation.Ai;

/// <summary>
/// The rough situation of the game for the computer players talking in the chat (<c>AiChat</c> in the client): per
/// player its people, character, army, land and buildings now and some minutes ago, the fights so far, who it attacks,
/// how strong it is against the speaker and how far apart they are; plus the latest chat lines. Players are numbered
/// from 1 as the game shows them. Only reads the world.
/// </summary>
public static class AiSituation
{
    /// <summary>How far back the trend looks.</summary>
    public const int TrendMinutes = 5;

    /// <summary>The latest chat lines given along.</summary>
    public const int ChatLines = 16;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public sealed record Report(int Minutes, int Speaker, IReadOnlyList<PlayerState> Players, IReadOnlyList<ChatLine> Chat);

    public sealed record PlayerState(
        int Player, string People, string? Character, IReadOnlyList<string>? Traits, int Team, bool Defeated, bool AllyOfSpeaker,
        Army Soldiers, float CombatStrength, double ArmyPower, double? PowerVsSpeaker, int? DistanceToSpeaker, int EnemySoldiersOnLand,
        int Land, int MilitaryBuildings, int Buildings, int Settlers, int Mana, Fights Fights, Trend? Trend, string? Doing);

    public sealed record Army(int Total, int Swordsmen, int Bowmen, int Pikemen);

    /// <summary>Running totals since the start of the game.</summary>
    public sealed record Fights(int Kills, int Losses, int BuildingsCaptured, int BuildingsLost);

    /// <summary>Change against <see cref="TrendMinutes"/> minutes ago (from the statistics' samples).</summary>
    public sealed record Trend(int Soldiers, int Land, int MilitaryBuildings, int Kills, int Losses, int BuildingsCaptured, int BuildingsLost);

    public sealed record ChatLine(int Minute, int Player, string Text);

    /// <summary>The situation as <paramref name="speaker"/> (0-based) sees it to talk about, as compact JSON.</summary>
    public static string Describe(World world, int speaker) => JsonSerializer.Serialize(Build(world, speaker), Json);

    public static Report Build(World world, int speaker)
    {
        int players = Math.Max(world.PlayerCount, world.AiPlayers.Select(a => a.Player + 1).DefaultIfEmpty(0).Max());
        players = Math.Max(players, speaker + 1);
        var land = new int[players];
        if (world.Map is { } map)
            for (int y = 0; y < map.Size; y++)
                for (int x = 0; x < map.Size; x++)
                    if (map.OwnerAt(new GridPoint(x, y)) is { } owner && owner < players)
                        land[owner]++;
        var soldiers = world.Settlers.Where(s => s.Profession == Profession.Soldier && !s.IsDead).ToList();
        double Power(int player) => soldiers.Count(s => s.Player == player) * world.Military.CombatStrength(player);
        var speakerBuildings = world.Buildings.Where(b => b.Exists && b.Player == speaker).Select(b => b.Position).ToList();
        int ticksBack = TrendMinutes * 60 * World.TicksPerSecond;

        var states = new List<PlayerState>();
        for (int player = 0; player < players; player++)
        {
            var ai = world.AiPlayers.FirstOrDefault(a => a.Player == player);
            var buildings = world.Buildings.Where(b => b.Exists && b.Player == player).ToList();
            var military = buildings.Where(b => b.State == BuildingState.Finished && b.Definition.Occupiers.Count > 0).ToList();
            var own = soldiers.Where(s => s.Player == player).ToList();
            var samples = world.History.Samples(player);
            var latest = samples.Count > 0 ? samples[^1] : null;
            var before = samples.LastOrDefault(s => s.Tick <= world.Tick - ticksBack);
            int? distance = player == speaker || military.Count == 0 || speakerBuildings.Count == 0
                ? null
                : military.Min(b => speakerBuildings.Min(p => PathFinder.Distance(b.Position, p)));
            double power = Power(player), speakerPower = Power(speaker);
            int invaders = world.Map is { } m
                ? soldiers.Count(s => !s.IsInside && !world.AreAllies(s.Player, player) && m.OwnerAt(s.Position) == player)
                : 0;
            states.Add(new PlayerState(
                Player: player + 1,
                People: world.PeopleOf(player).ToString(),
                Character: ai?.Character.Id,
                Traits: ai?.Character.Traits.Select(t => t.Id).ToList(),
                Team: world.TeamOf(player),
                Defeated: world.Goals?.IsDefeated(player) == true,
                AllyOfSpeaker: player != speaker && world.AreAllies(player, speaker),
                Soldiers: new Army(own.Count, own.Count(s => s.SoldierType == SoldierType.Swordsman),
                    own.Count(s => s.SoldierType == SoldierType.Bowman), own.Count(s => s.SoldierType == SoldierType.Pikeman)),
                CombatStrength: MathF.Round(world.Military.CombatStrength(player), 2),
                ArmyPower: Math.Round(power, 1),
                PowerVsSpeaker: player == speaker ? null : speakerPower > 0 ? Math.Round(power / speakerPower, 2) : null,
                DistanceToSpeaker: distance,
                EnemySoldiersOnLand: invaders,
                Land: land[player],
                MilitaryBuildings: military.Count,
                Buildings: buildings.Count(b => b.State == BuildingState.Finished),
                Settlers: world.Settlers.Count(s => s.Player == player && !s.IsDead),
                Mana: world.Magic.Mana(player),
                Fights: new Fights(latest?.Kills ?? 0, latest?.Losses ?? 0, latest?.Captured ?? 0, latest?.Lost ?? 0),
                Trend: latest is null || before is null ? null : new Trend(
                    own.Count - before.Soldiers, land[player] - before.Land, military.Count - before.MilitaryBuildings,
                    latest.Kills - before.Kills, latest.Losses - before.Losses, latest.Captured - before.Captured, latest.Lost - before.Lost),
                Doing: ai is null ? null : Doing(ai)));
        }
        var chat = world.Chat.Messages.TakeLast(ChatLines)
            .Select(c => new ChatLine((int)(c.Tick / (60 * World.TicksPerSecond)), c.Player + 1, c.Text)).ToList();
        return new Report((int)(world.Tick / (60 * World.TicksPerSecond)), speaker + 1, states, chat);
    }

    /// <summary>What the computer player is up to against others, in a few words.</summary>
    private static string? Doing(AiPlayer ai) =>
        ai.AssaultOn is { } assaulted ? $"all-out assault on player {assaulted + 1}"
        : ai.ChallengedBy is { } challenger ? $"about to attack player {challenger + 1}"
        : ai.CampaignAgainst is { } target ? $"campaign against player {target + 1} ({(ai.CampaignAttacking ? "attacking" : "gathering")})"
        : null;
}
