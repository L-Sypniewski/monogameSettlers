using System.Globalization;
using System.Text.Json;
using Homestead.Assets.Gfx;
using Homestead.Simulation;
using Homestead.Simulation.Buildings;
using Homestead.Simulation.Economy;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Homestead.Client;

/// <summary>
/// Siedler › Produktion in the blue panel, like the original's: a chart of how much of one good the player
/// produced over the last hour (units per minute averaged over two minutes, one bar per period of
/// <see cref="ProductionStatistics.PeriodTicks"/>, now at the right), and below it the goods to choose
/// from – dark the ones the people cannot make. Goods, icons and names in <c>data/ui.json</c>
/// (<c>statistics</c>). Panel space.
/// </summary>
public sealed class StatisticsMenu : IDisposable
{
    private static readonly Color Text = new(240, 225, 190);
    private static readonly Color Bar = new(230, 20, 10);
    private static readonly Color Selected = new(220, 30, 20);
    private const int Minutes = 60;
    private const int Smoothing = 2;
    private const int Columns = 6;
    private const int IconSize = 30;
    private const int Cell = 31;

    /// <summary>Scales of the chart (units per minute at the top line): the first that fits the highest bar.</summary>
    private static readonly double[] Scales = [1, 2, 3, 5, 10, 15, 20, 30, 50, 100, 200, 500, 1000];

    private readonly List<Good> _goods = new();
    private readonly HashSet<Material> _makeable;
    private Material _chosen = Material.Trunk;

    public StatisticsMenu(GraphicsDevice device, GfxDirectory gfx, BuildingCatalog catalog, ProductionConfig production)
    {
        var files = new Dictionary<int, GfxFile>();
        Texture2D Load(int file, int index)
        {
            if (!files.TryGetValue(file, out var opened))
                files[file] = opened = gfx.Open(file);
            var bitmap = opened.ReadGui(index);
            var texture = new Texture2D(device, bitmap.Width, bitmap.Height);
            texture.SetData(bitmap.Pixels);
            return texture;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(BuildingCatalog.DefaultDataDirectory, "ui.json")));
        var root = document.RootElement;
        var weapons = root.GetProperty("soldiersMenu").GetProperty("weapons").GetProperty(catalog.Civilization.ToString());
        int row = 0;
        foreach (var line in root.GetProperty("statistics").GetProperty("goods").EnumerateArray())
        {
            int column = 0;
            foreach (var entry in line.EnumerateArray())
            {
                var material = Materials.Parse(entry.GetProperty("good").GetString()!)!.Value;
                var icon = entry.GetProperty("icon");
                var texture = icon.ValueKind == JsonValueKind.Array
                    ? Load(icon[0].GetInt32(), icon[1].GetInt32())
                    : Load(weapons.GetProperty("file").GetInt32(), weapons.GetProperty(WeaponKey(material)).GetInt32());
                _goods.Add(new Good(material, entry.GetProperty("name").GetString()!, texture, row, column++));
            }
            row++;
        }
        _makeable = Makeable(catalog, production);
    }

    private static string WeaponKey(Material weapon) => weapon switch
    {
        Material.Sword => "SWORDSMAN",
        Material.Bow => "BOWMAN",
        _ => "PIKEMAN",
    };

    /// <summary>What the people's buildings make: workshop, mine and smith products, gathered goods (not the forester's trees).</summary>
    private static HashSet<Material> Makeable(BuildingCatalog catalog, ProductionConfig production)
    {
        var made = new HashSet<Material>();
        bool Has(string name) => catalog.Find(name) is not null;
        foreach (var (name, recipe) in production.Workshops.Where(w => Has(w.Key)))
            made.UnionWith(recipe.Outputs);
        foreach (var (name, mine) in production.Mines.Where(m => Has(m.Key)))
            made.Add(mine.Output);
        foreach (var (name, smith) in production.Smiths.Where(s => Has(s.Key)))
            made.UnionWith(smith.Products);
        foreach (var (name, gatherer) in production.Gatherers.Where(g => Has(g.Key) && g.Value.Action != GatherAction.PlantTree))
            made.Add(gatherer.Action switch
            {
                GatherAction.CutTree => Material.Trunk,
                GatherAction.CutStone => Material.Stone,
                GatherAction.Fish => Material.Fish,
                GatherAction.DrawWater => Material.Water,
                _ => gatherer.Output ?? Material.Crop,
            });
        return made;
    }

    // Layout (panel space), top down in the content area.
    private static int Left => BuildMenu.Content.X + 4;
    private static int Width => BuildMenu.Content.Width - 8;
    private static int Top => BuildMenu.Content.Y + 24; // below the title
    private static Rectangle Chart => new(Left + 2, Top + 4, Width - 4, 106);
    private static int GridTop => Chart.Bottom + 22;

    private static Rectangle IconRect(Good good)
    {
        int x = Left + (Width - Columns * Cell) / 2 + good.Column * Cell;
        return new Rectangle(x, GridTop + good.Row * Cell, IconSize, IconSize);
    }

    public void Draw(SpriteBatch batch, Texture2D pixel, UiFont font, World world, int player, Vector2 cursor)
    {
        var chart = Chart;
        var statistics = world.Statistics;
        int periodsPerMinute = 60 * World.TicksPerSecond / ProductionStatistics.PeriodTicks;
        int periods = Minutes * periodsPerMinute;
        // The last finished period at the right edge; the one under way is left out (it would always dip).
        int last = statistics.CurrentPeriod - 1;
        // Units per minute, averaged over the periods of the last Smoothing minutes (single periods jump).
        int window = Smoothing * periodsPerMinute;
        var perMinute = Enumerable.Range(0, periods)
            .Select(i => last - periods + 1 + i)
            .Select(period => Enumerable.Range(period - window + 1, window).Sum(p => statistics.Produced(player, _chosen, p)) / (double)Smoothing)
            .ToArray();
        double scale = Scales.FirstOrDefault(s => s >= perMinute.Max(), Scales[^1]);

        // Bars, then the dotted lines with their values over them, the axes, the minutes below.
        for (int i = 0; i < periods; i++)
        {
            if (perMinute[i] <= 0)
                continue;
            int x0 = chart.X + i * chart.Width / periods, x1 = chart.X + (i + 1) * chart.Width / periods;
            int height = (int)Math.Round(Math.Min(1, perMinute[i] / scale) * chart.Height);
            batch.Draw(pixel, new Rectangle(x0, chart.Bottom - height, Math.Max(1, x1 - x0), height), Bar);
        }
        for (int line = 1; line <= 5; line++)
        {
            int y = chart.Bottom - line * chart.Height / 5;
            for (int x = chart.X; x < chart.Right; x += 3)
                batch.Draw(pixel, new Rectangle(x, y, 1, 1), Color.White * 0.8f);
            string value = (scale * line / 5).ToString("0.##", CultureInfo.GetCultureInfo("de-DE"));
            var size = font.Measure(value, 13);
            Write(batch, font, value, new Vector2(chart.Center.X - size.X / 2, y - size.Y / 2 - 1), 13);
        }
        batch.Draw(pixel, new Rectangle(chart.X, chart.Bottom, chart.Width, 1), Color.White * 0.9f);
        batch.Draw(pixel, new Rectangle(chart.Right - 1, chart.Y - 4, 1, chart.Height + 8), Color.White * 0.9f);
        for (int minutes = Minutes; minutes > 0; minutes -= 15)
        {
            int x = chart.Right - minutes * chart.Width / Minutes;
            batch.Draw(pixel, new Rectangle(x, chart.Bottom, 1, 4), Color.White * 0.9f);
            string label = minutes.ToString(CultureInfo.InvariantCulture);
            var size = font.Measure(label, 13);
            Write(batch, font, label, new Vector2(Math.Max(Left, x - size.X / 2), chart.Bottom + 4), 13);
        }

        foreach (var good in _goods)
        {
            var rect = IconRect(good);
            bool makeable = _makeable.Contains(good.Material);
            batch.Draw(good.Icon, rect, makeable ? Color.White : Color.White * 0.35f);
            if (good.Material == _chosen)
                Frame(batch, pixel, rect, Selected, 2);
            else if (rect.Contains(cursor))
                batch.Draw(pixel, rect, Color.White * 0.15f);
        }
    }

    /// <summary>A click on a good shows its chart; returns a message, or null if it hit nothing.</summary>
    public string? Click(Vector2 panel, World world, int player)
    {
        if (GoodAt(panel) is not { } good)
            return null;
        _chosen = good.Material;
        return Describe(good, world, player);
    }

    /// <summary>The good under the point and how much of it was made (hover text), or null.</summary>
    public string? Hover(Vector2 panel, World world, int player) => GoodAt(panel) is { } good ? Describe(good, world, player) : null;

    private string Describe(Good good, World world, int player)
    {
        if (!_makeable.Contains(good.Material))
            return $"{good.Name}: not made by this people";
        int periodsPerHour = Minutes * 60 * World.TicksPerSecond / ProductionStatistics.PeriodTicks;
        int now = world.Statistics.CurrentPeriod;
        int lastHour = Enumerable.Range(now - periodsPerHour + 1, periodsPerHour).Sum(p => world.Statistics.Produced(player, good.Material, p));
        return $"{good.Name}: {lastHour} in the last hour, {world.Statistics.Total(player, good.Material)} in all";
    }

    private Good? GoodAt(Vector2 panel) => _goods.FirstOrDefault(g => IconRect(g).Contains(panel));

    private static void Frame(SpriteBatch batch, Texture2D pixel, Rectangle rect, Color color, int width)
    {
        batch.Draw(pixel, new Rectangle(rect.X, rect.Y, rect.Width, width), color);
        batch.Draw(pixel, new Rectangle(rect.X, rect.Bottom - width, rect.Width, width), color);
        batch.Draw(pixel, new Rectangle(rect.X, rect.Y, width, rect.Height), color);
        batch.Draw(pixel, new Rectangle(rect.Right - width, rect.Y, width, rect.Height), color);
    }

    private static void Write(SpriteBatch batch, UiFont font, string text, Vector2 position, int size) =>
        SelectionPanel.Shadowed(batch, font, text, position, size, Text);

    public void Dispose()
    {
        foreach (var good in _goods)
            good.Icon.Dispose();
    }

    private sealed record Good(Material Material, string Name, Texture2D Icon, int Row, int Column);
}
