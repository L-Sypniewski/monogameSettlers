using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Homestead.Client;

/// <summary>One line of a chart: x in minutes of play, y the value.</summary>
public sealed record ChartSeries(string Name, Color Color, IReadOnlyList<Vector2> Points);

/// <summary>
/// Charts for the statistics screen, drawn with a one-pixel texture: line charts (with the values under the
/// cursor), a stacked share chart over time and horizontal (stacked) bars, each on a dark panel with a thin
/// golden frame like the original's menus.
/// </summary>
public static class Charts
{
    public static readonly Color Text = new(240, 225, 190);
    public static readonly Color Label = new(190, 165, 120);
    private static readonly Color Gold = new(200, 170, 70);
    private static readonly Color Grid = Color.White * 0.08f;
    private static readonly Color Background = new Color(8, 14, 22) * 0.78f;

    /// <summary>A dark panel with a golden frame; returns the inner area below a title, if any.</summary>
    public static Rectangle Panel(SpriteBatch batch, Texture2D pixel, UiFont font, Rectangle rect, string? title)
    {
        batch.Draw(pixel, rect, Background);
        Frame(batch, pixel, rect, Gold * 0.85f);
        Frame(batch, pixel, new Rectangle(rect.X + 3, rect.Y + 3, rect.Width - 6, rect.Height - 6), Color.Black * 0.5f);
        if (title is null)
            return new Rectangle(rect.X + 8, rect.Y + 8, rect.Width - 16, rect.Height - 16);
        Write(batch, font, title, new Vector2(rect.X + 10, rect.Y + 7), Text, 15);
        return new Rectangle(rect.X + 8, rect.Y + 28, rect.Width - 16, rect.Height - 36);
    }

    public static void Frame(SpriteBatch batch, Texture2D pixel, Rectangle rect, Color color)
    {
        batch.Draw(pixel, new Rectangle(rect.X, rect.Y, rect.Width, 1), color);
        batch.Draw(pixel, new Rectangle(rect.X, rect.Bottom - 1, rect.Width, 1), color);
        batch.Draw(pixel, new Rectangle(rect.X, rect.Y, 1, rect.Height), color);
        batch.Draw(pixel, new Rectangle(rect.Right - 1, rect.Y, 1, rect.Height), color);
    }

    public static void Write(SpriteBatch batch, UiFont font, string text, Vector2 at, Color color, int size = 13) =>
        SelectionPanel.Shadowed(batch, font, text, at, size, color);

    /// <summary>A line from a to b, <paramref name="thickness"/> pixels wide.</summary>
    public static void Line(SpriteBatch batch, Texture2D pixel, Vector2 a, Vector2 b, Color color, float thickness = 2)
    {
        var d = b - a;
        float length = d.Length();
        if (length < 0.01f)
            return;
        batch.Draw(pixel, a, null, color, MathF.Atan2(d.Y, d.X), new Vector2(0, 0.5f), new Vector2(length, thickness), SpriteEffects.None, 0);
    }

    /// <summary>A round step (1, 2, 2.5, 5 × 10ⁿ) so that about <paramref name="count"/> steps reach <paramref name="max"/>.</summary>
    public static float NiceStep(float max, int count)
    {
        if (max <= 0)
            return 1;
        float raw = max / count;
        float magnitude = MathF.Pow(10, MathF.Floor(MathF.Log10(raw)));
        foreach (float f in new[] { 1f, 2f, 2.5f, 5f, 10f })
            if (f * magnitude >= raw)
                return f * magnitude;
        return 10 * magnitude;
    }

    public static string Number(float value) =>
        MathF.Abs(value) >= 10000 ? $"{value / 1000:0.#}k" : MathF.Abs(value - MathF.Round(value)) < 0.01f ? $"{value:0}" : $"{value:0.#}";

    /// <summary>
    /// A line chart over the minutes of play, one line per series; under the cursor a guide line and the values
    /// of every series at that time, highest first.
    /// </summary>
    public static void LineChart(SpriteBatch batch, Texture2D pixel, UiFont font, Rectangle rect, string title,
        IReadOnlyList<ChartSeries> series, Vector2 cursor, Func<float, string>? format = null)
    {
        format ??= Number;
        var inner = Panel(batch, pixel, font, rect, title);
        var plot = new Rectangle(inner.X + 40, inner.Y + 4, inner.Width - 46, inner.Height - 22);
        if (plot.Width < 20 || plot.Height < 20)
            return;
        var points = series.SelectMany(s => s.Points).ToList();
        if (points.Count == 0)
        {
            Write(batch, font, Lang.T("statistics.noData"), new Vector2(plot.X + 8, plot.Center.Y - 8), Label);
            return;
        }
        float maxX = Math.Max(1, points.Max(p => p.X));
        float maxY = Math.Max(points.Max(p => p.Y), 0.0001f);
        float stepY = NiceStep(maxY, 4);
        maxY = MathF.Ceiling(maxY / stepY) * stepY;
        float minY = Math.Min(0, points.Min(p => p.Y));
        Vector2 ToScreen(Vector2 p) => new(plot.X + p.X / maxX * plot.Width, plot.Bottom - (p.Y - minY) / (maxY - minY) * plot.Height);

        // Grid and axis labels.
        for (float y = 0; y <= maxY + stepY / 2; y += stepY)
        {
            float sy = ToScreen(new Vector2(0, y)).Y;
            batch.Draw(pixel, new Rectangle(plot.X, (int)sy, plot.Width, 1), Grid);
            string label = format(y);
            Write(batch, font, label, new Vector2(plot.X - 4 - font.Measure(label, 11).X, sy - 7), Label, 11);
        }
        float stepX = NiceStep(maxX, Math.Max(2, plot.Width / 70));
        for (float x = 0; x <= maxX + 0.001f; x += stepX)
        {
            float sx = ToScreen(new Vector2(x, 0)).X;
            batch.Draw(pixel, new Rectangle((int)sx, plot.Y, 1, plot.Height), Grid);
            string label = $"{x:0.#}";
            Write(batch, font, label, new Vector2(sx - font.Measure(label, 11).X / 2, plot.Bottom + 3), Label, 11);
        }
        Write(batch, font, "min", new Vector2(plot.X - 4 - font.Measure("min", 11).X, plot.Bottom + 3), Label * 0.7f, 11);
        batch.Draw(pixel, new Rectangle(plot.X, plot.Bottom, plot.Width, 1), Label * 0.6f);
        batch.Draw(pixel, new Rectangle(plot.X, plot.Y, 1, plot.Height), Label * 0.6f);

        foreach (var s in series)
            for (int i = 1; i < s.Points.Count; i++)
                Line(batch, pixel, ToScreen(s.Points[i - 1]), ToScreen(s.Points[i]), s.Color, 2);

        if (!plot.Contains(cursor))
            return;
        // The values at the cursor's time.
        float minute = (cursor.X - plot.X) / plot.Width * maxX;
        batch.Draw(pixel, new Rectangle((int)cursor.X, plot.Y, 1, plot.Height), Color.White * 0.35f);
        var values = series.Where(s => s.Points.Count > 0)
            .Select(s => (Series: s, Point: s.Points.MinBy(p => MathF.Abs(p.X - minute))))
            .OrderByDescending(v => v.Point.Y).ToList();
        foreach (var (s, p) in values)
        {
            var at = ToScreen(p);
            batch.Draw(pixel, new Rectangle((int)at.X - 3, (int)at.Y - 3, 7, 7), Color.Black);
            batch.Draw(pixel, new Rectangle((int)at.X - 2, (int)at.Y - 2, 5, 5), s.Color);
        }
        var lines = values.Select(v => ($"{v.Series.Name}: {format(v.Point.Y)}", v.Series.Color)).Prepend(($"{minute:0.#} min", Text)).ToList();
        Tooltip(batch, pixel, font, cursor, rect, lines);
    }

    /// <summary>A small box of lines next to the cursor, kept inside <paramref name="area"/>.</summary>
    public static void Tooltip(SpriteBatch batch, Texture2D pixel, UiFont font, Vector2 cursor, Rectangle area, IReadOnlyList<(string Text, Color Color)> lines)
    {
        int width = (int)lines.Max(l => font.Measure(l.Text, 12).X) + 22, height = lines.Count * 16 + 8;
        int x = (int)cursor.X + 14, y = (int)cursor.Y + 10;
        if (x + width > area.Right)
            x = (int)cursor.X - 14 - width;
        if (y + height > area.Bottom)
            y = area.Bottom - height;
        var box = new Rectangle(x, y, width, height);
        batch.Draw(pixel, box, new Color(10, 10, 14) * 0.92f);
        Frame(batch, pixel, box, Gold * 0.7f);
        for (int i = 0; i < lines.Count; i++)
        {
            batch.Draw(pixel, new Rectangle(x + 6, y + 9 + i * 16, 8, 8), lines[i].Color);
            Write(batch, font, lines[i].Text, new Vector2(x + 18, y + 4 + i * 16), Text, 12);
        }
    }

    /// <summary>
    /// The shares of the series over time, stacked to 100 % (e.g. the land of every player). All series must
    /// have their points at the same times.
    /// </summary>
    public static void ShareChart(SpriteBatch batch, Texture2D pixel, UiFont font, Rectangle rect, string title,
        IReadOnlyList<ChartSeries> series, Vector2 cursor)
    {
        var inner = Panel(batch, pixel, font, rect, title);
        var plot = new Rectangle(inner.X + 40, inner.Y + 4, inner.Width - 46, inner.Height - 22);
        int count = series.Count == 0 ? 0 : series.Min(s => s.Points.Count);
        if (count == 0 || plot.Width < 20 || plot.Height < 20)
            return;
        float maxX = Math.Max(1, series[0].Points[count - 1].X);
        for (int sx = 0; sx < plot.Width; sx++)
        {
            float minute = sx / (float)plot.Width * maxX;
            int i = Index(series[0].Points, count, minute);
            float total = series.Sum(s => s.Points[i].Y);
            if (total <= 0)
                continue;
            float y = plot.Bottom;
            foreach (var s in series)
            {
                float h = s.Points[i].Y / total * plot.Height;
                batch.Draw(pixel, new Rectangle(plot.X + sx, (int)MathF.Round(y - h), 1, (int)MathF.Round(y) - (int)MathF.Round(y - h)), s.Color * 0.9f);
                y -= h;
            }
        }
        for (int percent = 0; percent <= 100; percent += 25)
        {
            int sy = plot.Bottom - percent * plot.Height / 100;
            batch.Draw(pixel, new Rectangle(plot.X, sy, plot.Width, 1), Color.Black * 0.25f);
            Write(batch, font, $"{percent} %", new Vector2(plot.X - 4 - font.Measure($"{percent} %", 11).X, sy - 7), Label, 11);
        }
        float stepX = NiceStep(maxX, Math.Max(2, plot.Width / 70));
        for (float x = 0; x <= maxX + 0.001f; x += stepX)
        {
            float px = plot.X + x / maxX * plot.Width;
            Write(batch, font, $"{x:0.#}", new Vector2(px - font.Measure($"{x:0.#}", 11).X / 2, plot.Bottom + 3), Label, 11);
        }
        if (!plot.Contains(cursor))
            return;
        float at = (cursor.X - plot.X) / plot.Width * maxX;
        int index = Index(series[0].Points, count, at);
        float sum = Math.Max(1, series.Sum(s => s.Points[index].Y));
        batch.Draw(pixel, new Rectangle((int)cursor.X, plot.Y, 1, plot.Height), Color.White * 0.5f);
        Tooltip(batch, pixel, font, cursor, rect, series.OrderByDescending(s => s.Points[index].Y)
            .Select(s => ($"{s.Name}: {s.Points[index].Y / sum * 100:0.#} % ({Number(s.Points[index].Y)})", s.Color))
            .Prepend(($"{at:0.#} min", Text)).ToList());
    }

    private static int Index(IReadOnlyList<Vector2> points, int count, float minute)
    {
        int best = 0;
        for (int i = 0; i < count; i++)
            if (MathF.Abs(points[i].X - minute) < MathF.Abs(points[best].X - minute))
                best = i;
        return best;
    }

    /// <summary>Horizontal bars, one per row, each made of colored parts (one part: a plain bar); the total at its end.</summary>
    public static void Bars(SpriteBatch batch, Texture2D pixel, UiFont font, Rectangle rect, string title,
        IReadOnlyList<(string Label, IReadOnlyList<(float Value, Color Color)> Parts)> rows, Vector2 cursor,
        IReadOnlyList<(string Name, Color Color)>? legend = null, Func<float, string>? format = null)
    {
        format ??= Number;
        var inner = Panel(batch, pixel, font, rect, title);
        if (legend is { Count: > 0 })
        {
            float lx = inner.Right;
            foreach (var (name, color) in legend.Reverse())
            {
                lx -= font.Measure(name, 11).X + 18;
                batch.Draw(pixel, new Rectangle((int)lx, rect.Y + 11, 9, 9), color);
                Write(batch, font, name, new Vector2(lx + 12, rect.Y + 7), Label, 11);
            }
        }
        if (rows.Count == 0)
            return;
        float labelWidth = Math.Min(inner.Width * 0.4f, rows.Max(r => font.Measure(r.Label, 12).X) + 8);
        float max = Math.Max(rows.Max(r => r.Parts.Sum(p => p.Value)), 0.0001f);
        int rowHeight = Math.Min(30, inner.Height / rows.Count);
        int barWidth = (int)(inner.Width - labelWidth - 50);
        for (int i = 0; i < rows.Count; i++)
        {
            var (label, parts) = rows[i];
            int y = inner.Y + i * rowHeight;
            Write(batch, font, label, new Vector2(inner.X, y + (rowHeight - 14) / 2f), Text, 12);
            float x = inner.X + labelWidth;
            int h = Math.Max(4, rowHeight - 8);
            foreach (var (value, color) in parts)
            {
                float w = value / max * barWidth;
                var bar = new Rectangle((int)x, y + (rowHeight - h) / 2, (int)MathF.Ceiling(w), h);
                batch.Draw(pixel, bar, color);
                batch.Draw(pixel, new Rectangle(bar.X, bar.Y, bar.Width, 1), Color.White * 0.25f);
                if (bar.Contains(cursor) && parts.Count > 1 && legend is not null)
                    Tooltip(batch, pixel, font, cursor, rect,
                        [($"{label}", Text), .. parts.Select((p, k) => ($"{legend[k].Name}: {format(p.Value)}", p.Color))]);
                x += w;
            }
            Write(batch, font, format(parts.Sum(p => p.Value)), new Vector2(x + 5, y + (rowHeight - 14) / 2f), Label, 12);
        }
    }
}
