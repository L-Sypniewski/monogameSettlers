using Homestead.Simulation.Buildings;
using Homestead.Simulation.Pathfinding;

namespace Homestead.Simulation.Economy;

/// <summary>
/// Land trade with donkeys. The player gives a market (<see cref="TradeRules.Market"/>) a destination – any point a
/// donkey can reach from it; on a building its door – and then, if wanted, up to <see cref="Stops"/> stops on the way
/// (e.g. around an enemy; a new destination clears them), and chooses how many units of which goods to send
/// (<see cref="Unlimited"/>: all there are). Carriers bring them to the market's piles (<see cref="PileRole.Trade"/>, see
/// <see cref="Logistics"/>); every unit there calls the nearest donkey without a job, which loads up to
/// <see cref="TradeRules.Load"/> units, walks the stops in order to the destination, puts the goods down there (at an own
/// storehouse's door into its piles) and comes back the same way.
/// </summary>
/// <remarks>
/// Donkeys come from the donkey farm (<see cref="Production"/>). Nobody commands them, they walk on any land and never
/// die; an enemy soldier striking a loaded one makes it drop its load where it stands and turn home
/// (<see cref="Struck"/>). Watchdog: a stop the donkey makes no progress to for <see cref="StallTicks"/> is skipped; a
/// destination out of reach sends it home with its load, which it puts down next to the market.
/// A harbor (<see cref="TradeRules.Harbor"/>) has the same route and goods, its points on the water (the destination near
/// a beach); trade ships carry its goods (<see cref="Shipping"/>).
/// </remarks>
public sealed class Trade
{
    /// <summary>Stops a route may have before its destination.</summary>
    public const int Stops = 3;

    /// <summary>Amount of a good meaning "everything there is" (∞).</summary>
    public const int Unlimited = int.MaxValue;

    /// <summary>Most units of a good the player can ask for with the arrows.</summary>
    public const int MaxAmount = 999;

    private const int DispatchInterval = 10;
    private const int StallTicks = 400;

    private readonly World _world;
    private readonly Dictionary<int, Market> _markets = new();
    private readonly Dictionary<int, Job> _jobs = new();

    internal Trade(World world) => _world = world;

    private TradeRules Rules => _world.Production.Config.Trade ?? TradeRules.Default;

    /// <summary>Units carried to their destinations so far.</summary>
    public int Delivered { get; private set; }

    /// <summary>Loads dropped because an enemy struck the donkey.</summary>
    public int Robbed { get; private set; }

    public bool IsMarket(Building building) => building.Definition.Name == Rules.Market;

    /// <summary>Whether the building trades over the sea (with a dock and trade ships).</summary>
    public bool IsHarbor(Building building) => building.Definition.Name == Rules.Harbor;

    /// <summary>A market or a harbor: a route, goods to send.</summary>
    public bool IsTradingPost(Building building) => IsMarket(building) || IsHarbor(building);

    /// <summary>The building that breeds donkeys.</summary>
    public bool IsDonkeyFarm(string building) => building == Rules.DonkeyFarm;

    /// <summary>Units of the good still to bring to the market (<see cref="Unlimited"/>: everything there is).</summary>
    public int Remaining(Building market, Material material) =>
        _markets.TryGetValue(market.Id, out var state) ? state.Remaining.GetValueOrDefault(material) : 0;

    /// <summary>Sets how many units of the good to send (0: none, <see cref="Unlimited"/>: all).</summary>
    public void SetRemaining(Building market, Material material, int amount)
    {
        var state = StateOf(market);
        if (amount <= 0)
            state.Remaining.Remove(material);
        else
            state.Remaining[material] = amount == Unlimited ? Unlimited : Math.Min(amount, MaxAmount);
    }

    /// <summary>More or fewer units of the good (the arrows); an unlimited amount stays so.</summary>
    public void ChangeRemaining(Building market, Material material, int delta)
    {
        int now = Remaining(market, material);
        if (now != Unlimited)
            SetRemaining(market, material, Math.Clamp(now + delta, 0, MaxAmount));
    }

    /// <summary>Stop <paramref name="index"/> (0 to <see cref="Stops"/> − 1) of the market's route, if set.</summary>
    public GridPoint? StopOf(Building market, int index) =>
        _markets.TryGetValue(market.Id, out var state) ? state.Stops[index] : null;

    /// <summary>Sets a stop (false where a donkey cannot get from the market, see <see cref="Reachable"/>) or (null) clears it.</summary>
    public bool SetStop(Building market, int index, GridPoint? point)
    {
        GridPoint? at = null;
        if (point is { } p && (at = Reachable(market, p, destination: false)) is null)
            return false;
        StateOf(market).Stops[index] = at;
        return true;
    }

    /// <summary>Where the market's goods go (null: not set yet).</summary>
    public GridPoint? DestinationOf(Building market) => _markets.TryGetValue(market.Id, out var state) ? state.Destination : null;

    /// <summary>
    /// Makes the point the market's destination and clears the stops; false where a donkey cannot get from the market
    /// (see <see cref="Reachable"/>).
    /// </summary>
    public bool SetDestination(Building market, GridPoint point)
    {
        if (Reachable(market, point, destination: true) is not { } at)
            return false;
        var state = StateOf(market);
        state.Destination = at;
        Array.Clear(state.Stops);
        return true;
    }

    /// <summary>
    /// Where a donkey of the market goes for <paramref name="point"/>: on a building its door, else the point itself if walkable
    /// and connected to the market's door (not on water, not cut off); null if it cannot go there. For a harbor: the open
    /// water nearest the point its trade ships get to, as the <paramref name="destination"/> with a beach near (see
    /// <see cref="Shipping.SeaPoint"/>).
    /// </summary>
    public GridPoint? Reachable(Building market, GridPoint point, bool destination)
    {
        if (IsHarbor(market))
            return _world.Shipping.SeaPoint(market, point, destination);
        var map = _world.Map!;
        if (!map.Contains(point))
            return null;
        if ((_world.BuildingAt(point) ?? _world.Buildings.FirstOrDefault(b => b.Exists && b.Position == point)) is { } building)
            point = building.Door;
        return map.IsWalkable(point) && _world.PathFinder!.AreConnected(market.Door, point, null) ? point : null;
    }

    /// <summary>The way of the market's donkeys: the stops set, in order, then the destination's door (empty without one).</summary>
    public IReadOnlyList<GridPoint> Route(Building market)
    {
        if (DestinationOf(market) is not { } destination)
            return [];
        var way = new List<GridPoint>();
        for (int i = 0; i < Stops; i++)
            if (StopOf(market, i) is { } stop)
                way.Add(stop);
        way.Add(destination);
        return way;
    }

    /// <summary>Donkeys working for the market right now.</summary>
    public int DonkeysOf(Building market) => _jobs.Values.Count(j => j.Market == market);

    /// <summary>The player's donkeys without a job.</summary>
    public IEnumerable<Settler> IdleDonkeys(int player) =>
        _world.Settlers.Where(s => s.Profession == Profession.Donkey && s.Player == player && !s.IsDead && !_jobs.ContainsKey(s.Id));

    /// <summary>What the donkey carries (empty if nothing).</summary>
    public IReadOnlyList<Material> CargoOf(Settler donkey) => _jobs.TryGetValue(donkey.Id, out var job) ? job.Cargo : [];

    /// <summary>What the market is doing, for the player (null for other buildings).</summary>
    public string? Status(Building market)
    {
        if (!IsTradingPost(market) || market.State != BuildingState.Finished)
            return null;
        if (DestinationOf(market) is null)
            return "trade: no destination";
        if (IsHarbor(market))
            return _world.Shipping.ShipsOf(market) is var ships and > 0 ? $"trade: {ships} ships on the way"
                : market.Piles.Any(p => p.Role == PileRole.Trade && p.Count > 0) ? "trade: waiting for a ship" : "trade: waiting for goods";
        int donkeys = DonkeysOf(market);
        if (donkeys > 0)
            return $"trade: {donkeys} donkeys on the way";
        return market.Piles.Any(p => p.Role == PileRole.Trade && p.Count > 0) ? "trade: waiting for a donkey" : "trade: waiting for goods";
    }

    /// <summary>
    /// Whether carriers may bring a unit of <paramref name="material"/> to the market pile: it has a destination and wants more.
    /// Goods may come from the destination too (as in the original: with ∞ they then go round for ever).
    /// </summary>
    internal bool Wants(Pile pile, Material material)
    {
        var market = pile.Owner!;
        if (DestinationOf(market) is null)
            return false;
        int remaining = Remaining(market, material);
        if (remaining == Unlimited)
            return true;
        int coming = market.Piles.Where(p => p.Role == PileRole.Trade && p.Material == material).Sum(p => p.Incoming);
        return remaining > coming;
    }

    /// <summary>A trade ship put units down at its harbor's destination.</summary>
    internal void ShippedBySea(int units) => Delivered += units;

    /// <summary>A carrier put a unit down at the market: one fewer to bring.</summary>
    internal void Arrived(Pile pile, Material material)
    {
        int remaining = Remaining(pile.Owner!, material);
        if (remaining != Unlimited && remaining > 0)
            SetRemaining(pile.Owner!, material, remaining - 1);
    }

    /// <summary>An enemy struck the donkey: a loaded one drops its load where it stands and turns home.</summary>
    internal void Struck(Settler donkey)
    {
        if (!_jobs.TryGetValue(donkey.Id, out var job) || job.Cargo.Count == 0)
            return;
        foreach (var material in job.Cargo)
            _world.DropGoods(donkey.Position, material);
        job.Cargo.Clear();
        donkey.Carrying = null;
        Robbed++;
        TurnBack(job);
    }

    internal void Tick()
    {
        foreach (var job in _jobs.Values.ToList())
            Advance(job);
        if (_world.Tick % DispatchInterval == 0)
            Dispatch();
    }

    /// <summary>Each unit at a market nobody comes for yet calls the nearest donkey without a job.</summary>
    private void Dispatch()
    {
        List<Settler>? idle = null;
        foreach (var market in _world.Buildings)
        {
            if (!market.Exists || market.State != BuildingState.Finished || !IsMarket(market) || DestinationOf(market) is null)
                continue;
            while (market.Piles.Any(p => p.Role == PileRole.Trade && p.Count > p.Reserved))
            {
                idle ??= _world.Settlers.Where(s => s.Profession == Profession.Donkey && !s.IsDead && !s.IsInside && !_jobs.ContainsKey(s.Id)).ToList();
                var donkey = idle.Where(d => d.Player == market.Player && _world.CanFleeTo(d, market.Door))
                    .MinBy(d => PathFinder.Distance(d.Position, market.Door));
                if (donkey is null)
                    break;
                idle.Remove(donkey);
                var job = new Job(donkey, market);
                foreach (var pile in market.Piles.Where(p => p.Role == PileRole.Trade))
                    while (pile.Count > pile.Reserved && job.Reserved.Count < Rules.Load)
                    {
                        pile.Reserved++;
                        job.Reserved.Add(pile);
                    }
                _jobs[donkey.Id] = job;
                donkey.HasTask = true;
                donkey.IsStrolling = false;
                if (!Walk(job, market.Door, Phase.ToMarket))
                {
                    End(job);
                    break;
                }
            }
        }
    }

    private void Advance(Job job)
    {
        var donkey = job.Donkey;
        switch (job.Phase)
        {
            case Phase.ToMarket:
                if (!job.Market.Exists)
                    End(job);
                else if (Reached(job, out bool stalled))
                    BeginHandling(job, Phase.Loading);
                else if (stalled)
                    End(job);
                break;

            case Phase.Loading:
                if (--job.TicksLeft > 0)
                    break;
                if (job.Market.Exists && Load(job))
                {
                    job.TicksLeft = Rules.LoadTicks;
                    break;
                }
                StopHandling(job);
                ReleaseReserved(job);
                if (job.Cargo.Count == 0 || !job.Market.Exists || DestinationOf(job.Market) is null)
                {
                    // Nothing to carry (or nowhere to go any more): put it back down, free again.
                    foreach (var material in job.Cargo)
                        _world.DropGoods(job.Market.Door, material);
                    job.Cargo.Clear();
                    donkey.Carrying = null;
                    End(job);
                    break;
                }
                job.Way.Clear();
                job.Way.AddRange(Route(job.Market));
                job.Leg = 0;
                WalkLeg(job, Phase.Travelling);
                break;

            case Phase.Travelling:
            {
                bool last = job.Leg == job.Way.Count - 1;
                if (Reached(job, out bool stalled) || stalled && !last)
                {
                    if (last)
                    {
                        BeginHandling(job, Phase.Unloading);
                        break;
                    }
                    job.Leg++;
                    WalkLeg(job, Phase.Travelling);
                }
                else if (stalled)
                    TurnBack(job);
                break;
            }

            case Phase.Unloading:
                if (--job.TicksLeft > 0)
                    break;
                if (job.Cargo.Count > 0)
                {
                    Unload(job.Way[^1], donkey.Player, job.Cargo[^1]);
                    job.Cargo.RemoveAt(job.Cargo.Count - 1);
                    Delivered++;
                    donkey.Carrying = job.Cargo.Count > 0 ? job.Cargo[0] : null;
                    job.TicksLeft = Rules.LoadTicks;
                    break;
                }
                StopHandling(job);
                TurnBack(job);
                break;

            case Phase.Returning:
                if (Reached(job, out bool stalledHome) || stalledHome)
                {
                    if (job.Leg < job.Way.Count - 1)
                    {
                        job.Leg++;
                        WalkLeg(job, Phase.Returning);
                        break;
                    }
                    // Home with goods it could not deliver: they go down next to the market.
                    foreach (var material in job.Cargo)
                        _world.DropGoods(donkey.Position, material);
                    job.Cargo.Clear();
                    donkey.Carrying = null;
                    End(job);
                }
                break;
        }
    }

    /// <summary>Loads one unit: one promised to the donkey first, else any left there. False when full or nothing is left.</summary>
    private bool Load(Job job)
    {
        if (job.Cargo.Count >= Rules.Load)
            return false;
        Pile? pile = null;
        while (job.Reserved.Count > 0 && pile is null)
        {
            var promised = job.Reserved[^1];
            job.Reserved.RemoveAt(job.Reserved.Count - 1);
            promised.Reserved = Math.Max(0, promised.Reserved - 1);
            if (_world.HasPile(promised) && promised.Count > promised.Reserved)
                pile = promised;
        }
        pile ??= job.Market.Piles.FirstOrDefault(p => p.Role == PileRole.Trade && p.Count > p.Reserved && _world.HasPile(p));
        if (pile?.Material is not { } material)
            return false;
        pile.Count--;
        pile.ResetMaterialIfEmpty();
        job.Cargo.Add(material);
        job.Donkey.Carrying = job.Cargo[0];
        return true;
    }

    /// <summary>A unit at the destination: at the door of an own storehouse into a pile with room for it, else down on the ground there.</summary>
    private void Unload(GridPoint destination, int player, Material material)
    {
        // Put down on another player's land: handed over to it (the chat's computer players may answer).
        if (_world.Map!.OwnerAt(destination) is { } owner && owner != player)
            _world.HandOver(player, owner, material);
        var storehouse = _world.Buildings.FirstOrDefault(b => b.Exists && b.Door == destination && b.Player == player && b.State == BuildingState.Finished);
        var pile = storehouse?.Piles.Where(p => p.Role == PileRole.Storage && p.Accepts(material))
            .OrderByDescending(p => p.Material == material).FirstOrDefault();
        if (pile is not null)
        {
            pile.Count++;
            pile.Material = material;
        }
        else
            _world.DropGoods(destination, material);
    }

    /// <summary>Home the way it came: the stops passed in reverse, then the market's door.</summary>
    private void TurnBack(Job job)
    {
        StopHandling(job);
        var back = new List<GridPoint>();
        if (job.Phase is Phase.Travelling or Phase.Unloading)
            for (int i = Math.Min(job.Leg, job.Way.Count - 1) - 1; i >= 0; i--)
                back.Add(job.Way[i]);
        back.Add(job.Market.Door);
        job.Way.Clear();
        job.Way.AddRange(back);
        job.Leg = 0;
        WalkLeg(job, Phase.Returning);
    }

    /// <summary>On to the current point of the way, or the reachable point nearest to it.</summary>
    private void WalkLeg(Job job, Phase phase)
    {
        var point = job.Way[job.Leg];
        var to = _world.CommandTarget(job.Donkey, point) ?? point;
        Walk(job, to, phase);
    }

    private bool Walk(Job job, GridPoint destination, Phase phase)
    {
        job.Phase = phase;
        job.WalkingTo = destination;
        job.BestRemaining = int.MaxValue;
        job.LastProgress = _world.Tick;
        var donkey = job.Donkey;
        if (donkey.Position == destination && donkey.State != SettlerState.Stepping)
            return true;
        return _world.WalkTo(donkey, destination);
    }

    /// <summary>True once the donkey stands where it walks to; <paramref name="stalled"/> reports the watchdog.</summary>
    private bool Reached(Job job, out bool stalled)
    {
        var donkey = job.Donkey;
        stalled = false;
        if (donkey.State != SettlerState.Stepping && donkey.Destination is null)
        {
            if (donkey.Position == job.WalkingTo)
                return true;
            if (!_world.WalkTo(donkey, job.WalkingTo))
            {
                stalled = true;
                return false;
            }
        }
        int remaining = donkey.Destination == job.WalkingTo ? donkey.RemainingPath.Count() : int.MaxValue;
        if (remaining < job.BestRemaining)
        {
            job.BestRemaining = remaining;
            job.LastProgress = _world.Tick;
        }
        stalled = _world.Tick - job.LastProgress > StallTicks;
        return false;
    }

    private void BeginHandling(Job job, Phase phase)
    {
        job.Phase = phase;
        job.TicksLeft = Rules.LoadTicks;
        job.Donkey.IsWorking = true;
    }

    private static void StopHandling(Job job) => job.Donkey.IsWorking = false;

    private static void ReleaseReserved(Job job)
    {
        foreach (var pile in job.Reserved)
        {
            pile.Reserved = Math.Max(0, pile.Reserved - 1);
            pile.ResetMaterialIfEmpty();
        }
        job.Reserved.Clear();
    }

    private void End(Job job)
    {
        ReleaseReserved(job);
        StopHandling(job);
        _jobs.Remove(job.Donkey.Id);
        job.Donkey.HasTask = false;
        if (job.Donkey.Destination is not null)
            _world.WalkTo(job.Donkey, job.Donkey.StepTarget);
    }

    private Market StateOf(Building market)
    {
        if (!_markets.TryGetValue(market.Id, out var state))
            _markets[market.Id] = state = new Market();
        return state;
    }

    private enum Phase
    {
        ToMarket,
        Loading,
        Travelling,
        Unloading,
        Returning,
    }

    /// <summary>A market's route and the goods it sends.</summary>
    private sealed class Market
    {
        public GridPoint?[] Stops { get; } = new GridPoint?[Trade.Stops];
        public GridPoint? Destination { get; set; }
        public Dictionary<Material, int> Remaining { get; } = new();
    }

    private sealed class Job(Settler donkey, Building market)
    {
        public Settler Donkey { get; } = donkey;
        public Building Market { get; } = market;

        public Phase Phase { get; set; }

        /// <summary>The points it walks to in turn (out: stops and the destination's door; home: back and the market's door).</summary>
        public List<GridPoint> Way { get; } = new();
        public int Leg { get; set; }
        public List<Material> Cargo { get; } = new();

        /// <summary>Market piles holding a unit promised to this donkey (one entry per unit).</summary>
        public List<Pile> Reserved { get; } = new();
        public int TicksLeft { get; set; }
        public GridPoint WalkingTo { get; set; }
        public int BestRemaining { get; set; }
        public long LastProgress { get; set; }
    }
}
