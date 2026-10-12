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
/// of needed) and a mine's hit rate, and at the bottom a button to tear it down (asks first). Names and
/// states come from <c>data/ui.json</c>.
/// </summary>
public sealed partial class BuildingInfo
{
    private const int Margin = 6;
    private const int Inner = BuildMenu.Width - 2 * Margin;
    private static readonly Color Text = new(240, 225, 190);
    private static readonly Color Label = new(190, 165, 120);

    private readonly Dictionary<string, string> _names = new();
    private readonly List<(string Contains, string State)> _states = new();
    private readonly string _active;

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
    }

    public string NameOf(BuildingDefinition definition) => _names.GetValueOrDefault(definition.Name, definition.Name);

    private static Rectangle DemolishButton(int height) => new(Margin, height - Margin - 28, Inner, 28);

    private static Rectangle YesButton(int height) => new(Margin, height - Margin - 28, Inner / 2 - 2, 28);

    private static Rectangle NoButton(int height) => new(Margin + Inner / 2 + 2, height - Margin - 28, Inner / 2 - 2, 28);

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

    public void Draw(SpriteBatch batch, Texture2D pixel, int height, UiFont font, EconomyRenderer economy, World world, Building building,
        int ownPlayer, Vector2 cursor)
    {
        batch.Draw(pixel, new Rectangle(0, 0, BuildMenu.Width, height), new Color(36, 28, 18, 230));
        int y = Margin;
        foreach (string line in font.Wrap(NameOf(building.Definition), Inner, 16))
        {
            font.Draw(batch, line, new Vector2(Margin, y), Text, 16);
            y += 19;
        }
        y += 4;
        var imageBox = new Rectangle(Margin, y, Inner, 96);
        batch.Draw(pixel, imageBox, new Color(60, 47, 30));
        economy.DrawBuildingIcon(batch, building.Definition, imageBox, PlayerColors.Of(building.Player));
        y = imageBox.Bottom + 8;
        if (building.Player != ownPlayer)
            return;
        DrawDemolish(batch, pixel, height, font, building, cursor);

        // State.
        font.Draw(batch, "Status", new Vector2(Margin, y), Label, 12);
        y += 15;
        foreach (string line in font.Wrap(StateOf(world, building), Inner))
        {
            font.Draw(batch, line, new Vector2(Margin, y), Text);
            y += 17;
        }
        if (world.Production.MineHitRate(building) is { } rate)
        {
            y += 4;
            font.Draw(batch, $"Trefferquote: {rate} %", new Vector2(Margin, y), Text);
            y += 17;
        }

        // Goods: on a construction site delivered of needed, otherwise what lies in the piles.
        var goods = Goods(building);
        if (goods.Count == 0)
            return;
        y += 8;
        font.Draw(batch, building.State == BuildingState.Finished ? "Waren" : "Baumaterial", new Vector2(Margin, y), Label, 12);
        y += 16;
        for (int i = 0; i < goods.Count; i++)
        {
            var cell = new Rectangle(Margin + i % 2 * (Inner / 2), y + i / 2 * 32, Inner / 2, 30);
            economy.DrawMaterialIcon(batch, goods[i].Material, new Rectangle(cell.X, cell.Y, 26, 28));
            font.Draw(batch, goods[i].Text, new Vector2(cell.X + 29, cell.Y + 6), Text);
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
        font.Draw(batch, question, new Vector2(Margin + (Inner - font.Measure(question).X) / 2, height - Margin - 50), Text);
        Button(batch, pixel, font, YesButton(height), "Ja", cursor);
        Button(batch, pixel, font, NoButton(height), "Nein", cursor);
    }

    private static void Button(SpriteBatch batch, Texture2D pixel, UiFont font, Rectangle rect, string label, Vector2 cursor)
    {
        batch.Draw(pixel, rect, rect.Contains(cursor) ? new Color(150, 115, 60) : new Color(80, 62, 38));
        var size = font.Measure(label);
        font.Draw(batch, label, new Vector2(rect.X + (rect.Width - size.X) / 2, rect.Y + (rect.Height - size.Y) / 2), Text);
    }

    /// <summary>The short German state of the building (soldiers for military buildings).</summary>
    private string StateOf(World world, Building building)
    {
        string status = world.StatusOf(building);
        if (SoldiersPattern().Match(status) is { Success: true } soldiers)
            return $"{soldiers.Groups[1].Value}/{soldiers.Groups[2].Value} Soldaten"
                   + (soldiers.Groups[3].Success ? $", {soldiers.Groups[3].Value} unterwegs" : "");
        foreach (var (contains, state) in _states)
            if (status.Contains(contains, StringComparison.Ordinal))
                return state;
        return _active;
    }

    private static List<(Material Material, string Text)> Goods(Building building)
    {
        if (building.State != BuildingState.Finished)
        {
            // Construction piles count their capacity down as material is built in.
            var needed = building.Definition.ConstructionPiles.Where(p => p.Material is not null)
                .GroupBy(p => p.Material!.Value).ToDictionary(g => g.Key, g => g.Sum(p => p.Required));
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
}
