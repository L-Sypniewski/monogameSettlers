using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Homestead.Client;

/// <summary>
/// Short notes in white at the bottom left of the game view (a computer player gave up, the game was saved …), with a
/// dot in the player's color before those about a player (as in the original): a new
/// one slides in from below and pushes the older ones up; each fades out after <see cref="LifeSeconds"/>, the oldest
/// leave at the top beyond <see cref="MaxLines"/>. Times are the game's real seconds (<c>MapGame._now</c>).
/// </summary>
public sealed class InfoFeed
{
    public const double LifeSeconds = 60;
    public const int MaxLines = 6;
    private const double SlideSeconds = 0.25;
    private const double FadeSeconds = 2;

    private readonly List<(string Text, Color? Dot, double At)> _lines = new();

    /// <param name="dot">The color of the player the note is about (none: no dot).</param>
    public void Add(string text, double now, Color? dot = null)
    {
        _lines.Add((text, dot, now));
        if (_lines.Count > MaxLines)
            _lines.RemoveAt(0);
    }

    public void Clear() => _lines.Clear();

    /// <summary>The notes from <paramref name="left"/> upwards from <paramref name="bottom"/>, the newest at the bottom.</summary>
    public void Draw(SpriteBatch batch, Texture2D pixel, UiFont font, float left, float bottom, int size, double now)
    {
        _lines.RemoveAll(l => now - l.At >= LifeSeconds);
        if (_lines.Count == 0)
            return;
        float height = font.Measure("Ag", size).Y + 4;
        // The whole stack rises with the newest note sliding in.
        float slide = (float)Math.Clamp((now - _lines[^1].At) / SlideSeconds, 0, 1);
        float y = bottom - height * slide;
        for (int i = _lines.Count - 1; i >= 0; i--)
        {
            var (text, dot, at) = _lines[i];
            float alpha = (float)Math.Clamp((LifeSeconds - (now - at)) / FadeSeconds, 0, 1);
            if (i == _lines.Count - 1)
                alpha *= slide;
            var position = new Vector2(MathF.Round(left), MathF.Round(y));
            if (dot is { } color)
                DrawDot(batch, pixel, position, height - 4, color, alpha);
            position.X += (int)(height * 0.75f);
            font.Draw(batch, text, position + new Vector2(1, 1), Color.Black * (0.8f * alpha), size);
            font.Draw(batch, text, position, Color.White * alpha, size);
            y -= height;
        }
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
