using System.Collections.Concurrent;
using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using Homestead.Assets;
using Homestead.Simulation;
using Homestead.Simulation.Ai;
using Homestead.Simulation.Buildings;

namespace Homestead.Client;

/// <summary>
/// The computer players talk back in the chat: every line the own player says goes, with the situation
/// (<see cref="AiSituation"/>), to the Claude API, which answers for all computer players at once through two tools –
/// <c>say</c> (a chat line) and <c>attack</c> (<see cref="AiPlayer.Challenge"/>, or <see cref="AiPlayer.Assault"/>
/// all in). Only with an API key: section <c>aiChat</c> of settings.json (<c>apiKey</c>, <c>model</c>, <c>effort</c>)
/// or <c>ANTHROPIC_API_KEY</c>. Haiku answers without thinking (cheap and quick; other models always think). The tokens
/// of every request go to the console. The request runs in the background, one at a time (lines said meanwhile go along
/// with the next); its answers are applied on the game's thread (<see cref="Apply"/>). Prompt: data/ai-chat.json.
/// </summary>
public sealed class AiChat
{
    private const string Section = "aiChat";
    private const string DefaultModel = "claude-haiku-5-5";

    private readonly AnthropicClient _client;
    private readonly string _model;
    private readonly string _effort;
    private readonly string _system;
    private readonly IReadOnlyDictionary<string, string> _characters;
    private readonly long _attackCooldownTicks;
    private readonly ConcurrentQueue<Answer> _answers = new();
    private readonly Dictionary<int, long> _lastAttack = new();
    private Task? _running;
    private bool _pending;
    // The game the request under way is about: answers for an earlier one (loaded or started anew since) are dropped.
    private World? _asked;

    private abstract record Answer;
    private sealed record Said(int Player, string Text) : Answer;
    private sealed record Attacked(int Player, int Target, bool AllIn) : Answer;
    private sealed record Failed(string Reason) : Answer;

    private AiChat(string apiKey, string model, string effort, JsonElement data)
    {
        _client = new AnthropicClient { ApiKey = apiKey };
        _model = model;
        _effort = effort;
        _system = string.Join("\n", data.GetProperty("system").EnumerateArray().Select(l => l.GetString()));
        _characters = data.GetProperty("characters").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
        _attackCooldownTicks = data.GetProperty("attackCooldownSeconds").GetInt32() * World.TicksPerSecond;
    }

    /// <summary>The chat with the settings' key, or null without one (then the computer players stay silent).</summary>
    public static AiChat? Create(string? settingsPath = null)
    {
        var section = SettingsFile.ReadSection(Section, settingsPath);
        string? Value(string name) =>
            section is { ValueKind: JsonValueKind.Object } s && s.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString()
                : null;
        string? key = Value("apiKey");
        if (string.IsNullOrWhiteSpace(key))
            key = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (string.IsNullOrWhiteSpace(key))
            return null;
        string path = Path.Combine(BuildingCatalog.DefaultDataDirectory, "ai-chat.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return new AiChat(key, Value("model") ?? DefaultModel, Value("effort") ?? "low", document.RootElement.Clone());
    }

    /// <summary>The own player said something: the computer players hear it (now, or after the request under way).</summary>
    public void Heard(World world)
    {
        if (!world.AiPlayers.Any(ai => ai.Player != 0 && world.Goals?.IsDefeated(ai.Player) != true))
            return;
        if (_running is { IsCompleted: false })
        {
            _pending = true;
            return;
        }
        Ask(world);
    }

    /// <summary>Once a frame on the game's thread: applies the answers come in; returns lines for the info feed (failures).</summary>
    public IEnumerable<string> Apply(World world)
    {
        var notes = new List<string>();
        while (_answers.TryDequeue(out var answer))
            switch (_asked == world ? answer : null)
            {
                case Said said when IsVoice(world, said.Player):
                    world.Say(said.Player, said.Text);
                    break;
                case Attacked attack when IsVoice(world, attack.Player) && attack.Target >= 0 && attack.Target < Math.Max(world.PlayerCount, 1)
                                          && world.Goals?.IsDefeated(attack.Target) != true:
                    var ai = world.AiPlayers.First(a => a.Player == attack.Player);
                    if (_lastAttack.TryGetValue(ai.Player, out long last) && world.Tick - last < _attackCooldownTicks)
                        break;
                    if (attack.AllIn)
                        ai.Assault(attack.Target);
                    else
                        ai.Challenge(attack.Target);
                    if (ai.AssaultOn == attack.Target || ai.ChallengedBy == attack.Target)
                        _lastAttack[ai.Player] = world.Tick;
                    break;
                case Failed failed:
                    notes.Add(Lang.T("chat.aiFailed", failed.Reason));
                    break;
            }
        if (_pending && _running is { IsCompleted: true })
        {
            _pending = false;
            Ask(world);
        }
        return notes;
    }

    /// <summary>A computer player still in the game (not the own player 0).</summary>
    private static bool IsVoice(World world, int player) =>
        player != 0 && world.AiPlayers.Any(a => a.Player == player) && world.Goals?.IsDefeated(player) != true;

    private void Ask(World world)
    {
        // The world is read here, on the game's thread; only the request runs in the background.
        _asked = world;
        string situation = AiSituation.Describe(world, 0);
        var voices = world.AiPlayers.Where(ai => IsVoice(world, ai.Player))
            .Select(ai => $"- player {ai.Player + 1}: {_characters.GetValueOrDefault(ai.Character.Id, _characters["standard"])}");
        string request = $"Language: {(Lang.Current == Language.English ? "English" : "German")}\n" +
                         $"Computer players and how they talk:\n{string.Join("\n", voices)}\n" +
                         $"<situation>\n{situation}\n</situation>";
        _running = Task.Run(() => Request(request));
    }

    private async Task Request(string request)
    {
        try
        {
            var response = await _client.Messages.Create(new MessageCreateParams
            {
                Model = _model,
                MaxTokens = 4096,
                OutputConfig = new OutputConfig { Effort = _effort switch { "medium" => Effort.Medium, "high" => Effort.High, _ => Effort.Low } },
                Thinking = _model.Contains("haiku") ? new ThinkingConfigDisabled() : null,
                System = new List<TextBlockParam> { new() { Text = _system, CacheControl = new CacheControlEphemeral() } },
                Tools = [SayTool, AttackTool],
                Messages = [new UserMessageParam { Content = request }],
            });
            var usage = response.Usage;
            Console.WriteLine($"AI chat: {usage.InputTokens} in (cache read {usage.CacheReadInputTokens ?? 0}, written {usage.CacheCreationInputTokens ?? 0}), " +
                              $"{usage.OutputTokens} out, {_model}");
            if (response.StopReason == "refusal")
                return;
            foreach (var block in response.Content)
            {
                if (!block.TryPickToolUse(out ToolUseBlock? tool))
                    continue;
                var input = tool.Input;
                if (!input.TryGetValue("player", out var player) || !player.TryGetInt32(out int speaker))
                    continue;
                if (tool.Name == "say" && input.TryGetValue("text", out var text) && text.GetString() is { Length: > 0 } line)
                    _answers.Enqueue(new Said(speaker - 1, line));
                else if (tool.Name == "attack" && input.TryGetValue("target", out var target) && target.TryGetInt32(out int against))
                    _answers.Enqueue(new Attacked(speaker - 1, against - 1,
                        input.TryGetValue("all_in", out var allIn) && allIn.ValueKind == JsonValueKind.True));
            }
        }
        catch (AnthropicUnauthorizedException)
        {
            _answers.Enqueue(new Failed("API key rejected"));
        }
        catch (AnthropicRateLimitException)
        {
            _answers.Enqueue(new Failed("rate limit"));
        }
        catch (AnthropicApiException e)
        {
            _answers.Enqueue(new Failed(e.Message));
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            _answers.Enqueue(new Failed(e.Message));
        }
    }

    private static Tool Schema(string name, string description, Dictionary<string, object> properties) => new()
    {
        Name = name,
        Description = description,
        Strict = true,
        InputSchema = InputSchema.FromRawUnchecked(new Dictionary<string, JsonElement>
        {
            ["type"] = JsonSerializer.SerializeToElement("object"),
            ["properties"] = JsonSerializer.SerializeToElement(properties),
            ["required"] = JsonSerializer.SerializeToElement(properties.Keys),
            ["additionalProperties"] = JsonSerializer.SerializeToElement(false),
        }),
    };

    private static readonly Tool SayTool = Schema("say", "One computer player writes a line in the global chat.", new()
    {
        ["player"] = new { type = "integer", description = "Number of the computer player speaking." },
        ["text"] = new { type = "string", description = "The chat line, under 100 characters." },
    });

    private static readonly Tool AttackTool = Schema("attack",
        "One computer player sends soldiers against another player: a campaign against a weakly held military building (retreats when beaten), or all_in: everything, until that player is gone.",
        new()
        {
            ["player"] = new { type = "integer", description = "Number of the computer player attacking." },
            ["target"] = new { type = "integer", description = "Number of the player attacked." },
            ["all_in"] = new { type = "boolean", description = "Everything, never falling back." },
        });
}
