using System.Text.Json;
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
public sealed class SettlersOverview : IDisposable
{
    private static readonly Color Text = new(240, 225, 190);

    private readonly Texture2D _room;
    private readonly Texture2D _needing;
    private readonly Texture2D _total;
    private readonly Texture2D _soldiers;
    private readonly string[][] _rows;

    public SettlersOverview(GraphicsDevice device, GfxDirectory gfx, Civilization people)
    {
        Texture2D Load(GfxFile file, int index)
        {
            var bitmap = file.ReadGui(index);
            var texture = new Texture2D(device, bitmap.Width, bitmap.Height);
            texture.SetData(bitmap.Pixels);
            return texture;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(BuildingCatalog.DefaultDataDirectory, "ui.json")));
        var root = document.RootElement;
        var overview = root.GetProperty("settlersOverview");
        var icons = overview.GetProperty("icons");
        var gui = gfx.Open(3);
        _room = Load(gui, icons.GetProperty("room").GetInt32());
        _needing = Load(gui, icons.GetProperty("needing").GetInt32());
        _total = Load(gui, icons.GetProperty("total").GetInt32());
        var weapons = root.GetProperty("soldiersMenu").GetProperty("weapons").GetProperty(people.ToString());
        _soldiers = Load(gfx.Open(weapons.GetProperty("file").GetInt32()), weapons.GetProperty("SWORDSMAN").GetInt32());
        _rows = overview.GetProperty("rows").EnumerateArray().Select(r => r.EnumerateArray().Select(t => t.GetString()!).ToArray()).ToArray();
    }

    private static int Left => BuildMenu.Content.X + 4;
    private static int Width => BuildMenu.Content.Width - 8;
    private static int Top => BuildMenu.Content.Y + 24; // below the title
    private const int HeaderHeight = 54;
    private const int CellHeight = 80;

    public void Draw(SpriteBatch batch, UiFont font, World world, int player, Portraits portraits, SettlerRenderer settlers, Civilization people)
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
                if (cell.Bottom > BuildMenu.Content.Bottom)
                    break;
                portraits.Draw(batch, PortraitOf(kind), new Rectangle(cell.X, cell.Y, cell.Width, Portraits.Size.Y), settlers, people, PlayerColors.Of(player));
                string count = own.Count(s => Is(s, kind)).ToString();
                var countSize = font.Measure(count, 14);
                Write(batch, font, count, new Vector2(cell.Center.X - countSize.X / 2, cell.Y + Portraits.Size.Y + 1), 14);
            }
    }

    /// <summary>An icon centered in header column <paramref name="col"/> with its number below.</summary>
    private static void Icon(SpriteBatch batch, UiFont font, Texture2D icon, string number, int col)
    {
        int column = Width / 4;
        float center = Left + col * column + column / 2f;
        batch.Draw(icon, new Vector2(center - icon.Width / 2f, Top), Color.White);
        var size = font.Measure(number, 15);
        Write(batch, font, number, new Vector2(center - size.X / 2, Top + icon.Height + 1), 15);
    }

    /// <summary>The portrait of a kind: soldiers by their first level.</summary>
    private static string PortraitOf(string kind) => kind is "SWORDSMAN" or "BOWMAN" or "PIKEMAN" ? $"{kind}_L1" : kind;

    /// <summary>Whether the settler counts as the kind (pioneers and donkeys do not exist yet; thieves are the scouts).</summary>
    private static bool Is(Settler settler, string kind) => kind switch
    {
        "BEARER" => settler.Profession == Profession.Carrier,
        "DIGGER" => settler.Profession == Profession.Digger,
        "BRICKLAYER" => settler.Profession == Profession.Builder,
        "WORKER" => settler.Profession == Profession.Worker,
        "SWORDSMAN" => settler is { Profession: Profession.Soldier, SoldierType: SoldierType.Swordsman },
        "BOWMAN" => settler is { Profession: Profession.Soldier, SoldierType: SoldierType.Bowman },
        "PIKEMAN" => settler is { Profession: Profession.Soldier, SoldierType: SoldierType.Pikeman },
        "MAGE" => settler.Profession == Profession.Priest,
        "GEOLOGIST" => settler.Profession == Profession.Geologist,
        "THIEF" => settler.Profession == Profession.Scout,
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
