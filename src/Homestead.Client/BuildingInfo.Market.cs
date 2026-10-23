using System.Text.Json;
using Homestead.Assets.Gfx;
using Homestead.Simulation;
using Homestead.Simulation.Buildings;
using Homestead.Simulation.Economy;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Homestead.Client;

/// <summary>
/// A finished own market's panel (Handel – Land) as in the original: the route buttons ×1, ×2, ×3 (stops) and the
/// destination, the goods with how many to send each (∞: all there are), and for the chosen good ∞, 0 and the arrows
/// (5 or 1 more or fewer). After a route button, the next left click on the map sets that point (see <see cref="Trade"/>):
/// the destination first (it clears the stops), then stops if wanted.
/// </summary>
public sealed partial class BuildingInfo
{
    private static readonly int[] TradeSteps = [5, 1, -1, -5];

    private Texture2D? _routeStrip;
    private Texture2D? _allIcon;
    private Texture2D? _noneIcon;
    private Texture2D[] _tradeArrows = [];

    /// <summary>The route button chosen last: a stop (0–2) or the destination (<see cref="Trade.Stops"/>).</summary>
    public int RouteSlot { get; private set; } = Trade.Stops;

    /// <summary>The market whose route point the next left click on the map sets (<see cref="RouteSlot"/>), if any.</summary>
    private Building? _routeAiming;

    /// <summary>Whether the next left click on the map sets a route point of <paramref name="market"/>.</summary>
    public bool AimsRoute(Building? market) => market is not null && _routeAiming == market;

    /// <summary>No route point to set any more (right click, Esc).</summary>
    public void CancelRoute() => _routeAiming = null;

    /// <summary>The good the buttons below the goods change.</summary>
    public Material TradeGood { get; private set; } = Material.Plank;

    private void LoadMarketIcons(GraphicsDevice device, GfxFile gui)
    {
        Texture2D Load(int index)
        {
            var bitmap = gui.ReadGui(index);
            var texture = new Texture2D(device, bitmap.Width, bitmap.Height);
            texture.SetData(bitmap.Pixels);
            return texture;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(BuildingCatalog.DefaultDataDirectory, "ui.json")));
        var market = document.RootElement.GetProperty("market");
        _routeStrip = Load(market.GetProperty("route").GetInt32());
        _allIcon = Load(market.GetProperty("all").GetInt32());
        _noneIcon = Load(market.GetProperty("none").GetInt32());
        _tradeArrows = document.RootElement.GetProperty("soldiersMenu").GetProperty("arrows").EnumerateArray().Select(i => Load(i.GetInt32())).ToArray();
    }

    // Layout (panel space): below the name the route strip, then the goods with their amounts, the buttons at the bottom.
    private static int RouteTop => Top + DemolishSize + 4;
    private const int RouteHeight = 24;
    private static int MarketGoodsTop => RouteTop + RouteHeight + 6;
    private const int MarketRow = 41;
    private static Rectangle AllButton => new(Left, Bottom - 32, 30, 30);
    private static Rectangle NoneButton => new(Left + 34, Bottom - 32, 30, 30);
    private static Rectangle TradeArrow(int i) => new(Left + Inner - (4 - i) * 22 - 2, Bottom - 32, 20, 30);

    /// <summary>A button of the route strip: the stops, then the destination, side by side.</summary>
    private static Rectangle RouteButton(int i) =>
        new(Left + i * Inner / (Trade.Stops + 1), RouteTop, Inner / (Trade.Stops + 1), RouteHeight);

    private bool OffersMarket(World world, Building building) =>
        StorehouseGoods is not null && _routeStrip is not null && building.State == BuildingState.Finished && world.Trade.IsMarket(building);

    private void DrawMarket(SpriteBatch batch, Texture2D pixel, UiFont font, World world, Building building, Vector2 cursor)
    {
        var trade = world.Trade;
        // The route strip; buttons not set yet are dim, the one the next right click sets is framed.
        batch.Draw(_routeStrip!, new Rectangle(Left, RouteTop, Inner, RouteHeight), Color.White);
        for (int i = 0; i <= Trade.Stops; i++)
        {
            var button = RouteButton(i);
            bool set = i < Trade.Stops ? trade.StopOf(building, i) is not null : trade.DestinationOf(building) is not null;
            if (!set)
                batch.Draw(pixel, button, Color.Black * 0.45f);
            if (i == RouteSlot && AimsRoute(building))
                Frame(batch, pixel, button, Color.White);
            else if (button.Contains(cursor))
                batch.Draw(pixel, button, HoverColor);
        }

        var grid = StorehouseGoods!;
        grid.Draw(batch, MarketGoodsTop, MarketRow);
        foreach (var good in grid.Goods)
        {
            var rect = GoodsGrid.IconRect(good, MarketGoodsTop, MarketRow);
            if (good.Material == TradeGood)
                Frame(batch, pixel, rect, Selected);
            else if (rect.Contains(cursor))
                batch.Draw(pixel, rect, HoverColor);
            int amount = trade.Remaining(building, good.Material);
            if (amount == Trade.Unlimited)
                batch.Draw(_allIcon!, new Rectangle(rect.Center.X - 7, rect.Bottom, 14, 11), Color.White);
            else
            {
                string text = amount.ToString();
                var size = font.Measure(text, 11);
                Write(batch, font, text, new Vector2(rect.Center.X - size.X / 2, rect.Bottom - 1), Text, 11);
            }
        }

        Button(AllButton, _allIcon!);
        Button(NoneButton, _noneIcon!);
        for (int i = 0; i < _tradeArrows.Length; i++)
            Button(TradeArrow(i), _tradeArrows[i]);

        void Button(Rectangle rect, Texture2D icon)
        {
            batch.Draw(icon, rect, Color.White);
            if (rect.Contains(cursor))
                batch.Draw(pixel, rect, HoverColor);
        }
    }

    private static void Frame(SpriteBatch batch, Texture2D pixel, Rectangle rect, Color color)
    {
        batch.Draw(pixel, new Rectangle(rect.X, rect.Y, rect.Width, 2), color);
        batch.Draw(pixel, new Rectangle(rect.X, rect.Bottom - 2, rect.Width, 2), color);
        batch.Draw(pixel, new Rectangle(rect.X, rect.Y, 2, rect.Height), color);
        batch.Draw(pixel, new Rectangle(rect.Right - 2, rect.Y, 2, rect.Height), color);
    }

    /// <summary>A click on a market's panel (panel space). Returns a message, or null if it hit nothing.</summary>
    public string? MarketClick(Vector2 panel, World world, Building building, int ownPlayer)
    {
        if (building.Player != ownPlayer || !OffersMarket(world, building) || _confirming == building)
            return null;
        var trade = world.Trade;
        for (int i = 0; i <= Trade.Stops; i++)
            if (RouteButton(i).Contains(panel))
            {
                // The chosen stop once more: cleared.
                if (i == RouteSlot && AimsRoute(building) && i < Trade.Stops && trade.StopOf(building, i) is not null)
                {
                    trade.SetStop(building, i, null);
                    _routeAiming = null;
                    UiSounds.Toggle();
                    return $"stop {i + 1} cleared";
                }
                if (i < Trade.Stops && trade.DestinationOf(building) is null)
                    return "set the destination first (the parcel)";
                RouteSlot = i;
                _routeAiming = building;
                UiSounds.Toggle();
                return i < Trade.Stops ? $"click on the map: stop {i + 1} (this button again: clear it; right click: cancel)"
                    : "click on the map: the destination (clears the stops; right click: cancel)";
            }
        if (StorehouseGoods!.GoodAt(panel, MarketGoodsTop, MarketRow) is { } good)
        {
            TradeGood = good.Material;
            return $"{good.Name}: {AmountText(trade.Remaining(building, good.Material))} to send";
        }
        string name = StorehouseGoods.Goods.FirstOrDefault(g => g.Material == TradeGood)?.Name ?? TradeGood.ToString();
        if (AllButton.Contains(panel) || NoneButton.Contains(panel))
        {
            bool all = AllButton.Contains(panel);
            trade.SetRemaining(building, TradeGood, all ? Trade.Unlimited : 0);
            UiSounds.Step(all);
            return all ? $"{name}: send all there is" : $"{name}: send none";
        }
        for (int i = 0; i < _tradeArrows.Length; i++)
            if (TradeArrow(i).Contains(panel))
            {
                trade.ChangeRemaining(building, TradeGood, TradeSteps[i]);
                UiSounds.Step(TradeSteps[i] > 0);
                return $"{name}: {AmountText(trade.Remaining(building, TradeGood))} to send";
            }
        return null;
    }

    /// <summary>What the market's button or good under the point does (hover text), or null.</summary>
    public string? MarketHover(Vector2 panel, World world, Building building, int ownPlayer)
    {
        if (building.Player != ownPlayer || !OffersMarket(world, building) || _confirming == building)
            return null;
        var trade = world.Trade;
        for (int i = 0; i < Trade.Stops; i++)
            if (RouteButton(i).Contains(panel))
                return trade.StopOf(building, i) is { } stop ? $"stop {i + 1} at {stop.X},{stop.Y}" : $"stop {i + 1}: not set";
        if (RouteButton(Trade.Stops).Contains(panel))
            return trade.DestinationOf(building) is { } destination
                ? $"destination at {destination.X},{destination.Y}; {world.StatusOf(building)}"
                : $"destination: not set; {world.StatusOf(building)}";
        if (StorehouseGoods!.GoodAt(panel, MarketGoodsTop, MarketRow) is { } good)
            return $"{good.Name}: {AmountText(trade.Remaining(building, good.Material))} to send";
        if (AllButton.Contains(panel))
            return "send all there is of the chosen good";
        if (NoneButton.Contains(panel))
            return "send none of the chosen good";
        for (int i = 0; i < _tradeArrows.Length; i++)
            if (TradeArrow(i).Contains(panel))
                return $"{TradeSteps[i]:+0;-0} of the chosen good";
        return null;
    }

    /// <summary>
    /// A left click on the map while aiming (<see cref="AimsRoute"/>): sets the chosen stop or the destination there, where a
    /// donkey can get from the market. Returns a message.
    /// </summary>
    public string MarketCommand(World world, Building market, GridPoint clicked)
    {
        var trade = world.Trade;
        bool destination = RouteSlot == Trade.Stops;
        if (!(destination ? trade.SetDestination(market, clicked) : trade.SetStop(market, RouteSlot, clicked)))
            return "no way there for donkeys (water or cut off)";
        _routeAiming = null;
        UiSounds.WorkArea();
        var at = destination ? trade.DestinationOf(market)!.Value : trade.StopOf(market, RouteSlot)!.Value;
        return destination ? $"destination at {at.X},{at.Y}" : $"stop {RouteSlot + 1} at {at.X},{at.Y}";
    }

    private static string AmountText(int amount) => amount == Trade.Unlimited ? "all" : amount.ToString();
}
