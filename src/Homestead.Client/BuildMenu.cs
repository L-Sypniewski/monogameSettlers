using System.Text.Json;
using Homestead.Assets.Gfx;
using Homestead.Simulation.Buildings;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Homestead.Client;

/// <summary>
/// Build menu in the blue side panel (<see cref="SidePanel"/>), like the original's: the category buttons
/// in the dark bar below the main tabs, the buildings of the chosen category below as icons. Graphics from
/// the installation (large size, as the original at 1024 × 768); categories come from <c>data/ui.json</c>.
/// Drawn and hit-tested in panel space.
/// </summary>
public sealed class BuildMenu : IDisposable
{
    /// <summary>Width of the menus in the side panel (panel space).</summary>
    public const int Width = SidePanel.Width;

    /// <summary>Category buttons: GFX file 03.</summary>
    private const int TabFile = 3;

    /// <summary>Building icons come in three sizes one after another; the large one is two after the small one.</summary>
    private const int LargeIcon = 2;

    // Areas of the large panel (216 × 544), measured on the image.
    /// <summary>The dark bar for the sub-menus below the main tabs (panel space).</summary>
    public static readonly Rectangle TabBar = new(10, 64, 196, 39);
    /// <summary>The content area of the blue panel, below the bars (panel space); the other menus there use it too.</summary>
    public static readonly Rectangle Content = new(10, 118, 196, 354);
    private const int Columns = 3;
    private const int CellHeight = 70;

    private readonly List<(string Name, Texture2D? Tab, Texture2D? Selected, List<BuildingDefinition> Buildings)> _categories = new();
    private readonly Dictionary<string, Texture2D> _icons = new();
    private int _category;

    /// <summary>Seconds the cursor rests on an icon before its tooltip appears.</summary>
    private const double TooltipDelay = 1;
    private BuildingDefinition? _hovered;
    private double _hoverSince, _lastDrawn = double.NegativeInfinity;

    public BuildMenu(GraphicsDevice device, GfxDirectory gfx, BuildingCatalog catalog)
    {
        Texture2D ToTexture(GfxBitmap bitmap)
        {
            var texture = new Texture2D(device, bitmap.Width, bitmap.Height);
            texture.SetData(bitmap.Pixels);
            return texture;
        }

        var tabs = gfx.Open(TabFile);
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(BuildingCatalog.DefaultDataDirectory, "ui.json")));
        foreach (var category in document.RootElement.GetProperty("buildMenu").EnumerateArray())
        {
            var buildings = category.GetProperty("buildings").EnumerateArray()
                .Select(b => catalog.Find(b.GetString()!))
                // Not every building has a construction image (the storehouse is only posts): its site shows sign and posts.
                .Where(b => b is not null && b.ConstructionPiles.Count > 0)
                .Cast<BuildingDefinition>().ToList();
            Texture2D? tab = null, selected = null;
            if (category.TryGetProperty("tab", out var images))
            {
                tab = ToTexture(tabs.ReadGui(images[0].GetInt32()));
                selected = ToTexture(tabs.ReadGui(images[1].GetInt32()));
            }
            _categories.Add((category.GetProperty("name").GetString()!, tab, selected, buildings));
        }
        foreach (var building in _categories.SelectMany(c => c.Buildings).Distinct())
            if (building.GuiImage is { } image)
                _icons[building.Name] = ToTexture(gfx.Open(image.File).ReadGui(image.Sequence + LargeIcon));
    }

    /// <summary>A click on the menu (panel space): switches the category, or returns the building to place.</summary>
    public BuildingDefinition? Click(Vector2 screen)
    {
        if (TabAt(screen) is { } tab)
        {
            _category = tab;
            return null;
        }
        return BuildingAt(screen);
    }

    /// <summary>The building icon under the cursor (for the hover text).</summary>
    public BuildingDefinition? BuildingAt(Vector2 screen)
    {
        var buildings = _categories[_category].Buildings;
        for (int i = 0; i < buildings.Count; i++)
            if (CellRect(i).Contains(screen))
                return buildings[i];
        return null;
    }

    private int? TabAt(Vector2 screen)
    {
        for (int i = 0; i < _categories.Count; i++)
            if (TabRect(i).Contains(screen))
                return i;
        return null;
    }

    /// <summary>Category button <paramref name="i"/>: the bar split evenly, the button centered in its part.</summary>
    private Rectangle TabRect(int i)
    {
        int part = TabBar.Width / _categories.Count;
        var size = _categories[i].Tab is { } tab ? new Point(tab.Width, tab.Height) : new Point(part - 4, TabBar.Height - 2);
        return new Rectangle(TabBar.X + i * part + (part - size.X) / 2, TabBar.Y + (TabBar.Height - size.Y) / 2, size.X, size.Y);
    }

    private static Rectangle CellRect(int i)
    {
        int width = Content.Width / Columns;
        return new Rectangle(Content.X + i % Columns * width, Content.Y + i / Columns * CellHeight, width, CellHeight);
    }

    /// <summary>Width at the right of each cell for the counts next to the icon.</summary>
    private const int CountWidth = 16;

    /// <param name="counts">The player's buildings by name: finished, and under construction (shown right of the icon, zeros left out).</param>
    /// <param name="now">Real seconds, for the tooltip delay.</param>
    public void Draw(SpriteBatch batch, Texture2D pixel, UiFont font, BuildingDefinition? placing, Vector2 cursor,
        IReadOnlyDictionary<string, (int Finished, int Building)> counts, double now)
    {
        // The hover starts anew on another icon, off the icons, or when the menu was hidden in between.
        var hovered = BuildingAt(cursor);
        if (hovered != _hovered || now - _lastDrawn > 0.2)
        {
            _hovered = hovered;
            _hoverSince = now;
        }
        _lastDrawn = now;

        for (int i = 0; i < _categories.Count; i++)
        {
            var rect = TabRect(i);
            var image = i == _category ? _categories[i].Selected : _categories[i].Tab;
            if (image is not null)
                batch.Draw(image, rect, Color.White);
            else
                batch.Draw(pixel, rect, i == _category ? new Color(20, 30, 60) : new Color(60, 75, 110));
        }

        var buildings = _categories[_category].Buildings;
        for (int i = 0; i < buildings.Count; i++)
        {
            var rect = CellRect(i);
            bool selected = buildings[i] == placing, hover = rect.Contains(cursor);
            if (hover && !selected)
                batch.Draw(pixel, rect, Color.White * 0.08f);
            var (finished, building) = counts.GetValueOrDefault(buildings[i].Name);
            var icon = rect;
            if (finished > 0 || building > 0)
            {
                icon.Width -= CountWidth;
                icon.X += 2;
                DrawCount(batch, font, finished, new Vector2(rect.Right - 3, rect.Center.Y - 15), Color.White);
                DrawCount(batch, font, building, new Vector2(rect.Right - 3, rect.Center.Y + 1), new Color(255, 200, 90));
            }
            DrawIcon(batch, buildings[i], icon);
            if (selected)
                DrawFrame(batch, pixel, rect, Color.Gold);
        }
    }

    /// <summary>
    /// The name of the icon the cursor has rested on for <see cref="TooltipDelay"/>, next to the cursor in screen
    /// space (so it may reach over the map), kept inside <paramref name="area"/>. Call after <see cref="Draw"/>.
    /// </summary>
    public void DrawTooltip(SpriteBatch batch, Texture2D pixel, UiFont font, Vector2 cursor, Rectangle area, float scale, double now)
    {
        if (_hovered is null || now - _lastDrawn > 0.2 || now - _hoverSince < TooltipDelay)
            return;
        string text = Lang.Find("buildings." + _hovered.Name) ?? _hovered.Name;
        int size = Math.Max(12, (int)MathF.Round(14 * scale));
        var measured = font.Measure(text, size);
        int width = (int)measured.X + 16, height = (int)measured.Y + 10;
        int x = (int)cursor.X + 14, y = (int)cursor.Y + 18;
        if (x + width > area.Right)
            x = (int)cursor.X - 14 - width;
        if (y + height > area.Bottom)
            y = (int)cursor.Y - 8 - height;
        var box = new Rectangle(x, y, width, height);
        batch.Draw(pixel, box, new Color(10, 10, 14) * 0.92f);
        Charts.Frame(batch, pixel, box, new Color(200, 170, 70) * 0.7f);
        Charts.Write(batch, font, text, new Vector2(x + 8, y + 5), Charts.Text, size);
    }

    /// <summary>A count right-aligned at <paramref name="topRight"/>; nothing for zero.</summary>
    private static void DrawCount(SpriteBatch batch, UiFont font, int count, Vector2 topRight, Color color)
    {
        if (count == 0)
            return;
        string text = count.ToString();
        var size = font.Measure(text, 13);
        font.Draw(batch, text, new Vector2(topRight.X - size.X + 1, topRight.Y + 1), Color.Black, 13);
        font.Draw(batch, text, new Vector2(topRight.X - size.X, topRight.Y), color, 13);
    }

    private static void DrawFrame(SpriteBatch batch, Texture2D pixel, Rectangle rect, Color color)
    {
        batch.Draw(pixel, new Rectangle(rect.X, rect.Y, rect.Width, 2), color);
        batch.Draw(pixel, new Rectangle(rect.X, rect.Bottom - 2, rect.Width, 2), color);
        batch.Draw(pixel, new Rectangle(rect.X, rect.Y, 2, rect.Height), color);
        batch.Draw(pixel, new Rectangle(rect.Right - 2, rect.Y, 2, rect.Height), color);
    }

    /// <summary>The icon scaled down to fit the box, centered.</summary>
    private void DrawIcon(SpriteBatch batch, BuildingDefinition building, Rectangle box)
    {
        if (!_icons.TryGetValue(building.Name, out var texture))
            return;
        float scale = Math.Min(1f, Math.Min((box.Width - 4f) / texture.Width, (box.Height - 4f) / texture.Height));
        var size = new Vector2(texture.Width, texture.Height) * scale;
        batch.Draw(texture, new Vector2(box.Center.X, box.Center.Y) - size / 2, null, Color.White, 0, Vector2.Zero, scale, SpriteEffects.None, 0);
    }

    public void Dispose()
    {
        foreach (var category in _categories)
        {
            category.Tab?.Dispose();
            category.Selected?.Dispose();
        }
        foreach (var texture in _icons.Values)
            texture.Dispose();
    }
}
