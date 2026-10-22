using Homestead.Simulation.Buildings;
using Homestead.Simulation.Economy;

namespace Homestead.Simulation.Ai;

/// <summary>
/// Growth beyond the build order (see <c>growth</c> in ai.json): every think the AI notes which goods its
/// finished workshops wait for. After each window a good whose consumers waited for it most of the time,
/// with none of it in stock, gets one more producer. The chain grows by itself: a weaponsmith waiting for
/// iron brings an iron melt, that one waiting for ore an iron mine, the mine waiting for food the food chain
/// (<see cref="Wishes"/>); the temple waiting for mead the mead brewery, that one honey the beekeeping. A further workshop or mine only while the ones there are busy (see <c>busy</c>):
/// otherwise the shortage lies further up the chain. At the root, weaponsmiths busy nearly all the time get another one, and barracks
/// busy all the time while weapons pile up another barrack, small temples busy all the time another one (mana).
/// These wishes come after the build order.
/// </summary>
public sealed partial class AiPlayer
{
    private const string Root = "weaponsmith";
    private const string Barrack = "barrack";

    /// <summary>
    /// Per building name and good: thinks its finished, staffed buildings waited for the good, in the current window.
    /// Per kind, not pooled: a weaponsmith waiting for iron says nothing about the coal the iron melts lack.
    /// </summary>
    private readonly Dictionary<(string Building, Material Good), int> _starved = new();

    /// <summary>Per building name: thinks its finished, staffed buildings were looked at, and of those busy, in the current window.</summary>
    private readonly Dictionary<string, (int Samples, int Busy)> _usage = new();

    private long _growthWindowEnd;

    /// <summary>The buildings wished after the last window: name and the count to reach.</summary>
    private List<(string Name, int Count)> _growth = new();

    /// <summary>Per building name: how many were finished at the start of the current window.</summary>
    private readonly Dictionary<string, int> _finishedAtStart = new();

    private void SampleGrowth(List<Building> mine)
    {
        var rules = _config.Growth ?? AiConfig.GrowthRules.Default;
        if (rules.WindowTicks <= 0)
            return;
        var mines = _world.Production.Config.Mines;
        foreach (var building in mine)
        {
            if (building.State != BuildingState.Finished)
                continue;
            string name = building.Definition.Name, status = _world.StatusOf(building);
            // Only staffed and working: a worker still coming tells nothing about the goods.
            if (status.StartsWith("no worker", StringComparison.Ordinal) || status.StartsWith("worker is", StringComparison.Ordinal)
                || status is "paused" or "pausing after this work" or "on strike")
                continue;
            var (samples, busy) = _usage.GetValueOrDefault(name);
            _usage[name] = (samples + 1, busy + (IsIdle(status) ? 0 : 1));
            // Mines wait for food, not for one good: the hungry mines in Wishes see to that.
            if (mines.ContainsKey(name))
                continue;
            // "waiting for Mead (12 mana)" at a temple.
            if (status.StartsWith("waiting for ", StringComparison.Ordinal)
                && Enum.TryParse<Material>(status["waiting for ".Length..].Split(' ')[0], out var lacking) && Inputs(building.Definition).Contains(lacking))
                _starved[(name, lacking)] = _starved.GetValueOrDefault((name, lacking)) + 1;
        }
        if (_world.Tick < _growthWindowEnd)
            return;
        if (_growthWindowEnd > 0)
            _growth = GrowthWishes(mine, rules);
        _growthWindowEnd = _world.Tick + rules.WindowTicks;
        _starved.Clear();
        _usage.Clear();
        _finishedAtStart.Clear();
        foreach (var group in mine.Where(b => b.State == BuildingState.Finished).GroupBy(b => b.Definition.Name))
            _finishedAtStart[group.Key] = group.Count();
    }

    /// <summary>Whether a building with this status stands still for want of something (input, food, room, work).</summary>
    private static bool IsIdle(string status) =>
        status.StartsWith("waiting for", StringComparison.Ordinal) || status.StartsWith("everything in stock", StringComparison.Ordinal)
        || status.StartsWith("barracks: waiting", StringComparison.Ordinal) || status.StartsWith("no ", StringComparison.Ordinal)
        || status is "output pile is full" or "mine is exhausted";

    private List<(string Name, int Count)> GrowthWishes(List<Building> mine, AiConfig.GrowthRules rules)
    {
        var wishes = new List<(string Name, int Count)>();
        // Per good the kind of consumer that waited for it most.
        var shortages = _starved.GroupBy(e => e.Key.Good)
            .Select(g => (Good: g.Key, Share: g.Max(e => (double)e.Value / _usage[e.Key.Building].Samples)))
            .OrderByDescending(g => g.Share);
        foreach (var (good, share) in shortages)
        {
            if (share < rules.Starved || _world.InStock(Player, good) >= rules.InStock || ProducerOf(good, mine) is not { } producer)
                continue;
            // More of a kind only helps while the ones there are work: an iron melt waiting for ore wants a mine, not a melt.
            if (UsageOf(producer) is { } usage && usage < rules.Busy && Inputs(_catalog[producer]).Count + (IsMine(producer) ? 1 : 0) > 0)
                continue;
            Wish(producer, $"{good} short ({share:P0} of the time waited for)");
        }
        if (UsageOf(Root) >= rules.Busy)
            Wish(Root, $"weaponsmiths busy {UsageOf(Root):P0} of the time");
        // Mana: spells and upgrades.
        string temple = _world.Magic.Config.Temple;
        if (UsageOf(temple) >= rules.Busy)
            Wish(temple, $"temples busy {UsageOf(temple):P0} of the time");
        int weapons = _world.Production.Config.Smiths.TryGetValue(Root, out var smith) ? smith.Products.Sum(w => _world.InStock(Player, w)) : 0;
        if (UsageOf(Barrack) >= rules.Busy && weapons >= rules.WeaponsInStock)
            Wish(Barrack, $"barracks busy {UsageOf(Barrack):P0} of the time, {weapons} weapons waiting");
        return wishes;

        void Wish(string name, string why)
        {
            int count = mine.Count(b => b.Definition.Name == name) + 1;
            int finished = mine.Count(b => b.Definition.Name == name && b.State == BuildingState.Finished);
            // One at a time: not while one of the kind is still a site, nor when one was finished (or lost)
            // during the window – it was measured without that one; nor what the build order brings anyway.
            if (wishes.Any(w => w.Name == name) || finished != _finishedAtStart.GetValueOrDefault(name)
                || mine.Any(b => b.Definition.Name == name && b.State != BuildingState.Finished)
                || _config.BuildOrder.Any(e => e.Building == name && e.Count >= count))
                return;
            wishes.Add((name, count));
            if (!_growth.Contains((name, count)))
                Note($"grows: wants {name} {count}: {why}");
        }
    }

    /// <summary>Share of the window the finished, staffed buildings of the kind were busy; null without any.</summary>
    private double? UsageOf(string name) =>
        _usage.TryGetValue(name, out var usage) && usage.Samples > 0 ? (double)usage.Busy / usage.Samples : null;

    private bool IsMine(string name) => _world.Production.Config.Mines.ContainsKey(name);

    /// <summary>
    /// The building to make more of a good: of those that make it, the one the player has most of, else
    /// the first in its build order (a charcoal burner only where the people's build order has one). Null if none.
    /// </summary>
    private string? ProducerOf(Material good, List<Building> mine)
    {
        var makers = _catalog.All.Where(d => d.Occupiers.Count == 0 && Outputs(d).Contains(good)).Select(d => d.Name).ToList();
        return makers.Where(n => mine.Any(b => b.Definition.Name == n)).MaxBy(n => mine.Count(b => b.Definition.Name == n))
               ?? _config.BuildOrder.Select(e => e.Building).FirstOrDefault(makers.Contains);
    }
}
