using System.Text.Json;
using System.Text.RegularExpressions;
using Homestead.Simulation;
using Homestead.Simulation.Buildings;
using Homestead.Simulation.Economy;
using Homestead.Simulation.Magic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Homestead.Client;

/// <summary>
/// Shown at the left edge instead of the build menu while a building is selected: its name and image;
/// for own buildings also its state, the goods in its piles (construction sites: material delivered
/// of needed) and a mine's hit rate, and at the bottom a button to tear it down (asks first), above it for
/// production buildings one to stop or restart them. A finished
/// small temple also offers the soldier upgrades for mana, a finished military building its garrison per
/// soldier type with buttons for more or fewer. Names and states come from <c>data/ui.json</c>.
/// </summary>
public sealed partial class BuildingInfo
{
    // The content area of the blue panel (like the build menu), no background of its own.
    private static readonly int Left = BuildMenu.Content.X + 4; // clear of the vines on the panel's frame
    private static readonly int Top = BuildMenu.Content.Y;
    private static readonly int Bottom = BuildMenu.Content.Bottom;
    private static readonly int Inner = BuildMenu.Content.Width - 8;
    private static readonly Color ButtonColor = Color.Black * 0.3f;
    private static readonly Color HoverColor = Color.White * 0.15f;
    private static readonly Color Text = new(240, 225, 190);
    private static readonly Color Label = new(190, 165, 120);

    private readonly Dictionary<string, string> _names = new();
    private readonly List<(string Contains, string State)> _states = new();
    private readonly string _active;
    private readonly Dictionary<string, string> _soldierNames = new();
    private readonly string[] _soldierLevels;

    /// <summary>The building whose tear-down button was pressed: the panel asks whether to go on.</summary>
    private Building? _confirming;

    public BuildingInfo()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(BuildingCatalog.DefaultDataDirectory, "ui.json")));
        var root = document.RootElement;
        foreach (var name in root.GetProperty("buildingNames").EnumerateObject())
            _names[name.Name] = name.Value.GetString()!;
        foreach (var state in root.GetProperty("status").EnumerateArray())
            _states.Add((state[0].GetString()!, state[1].GetString()!));
        _active = root.GetProperty("activeStatus").GetString()!;
        foreach (var name in root.GetProperty("soldierNames").EnumerateObject())
            _soldierNames[name.Name] = name.Value.GetString()!;
        _soldierLevels = root.GetProperty("soldierLevels").EnumerateArray().Select(l => l.GetString()!).ToArray();
    }

    private const int UpgradeRow = 46;

    /// <summary>Height of the building image (cut off at the top when it is taller); less when the panel is full.</summary>
    private const int ImageHeight = 92;
    private const int MinImageHeight = 36;

    /// <summary>Below this the goods are left out to give the image room.</summary>
    private const int GoodImageHeight = 64;

    /// <summary>Where the buttons and rows at the bottom of an own building's panel begin.</summary>
    private int BlockTop(World world, Building building)
    {
        if (_confirming == building)
            return Bottom - 50 - 4;
        if (OffersUpgrades(world, building))
            return UpgradeButton(0, 0).Y - 16 - 4;
        if (OffersGarrison(building))
            return GarrisonRowRect(0, 0).Y - 16 - 4;
        return OffersPause(world, building) ? PauseButton(0).Y - 6 : DemolishButton(0).Y - 6;
    }

    /// <summary>Text with a dark shadow, readable on the blue panel.</summary>
    private static void Write(SpriteBatch batch, UiFont font, string text, Vector2 position, Color color, int size = 14) =>
        SelectionPanel.Shadowed(batch, font, text, position, size, color);

    /// <summary>The upgrade buttons of a small temple, one per soldier type, above the tear-down button.</summary>
    private static Rectangle UpgradeButton(int height, int index) =>
        new(Left, Bottom - 28 - 10 - (Enum.GetValues<SoldierType>().Length - index) * (UpgradeRow + 2), Inner, UpgradeRow);

    private static bool OffersUpgrades(World world, Building building) =>
        building.Definition.Name == world.Magic.Config.Temple && building.State == BuildingState.Finished && world.Magic.Config.UpgradeThresholds.Count > 0;

    /// <summary>The soldier type whose upgrade button of an own small temple is under the screen point.</summary>
    public SoldierType? UpgradeAt(Vector2 screen, int height, World world, Building building, int ownPlayer)
    {
        if (building.Player != ownPlayer || !OffersUpgrades(world, building) || _confirming == building)
            return null;
        var types = Enum.GetValues<SoldierType>();
        for (int i = 0; i < types.Length; i++)
            if (UpgradeButton(height, i).Contains(screen))
                return types[i];
        return null;
    }

    private const int GarrisonRow = 38;
    private const int GarrisonButtonSize = 20;

    /// <summary>The garrison rows of a military building, one per soldier type, above the tear-down button.</summary>
    private static Rectangle GarrisonRowRect(int height, int index) =>
        new(Left, Bottom - 28 - 10 - (Enum.GetValues<SoldierType>().Length - index) * (GarrisonRow + 2), Inner, GarrisonRow);

    private static Rectangle GarrisonButton(int height, int index, int delta)
    {
        var row = GarrisonRowRect(height, index);
        int x = delta < 0 ? row.Right - 2 * GarrisonButtonSize - 6 : row.Right - GarrisonButtonSize - 2;
        return new Rectangle(x, row.Bottom - GarrisonButtonSize - 2, GarrisonButtonSize, GarrisonButtonSize);
    }

    private static bool OffersGarrison(Building building) =>
        building.Definition.Occupiers.Count > 0 && building.State == BuildingState.Finished;

    /// <summary>The soldier type and change (+1 or -1) of the garrison button of an own military building under the screen point.</summary>
    public (SoldierType Type, int Delta)? GarrisonChangeAt(Vector2 screen, int height, Building building, int ownPlayer)
    {
        if (building.Player != ownPlayer || !OffersGarrison(building) || _confirming == building)
            return null;
        var types = Enum.GetValues<SoldierType>();
        for (int i = 0; i < types.Length; i++)
            foreach (int delta in new[] { -1, 1 })
                if (GarrisonButton(height, i, delta).Contains(screen))
                    return (types[i], delta);
        return null;
    }

    /// <summary>
    /// Per soldier type its figure, name, how many are inside of how many the building wants, and buttons
    /// for one fewer or one more (greyed out where the building would be left empty or overfull).
    /// </summary>
    private void DrawGarrison(SpriteBatch batch, Texture2D pixel, int height, UiFont font, SettlerRenderer settlers, Civilization people,
        World world, Building building, Vector2 cursor)
    {
        var military = world.Military;
        var types = Enum.GetValues<SoldierType>();
        Write(batch, font, "Besatzung (da / Soll)", new Vector2(Left, GarrisonRowRect(height, 0).Y - 16), Label, 12);
        var occupants = military.Occupants(building);
        for (int i = 0; i < types.Length; i++)
        {
            var type = types[i];
            var row = GarrisonRowRect(height, i);
            int level = world.Magic.SoldierLevel(building.Player, type);
            settlers.DrawIcon(batch, people, $"{type.ToString().ToUpperInvariant()}_L{level}", new Rectangle(row.X + 2, row.Y + 3, 24, row.Height - 6),
                1.2f, PlayerColors.Of(building.Player));
            Write(batch, font, SoldierName(type), new Vector2(row.X + 28, row.Y + 2), Text, 12);
            int inside = occupants.Count(s => s?.SoldierType == type);
            Write(batch, font, $"{inside} / {military.Wanted(building, type)}", new Vector2(row.X + 28, row.Y + 19), Text, 12);
            foreach (int delta in new[] { -1, 1 })
            {
                var button = GarrisonButton(height, i, delta);
                bool possible = military.CanChangeWanted(building, type, delta);
                batch.Draw(pixel, button, !possible ? Color.Black * 0.15f : button.Contains(cursor) ? HoverColor : ButtonColor);
                if (possible)
                    Outline(batch, pixel, button);
                string label = delta < 0 ? "−" : "+";
                var size = font.Measure(label);
                Write(batch, font, label, new Vector2(button.X + (button.Width - size.X) / 2, button.Y + (button.Height - size.Y) / 2),
                    possible ? Text : new Color(110, 95, 70));
            }
        }
    }

    public string SoldierName(SoldierType type) => _soldierNames.GetValueOrDefault(type.ToString(), type.ToString());

    public string LevelName(int level) => _soldierLevels[Math.Clamp(level, 1, _soldierLevels.Length) - 1];

    /// <summary>
    /// Mana earned in all and one button per soldier type: its figure at the level new soldiers get, name
    /// and rank, and how much earned mana the next upgrade takes (greyed out while it is not possible yet).
    /// </summary>
    private void DrawUpgrades(SpriteBatch batch, Texture2D pixel, int height, UiFont font, SettlerRenderer settlers, Civilization people,
        World world, int player, Vector2 cursor)
    {
        var magic = world.Magic;
        var types = Enum.GetValues<SoldierType>();
        var first = UpgradeButton(height, 0);
        Write(batch, font, $"Aufwertung · Mana gesamt {magic.ManaEarned(player)}", new Vector2(Left, first.Y - 16), Label, 12);
        for (int i = 0; i < types.Length; i++)
        {
            var type = types[i];
            var rect = UpgradeButton(height, i);
            int level = magic.SoldierLevel(player, type);
            var order = magic.UpgradeOrder(player, type);
            bool possible = order == UpgradeResult.Done && magic.CanAffordUpgrade(player);
            batch.Draw(pixel, rect, possible && rect.Contains(cursor) ? HoverColor : ButtonColor);
            if (possible)
                Outline(batch, pixel, rect);
            settlers.DrawIcon(batch, people, $"{type.ToString().ToUpperInvariant()}_L{level}", new Rectangle(rect.X + 2, rect.Y + 3, 30, rect.Height - 6),
                1.5f, PlayerColors.Of(player));
            Write(batch, font, SoldierName(type), new Vector2(rect.X + 34, rect.Y + 1), Text, 12);
            Write(batch, font, LevelName(level), new Vector2(rect.X + 34, rect.Y + 15), Label, 12);
            string next = order switch
            {
                UpgradeResult.Highest => "höchste Stufe",
                UpgradeResult.OthersFirst => "erst die anderen",
                _ => $"ab {magic.UpgradeThreshold(player)} Mana",
            };
            Write(batch, font, next, new Vector2(rect.X + 34, rect.Y + 29), possible ? Text : new Color(150, 130, 100), 12);
        }
    }

    public string NameOf(BuildingDefinition definition) => _names.GetValueOrDefault(definition.Name, definition.Name);

    private static Rectangle DemolishButton(int height) => new(Left, Bottom - 28, Inner, 28);

    /// <summary>The button stopping or restarting production, above the tear-down button.</summary>
    private static Rectangle PauseButton(int height) => new(Left, Bottom - 28 - 6 - 28, Inner, 28);

    private static bool OffersPause(World world, Building building) =>
        world.Production.CanPause(building) && !OffersUpgrades(world, building) && !OffersGarrison(building);

    /// <summary>Whether the screen point is on the stop/restart button of an own production building.</summary>
    public bool PauseAt(Vector2 screen, int height, World world, Building building, int ownPlayer) =>
        building.Player == ownPlayer && _confirming != building && OffersPause(world, building) && PauseButton(height).Contains(screen);

    private static Rectangle YesButton(int height) => new(Left, Bottom - 28, Inner / 2 - 2, 28);

    private static Rectangle NoButton(int height) => new(Left + Inner / 2 + 2, Bottom - 28, Inner / 2 - 2, 28);

    /// <summary>
    /// A left click on the panel. True if it confirmed tearing down <paramref name="building"/>: first the
    /// button, then "Ja"; "Nein" goes back.
    /// </summary>
    public bool Click(Vector2 screen, int height, Building building, int ownPlayer)
    {
        if (building.Player != ownPlayer)
            return false;
        if (_confirming != building)
        {
            if (DemolishButton(height).Contains(screen))
                _confirming = building;
            return false;
        }
        if (YesButton(height).Contains(screen))
        {
            _confirming = null;
            return true;
        }
        if (NoButton(height).Contains(screen))
            _confirming = null;
        return false;
    }

    public void Draw(SpriteBatch batch, Texture2D pixel, int height, UiFont font, EconomyRenderer economy, SettlerRenderer settlers,
        Civilization people, World world, Building building, int ownPlayer, Vector2 cursor)
    {
        int y = Top;
        foreach (string line in font.Wrap(NameOf(building.Definition), Inner, 16))
        {
            Write(batch, font, line, new Vector2(Left, y), Text, 16);
            y += 19;
        }
        y += 4;
        bool own = building.Player == ownPlayer;
        var state = StateOf(world, building);
        int? hitRate = world.Production.MineHitRate(building);
        var goods = own ? Goods(world, building) : [];
        // The image gives way when the panel is full (cut off at the top).
        int limit = own ? BlockTop(world, building) : Bottom;
        int text = 15 + font.Wrap(state, Inner).Count() * 17 + (hitRate is null ? 0 : 21);
        // When it gets tight the goods go first, then the image shrinks.
        if (goods.Count > 0 && limit - y - 8 - text - 4 - (24 + 32) < GoodImageHeight)
            goods = [];
        if (goods.Count > 0)
            text += 24 + 32;
        int imageHeight = own ? Math.Clamp(limit - y - 8 - text - 4, MinImageHeight, ImageHeight) : ImageHeight;
        var imageBox = new Rectangle(Left, y, Inner, imageHeight);
        economy.DrawBuildingImage(batch, building.Definition, imageBox, PlayerColors.Of(building.Player));
        y = imageBox.Bottom + 8;
        if (!own)
            return;
        DrawDemolish(batch, pixel, height, font, building, cursor);
        if (OffersPause(world, building) && _confirming != building)
            Button(batch, pixel, font, PauseButton(height), building.IsPaused ? "Fortsetzen" : "Anhalten", cursor);
        if (OffersUpgrades(world, building) && _confirming != building)
            DrawUpgrades(batch, pixel, height, font, settlers, people, world, ownPlayer, cursor);
        if (OffersGarrison(building) && _confirming != building)
            DrawGarrison(batch, pixel, height, font, settlers, people, world, building, cursor);

        // State.
        Write(batch, font, "Status", new Vector2(Left, y), Label, 12);
        y += 15;
        foreach (string line in font.Wrap(state, Inner))
        {
            Write(batch, font, line, new Vector2(Left, y), Text);
            y += 17;
        }
        if (hitRate is { } rate)
        {
            y += 4;
            Write(batch, font, $"Trefferquote: {rate} %", new Vector2(Left, y), Text);
            y += 17;
        }

        // Goods: on a construction site delivered of needed, otherwise what lies in the piles (as far as there is room).
        if (goods.Count == 0 || y + 8 + 16 + 30 > limit)
            return;
        y += 8;
        Write(batch, font, building.State == BuildingState.Finished ? "Waren" : "Baumaterial", new Vector2(Left, y), Label, 12);
        y += 16;
        for (int i = 0; i < goods.Count; i++)
        {
            var cell = new Rectangle(Left + i % 2 * (Inner / 2), y + i / 2 * 32, Inner / 2, 30);
            if (cell.Bottom > limit)
                break;
            economy.DrawMaterialIcon(batch, goods[i].Material, new Rectangle(cell.X, cell.Y, 26, 28));
            Write(batch, font, goods[i].Text, new Vector2(cell.X + 29, cell.Y + 6), Text);
        }
    }

    /// <summary>The tear-down button at the bottom, or the question whether to go on.</summary>
    private void DrawDemolish(SpriteBatch batch, Texture2D pixel, int height, UiFont font, Building building, Vector2 cursor)
    {
        if (_confirming != building)
        {
            Button(batch, pixel, font, DemolishButton(height), "Abreißen", cursor);
            return;
        }
        string question = "Wirklich abreißen?";
        Write(batch, font, question, new Vector2(Left + (Inner - font.Measure(question).X) / 2, Bottom - 50), Text);
        Button(batch, pixel, font, YesButton(height), "Ja", cursor);
        Button(batch, pixel, font, NoButton(height), "Nein", cursor);
    }

    private static void Button(SpriteBatch batch, Texture2D pixel, UiFont font, Rectangle rect, string label, Vector2 cursor)
    {
        batch.Draw(pixel, rect, rect.Contains(cursor) ? HoverColor : ButtonColor);
        Outline(batch, pixel, rect);
        var size = font.Measure(label);
        Write(batch, font, label, new Vector2(rect.X + (rect.Width - size.X) / 2, rect.Y + (rect.Height - size.Y) / 2), Text);
    }

    /// <summary>A thin light frame, so a button stands out on the blue panel.</summary>
    private static void Outline(SpriteBatch batch, Texture2D pixel, Rectangle rect)
    {
        var color = Label * 0.7f;
        batch.Draw(pixel, new Rectangle(rect.X, rect.Y, rect.Width, 1), color);
        batch.Draw(pixel, new Rectangle(rect.X, rect.Bottom - 1, rect.Width, 1), color);
        batch.Draw(pixel, new Rectangle(rect.X, rect.Y, 1, rect.Height), color);
        batch.Draw(pixel, new Rectangle(rect.Right - 1, rect.Y, 1, rect.Height), color);
    }

    /// <summary>The short German state of the building (soldiers for military buildings).</summary>
    private string StateOf(World world, Building building)
    {
        string status = world.StatusOf(building);
        if (SoldiersPattern().Match(status) is { Success: true } soldiers)
            return $"{soldiers.Groups[1].Value}/{soldiers.Groups[2].Value} Soldaten"
                   + (soldiers.Groups[3].Success ? $", {soldiers.Groups[3].Value} unterwegs" : "")
                   + (DoorPattern().Match(status) is { Success: true } door ? $", Tür {door.Groups[1].Value} %" : "");
        foreach (var (contains, state) in _states)
            if (status.Contains(contains, StringComparison.Ordinal))
                return state;
        return _active;
    }

    private static List<(Material Material, string Text)> Goods(World world, Building building)
    {
        if (building.State != BuildingState.Finished)
        {
            // Construction piles count their capacity down as material is built in.
            var needed = building.Definition.ConstructionPiles.Where(p => p.Material is not null)
                .GroupBy(p => p.Material!.Value).ToDictionary(g => g.Key, g => g.Sum(p => world.ConstructionCost(building.Player, p)));
            return needed.Select(n =>
            {
                var piles = building.Piles.Where(p => p.Role == PileRole.Request && p.AcceptedMaterial == n.Key).ToList();
                int delivered = n.Value - piles.Sum(p => p.Capacity) + piles.Sum(p => p.Count);
                return (n.Key, $"{Math.Clamp(delivered, 0, n.Value)}/{n.Value}");
            }).ToList();
        }
        return building.Piles.Where(p => (p.Material ?? p.AcceptedMaterial) is not null && (p.Role != PileRole.Storage || p.Count > 0))
            .GroupBy(p => (p.Material ?? p.AcceptedMaterial)!.Value)
            .Select(g => (g.Key, g.Sum(p => p.Count).ToString()))
            .ToList();
    }

    [GeneratedRegex(@"^(\d+)/(\d+) soldiers(?:, (\d+) on the way)?")]
    private static partial Regex SoldiersPattern();

    [GeneratedRegex(@", door (\d+)%")]
    private static partial Regex DoorPattern();
}
