using Homestead.Simulation.Buildings;

namespace Homestead.Simulation.Events;

/// <summary>
/// Something a player should notice, at a place: the settler or building it concerns (followed while it
/// is there), otherwise where it happened.
/// </summary>
public sealed record GameEvent(EventDefinition Definition, int Player, GridPoint Position, long Tick, Settler? Settler = null, Building? Building = null)
{
    public EventCategory Category => Definition.Category;

    /// <summary>Where it is now: the building, else the settler (while alive), else where it happened.</summary>
    public GridPoint Where => Building is { Exists: true } building ? building.Position
        : Settler is { IsDead: false } settler ? settler.Position
        : Position;
}

/// <summary>
/// Events per player (<see cref="EventConfig"/>): raised by the simulation where something happens, each
/// kind at most once per its cooldown and player. The client shows them (<see cref="Since"/>).
/// </summary>
public sealed class GameEvents(World world, EventConfig config)
{
    /// <summary>The latest events kept; older ones are dropped.</summary>
    public const int Kept = 100;

    private readonly List<GameEvent> _events = new();
    private readonly Dictionary<(int Player, string Id), long> _last = new();

    /// <summary>How many events were raised in the whole game (also the dropped ones): a reader's bookmark for <see cref="Since"/>.</summary>
    public int Count { get; private set; }

    public EventConfig Config => config;

    /// <summary>
    /// Raises the event <paramref name="id"/> for the player, unless it is unknown, disabled or came within its
    /// cooldown. Returns the event, or null.
    /// </summary>
    public GameEvent? Raise(string id, int player, GridPoint position, Settler? settler = null, Building? building = null)
    {
        if (!config.Events.TryGetValue(id, out var definition) || !definition.Enabled)
            return null;
        if (_last.TryGetValue((player, id), out long last) && world.Tick - last < definition.CooldownTicks)
            return null;
        _last[(player, id)] = world.Tick;
        var raised = new GameEvent(definition, player, position, world.Tick, settler, building);
        _events.Add(raised);
        if (_events.Count > Kept)
            _events.RemoveAt(0);
        Count++;
        return raised;
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
}
