using Homestead.Simulation;
using Homestead.Simulation.Commands;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Homestead.Client;

/// <summary>
/// Waren › Lagerbestand in the blue panel, like the original's: every good (<see cref="GoodsGrid"/>) with
/// how many units the player has in stock (<see cref="World.InStock"/>) below it; goods the people cannot
/// make show their number only when there are some. Panel space.
/// </summary>
public sealed class StockMenu : IPanelMenu
{
    private static readonly Color Text = new(240, 225, 190);
    private const int RowHeight = 46;

    private readonly GoodsGrid _grid;
    private readonly PanelLayout _layout;

    public StockMenu(GoodsGrid grid, PanelLayout layout)
    {
        _grid = grid;
        _layout = layout;
    }

    private int GridTop => _layout.Content.Y + 32; // below the title

    public void Draw(SpriteBatch batch, Texture2D pixel, UiFont font, World world, int player, Vector2 cursor, float seconds)
    {
        _grid.Draw(batch, GridTop, RowHeight);
        foreach (var good in _grid.Goods)
        {
            var rect = _grid.IconRect(good, GridTop, RowHeight);
            if (rect.Contains(cursor))
                PanelUi.HoverOverlay(batch, pixel, rect);
            int count = world.InStock(player, good.Material);
            if (count == 0 && !_grid.IsMakeable(good.Material))
                continue;
            string text = count.ToString();
            var size = font.Measure(text, 15);
            SelectionPanel.Shadowed(batch, font, text, new Vector2(rect.Center.X - size.X / 2, rect.Bottom), 15, Text);
        }
    }

    /// <summary>No clicks here: the stock only shows.</summary>
    public string? Update(Vector2 panel, bool pressed, bool justPressed, bool onMenu, World world, int player, IssueCommand issue) => null;

    public void ResetInput() { }

    /// <summary>The good under the point and how many are in stock (hover text), or null.</summary>
    public string? Hover(Vector2 panel, World world, int player) =>
        _grid.GoodAt(panel, GridTop, RowHeight) is { } good ? $"{good.Name}: {world.InStock(player, good.Material)} in stock" : null;

    /// <summary>The tooltip of the good under the panel point (its name), or null.</summary>
    public string? TooltipAt(Vector2 panel) => _grid.GoodAt(panel, GridTop, RowHeight) is { } good ? good.Name : null;
}
