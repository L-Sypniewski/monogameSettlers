using Homestead.Assets.Gfx;
using Homestead.Simulation;
using Homestead.Simulation.Buildings;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Homestead.Client;

/// <summary>
/// Siedler › Siedlerstatistik in the blue panel, like the original's: at the top the room in all (start
/// population and living houses), the settlers needing room (tearing a house down would leave the
/// difference on strike), all settlers and the soldiers; below a portrait per kind with how many the
/// player has. Kinds and icons in <c>data/ui.json</c> (<c>settlersOverview</c>). Panel space.
/// </summary>
public sealed class SettlersOverview : IPanelMenu, IDisposable
{
    private static readonly Color Text = new(240, 225, 190);

    private readonly PanelLayout _layout;
    private readonly Portraits _portraits;
    private readonly SettlerRenderer _settlers;
    private readonly Civilization _people;
    private readonly Texture2D _room;
    private readonly Texture2D _needing;
    private readonly Texture2D _total;
    private readonly Texture2D _soldiers;
    private readonly string[][] _rows;

    public SettlersOverview(GraphicsDevice device, GfxDirectory gfx, Civilization people, Portraits portraits, SettlerRenderer settlers,
        PanelLayout layout, UiLayout ui)
    {
        _layout = layout;
        _portraits = portraits;
        _settlers = settlers;
        _people = people;
        Texture2D Load(GfxFile file, int index)
        {
            var bitmap = file.ReadGui(index);
            var texture = new Texture2D(device, bitmap.Width, bitmap.Height);
            texture.SetData(bitmap.Pixels);
            return texture;
        }

        var icons = ui.SettlersOverview.Icons;
        var gui = gfx.Open(3);
        _room = Load(gui, icons.Room);
        _needing = Load(gui, icons.Needing);
        _total = Load(gui, icons.Total);
        var weapons = ui.SoldiersMenu.Weapons[people.ToString()];
        _soldiers = Load(gfx.Open(weapons.File), weapons.Swordsman);
        _rows = ui.SettlersOverview.Rows.Select(r => r.ToArray()).ToArray();
    }

    private int Left => _layout.Content.X + 4;
    private int Width => _layout.Content.Width - 8;
    private int Top => _layout.Content.Y + 24; // below the title
    private const int HeaderHeight = 54;
    private const int CellHeight = 80;

    public void Draw(SpriteBatch batch, Texture2D pixel, UiFont font, World world, int player, Vector2 cursor, float seconds)
    {
        var own = world.Settlers.Where(s => s.Player == player).ToList();
        int column = Width / 4;

        // Header: room, needing room, all settlers (number above the arrow), soldiers.
        string room = world.Housing.Room(player)?.ToString() ?? "–";
        int soldiers = own.Count(s => s.Profession == Profession.Soldier);
        Icon(batch, font, _room, room, 0);
        Icon(batch, font, _needing, world.Housing.Needing(player).ToString(), 1);
        Icon(batch, font, _soldiers, soldiers.ToString(), 3);
        string total = own.Count.ToString();
        var size = font.Measure(total, 15);
        float center = Left + 2 * column + column / 2f;
        Write(batch, font, total, new Vector2(center - size.X / 2, Top), 15);
        batch.Draw(_total, new Vector2(center - _total.Width / 2f, Top + 20), Color.White);

        // A portrait per kind with its count.
        for (int row = 0; row < _rows.Length; row++)
            for (int col = 0; col < _rows[row].Length; col++)
            {
                string kind = _rows[row][col];
                var cell = new Rectangle(Left + col * column, Top + HeaderHeight + row * CellHeight, column, CellHeight);
                if (cell.Bottom > _layout.Content.Bottom)
                    break;
                _portraits.Draw(batch, PortraitOf(kind), new Rectangle(cell.X, cell.Y, cell.Width, Portraits.Size.Y), _settlers, _people, PlayerColors.Of(player));
                string count = own.Count(s => Is(s, kind)).ToString();
                var countSize = font.Measure(count, 14);
                Write(batch, font, count, new Vector2(cell.Center.X - countSize.X / 2, cell.Y + Portraits.Size.Y + 1), 14);
            }
    }

    /// <summary>An icon centered in header column <paramref name="col"/> with its number below.</summary>
    private void Icon(SpriteBatch batch, UiFont font, Texture2D icon, string number, int col)
    {
        int column = Width / 4;
        float center = Left + col * column + column / 2f;
        batch.Draw(icon, new Vector2(center - icon.Width / 2f, Top), Color.White);
        var size = font.Measure(number, 15);
        Write(batch, font, number, new Vector2(center - size.X / 2, Top + icon.Height + 1), 15);
    }

    /// <summary>A passive overview: nothing to click or hover.</summary>
    public string? Update(Vector2 panel, bool pressed, bool justPressed, bool onMenu, World world, int player, IssueCommand issue) => null;

    public void ResetInput() { }

    /// <summary>A passive overview: nothing to click or hover.</summary>
    public string? Hover(Vector2 panel, World world, int player) => null;

    /// <summary>The tooltip of the kind's portrait cell under the panel point, or null.</summary>
    public string? TooltipAt(Vector2 panel)
    {
        int column = Width / 4;
        for (int row = 0; row < _rows.Length; row++)
            for (int col = 0; col < _rows[row].Length; col++)
            {
                var cell = new Rectangle(Left + col * column, Top + HeaderHeight + row * CellHeight, column, CellHeight);
                if (cell.Bottom > _layout.Content.Bottom)
                    break;
                if (cell.Contains(panel))
                    return NameOf(_rows[row][col]);
            }
        return null;
    }

    /// <summary>The kind's localized name (the soldiers' types reuse the soldiers menu's names).</summary>
    private static string NameOf(string kind) => kind switch
    {
        SettlerKinds.Swordsman or SettlerKinds.Bowman or SettlerKinds.Pikeman => Lang.Find("soldiers.types." + kind[0] + kind[1..].ToLowerInvariant()) ?? kind,
        _ => Lang.T("settlers." + kind.ToLowerInvariant()),
    };

    /// <summary>The portrait of a kind: soldiers by their first level.</summary>
    private static string PortraitOf(string kind) =>
        kind is SettlerKinds.Swordsman or SettlerKinds.Bowman or SettlerKinds.Pikeman ? $"{kind}_L1" : kind;

    /// <summary>Whether the settler counts as the kind (thieves are the scouts).</summary>
    private static bool Is(Settler settler, string kind) => kind switch
    {
        SettlerKinds.Bearer => settler.Profession == Profession.Carrier,
        SettlerKinds.Digger => settler.Profession == Profession.Digger,
        SettlerKinds.Bricklayer => settler.Profession == Profession.Builder,
        SettlerKinds.Worker => settler.Profession == Profession.Worker,
        SettlerKinds.Swordsman => settler is { Profession: Profession.Soldier, SoldierType: SoldierType.Swordsman },
        SettlerKinds.Bowman => settler is { Profession: Profession.Soldier, SoldierType: SoldierType.Bowman },
        SettlerKinds.Pikeman => settler is { Profession: Profession.Soldier, SoldierType: SoldierType.Pikeman },
        SettlerKinds.Mage => settler.Profession == Profession.Priest,
        SettlerKinds.Geologist => settler.Profession == Profession.Geologist,
        SettlerKinds.Thief => settler.Profession == Profession.Scout,
        SettlerKinds.Pioneer => settler.Profession == Profession.Pioneer,
        SettlerKinds.Donkey => settler.Profession == Profession.Donkey,
        _ => false,
    };

    private static void Write(SpriteBatch batch, UiFont font, string text, Vector2 position, int size) =>
        SelectionPanel.Shadowed(batch, font, text, position, size, Text);

    public void Dispose()
    {
        _room.Dispose();
        _needing.Dispose();
        _total.Dispose();
        _soldiers.Dispose();
    }

}

/// <summary>
/// The overview's settler kinds: the row names in <c>data/ui.json</c> (<c>settlersOverview.rows</c>) and the
/// portraits' keys in the original's GUI data, so the spellings must stay as they are.
/// </summary>
internal static class SettlerKinds
{
    public const string Bearer = "BEARER";
    public const string Digger = "DIGGER";
    public const string Bricklayer = "BRICKLAYER";
    public const string Worker = "WORKER";
    public const string Swordsman = "SWORDSMAN";
    public const string Bowman = "BOWMAN";
    public const string Pikeman = "PIKEMAN";
    public const string Mage = "MAGE";
    public const string Geologist = "GEOLOGIST";
    public const string Thief = "THIEF";
    public const string Pioneer = "PIONEER";
    public const string Donkey = "DONKEY";
}
