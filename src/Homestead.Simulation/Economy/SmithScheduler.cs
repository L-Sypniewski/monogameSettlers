namespace Homestead.Simulation.Economy;

/// <summary>
/// What a smith makes next (see <see cref="ChooseProduct"/>): the player's orders first, then the tool
/// most short for waiting workplaces and construction, otherwise the player's shares as a smooth
/// weighted round robin among the products below the stock target. Also keeps the player's orders,
/// share bars and round robin credits.
/// </summary>
public sealed partial class Production
{
    /// <summary>Workplaces still waiting for a worker whose trade needs this tool.</summary>
    internal int WaitingFor(Material tool, int player) => _workplaces.Values.Count(w =>
        w.Building.Player == player && w.Phase is Phase.NeedsWorker && _world.Recruitment.ToolFor(w.Building.Definition.Worker) == tool);

    private readonly Dictionary<(int Player, Material Product), int> _orders = new();
    private readonly Dictionary<(int Player, Material Product), double> _shares = new();
    private readonly Dictionary<(int Player, Material Product), double> _credits = new();

    /// <summary>Units of a smith's product the player ordered and no smith has started yet (Waren › Herstellung).</summary>
    public int Ordered(int player, Material product) => _orders.GetValueOrDefault((player, product));

    /// <summary>Orders more (or fewer, never below 0) units of a smith's product.</summary>
    public void ChangeOrdered(int player, Material product, int delta) =>
        _orders[(player, product)] = Math.Max(0, Ordered(player, product) + delta);

    /// <summary>
    /// The player's share for a smith's product (0–1, the bar in Waren › Herstellung): without orders or
    /// need, the smith makes its products in proportion to these; 0 = only on order or need. Defaults
    /// from <see cref="SmithRecipe.Shares"/>. Without a player override, the default shown here is the
    /// first smith's in data order; the scheduler itself resolves each smith's own default
    /// (<see cref="ChooseProduct"/> through the smith-aware overload below).
    /// </summary>
    public double ShareOf(int player, Material product) =>
        _shares.TryGetValue((player, product), out double share) ? share
        : _config.Smiths.Values.FirstOrDefault(s => s.Products.Contains(product))?.ShareOf(product) ?? 0;

    /// <summary>The share that applies for this smith: the player's override, otherwise the smith's own default
    /// (<see cref="SmithRecipe.Shares"/>), not that of whichever smith the configuration lists first.</summary>
    private double ShareOf(SmithRecipe smith, int player, Material product) =>
        _shares.TryGetValue((player, product), out double share) ? share : smith.ShareOf(product);

    /// <summary>Sets a product's share (clamped to 0–1).</summary>
    public void SetShare(int player, Material product, double share) => _shares[(player, product)] = Math.Clamp(share, 0, 1);

    /// <summary>
    /// What a smith makes next: what the player ordered (the first product of the smith with an order),
    /// then the tool most short for waiting workplaces and construction, otherwise by the player's shares
    /// (smooth weighted round robin) among the products below the stock target; null to rest.
    /// </summary>
    private Material? ChooseProduct(SmithRecipe smith, int player)
    {
        foreach (var product in smith.Products)
            if (Ordered(player, product) > 0)
            {
                ChangeOrdered(player, product, -1);
                return product;
            }

        // Only the smith's own player counts: another player's tools are no use here.
        var piles = _world.Piles.Where(p => _world.BelongsTo(p, player)).ToList();
        Material? best = null;
        int bestShortage = 0;
        foreach (var product in smith.Products)
        {
            int free = piles.Where(p => p.Material == product).Sum(p => p.Available);
            int shortage = WaitingFor(product, player) + _world.Construction.WaitingFor(product, player) - free;
            if (shortage > bestShortage)
            {
                best = product;
                bestShortage = shortage;
            }
        }
        if (best is not null)
            return best;

        int Around(Material m) => piles.Where(p => p.Material == m).Sum(p => p.Count) + _world.Settlers.Count(s => s.Player == player && s.Carrying == m);
        var candidates = smith.Products
            .Where(m => ShareOf(smith, player, m) > 0 && (smith.KeepInStock <= 0 || Around(m) < smith.KeepInStock))
            .ToList();
        if (candidates.Count == 0)
            return null;
        // Each candidate earns its share; the one with the most credit is made and pays the sum back.
        foreach (var m in candidates)
            _credits[(player, m)] = _credits.GetValueOrDefault((player, m)) + ShareOf(smith, player, m);
        var chosen = candidates.MaxBy(m => _credits[(player, m)]);
        _credits[(player, chosen)] -= candidates.Sum(m => ShareOf(smith, player, m));
        return chosen;
    }
}
