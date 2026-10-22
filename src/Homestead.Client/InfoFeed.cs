using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Homestead.Client;

/// <summary>
/// Short notes in white at the bottom left of the game view (a computer player gave up, the game was saved …): a new
/// one slides in from below and pushes the older ones up; each fades out after <see cref="LifeSeconds"/>, the oldest
/// leave at the top beyond <see cref="MaxLines"/>. Times are the game's real seconds (<c>MapGame._now</c>).
/// </summary>
public sealed class InfoFeed
{
    public const double LifeSeconds = 60;
    public const int MaxLines = 6;
    private const double SlideSeconds = 0.25;
    private const double FadeSeconds = 2;

    private readonly List<(string Text, double At)> _lines = new();

    public void Add(string text, double now)
    {
        _lines.Add((text, now));
        if (_lines.Count > MaxLines)
            _lines.RemoveAt(0);
    }

    public void Clear() => _lines.Clear();

    /// <summary>The notes from <paramref name="left"/> upwards from <paramref name="bottom"/>, the newest at the bottom.</summary>
    public void Draw(SpriteBatch batch, UiFont font, float left, float bottom, int size, double now)
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
            var (text, at) = _lines[i];
            float alpha = (float)Math.Clamp((LifeSeconds - (now - at)) / FadeSeconds, 0, 1);
            if (i == _lines.Count - 1)
                alpha *= slide;
            var position = new Vector2(MathF.Round(left), MathF.Round(y));
            font.Draw(batch, text, position + new Vector2(1, 1), Color.Black * (0.8f * alpha), size);
            font.Draw(batch, text, position, Color.White * alpha, size);
            y -= height;
        }
    }
}
