using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Homestead.Client;

/// <summary>
/// Short notes in white at the bottom left of the game view (a computer player gave up, the game was saved …), with a
/// dot in the player's color before those about a player (as in the original), player names in their color: a new
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

    private sealed class Note(string text, Color? dot, double at, (string Name, Color Color)[] names)
    {
        public string Text { get; } = text;
        public Color? Dot { get; } = dot;
        public double At { get; } = at;
        public (string Name, Color Color)[] Names { get; } = names;

        /// <summary>Wrapped rows for <see cref="RowsFor"/> (font size, width).</summary>
        public List<List<(string Text, Color? Color)>>? Rows { get; set; }
        public (int Size, float Width) RowsFor { get; set; }
    }

    private readonly List<Note> _lines = new();

    /// <param name="dot">The color of the player the note is about (none: no dot).</param>
    /// <param name="names">Player names drawn in their color wherever they stand in the text.</param>
    public void Add(string text, double now, Color? dot = null, params (string Name, Color Color)[] names)
    {
        _lines.Add(new Note(text, dot, now, names));
        if (_lines.Count > HistoryLines)
            _lines.RemoveAt(0);
    }

    public void Clear() => _lines.Clear();

    /// <summary>
    /// The notes from <paramref name="left"/> upwards from <paramref name="bottom"/>, the newest at the bottom; with
    /// <paramref name="input"/> (the chat line is open) that line under them. Notes wider than the box (at most
    /// <paramref name="width"/>) wrap at spaces onto further rows.
    /// </summary>
    public void Draw(SpriteBatch batch, Texture2D pixel, UiFont font, float left, float bottom, float width, int size, double now,
        string? input = null)
    {
        float height = font.Measure("Ag", size).Y + 4;
        width = Math.Min(width, size * 40);
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
        float y = bottom - height * (Rows(_lines[^1], font, size, width, height).Count - 1) - height * slide;
        for (int i = _lines.Count - 1; i >= first; i--)
        {
            float alpha = (float)Math.Clamp((LifeSeconds - (now - _lines[i].At)) / FadeSeconds, 0, 1);
            if (i == _lines.Count - 1)
                alpha *= slide;
            var rows = Rows(_lines[i], font, size, width, height);
            if (i < _lines.Count - 1)
                y -= height * rows.Count;
            DrawNote(batch, pixel, font, _lines[i], rows, left, y, size, height, alpha);
        }
    }

    private void DrawOpen(SpriteBatch batch, Texture2D pixel, UiFont font, float left, float bottom, float width, int size, float height,
        double now, string input)
    {
        int shown = Math.Min(OpenLines, _lines.Count);
        int rowCount = _lines.Skip(_lines.Count - shown).Sum(n => Rows(n, font, size, width, height).Count);
        const int pad = 4;
        float top = bottom - height * (rowCount + 1);
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
            var rows = Rows(_lines[i], font, size, width, height);
            y -= height * rows.Count;
            DrawNote(batch, pixel, font, _lines[i], rows, left, y, size, height, 1);
        }
    }

    /// <summary>The note from <paramref name="y"/> down, row by row; the dot before the first.</summary>
    private static void DrawNote(SpriteBatch batch, Texture2D pixel, UiFont font, Note note, List<List<(string Text, Color? Color)>> rows,
        float left, float y, int size, float height, float alpha)
    {
        var position = new Vector2(MathF.Round(left), MathF.Round(y));
        if (note.Dot is { } color)
            DrawDot(batch, pixel, position, height - 4, color, alpha);
        for (int r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            float x = position.X + Indent(height) + (r > 0 ? Hang(height) : 0);
            foreach (var (text, pieceColor) in row)
            {
                DrawText(batch, font, text, new Vector2(MathF.Round(x), position.Y), size, alpha, pieceColor);
                x += font.Measure(text, size).X;
            }
            position.Y += height;
        }
    }

    private static int Indent(float height) => (int)(height * 0.75f);

    /// <summary>How much further the wrapped rows of a note stand in than its first.</summary>
    private static int Hang(float height) => (int)(height * 1.25f);

    /// <summary>
    /// The note in rows that fit <paramref name="width"/> (after the dot's room), each in pieces of one color: white, the names
    /// in their color. Wrapped at spaces, the further rows narrower by <see cref="Hang"/>; a word wider than a row is cut.
    /// Kept until the size or width changes.
    /// </summary>
    private static List<List<(string Text, Color? Color)>> Rows(Note note, UiFont font, int size, float width, float height)
    {
        if (note.Rows is { } known && note.RowsFor == (size, width))
            return known;
        float first = width - Indent(height), room = first;
        var rows = new List<List<(string Text, Color? Color)>> { new() };
        float x = 0;
        void Put(string token, Color? color)
        {
            var row = rows[^1];
            if (row.Count > 0 && row[^1].Color == color)
                row[^1] = (row[^1].Text + token, color);
            else
                row.Add((token, color));
            x += font.Measure(token, size).X;
        }
        foreach (var (piece, color) in Pieces(note))
            foreach (string word in SplitAfterSpaces(piece))
            {
                string token = word;
                if (x > 0 && x + font.Measure(token.TrimEnd(), size).X > room)
                {
                    rows.Add(new());
                    x = 0;
                    room = first - Hang(height);
                }
                // Longer than a whole row: cut where it no longer fits.
                while (x == 0 && token.Length > 1 && font.Measure(token.TrimEnd(), size).X > room)
                {
                    int fit = 1;
                    while (fit < token.Length && font.Measure(token[..(fit + 1)], size).X <= room)
                        fit++;
                    Put(token[..fit], color);
                    rows.Add(new());
                    x = 0;
                    room = first - Hang(height);
                    token = token[fit..];
                }
                Put(token, color);
            }
        note.Rows = rows;
        note.RowsFor = (size, width);
        return rows;
    }

    /// <summary>The words of <paramref name="text"/>, each with the spaces after it.</summary>
    private static IEnumerable<string> SplitAfterSpaces(string text)
    {
        int start = 0;
        for (int i = 0; i < text.Length; i++)
            if (text[i] == ' ' && (i + 1 == text.Length || text[i + 1] != ' '))
            {
                yield return text[start..(i + 1)];
                start = i + 1;
            }
        if (start < text.Length)
            yield return text[start..];
    }

    /// <summary>The note's text in pieces: white, the names in their color (from the earliest name on).</summary>
    private static IEnumerable<(string Text, Color? Color)> Pieces(Note note)
    {
        string text = note.Text;
        while (note.Names.Select(n => (n.Name, n.Color, At: n.Name.Length == 0 ? -1 : text.IndexOf(n.Name, StringComparison.Ordinal)))
                   .Where(n => n.At >= 0).OrderBy(n => n.At).FirstOrDefault() is { Name: not null } next)
        {
            if (next.At > 0)
                yield return (text[..next.At], null);
            yield return (next.Name, Readable(next.Color));
            text = text[(next.At + next.Name.Length)..];
        }
        if (text.Length > 0)
            yield return (text, null);
    }

    /// <summary>The player's color, very dark ones (black) lightened enough to read on the dark ground.</summary>
    private static Color Readable(Color color) =>
        color.R * 0.3f + color.G * 0.59f + color.B * 0.11f < 90 ? Color.Lerp(color, Color.White, 0.4f) : color;

    private static void DrawText(SpriteBatch batch, UiFont font, string text, Vector2 position, int size, float alpha, Color? color = null)
    {
        if (text.Length == 0)
            return;
        font.Draw(batch, text, position + new Vector2(1, 1), Color.Black * (0.8f * alpha), size);
        font.Draw(batch, text, position, (color ?? Color.White) * alpha, size);
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
