using Homestead.Simulation;

namespace Homestead.Client;

/// <summary>
/// The line the player types into the global chat, under the info feed (Enter opens it): typed characters, Backspace,
/// Ctrl+V; ↑/↓ go through the lines sent before, as in a terminal.
/// </summary>
public sealed class ChatInput
{
    private readonly List<string> _sent = new();
    // Index into _sent while going through it; _sent.Count: the new line.
    private int _recall;

    public bool IsOpen { get; private set; }

    public string Text { get; private set; } = "";

    public void Open()
    {
        IsOpen = true;
        Text = "";
        _recall = _sent.Count;
    }

    public void Close()
    {
        IsOpen = false;
        Text = "";
    }

    public void Type(char c)
    {
        if (!char.IsControl(c) && Text.Length < ChatLog.MaxLength)
            Text += c;
    }

    /// <summary>Pasted text in one line (line breaks become spaces).</summary>
    public void Paste(string text)
    {
        foreach (char c in text)
            Type(c is '\r' or '\n' or '\t' ? ' ' : c);
    }

    public void Back()
    {
        if (Text.Length > 0)
            Text = Text[..^1];
    }

    /// <summary>Goes <paramref name="step"/> lines back (−1) or forward (+1) through those sent; past the newest: empty again.</summary>
    public void Recall(int step)
    {
        _recall = Math.Clamp(_recall + step, 0, _sent.Count);
        Text = _recall < _sent.Count ? _sent[_recall] : "";
    }

    /// <summary>Closes the line and returns what was typed (remembered for <see cref="Recall"/> unless blank or the same as the last).</summary>
    public string Submit()
    {
        string text = Text;
        Close();
        if (text.Trim().Length > 0 && (_sent.Count == 0 || _sent[^1] != text))
            _sent.Add(text);
        return text;
    }
}
