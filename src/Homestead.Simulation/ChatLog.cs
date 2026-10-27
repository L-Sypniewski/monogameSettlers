namespace Homestead.Simulation;

/// <summary>A line said in the global chat: by whom and at which tick.</summary>
public readonly record struct ChatMessage(long Tick, int Player, string Text);

/// <summary>
/// The game's global chat (<see cref="World.Chat"/>): what the players said, in order, saved with the game. Every player may
/// write; the client shows new lines as they come (<see cref="Messages"/> only grows).
/// </summary>
public sealed class ChatLog
{
    /// <summary>The longest line kept; longer ones are cut.</summary>
    public const int MaxLength = 200;

    private readonly List<ChatMessage> _messages = new();

    public IReadOnlyList<ChatMessage> Messages => _messages;

    /// <summary>Adds what <paramref name="player"/> said (trimmed, cut at <see cref="MaxLength"/>); blank text says nothing.</summary>
    public ChatMessage? Say(int player, string text, long tick)
    {
        text = text.Trim();
        if (text.Length == 0)
            return null;
        if (text.Length > MaxLength)
            text = text[..MaxLength];
        var message = new ChatMessage(tick, player, text);
        _messages.Add(message);
        return message;
    }
}
