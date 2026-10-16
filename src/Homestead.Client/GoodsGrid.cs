using System.Text.Json;
using Homestead.Assets.Gfx;
using Homestead.Simulation;
using Homestead.Simulation.Buildings;
using Homestead.Simulation.Economy;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Homestead.Client;

/// <summary>
/// The goods of the goods menus (Siedler › Produktion, Waren › Lagerbestand) as the original shows them:
/// rows of six GUI icons, dark the ones the people cannot make. Goods, icons and names in
/// <c>data/ui.json</c> (<c>goodsGrid</c>). Panel space.
/// </summary>
public sealed class GoodsGrid : IDisposable
{
    private const int Columns = 6;
    public const int IconSize = 30;
    private const int CellWidth = 31;

    private readonly List<Good> _goods = new();
    private readonly HashSet<Material> _makeable;

    public GoodsGrid(GraphicsDevice device, GfxDirectory gfx, BuildingCatalog catalog, ProductionConfig production)
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
        foreach (var line in root.GetProperty("goodsGrid").GetProperty("goods").EnumerateArray())
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

    public IReadOnlyList<Good> Goods => _goods;

    public bool IsMakeable(Material material) => _makeable.Contains(material);

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
        foreach (var (_, recipe) in production.Workshops.Where(w => Has(w.Key)))
            made.UnionWith(recipe.Outputs);
        foreach (var (_, mine) in production.Mines.Where(m => Has(m.Key)))
            made.Add(mine.Output);
        foreach (var (_, smith) in production.Smiths.Where(s => Has(s.Key)))
            made.UnionWith(smith.Products);
        foreach (var (_, gatherer) in production.Gatherers.Where(g => Has(g.Key) && g.Value.Action != GatherAction.PlantTree))
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

    /// <summary>Where a good's icon lies: the grid centered in the content area from <paramref name="top"/> on, rows <paramref name="rowHeight"/> apart.</summary>
    public static Rectangle IconRect(Good good, int top, int rowHeight)
    {
        var content = BuildMenu.Content;
        int x = content.X + (content.Width - Columns * CellWidth) / 2 + good.Column * CellWidth;
        return new Rectangle(x, top + good.Row * rowHeight, IconSize, IconSize);
    }

    /// <summary>The good whose icon lies under the point.</summary>
    public Good? GoodAt(Vector2 panel, int top, int rowHeight) => _goods.FirstOrDefault(g => IconRect(g, top, rowHeight).Contains(panel));

    /// <summary>Draws the icon of each good, dark if the people cannot make it.</summary>
    public void Draw(SpriteBatch batch, int top, int rowHeight)
    {
        foreach (var good in _goods)
            batch.Draw(good.Icon, IconRect(good, top, rowHeight), IsMakeable(good.Material) ? Color.White : Color.White * 0.35f);
    }

    public void Dispose()
    {
        foreach (var good in _goods)
            good.Icon.Dispose();
    }

    public sealed record Good(Material Material, string Name, Texture2D Icon, int Row, int Column);
}
