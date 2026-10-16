using Homestead.Simulation.Buildings;
using Homestead.Simulation.Economy;

namespace Homestead.Simulation.Events;

/// <summary>
/// Something a player should notice, at a place: the settler or building it concerns (followed while it
/// is there), otherwise where it happened; some (greetings) have no place.
/// </summary>
public sealed record GameEvent(EventDefinition Definition, int Player, GridPoint? Position, long Tick, Settler? Settler = null, Building? Building = null)
{
    public EventCategory Category => Definition.Category;

    /// <summary>Where it is now: the building, else the settler (while alive), else where it happened.</summary>
    public GridPoint? Where => Building is { Exists: true } building ? building.Position
        : Settler is { IsDead: false } settler ? settler.Position
        : Position;
}

/// <summary>
/// Events per player (<see cref="EventConfig"/>): raised by the simulation where something happens, each
/// kind at most once per its cooldown and player. States of buildings (no staff, no tool, nothing to gather,
/// mine exhausted) are checked once a second and reported once they have lasted the event's
/// <see cref="EventDefinition.AfterTicks"/> – once per building until the state ends (a report held back by
/// the cooldown comes later). The client shows them (<see cref="Since"/>).
/// </summary>
public sealed class GameEvents(World world, EventConfig config)
{
    /// <summary>The latest events kept; older ones are dropped.</summary>
    public const int Kept = 100;

    private readonly List<GameEvent> _events = new();
    private readonly Dictionary<(int Player, string Id), long> _last = new();

    /// <summary>Building states (building id, event id) since when, and those already reported.</summary>
    private readonly Dictionary<(int Building, string Id), long> _states = new();
    private readonly HashSet<(int Building, string Id)> _reported = new();

    /// <summary>How many events were raised in the whole game (also the dropped ones): a reader's bookmark for <see cref="Since"/>.</summary>
    public int Count { get; private set; }

    public EventConfig Config => config;

    /// <summary>
    /// Raises the event <paramref name="id"/> for the player, unless it is unknown, disabled or came within its
    /// cooldown. Returns the event, or null.
    /// </summary>
    public GameEvent? Raise(string id, int player, GridPoint? position, Settler? settler = null, Building? building = null)
    {
        if (!config.Events.TryGetValue(id, out var definition) || !definition.Enabled)
            return null;
        if (_last.TryGetValue((player, id), out long last) && world.Tick - last < definition.CooldownTicks)
            return null;
        _last[(player, id)] = world.Tick;
        return Add(new GameEvent(definition, player, position, world.Tick, settler, building));
    }

    /// <summary>A message without a place and outside the configured events (the client's greetings on certain days).</summary>
    public GameEvent Announce(string id, EventCategory category, string text, int player) =>
        Add(new GameEvent(new EventDefinition(id, category, 0, text), player, null, world.Tick));

    private GameEvent Add(GameEvent e)
    {
        _events.Add(e);
        if (_events.Count > Kept)
            _events.RemoveAt(0);
        Count++;
        return e;
    }

    /// <summary>The player's events raised after the first <paramref name="count"/> of all (<see cref="Count"/>), oldest first.</summary>
    public IEnumerable<GameEvent> Since(int count, int player)
    {
        int skip = Math.Max(0, _events.Count - (Count - count));
        return _events.Skip(skip).Where(e => e.Player == player);
    }

    /// <summary>The player's latest event, of the category if given.</summary>
    public GameEvent? Latest(int player, EventCategory? category = null) =>
        _events.LastOrDefault(e => e.Player == player && (category is null || e.Category == category));

    internal void Tick()
    {
        if (world.Tick % World.TicksPerSecond != 0)
            return;
        var now = new HashSet<(int, string)>();
        foreach (var building in world.Buildings)
        {
            if (!building.Exists || building.State != BuildingState.Finished)
                continue;
            if (StateOf(building) is not { } id)
                continue;
            var key = (building.Id, id);
            now.Add(key);
            if (!_states.TryGetValue(key, out long since))
                _states[key] = since = world.Tick;
            if (!_reported.Contains(key) && config.Events.TryGetValue(id, out var definition)
                && world.Tick - since >= definition.AfterTicks && Raise(id, building.Player, building.Position, building: building) is not null)
                _reported.Add(key);
        }
        foreach (var key in _states.Keys.Where(k => !now.Contains(k)).ToList())
        {
            _states.Remove(key);
            _reported.Remove(key);
        }
    }

    /// <summary>The building's state worth an event, if any (one at a time, the most pressing first).</summary>
    private string? StateOf(Building building)
    {
        var production = world.Production;
        if (production.LacksTool(building))
            return "noTool";
        if (production.NeedsWorker(building) || world.Military.AwaitsSoldiers(building))
            return "noStaff";
        if (production.FindsNothing(building))
            return "noResources";
        if (production.MineRuns(building) >= Production.HitRateRuns && config.Events.TryGetValue("mineExhausted", out var mine)
            && production.MineHitRate(building) <= mine.MaxHitRate)
            return "mineExhausted";
        return null;
    }
}
