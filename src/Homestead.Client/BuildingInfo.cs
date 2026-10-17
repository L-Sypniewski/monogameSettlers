using System.Text.Json;
using System.Text.RegularExpressions;
using Homestead.Simulation;
using Homestead.Simulation.Buildings;
using Homestead.Simulation.Economy;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Homestead.Client;

/// <summary>
/// Shown at the left edge instead of the build menu while a building is selected: its name and image;
/// for own buildings also its state, the goods in its piles (construction sites: material delivered
/// of needed) and a mine's hit rate, and at the bottom a button to tear it down (asks first), above it for
/// production buildings one to stop or restart them. A finished
/// military building also offers its garrison per soldier type with buttons for more or fewer (the soldier
/// upgrades are in Siedler › Krieger, <see cref="SoldiersMenu"/>). Names and states come from <c>data/ui.json</c>.
/// </summary>
public sealed partial class BuildingInfo
{
    // The content area of the blue panel (like the build menu), no background of its own.
    private static readonly int Left = BuildMenu.Content.X + 4; // clear of the vines on the panel's frame
    private static readonly int Top = BuildMenu.Content.Y;
    private static readonly int Bottom = BuildMenu.Content.Bottom;
    private static readonly int Inner = BuildMenu.Content.Width - 8;
    private static readonly Color HoverColor = Color.White * 0.15f;
    private static readonly Color Text = new(240, 225, 190);
    private static readonly Color Label = new(190, 165, 120);
    private static readonly Color Selected = new(220, 30, 20);

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

    /// <summary>Height of the building image (cut off at the top when it is taller); less when the panel is full.</summary>
    private const int ImageHeight = 92;
    private const int MinImageHeight = 36;

    /// <summary>Below this the goods are left out to give the image room.</summary>
    private const int GoodImageHeight = 64;

    /// <summary>The goods of a storehouse's panel (set once the people is known).</summary>
    public GoodsGrid? StorehouseGoods { get; set; }

    private const int StorehouseRow = 31;

    /// <summary>A finished own storehouse shows the goods to specialize it on.</summary>
    private bool OffersStorehouse(Building building) =>
        StorehouseGoods is not null && building.State == BuildingState.Finished && building.Piles.Any(p => p.Role == PileRole.Storage);

    private int StorehouseTop => DemolishButton(0).Y - 6 - (StorehouseGoods!.Goods.Max(g => g.Row) + 1) * StorehouseRow;

    /// <summary>Where the buttons and rows at the bottom of an own building's panel begin.</summary>
    private int BlockTop(World world, Building building)
    {
        if (_confirming == building)
            return Bottom - 50 - 4;
        if (OffersStorehouse(building))
            return StorehouseTop - 4;
        if (OffersGarrison(building))
            return GarrisonRowRect(0, 0).Y - 16 - 4;
        return OffersPause(world, building) ? PauseButton(0).Y - 6 : DemolishButton(0).Y - 6;
    }

    /// <summary>Text with a dark shadow, readable on the blue panel.</summary>
    private static void Write(SpriteBatch batch, UiFont font, string text, Vector2 position, Color color, int size = 14) =>
        SelectionPanel.Shadowed(batch, font, text, position, size, color);

    private const int GarrisonRow = 38;

    /// <summary>The garrison rows of a military building, one per soldier type, above the tear-down button.</summary>
    private static Rectangle GarrisonRowRect(int height, int index) =>
        new(Left, Bottom - 28 - 10 - (Enum.GetValues<SoldierType>().Length - index) * (GarrisonRow + 2), Inner, GarrisonRow);

    /// <summary>The up/down spinner of a garrison row (as in Waren › Herstellung): upper half one more, lower half one fewer.</summary>
    private static Rectangle GarrisonSpinner(int height, int index)
    {
        var row = GarrisonRowRect(height, index);
        return new Rectangle(row.Right - 19 - 2, row.Y + (row.Height - 28) / 2, 19, 28);
    }

    private static Rectangle SpinnerHalf(Rectangle spinner, int delta) =>
        new(spinner.X, delta > 0 ? spinner.Y : spinner.Center.Y, spinner.Width, spinner.Height / 2);

    /// <summary>The spinner image (that of Waren › Herstellung; set once loaded).</summary>
    public Texture2D? Spinner { get; set; }

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
                if (SpinnerHalf(GarrisonSpinner(height, i), delta).Contains(screen))
                    return (types[i], delta);
        return null;
    }

    /// <summary>
    /// Per soldier type its figure, name, how many are inside of how many the building wants, and a spinner
    /// for one more or one fewer (a half greyed out where the building would be left empty or overfull).
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
            var spinner = GarrisonSpinner(height, i);
            if (Spinner is not null)
                batch.Draw(Spinner, spinner, Color.White);
            foreach (int delta in new[] { -1, 1 })
            {
                var half = SpinnerHalf(spinner, delta);
                // Greyed out where the building would be left empty or overfull.
                if (!military.CanChangeWanted(building, type, delta))
                    batch.Draw(pixel, half, Color.Black * 0.55f);
                else if (half.Contains(cursor))
                    batch.Draw(pixel, half, HoverColor);
            }
        }
    }

    public string SoldierName(SoldierType type) => _soldierNames.GetValueOrDefault(type.ToString(), type.ToString());

    public string LevelName(int level) => _soldierLevels[Math.Clamp(level, 1, _soldierLevels.Length) - 1];

    public string NameOf(BuildingDefinition definition) => _names.GetValueOrDefault(definition.Name, definition.Name);

    private static Rectangle DemolishButton(int height) => new(Left, Bottom - 28, Inner, 28);

    /// <summary>The button stopping or restarting production, above the tear-down button.</summary>
    private static Rectangle PauseButton(int height) => new(Left, Bottom - 28 - 6 - 28, Inner, 28);

    private static bool OffersPause(World world, Building building) =>
        world.Production.CanPause(building) && !OffersGarrison(building);

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
        double? chance = world.Production.MineChance(building);
        var goods = own && !OffersStorehouse(building) ? Goods(world, building) : [];
        // The image gives way when the panel is full (cut off at the top).
        int limit = own ? BlockTop(world, building) : Bottom;
        int text = 15 + font.Wrap(state, Inner).Count() * 17 + (chance is null ? 0 : 21);
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
        if (OffersGarrison(building) && _confirming != building)
            DrawGarrison(batch, pixel, height, font, settlers, people, world, building, cursor);
        if (OffersStorehouse(building) && _confirming != building)
            DrawStorehouse(batch, pixel, world, building, cursor);

        // State.
        Write(batch, font, "Status", new Vector2(Left, y), Label, 12);
        y += 15;
        foreach (string line in font.Wrap(state, Inner))
        {
            Write(batch, font, line, new Vector2(Left, y), Text);
            y += 17;
        }
        if (chance is { } c)
        {
            y += 4;
            int percent = (int)Math.Round(c * 100);
            Write(batch, font, percent == 0 && c > 0 ? "Erfolgschance: unter 1 %" : $"Erfolgschance: {percent} %", new Vector2(Left, y), Text);
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

    /// <summary>
    /// The storehouse's goods: without a specialization a dot on each from the player's rule (green: stored,
    /// red: not), with one only that good framed red.
    /// </summary>
    private void DrawStorehouse(SpriteBatch batch, Texture2D pixel, World world, Building building, Vector2 cursor)
    {
        var grid = StorehouseGoods!;
        int top = StorehouseTop;
        grid.Draw(batch, top, StorehouseRow);
        var only = world.Logistics.SpecializationOf(building);
        foreach (var good in grid.Goods)
        {
            var rect = GoodsGrid.IconRect(good, top, StorehouseRow);
            if (good.Material == only)
            {
                batch.Draw(pixel, new Rectangle(rect.X, rect.Y, rect.Width, 2), Selected);
                batch.Draw(pixel, new Rectangle(rect.X, rect.Bottom - 2, rect.Width, 2), Selected);
                batch.Draw(pixel, new Rectangle(rect.X, rect.Y, 2, rect.Height), Selected);
                batch.Draw(pixel, new Rectangle(rect.Right - 2, rect.Y, 2, rect.Height), Selected);
            }
            else if (rect.Contains(cursor))
                batch.Draw(pixel, rect, Color.White * 0.15f);
            if (only is null)
                TransportMenu.Dot(batch, pixel, new Point(rect.X + 3, rect.Y - 3),
                    world.Logistics.IsStored(building.Player, good.Material) ? TransportMenu.StoredDot : TransportMenu.NotStoredDot);
        }
    }

    /// <summary>
    /// A click on a storehouse's goods (panel space): specializes it on that good, or – on its own good –
    /// gives it the general rule again. Returns a message, or null if it hit nothing.
    /// </summary>
    public string? StorehouseClick(Vector2 panel, World world, Building building, int ownPlayer)
    {
        if (building.Player != ownPlayer || !OffersStorehouse(building) || _confirming == building
            || StorehouseGoods!.GoodAt(panel, StorehouseTop, StorehouseRow) is not { } good)
            return null;
        bool clear = world.Logistics.SpecializationOf(building) == good.Material;
        world.Logistics.SetSpecialization(building, clear ? null : good.Material);
        return clear ? "storehouse: the general rule again (Waren › Warentransport)" : $"storehouse: only {good.Name}";
    }

    /// <summary>What the storehouse's good under the point means (hover text), or null.</summary>
    public string? StorehouseHover(Vector2 panel, World world, Building building, int ownPlayer)
    {
        if (building.Player != ownPlayer || !OffersStorehouse(building) || _confirming == building
            || StorehouseGoods!.GoodAt(panel, StorehouseTop, StorehouseRow) is not { } good)
            return null;
        return world.Logistics.SpecializationOf(building) == good.Material
            ? $"only {good.Name} here (click: the general rule again)"
            : $"{good.Name}: click to take only this here";
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

    private static void Button(SpriteBatch batch, Texture2D pixel, UiFont font, Rectangle rect, string label, Vector2 cursor) =>
        TextButton.Draw(batch, pixel, font, rect, label, cursor);

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
