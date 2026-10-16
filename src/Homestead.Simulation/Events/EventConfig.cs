using System.Text.Json;
using Homestead.Simulation.Buildings;

namespace Homestead.Simulation.Events;

/// <summary>The kinds of event, one button each at the bottom of the side panel.</summary>
public enum EventCategory
{
    Combat,
    Resources,
    Staff,
    Trade,
}

/// <summary>One event as configured: its button, how often it may come per player, and its text.</summary>
public sealed record EventDefinition(string Id, EventCategory Category, int CooldownTicks, string Text, bool Enabled = true);

/// <summary>The events, loaded from <c>data/events.json</c> (<c>events</c>; the buttons there are the client's).</summary>
public sealed record EventConfig(IReadOnlyDictionary<string, EventDefinition> Events)
{
    public static EventConfig None { get; } = new(new Dictionary<string, EventDefinition>());

    /// <summary>Reads the events; without the file there are none.</summary>
    public static EventConfig Load(string? dataDirectory = null)
    {
        string path = Path.Combine(dataDirectory ?? BuildingCatalog.DefaultDataDirectory, "events.json");
        if (!File.Exists(path))
            return None;
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var events = new Dictionary<string, EventDefinition>();
        foreach (var e in document.RootElement.GetProperty("events").EnumerateObject())
        {
            var value = e.Value;
            events[e.Name] = new EventDefinition(e.Name,
                Enum.Parse<EventCategory>(value.GetProperty("category").GetString()!, ignoreCase: true),
                (int)Math.Round(value.GetProperty("cooldownSeconds").GetDouble() * World.TicksPerSecond),
                value.TryGetProperty("text", out var text) ? text.GetString()! : e.Name,
                !value.TryGetProperty("enabled", out var enabled) || enabled.GetBoolean());
        }
        return new EventConfig(events);
    }
}
