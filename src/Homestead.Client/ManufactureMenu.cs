using System.Text.Json;
using Homestead.Assets.Gfx;
using Homestead.Simulation;
using Homestead.Simulation.Buildings;
using Homestead.Simulation.Economy;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Homestead.Client;

/// <summary>
/// Waren › Herstellung in the blue panel, like the original's: tools, then weapons, each a row with the
/// product's icon, the units ordered (made first; the spinner orders one more or less) and a bar with the
/// product's share of what the smith makes otherwise (<see cref="Production.ShareOf"/>; empty: only on
/// order or need). A click on a bar sets it, dragging moves it; near either end it snaps to 0 or 100 %.
/// Panel space.
/// </summary>
public sealed class ManufactureMenu : IDisposable
{
    private static readonly Color Text = new(240, 225, 190);
    private static readonly Color Fill = new(20, 190, 200);
    private static readonly Material[] Tools = [Material.Hammer, Material.Blade, Material.Pick, Material.Axe, Material.Saw, Material.Scythe, Material.FishingRod];
    private static readonly Material[] Weapons = [Material.Sword, Material.Bow, Material.Spear];

    /// <summary>A press this close to (or beyond) a bar's end sets 0 or 100 %.</summary>
    private const int SnapPixels = 6;

    private const int RowHeight = 30;
    private readonly GoodsGrid _grid;
    private readonly Texture2D _spinner;

    /// <summary>The bar being dragged (its product), while the button is held.</summary>
    private Material? _dragging;

    public ManufactureMenu(GraphicsDevice device, GfxDirectory gfx, GoodsGrid grid)
    {
        _grid = grid;
        // The spinner of Siedler › Berufe.
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(BuildingCatalog.DefaultDataDirectory, "ui.json")));
        var bitmap = gfx.Open(3).ReadGui(document.RootElement.GetProperty("professionsMenu").GetProperty("spinner").GetInt32());
        _spinner = new Texture2D(device, bitmap.Width, bitmap.Height);
        _spinner.SetData(bitmap.Pixels);
    }

    public void Dispose() => _spinner.Dispose();

    // Layout (panel space): no title, the two headings instead.
    private static int Left => BuildMenu.Content.X + 4;
    private static int Width => BuildMenu.Content.Width - 8;
    private static int ToolsY => BuildMenu.Content.Y + 22;
    private static int WeaponsHeadingY => ToolsY + Tools.Length * RowHeight + 2;
    private static int WeaponsY => WeaponsHeadingY + 22;

    private static IEnumerable<(Material Product, int Y)> Rows() =>
        Tools.Select((m, i) => (m, ToolsY + i * RowHeight)).Concat(Weapons.Select((m, i) => (m, WeaponsY + i * RowHeight)));

    private static Rectangle Icon(int y) => new(Left, y, 28, 28);
    private static Rectangle Spinner(int y) => new(Left + 58, y, 19, 28);
    private static Rectangle Bar(int y) => new(Left + 84, y + 5, Width - 84, 18);

    /// <summary>The bar's inner part, 0 to 100 %.</summary>
    private static Rectangle Inner(Rectangle bar) => new(bar.X + 2, bar.Y + 2, bar.Width - 4, bar.Height - 4);

    public void Draw(SpriteBatch batch, Texture2D pixel, UiFont font, World world, int player, Vector2 cursor)
    {
        Heading(batch, font, "Werkzeug", ToolsY - 22);
        Heading(batch, font, "Waffen", WeaponsHeadingY);
        foreach (var (product, y) in Rows())
        {
            if (_grid.Goods.FirstOrDefault(g => g.Material == product) is { } good)
                batch.Draw(good.Icon, Icon(y), Color.White);
            string ordered = world.Production.Ordered(player, product).ToString();
            var size = font.Measure(ordered, 16);
            SelectionPanel.Shadowed(batch, font, ordered, new Vector2(Left + 44 - size.X / 2, y + 4), 16, Text);

            var spinner = Spinner(y);
            batch.Draw(_spinner, spinner, Color.White);
            if (spinner.Contains(cursor))
                batch.Draw(pixel, Half(spinner, cursor.Y < spinner.Center.Y), Color.White * 0.15f);

            var bar = Bar(y);
            batch.Draw(pixel, bar, Color.Black * 0.45f);
            Outline(batch, pixel, bar, new Color(170, 185, 195));
            var inner = Inner(bar);
            int filled = (int)Math.Round(world.Production.ShareOf(player, product) * inner.Width);
            if (filled > 0)
                batch.Draw(pixel, new Rectangle(inner.X, inner.Y, filled, inner.Height), Fill);
        }
    }

    /// <summary>
    /// Mouse handling (panel space): a press on a spinner orders one more/less, a press on a bar starts
    /// dragging it; while held, the dragged bar follows the cursor (also beyond the panel). Returns a message, or null.
    /// </summary>
    public string? Update(Vector2 panel, bool pressed, bool justPressed, bool onMenu, World world, int player)
    {
        if (!pressed)
        {
            _dragging = null;
            return null;
        }
        if (justPressed && onMenu)
            foreach (var (product, y) in Rows())
            {
                var spinner = Spinner(y);
                if (spinner.Contains(panel))
                {
                    world.Production.ChangeOrdered(player, product, panel.Y < spinner.Center.Y ? 1 : -1);
                    return $"{NameOf(product)}: {world.Production.Ordered(player, product)} ordered";
                }
                if (Bar(y).Contains(panel))
                    _dragging = product;
            }
        if (_dragging is not { } dragged)
            return null;
        var inner = Inner(Bar(Rows().First(r => r.Product == dragged).Y));
        double share = panel.X <= inner.X + SnapPixels ? 0
            : panel.X >= inner.Right - SnapPixels ? 1
            : Math.Round((panel.X - inner.X) / inner.Width * 20) / 20; // 5 % steps
        world.Production.SetShare(player, dragged, share);
        return $"{NameOf(dragged)}: {world.Production.ShareOf(player, dragged) * 100:0} %";
    }

    /// <summary>What the element under the point does (hover text), or null.</summary>
    public string? Hover(Vector2 panel, World world, int player)
    {
        foreach (var (product, y) in Rows())
        {
            if (Spinner(y).Contains(panel))
                return $"{NameOf(product)}: order one more (up) or less (down) – ordered ones are made first";
            if (Bar(y).Contains(panel))
                return $"{NameOf(product)}: {world.Production.ShareOf(player, product) * 100:0} % share (click or drag; empty: only on order or need)";
            if (Icon(y).Contains(panel))
                return $"{NameOf(product)}: {world.Production.Ordered(player, product)} ordered";
        }
        return null;
    }

    private string NameOf(Material product) => _grid.Goods.FirstOrDefault(g => g.Material == product)?.Name ?? product.ToString();

    private static Rectangle Half(Rectangle spinner, bool upper) =>
        new(spinner.X, upper ? spinner.Y : spinner.Center.Y, spinner.Width, spinner.Height / 2);

    private static void Heading(SpriteBatch batch, UiFont font, string text, int y)
    {
        var size = font.Measure(text, 16);
        SelectionPanel.Shadowed(batch, font, text, new Vector2(Left + (Width - size.X) / 2, y), 16, Text);
    }

    private static void Outline(SpriteBatch batch, Texture2D pixel, Rectangle rect, Color color)
    {
        batch.Draw(pixel, new Rectangle(rect.X, rect.Y, rect.Width, 1), color);
        batch.Draw(pixel, new Rectangle(rect.X, rect.Bottom - 1, rect.Width, 1), color);
        batch.Draw(pixel, new Rectangle(rect.X, rect.Y, 1, rect.Height), color);
        batch.Draw(pixel, new Rectangle(rect.Right - 1, rect.Y, 1, rect.Height), color);
    }
}
