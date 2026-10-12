using System.Text.Json;
using Homestead.Assets.Gfx;
using Homestead.Simulation.Buildings;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Homestead.Client;

/// <summary>
/// Build menu at the left edge, like the original's: category tabs on top, the buildings of the chosen
/// category below as icons (GUI images from the installation). Categories come from <c>data/ui.json</c>.
/// </summary>
public sealed class BuildMenu : IDisposable
{
    public const int Width = 132;
    private const int TabSize = 33;
    private const int Cell = 66;
    private const int Top = 4;

    private readonly List<(string Name, BuildingDefinition? Icon, List<BuildingDefinition> Buildings)> _categories = new();
    private readonly Dictionary<string, Texture2D> _icons = new();
    private int _category;

    public BuildMenu(GraphicsDevice device, GfxDirectory gfx, BuildingCatalog catalog)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(BuildingCatalog.DefaultDataDirectory, "ui.json")));
        foreach (var category in document.RootElement.GetProperty("buildMenu").EnumerateArray())
        {
            var buildings = category.GetProperty("buildings").EnumerateArray()
                .Select(b => catalog.Find(b.GetString()!))
                .Where(b => b is { ConstructionImage: not null } && b.ConstructionPiles.Count > 0)
                .Cast<BuildingDefinition>().ToList();
            _categories.Add((category.GetProperty("name").GetString()!, catalog.Find(category.GetProperty("icon").GetString()!), buildings));
        }
        foreach (var building in _categories.SelectMany(c => c.Buildings.Append(c.Icon)).OfType<BuildingDefinition>().Distinct())
        {
            if (building.GuiImage is not { } image)
                continue;
            var bitmap = gfx.Open(image.File).ReadGui(image.Sequence);
            var texture = new Texture2D(device, bitmap.Width, bitmap.Height);
            texture.SetData(bitmap.Pixels);
            _icons[building.Name] = texture;
        }
    }

    /// <summary>Whether the screen point is on the menu (clicks there do not reach the map).</summary>
    public static bool Contains(Vector2 screen) => screen.X < Width;

    /// <summary>A click on the menu: switches the category, or returns the building to place.</summary>
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

    private static Rectangle TabRect(int i) => new(i * TabSize, Top, TabSize, TabSize);

    private static Rectangle CellRect(int i) => new(i % 2 * Cell, Top + TabSize + 4 + i / 2 * Cell, Cell, Cell);

    public void Draw(SpriteBatch batch, Texture2D pixel, int height, BuildingDefinition? placing, Vector2 cursor)
    {
        batch.Draw(pixel, new Rectangle(0, 0, Width, height), new Color(36, 28, 18, 230));
        for (int i = 0; i < _categories.Count; i++)
        {
            var rect = TabRect(i);
            batch.Draw(pixel, rect, i == _category ? new Color(150, 115, 60) : new Color(80, 62, 38));
            if (_categories[i].Icon is { } icon)
                DrawIcon(batch, icon, rect);
        }
        var buildings = _categories[_category].Buildings;
        for (int i = 0; i < buildings.Count; i++)
        {
            var rect = CellRect(i);
            bool selected = buildings[i] == placing, hover = rect.Contains(cursor);
            if (selected || hover)
                batch.Draw(pixel, rect, selected ? new Color(150, 115, 60) : new Color(90, 70, 45));
            DrawIcon(batch, buildings[i], rect);
        }
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
        foreach (var texture in _icons.Values)
            texture.Dispose();
    }
}
