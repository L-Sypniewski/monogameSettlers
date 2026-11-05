using Homestead.Simulation;
using Homestead.Simulation.Commands;
using Homestead.Simulation.Buildings;
using Homestead.Simulation.Economy;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Homestead.Client;

/// <summary>
/// Waren › Verteilung in the blue panel, like the original's: at the left the goods with more than one
/// kind of user, at the right for the chosen one each user – construction sites („Baustelle“) and every
/// building type of the people that takes it – with its part in percent and a bar for its share
/// (<see cref="Logistics.ShareOf"/>, <see cref="ShareBar"/>). Goods in <c>data/ui.json</c>
/// (<c>distributionMenu</c>). Panel space.
/// </summary>
public sealed class DistributionMenu : IPanelMenu
{
    private static readonly Color Text = new(240, 225, 190);
    private static readonly Color Selected = new(220, 30, 20);
    private const int IconStep = 34;
    private const int EntryHeight = 46;

    private readonly GoodsGrid _grid;
    private readonly PanelLayout _layout;
    private readonly List<(GoodsGrid.Good Good, List<(string Target, string Name)> Targets)> _goods = new();
    private int _chosen;

    /// <summary>The target whose bar is being dragged, while the button is held.</summary>
    private string? _dragging;

    public DistributionMenu(GoodsGrid grid, BuildingCatalog catalog, Func<BuildingDefinition, string> nameOf, PanelLayout layout, UiLayout ui)
    {
        _grid = grid;
        _layout = layout;
        var buildings = catalog.All.Where(b => b.Name != "stock").ToList();
        foreach (var entry in ui.DistributionMenu.Goods)
        {
            var material = Materials.Parse(entry)!.Value;
            var targets = new List<(string, string)>();
            if (buildings.Any(b => b.ConstructionPiles.Any(p => p.Material == material)))
                targets.Add((Logistics.Construction, Lang.T("distribution.construction")));
            targets.AddRange(buildings.Where(b => b.RequestPiles.Any(p => p.Material == material)).Select(b => (b.Name, nameOf(b))));
            if (targets.Count >= 2 && grid.Goods.FirstOrDefault(g => g.Material == material) is { } good)
                _goods.Add((good, targets));
        }
        _chosen = Math.Max(0, _goods.FindIndex(g => g.Good.Material == Material.Plank));
    }

    /// <summary>Shows the distribution of this good, if the menu has it.</summary>
    public void Choose(Material material)
    {
        int index = _goods.FindIndex(g => g.Good.Material == material);
        if (index >= 0)
            _chosen = index;
    }

    // Layout (panel space), top down in the content area.
    private int Left => _layout.Content.X + 4;
    private int Width => _layout.Content.Width - 8;
    private int Top => _layout.Content.Y + 26; // below the title

    private Rectangle Icon(int i) => new(Left, Top + i * IconStep, GoodsGrid.IconSize, GoodsGrid.IconSize);

    private int EntryX => Left + 42;
    private int EntryY(int i) => Top + i * EntryHeight;
    private Rectangle Bar(int i) => new(EntryX + 46, EntryY(i) + 21, Left + Width - EntryX - 46, ShareBar.Height);

    public void Draw(SpriteBatch batch, Texture2D pixel, UiFont font, World world, int player, Vector2 cursor, float seconds)
    {
        for (int i = 0; i < _goods.Count; i++)
        {
            var icon = Icon(i);
            batch.Draw(_goods[i].Good.Icon, icon, Color.White);
            if (i == _chosen)
                PanelUi.Frame(batch, pixel, icon, Selected, 2);
            else if (icon.Contains(cursor))
                PanelUi.HoverOverlay(batch, pixel, icon);
        }
        if (_goods.Count == 0)
            return;
        var (good, targets) = _goods[_chosen];
        double total = targets.Sum(t => world.Logistics.ShareOf(player, good.Material, t.Target));
        for (int i = 0; i < targets.Count; i++)
        {
            var (target, name) = targets[i];
            double share = world.Logistics.ShareOf(player, good.Material, target);
            SelectionPanel.Shadowed(batch, font, name, new Vector2(EntryX, EntryY(i)), 15, Text);
            string percent = $"{(total > 0 ? share / total * 100 : 0):0}%";
            SelectionPanel.Shadowed(batch, font, percent, new Vector2(EntryX, EntryY(i) + 20), 15, Text);
            ShareBar.Draw(batch, pixel, Bar(i), share);
        }
    }

    /// <summary>
    /// Mouse handling (panel space): a press on a good chooses it, a press on a bar starts dragging it;
    /// while held, the dragged bar follows the cursor. Returns a message, or null.
    /// </summary>
    public string? Update(Vector2 panel, bool pressed, bool justPressed, bool onMenu, World world, int player, IssueCommand issue)
    {
        if (!pressed || _goods.Count == 0)
        {
            _dragging = null;
            return null;
        }
        var (good, targets) = _goods[_chosen];
        if (justPressed && onMenu)
        {
            for (int i = 0; i < _goods.Count; i++)
                if (Icon(i).Contains(panel))
                {
                    _chosen = i;
                    return $"{_goods[i].Good.Name}: distribution";
                }
            for (int i = 0; i < targets.Count; i++)
                if (Bar(i).Contains(panel))
                    _dragging = targets[i].Target;
        }
        if (_dragging is not { } dragged || targets.FindIndex(t => t.Target == dragged) is var index && index < 0)
            return null;
        double share = ShareBar.ValueAt(Bar(index), panel.X);
        issue(new SetDistribution(player, good.Material, dragged, share));
        return $"{good.Name} for {targets[index].Name}: share {share * 100:0} %";
    }

    /// <summary>What the element under the point does (hover text), or null.</summary>
    public string? Hover(Vector2 panel, World world, int player)
    {
        for (int i = 0; i < _goods.Count; i++)
            if (Icon(i).Contains(panel))
                return $"{_goods[i].Good.Name}: show its distribution";
        if (_goods.Count > 0)
        {
            var (good, targets) = _goods[_chosen];
            for (int i = 0; i < targets.Count; i++)
                if (Bar(i).Contains(panel))
                    return $"{good.Name} for {targets[i].Name} (click or drag; empty: only when no one else wants it)";
        }
        return null;
    }
}
