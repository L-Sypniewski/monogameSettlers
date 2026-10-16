using System.Text.Json;
using Homestead.Assets.Gfx;
using Homestead.Simulation;
using Homestead.Simulation.Buildings;
using Homestead.Simulation.Events;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Homestead.Client;

/// <summary>
/// The event buttons at the bottom of the side panel, right of the computer button (combat, resources,
/// staff, trade – <c>data/events.json</c>): a new event of the own player makes its button blink, then it
/// stays lit until looked at. A click on a button, or space for the latest event of all, jumps there
/// (<see cref="Look"/>); the game then points arrows at the place. Panel space.
/// </summary>
public sealed class EventButtons : IDisposable
{
    /// <summary>GUI images of file 03.</summary>
    private const int File = 3;

    /// <summary>Blinks per second.</summary>
    private const float BlinkRate = 1;

    private readonly Dictionary<EventCategory, Button> _buttons = new();
    private readonly float _blinkSeconds;

    /// <summary>The world read last and how many of its events were seen there.</summary>
    private World? _world;
    private int _read;

    public EventButtons(GraphicsDevice device, GfxDirectory gfx)
    {
        var file = gfx.Open(File);
        Texture2D Image(int index)
        {
            var bitmap = file.ReadGui(index);
            var texture = new Texture2D(device, bitmap.Width, bitmap.Height);
            texture.SetData(bitmap.Pixels);
            return texture;
        }

        using var document = JsonDocument.Parse(System.IO.File.ReadAllText(Path.Combine(BuildingCatalog.DefaultDataDirectory, "events.json")));
        var root = document.RootElement;
        _blinkSeconds = root.GetProperty("blinkSeconds").GetSingle();
        ArrowSeconds = root.GetProperty("arrowSeconds").GetSingle();
        foreach (var category in root.GetProperty("categories").EnumerateObject())
        {
            var value = category.Value;
            var slot = value.GetProperty("slot");
            var icons = value.GetProperty("icons");
            var lit = Image(icons[1].GetInt32());
            _buttons[Enum.Parse<EventCategory>(category.Name, ignoreCase: true)] = new Button(value.GetProperty("name").GetString()!,
                new Rectangle(slot[0].GetInt32(), slot[1].GetInt32(), lit.Width, lit.Height), lit);
        }
    }

    /// <summary>How long the arrows point at the place after a jump.</summary>
    public float ArrowSeconds { get; }

    /// <summary>
    /// Takes in the player's new events (at <paramref name="now"/>, seconds of real time); returns the newest
    /// one, if any (its text becomes the message). A new world starts over.
    /// </summary>
    public GameEvent? Update(World world, int player, double now)
    {
        if (world != _world)
        {
            _world = world;
            _read = world.Events.Count;
            foreach (var button in _buttons.Values)
                button.Unseen = null;
        }
        GameEvent? newest = null;
        foreach (var e in world.Events.Since(_read, player))
        {
            if (_buttons.TryGetValue(e.Category, out var button))
            {
                button.Unseen = e;
                button.Since = now;
            }
            newest = e;
        }
        _read = world.Events.Count;
        return newest;
    }

    /// <summary>The category whose button is under the point.</summary>
    public EventCategory? At(Vector2 panel)
    {
        foreach (var (category, button) in _buttons)
            if (button.Rect.Contains(panel))
                return category;
        return null;
    }

    public string? NameAt(Vector2 panel) => At(panel) is { } category ? _buttons[category].Name : null;

    /// <summary>
    /// The event to look at: of the category (its button was clicked) or, without one (space), the newest
    /// not yet looked at, else the newest of all. Its button goes dark.
    /// </summary>
    public GameEvent? Look(World world, int player, EventCategory? category)
    {
        var chosen = category is { } c
            ? (_buttons.TryGetValue(c, out var b) ? b.Unseen : null) ?? world.Events.Latest(player, c)
            : _buttons.Values.Where(b => b.Unseen is not null).MaxBy(b => b.Unseen!.Tick)?.Unseen ?? world.Events.Latest(player);
        if (chosen is not null && _buttons.TryGetValue(chosen.Category, out var button))
            button.Unseen = null;
        return chosen;
    }

    /// <summary>The lit buttons over the panel's dark ones: blinking for a while after the event, then steady until looked at.</summary>
    public void Draw(SpriteBatch batch, double now)
    {
        foreach (var button in _buttons.Values)
        {
            if (button.Unseen is null)
                continue;
            double age = now - button.Since;
            if (age < _blinkSeconds && (int)(age * BlinkRate * 2) % 2 == 1)
                continue;
            batch.Draw(button.Lit, button.Rect, Color.White);
        }
    }

    public void Dispose()
    {
        foreach (var button in _buttons.Values)
            button.Lit.Dispose();
    }

    private sealed class Button(string name, Rectangle rect, Texture2D lit)
    {
        public string Name { get; } = name;
        public Rectangle Rect { get; } = rect;
        public Texture2D Lit { get; } = lit;

        /// <summary>The newest event of this kind not yet looked at.</summary>
        public GameEvent? Unseen { get; set; }

        /// <summary>When it came (real time, seconds).</summary>
        public double Since { get; set; }
    }
}
