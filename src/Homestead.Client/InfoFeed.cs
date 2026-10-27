using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Homestead.Client;

/// <summary>
/// Short notes in white at the bottom left of the game view (a computer player gave up, the game was saved …), with a
/// dot in the player's color before those about a player (as in the original): a new
/// one slides in from below and pushes the older ones up; each fades out after <see cref="LifeSeconds"/>, the oldest
/// leave at the top beyond <see cref="MaxLines"/>. Times are the game's real seconds (<c>MapGame._now</c>).
/// With the chat line open (<see cref="ChatInput"/>) it is a terminal: the line at the bottom, above it the last
/// <see cref="OpenLines"/> notes, also faded ones, on a dark ground.
/// </summary>
public sealed class InfoFeed
{
    public const double LifeSeconds = 60;
    public const int MaxLines = 6;
    public const int OpenLines = 12;
    private const int HistoryLines = 100;
    private const double SlideSeconds = 0.25;
    private const double FadeSeconds = 2;

    private readonly List<(string Text, Color? Dot, double At)> _lines = new();

    /// <param name="dot">The color of the player the note is about (none: no dot).</param>
    public void Add(string text, double now, Color? dot = null)
    {
        _lines.Add((text, dot, now));
        if (_lines.Count > HistoryLines)
            _lines.RemoveAt(0);
    }

    public void Clear() => _lines.Clear();

    /// <summary>
    /// The notes from <paramref name="left"/> upwards from <paramref name="bottom"/>, the newest at the bottom; with
    /// <paramref name="input"/> (the chat line is open) that line under them, at most <paramref name="width"/> wide.
    /// </summary>
    public void Draw(SpriteBatch batch, Texture2D pixel, UiFont font, float left, float bottom, float width, int size, double now,
        string? input = null)
    {
        float height = font.Measure("Ag", size).Y + 4;
        if (input is not null)
        {
            DrawOpen(batch, pixel, font, left, bottom, width, size, height, now, input);
            return;
        }
        // The newest MaxLines not faded yet.
        int first = _lines.Count;
        while (first > 0 && _lines.Count - first < MaxLines && now - _lines[first - 1].At < LifeSeconds)
            first--;
        if (first == _lines.Count)
            return;
        // The whole stack rises with the newest note sliding in.
        float slide = (float)Math.Clamp((now - _lines[^1].At) / SlideSeconds, 0, 1);
        float y = bottom - height * slide;
        for (int i = _lines.Count - 1; i >= first; i--)
        {
            float alpha = (float)Math.Clamp((LifeSeconds - (now - _lines[i].At)) / FadeSeconds, 0, 1);
            if (i == _lines.Count - 1)
                alpha *= slide;
            DrawLine(batch, pixel, font, _lines[i].Text, _lines[i].Dot, left, y, size, height, alpha);
            y -= height;
        }
    }

    private void DrawOpen(SpriteBatch batch, Texture2D pixel, UiFont font, float left, float bottom, float width, int size, float height,
        double now, string input)
    {
        int shown = Math.Min(OpenLines, _lines.Count);
        width = Math.Min(width, size * 40);
        const int pad = 4;
        float top = bottom - height * (shown + 1);
        batch.Draw(pixel, new Rectangle((int)left - pad, (int)top - pad, (int)width + 2 * pad, (int)(bottom - top) + 2 * pad), Color.Black * 0.55f);
        // The typed line, its start cut off once it is wider than the ground; a blinking caret after it.
        string prompt = "> ";
        float room = width - font.Measure(prompt + "_", size).X;
        int from = 0;
        while (from < input.Length && font.Measure(input[from..], size).X > room)
            from++;
        string line = prompt + input[from..];
        float y = bottom - height;
        DrawText(batch, font, line, new Vector2(MathF.Round(left), MathF.Round(y)), size, 1);
        if (now % 1 < 0.5)
            DrawText(batch, font, "_", new Vector2(MathF.Round(left + font.Measure(line, size).X), MathF.Round(y)), size, 1);
        for (int i = _lines.Count - 1; i >= _lines.Count - shown; i--)
        {
            y -= height;
            DrawLine(batch, pixel, font, _lines[i].Text, _lines[i].Dot, left, y, size, height, 1);
        }
    }

    private static void DrawLine(SpriteBatch batch, Texture2D pixel, UiFont font, string text, Color? dot, float left, float y, int size,
        float height, float alpha)
    {
        var position = new Vector2(MathF.Round(left), MathF.Round(y));
        if (dot is { } color)
            DrawDot(batch, pixel, position, height - 4, color, alpha);
        position.X += (int)(height * 0.75f);
        DrawText(batch, font, text, position, size, alpha);
    }

    private static void DrawText(SpriteBatch batch, UiFont font, string text, Vector2 position, int size, float alpha)
    {
        font.Draw(batch, text, position + new Vector2(1, 1), Color.Black * (0.8f * alpha), size);
        font.Draw(batch, text, position, Color.White * alpha, size);
    }

    /// <summary>A round dot in front of a line of <paramref name="lineHeight"/>, lit from the top left, with a dark rim.</summary>
    private static void DrawDot(SpriteBatch batch, Texture2D pixel, Vector2 line, float lineHeight, Color color, float alpha)
    {
        int diameter = Math.Max(5, (int)(lineHeight * 0.55f)) | 1;
        int left = (int)line.X, top = (int)(line.Y + (lineHeight - diameter) / 2 + 1);
        float r = diameter / 2f;
        for (int y = 0; y < diameter; y++)
        for (int x = 0; x < diameter; x++)
        {
            float dx = x + 0.5f - r, dy = y + 0.5f - r, d = MathF.Sqrt(dx * dx + dy * dy);
            if (d > r)
                continue;
            // Lighter towards the top left, darker towards the rim.
            float light = Math.Clamp(1 - MathF.Sqrt((dx + r * 0.35f) * (dx + r * 0.35f) + (dy + r * 0.35f) * (dy + r * 0.35f)) / r, 0, 1);
            var c = d > r - 1 ? Color.Lerp(color, Color.Black, 0.6f) : Color.Lerp(Color.Lerp(color, Color.Black, 0.25f), Color.White, light * 0.7f);
            batch.Draw(pixel, new Rectangle(left + x, top + y, 1, 1), c * alpha);
        }
    }
}
