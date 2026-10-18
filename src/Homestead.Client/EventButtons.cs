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
/// trade – <c>data/events.json</c>): a new event of the own player makes its button blink, then it stays
/// lit while events of its kind are not looked at yet. A click on a button, or space for the newest event
/// not looked at, jumps there (<see cref="Look"/>); the game then points arrows at the place. Once all are
/// looked at, space does nothing. Panel space.
/// <para>Also the greetings on certain days of the real calendar (<c>dates</c>): announced while a game
/// runs, each at most once a year (remembered in <c>events-shown.json</c> next to the settings).</para>
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

    private readonly List<DateMessage> _dates = new();

    /// <summary>Year a date message was shown last, by its date (MM-dd); null: never remembered (capture runs).</summary>
    private readonly Dictionary<string, int>? _shown;
    private readonly string _shownPath = Path.Combine(OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "homestead")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "homestead"), "events-shown.json");

    /// <summary>Today's date as MM-dd (HOMESTEAD_EVENT_DATE replaces it, for trying the greetings out).</summary>
    private static string Today => Environment.GetEnvironmentVariable("HOMESTEAD_EVENT_DATE") ?? DateTime.Now.ToString("MM-dd");

    /// <param name="remember">False: date messages come in every game and nothing is written (capture runs).</param>
    public EventButtons(GraphicsDevice device, GfxDirectory gfx, bool remember = true)
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
        if (root.TryGetProperty("dates", out var dates))
            foreach (var date in dates.EnumerateArray())
                _dates.Add(new DateMessage(date.GetProperty("date").GetString()!,
                    Enum.Parse<EventCategory>(date.GetProperty("category").GetString()!, ignoreCase: true),
                    Lang.T("events.dates." + date.GetProperty("date").GetString())));
        if (remember)
            _shown = ReadShown();
        foreach (var category in root.GetProperty("categories").EnumerateObject())
        {
            var value = category.Value;
            var slot = value.GetProperty("slot");
            var icons = value.GetProperty("icons");
            var lit = Image(icons[1].GetInt32());
            _buttons[Enum.Parse<EventCategory>(category.Name, ignoreCase: true)] = new Button(Lang.T("events.categories." + category.Name),
                new Rectangle(slot[0].GetInt32(), slot[1].GetInt32(), lit.Width, lit.Height), lit);
        }
    }

    /// <summary>How long the arrows point at the place after a jump.</summary>
    public float ArrowSeconds { get; }

    private Dictionary<string, int> ReadShown()
    {
        try
        {
            return System.IO.File.Exists(_shownPath)
                ? JsonSerializer.Deserialize<Dictionary<string, int>>(System.IO.File.ReadAllText(_shownPath)) ?? new()
                : new();
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            return new();
        }
    }

    /// <summary>Announces today's date messages not shown this year yet (once per world in capture runs).</summary>
    private void AnnounceDates(World world, int player)
    {
        string today = Today;
        foreach (var date in _dates.Where(d => d.Date == today))
        {
            if (_shown is not null)
            {
                if (_shown.GetValueOrDefault(date.Date) == DateTime.Now.Year)
                    continue;
                _shown[date.Date] = DateTime.Now.Year;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_shownPath)!);
                    System.IO.File.WriteAllText(_shownPath, JsonSerializer.Serialize(_shown));
                }
                catch (IOException)
                {
                    // Not remembered: it may come once more.
                }
            }
            world.Events.Announce("date:" + date.Date, date.Category, date.Text, player);
        }
    }

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
                button.Unseen.Clear();
            _day = null;
        }
        // Checked when a game starts and when the day changes during one.
        if (_day != Today)
        {
            _day = Today;
            AnnounceDates(world, player);
        }
        GameEvent? newest = null;
        foreach (var e in world.Events.Since(_read, player))
        {
            if (_buttons.TryGetValue(e.Category, out var button))
            {
                button.Unseen.Add(e);
                button.Since = now;
            }
            newest = e;
        }
        _read = world.Events.Count;
        return newest;
    }

    /// <summary>The day the date messages were checked last (MM-dd).</summary>
    private string? _day;

    /// <summary>The category whose button is under the point.</summary>
    public EventCategory? At(Vector2 panel)
    {
        foreach (var (category, button) in _buttons)
            if (button.Rect.Contains(panel))
                return category;
        return null;
    }

    public string? NameAt(Vector2 panel) => At(panel) is { } category ? _buttons[category].Name : null;

    /// <summary>The name of the category's button (the heading of the event's page).</summary>
    public string NameOf(EventCategory category) => _buttons.TryGetValue(category, out var button) ? button.Name : category.ToString();

    /// <summary>
    /// The event to look at, counted as looked at: the newest not looked at yet, of the category (its button
    /// was clicked; else its newest of all, again) or of all (space; else none).
    /// </summary>
    public GameEvent? Look(World world, int player, EventCategory? category)
    {
        var button = category is { } c
            ? _buttons.GetValueOrDefault(c)
            : _buttons.Values.Where(b => b.Unseen.Count > 0).MaxBy(b => b.Unseen[^1].Tick);
        if (button is { Unseen.Count: > 0 })
        {
            var newest = button.Unseen[^1];
            button.Unseen.RemoveAt(button.Unseen.Count - 1);
            return newest;
        }
        return category is { } again ? world.Events.Latest(player, again) : null;
    }

    /// <summary>The lit buttons over the panel's dark ones: blinking for a while after the event, then steady until looked at.</summary>
    public void Draw(SpriteBatch batch, double now)
    {
        foreach (var button in _buttons.Values)
        {
            if (button.Unseen.Count == 0)
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

    private sealed record DateMessage(string Date, EventCategory Category, string Text);

    private sealed class Button(string name, Rectangle rect, Texture2D lit)
    {
        public string Name { get; } = name;
        public Rectangle Rect { get; } = rect;
        public Texture2D Lit { get; } = lit;

        /// <summary>The events of this kind not looked at yet, oldest first.</summary>
        public List<GameEvent> Unseen { get; } = new();

        /// <summary>When the newest came (real time, seconds).</summary>
        public double Since { get; set; }
    }
}
