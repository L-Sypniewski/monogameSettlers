using System.Text.Json;
using Homestead.Assets.Maps;
using Homestead.Simulation.Buildings;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Homestead.Client;

/// <summary>
/// Start screen: map list on the left; on the right a preview of the selected map with its start positions,
/// one row per player (people, color, hardcore) and the start button.
/// </summary>
/// <remarks>
/// The list shows one category at a time (buttons above it, as in the original), sorted by player count.
/// On multiplayer maps the start positions are drawn at random; a click on a position's "?" assigns it a
/// player (or makes it random again). Mouse: click selects, wheel scrolls the list, arrows change the
/// people, a click on the color takes the next free one (right click: the previous one). Keys: up/down/PgUp/PgDn
/// map · Tab category · Enter start · Esc quit.
/// </remarks>
public sealed class StartScreen : IDisposable
{
    /// <summary>One player's settings: <c>null</c> as people or color (palette index) means drawn at random.</summary>
    public sealed record PlayerSetup(Civilization? People, int? Color, bool Hardcore);

    /// <summary>
    /// What was chosen: settings per player (entry 0 is the own one) and, on multiplayer maps, per start
    /// position (map player) the player taking it (<c>null</c>: drawn); <c>null</c> as a whole: as the map says.
    /// </summary>
    public sealed record Choice(int MapIndex, IReadOnlyList<PlayerSetup> Players, IReadOnlyList<int?>? Positions);

    private const int RowHeight = 22;
    private const int Margin = 40;
    private const int Swatch = 28;
    private const int TabHeight = 30;
    /// <summary>Vertical distance of the player rows.</summary>
    private const int PlayerRow = 36;
    /// <summary>Size of a start position mark on the preview.</summary>
    private const int Mark = 16;
    private const int MenuItemHeight = 22;
    private const int MenuWidth = 110;

    /// <summary>A map in the list: index into the map files, name, player count (0: unreadable), category.</summary>
    private sealed record MapEntry(int Index, string Name, int Players, int Category);

    /// <summary>Map categories: the folders of the original's map directory (the tutorial counts as single player).</summary>
    private static readonly string[] Categories = ["Einzelspieler", "Mehrspieler", "Eigene"];

    private static int CategoryOf(string folder) => folder.ToUpperInvariant() switch
    {
        "MULTI" => 1,
        "USER" => 2,
        _ => 0,
    };

    private readonly GraphicsDevice _device;
    private readonly IReadOnlyList<string> _mapFiles;
    private readonly MapEntry[] _entries;

    /// <summary>Maps of the current category in list order.</summary>
    private MapEntry[] _shown = [];
    private int _category;
    private readonly Dictionary<Civilization, string> _peopleNames = new();

    private int _selected;
    private int _scroll;

    /// <summary>Peoples preset on the command line (player i: entry i modulo the count).</summary>
    private readonly IReadOnlyList<Civilization>? _presetPeoples;
    /// <summary>Settings per player; they grow with the maps' player counts and are kept across maps.</summary>
    private readonly List<Civilization?> _peoples = new();
    /// <summary>Palette index of the chosen color; <c>null</c>: random (the default).</summary>
    private readonly List<int?> _colors = new();
    /// <summary>The player gets the <see cref="Homestead.Simulation.Hardcore"/> head start.</summary>
    private readonly List<bool> _hardcore = new();
    /// <summary>Per start position of the map the player assigned to it; <c>null</c>: drawn at the start.</summary>
    private int?[] _positions = [];
    /// <summary>Start position whose player menu is open.</summary>
    private int? _positionMenu;

    private OriginalMap? _map;
    private string? _mapError;
    private Texture2D? _preview;

    private KeyboardState _previousKeys = Keyboard.GetState();
    private MouseState _previousMouse = Mouse.GetState();

    /// <param name="mapDirectory">Map directory of the installation; its first level of folders gives the category.</param>
    /// <param name="peoples">Preset peoples (command line): player i gets entry i modulo the count.</param>
    public StartScreen(GraphicsDevice device, IReadOnlyList<string> mapFiles, string mapDirectory, int selected,
        IReadOnlyList<Civilization>? peoples)
    {
        _device = device;
        _mapFiles = mapFiles;
        _entries = mapFiles.Select((file, index) =>
        {
            string folder = Path.GetRelativePath(mapDirectory, file).Split(Path.DirectorySeparatorChar)[0];
            int players;
            try
            {
                players = OriginalMap.ReadPlayerCount(file);
            }
            catch (Exception e) when (e is InvalidDataException or IOException)
            {
                players = 0;
            }
            return new MapEntry(index, Path.GetFileNameWithoutExtension(file), players, CategoryOf(folder));
        }).ToArray();
        _presetPeoples = peoples is { Count: > 0 } ? peoples : null;
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(BuildingCatalog.DefaultDataDirectory, "ui.json")));
        if (document.RootElement.TryGetProperty("peoples", out var names))
            foreach (var name in names.EnumerateObject())
                if (Enum.TryParse<Civilization>(name.Name, out var people))
                    _peopleNames[people] = name.Value.GetString()!;
        var preset = _entries.FirstOrDefault(e => e.Index == selected);
        ShowCategory(preset?.Category ?? 1);
        Select(Math.Max(0, Array.IndexOf(_shown, preset)));
    }

    /// <summary>Shows the maps of <paramref name="category"/>: by player count, unreadable ones last, then by name.</summary>
    private void ShowCategory(int category)
    {
        _category = category;
        _shown = _entries.Where(e => e.Category == category)
            .OrderBy(e => e.Players == 0 ? int.MaxValue : e.Players)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _scroll = 0;
        Select(0);
    }

    private string NameOf(Civilization? people) =>
        people is { } p ? _peopleNames.GetValueOrDefault(p, p.ToString()) : "Zufall";

    /// <summary>Players of the selected map (at least one row).</summary>
    private int PlayerCount => Math.Max(1, _map?.Players.Count ?? 2);

    /// <summary>Start positions are drawn on multiplayer maps; single player maps give each player its own.</summary>
    private bool RandomPositions => _map is { IsSinglePlayer: false };

    /// <summary>Start position of <paramref name="player"/>, if known yet.</summary>
    private int? PositionOf(int player)
    {
        if (!RandomPositions)
            return player;
        int index = Array.IndexOf(_positions, player);
        return index >= 0 ? index : null;
    }

    /// <summary>People the map gives the start position, if it gives one.</summary>
    private Civilization? FixedPeopleAt(int position) =>
        _map is not null && position < _map.Players.Count && _map.Players[position].Tribe is >= 0 and <= 3
            ? (Civilization)_map.Players[position].Tribe
            : null;

    /// <summary>People the map gives <paramref name="player"/> through its start position, if known.</summary>
    private Civilization? FixedPeople(int player) => PositionOf(player) is { } position ? FixedPeopleAt(position) : null;

    /// <summary>Grows the per-player settings to the player count of the selected map.</summary>
    private void EnsurePlayers()
    {
        while (_peoples.Count < PlayerCount)
        {
            int player = _peoples.Count;
            _peoples.Add(_presetPeoples is { } preset ? preset[player % preset.Count] : player == 0 ? Civilization.Roman : null);
            _colors.Add(null);
            _hardcore.Add(false);
        }
    }

    private void Select(int index)
    {
        _selected = Math.Clamp(index, 0, Math.Max(0, _shown.Length - 1));
        _preview?.Dispose();
        _preview = null;
        _map = null;
        _mapError = null;
        _positionMenu = null;
        _positions = [];
        if (_shown.Length == 0)
        {
            _mapError = "keine Karten in dieser Kategorie";
            return;
        }
        try
        {
            _map = OriginalMap.Open(_mapFiles[_shown[_selected].Index]);
            _preview = new Texture2D(_device, _map.Area.Size, _map.Area.Size);
            _preview.SetData(MapOverview.Pixels(_map.Area));
            _positions = new int?[_map.Players.Count];
        }
        catch (Exception e) when (e is InvalidDataException or IOException)
        {
            _map = null;
            _mapError = e.Message;
        }
        EnsurePlayers();
    }

    // Layout, from the viewport.
    private static Rectangle ListArea(Rectangle view) =>
        new(Margin, 90 + TabHeight + 6, Math.Min(420, view.Width * 2 / 5), Math.Max(RowHeight, view.Height - 90 - TabHeight - 6 - Margin));

    private static Rectangle Tab(Rectangle view, int category)
    {
        var list = ListArea(view);
        int width = (list.Width - (Categories.Length - 1) * 6) / Categories.Length;
        return new Rectangle(list.X + category * (width + 6), 90, width, TabHeight);
    }

    private static int PanelX(Rectangle view) => ListArea(view).Right + Margin;

    /// <summary>The preview: as large as the player rows, the start button and the hints below it leave room for.</summary>
    private Rectangle PreviewArea(Rectangle view)
    {
        int below = 24 + (PlayerCount + 1) * PlayerRow + 30 + 44 + Margin;
        int size = Math.Clamp(Math.Min(view.Width - PanelX(view) - Margin, view.Height - 90 - below), 96, 260);
        return new Rectangle(PanelX(view), 90, size, size);
    }

    /// <summary>The header row above the player rows.</summary>
    private int HeaderY(Rectangle view) => PreviewArea(view).Bottom + 24;

    /// <summary>The row of <paramref name="player"/>.</summary>
    private int RowY(Rectangle view, int player) => HeaderY(view) + (player + 1) * PlayerRow;

    private Rectangle LeftArrow(Rectangle view, int player) => new(PanelX(view) + 125, RowY(view, player), 28, 28);

    private Rectangle RightArrow(Rectangle view, int player) => new(PanelX(view) + 125 + 28 + 120, RowY(view, player), 28, 28);

    private Rectangle ColorBox(Rectangle view, int player) => new(PanelX(view) + 321, RowY(view, player), Swatch, Swatch);

    private Rectangle HardcoreBox(Rectangle view, int player) => new(PanelX(view) + 390, RowY(view, player), 28, 28);

    private const string HardcoreHint = "Hardcore: +10 % Kampfkraft, +30 % Siedler je Wohnhaus, +20 % Lebenspunkte, −25 % Baukosten, 10 % schnellere Produktion, mehr Startausstattung.";

    private Rectangle StartButton(Rectangle view) => new(PanelX(view), RowY(view, PlayerCount) + 30, 220, 44);

    /// <summary>The mark of start position <paramref name="position"/> on the preview.</summary>
    private Rectangle MarkAt(Rectangle view, int position)
    {
        var preview = PreviewArea(view);
        float scale = preview.Width / (float)_map!.Area.Size;
        var home = _map.Players[position];
        return new Rectangle(preview.X + (int)(home.StartX * scale) - Mark / 2, preview.Y + (int)(home.StartY * scale) - Mark / 2, Mark, Mark);
    }

    /// <summary>Entry <paramref name="item"/> of the open position menu: 0 is "?", entry i + 1 player i.</summary>
    private Rectangle MenuItem(Rectangle view, int item)
    {
        var mark = MarkAt(view, _positionMenu!.Value);
        int height = (PlayerCount + 1) * MenuItemHeight;
        // Right of the mark, kept inside the window.
        int x = Math.Min(mark.Right + 6, view.Width - MenuWidth - 4);
        int y = Math.Clamp(mark.Y, 4, Math.Max(4, view.Height - height - 4));
        return new Rectangle(x, y + item * MenuItemHeight, MenuWidth, MenuItemHeight);
    }

    private int VisibleRows(Rectangle view) => ListArea(view).Height / RowHeight;

    private static string PlayerName(int player) => $"Spieler {player + 1}";

    /// <summary>Handles input; returns the choice once the game is started, <c>null</c> otherwise.</summary>
    public Choice? Update(Rectangle view, out bool quit)
    {
        var keys = Keyboard.GetState();
        var mouse = Mouse.GetState();
        bool Pressed(Keys k) => keys.IsKeyDown(k) && !_previousKeys.IsKeyDown(k);
        bool clicked = mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released;
        bool rightClicked = mouse.RightButton == ButtonState.Pressed && _previousMouse.RightButton == ButtonState.Released;
        int wheel = mouse.ScrollWheelValue - _previousMouse.ScrollWheelValue;
        _previousKeys = keys;
        _previousMouse = mouse;
        quit = false;

        // An open position menu takes the next click (or Esc) for itself.
        if (_positionMenu is { } open)
        {
            if (Pressed(Keys.Escape))
                _positionMenu = null;
            else if (clicked || rightClicked)
            {
                for (int item = 0; item <= PlayerCount; item++)
                    if (clicked && MenuItem(view, item).Contains(mouse.Position))
                        AssignPosition(open, item == 0 ? null : item - 1);
                _positionMenu = null;
            }
            return null;
        }

        quit = Pressed(Keys.Escape);
        if (Pressed(Keys.Tab))
            ShowCategory((_category + 1) % Categories.Length);

        int rows = VisibleRows(view);
        int move = (Pressed(Keys.Down) ? 1 : 0) - (Pressed(Keys.Up) ? 1 : 0)
                   + (Pressed(Keys.PageDown) ? rows : 0) - (Pressed(Keys.PageUp) ? rows : 0);
        if (move != 0)
        {
            Select(_selected + move);
            _scroll = Math.Clamp(_scroll, _selected - rows + 1, _selected);
        }
        if (wheel != 0)
            _scroll -= Math.Sign(wheel) * 3;
        _scroll = Math.Clamp(_scroll, 0, Math.Max(0, _shown.Length - rows));

        if (clicked || rightClicked)
        {
            var at = mouse.Position;
            for (int player = 0; player < PlayerCount; player++)
                if (ColorBox(view, player).Contains(at))
                    _colors[player] = NextColor(player, clicked ? 1 : -1);
        }
        if (clicked)
        {
            var at = mouse.Position;
            var list = ListArea(view);
            if (list.Contains(at))
            {
                int index = _scroll + (at.Y - list.Y) / RowHeight;
                if (index < _shown.Length && index != _selected)
                    Select(index);
            }
            for (int category = 0; category < Categories.Length; category++)
                if (Tab(view, category).Contains(at) && category != _category)
                    ShowCategory(category);
            if (_map is not null && RandomPositions)
                for (int position = 0; position < _map.Players.Count; position++)
                    if (MarkAt(view, position).Contains(at))
                        _positionMenu = position;
            for (int player = 0; player < PlayerCount; player++)
            {
                int step = LeftArrow(view, player).Contains(at) ? -1 : RightArrow(view, player).Contains(at) ? 1 : 0;
                if (step != 0 && FixedPeople(player) is null)
                    _peoples[player] = Cycle(_peoples[player], step);
                if (HardcoreBox(view, player).Contains(at))
                    _hardcore[player] = !_hardcore[player];
            }
            if (StartButton(view).Contains(at) && _map is not null)
                return Start();
        }
        if (Pressed(Keys.Enter) && _map is not null)
            return Start();
        return null;
    }

    private Choice Start() => new(_shown[_selected].Index,
        Enumerable.Range(0, PlayerCount).Select(p => new PlayerSetup(_peoples[p], _colors[p], _hardcore[p])).ToArray(),
        RandomPositions ? _positions.ToArray() : null);

    /// <summary>
    /// Start position per player: the assigned ones, the others drawn from the positions left
    /// (entry i: the map player whose start player i takes).
    /// </summary>
    public static int[] ResolvePositions(IReadOnlyList<int?> chosen, Random random)
    {
        var positions = new int[chosen.Count];
        var assigned = new HashSet<int>();
        for (int position = 0; position < chosen.Count; position++)
            if (chosen[position] is { } player && player < chosen.Count)
            {
                positions[player] = position;
                assigned.Add(player);
            }
        var free = Enumerable.Range(0, chosen.Count).Where(p => chosen[p] is null || chosen[p] >= chosen.Count)
            .OrderBy(_ => random.Next()).ToList();
        foreach (int player in Enumerable.Range(0, chosen.Count).Where(p => !assigned.Contains(p)))
        {
            positions[player] = free[^1];
            free.RemoveAt(free.Count - 1);
        }
        return positions;
    }

    /// <summary>Gives the start position to <paramref name="player"/> (null: random); its previous position becomes random.</summary>
    private void AssignPosition(int position, int? player)
    {
        if (player is { } p && Array.IndexOf(_positions, p) is var previous and >= 0)
            _positions[previous] = null;
        _positions[position] = player;
    }

    /// <summary>Next people in the order random, Romans, Egyptians, Asians, Amazons.</summary>
    private static Civilization? Cycle(Civilization? people, int step)
    {
        var all = Enum.GetValues<Civilization>();
        int count = all.Length + 1;
        int index = people is { } p ? Array.IndexOf(all, p) + 1 : 0;
        index = ((index + step) % count + count) % count;
        return index == 0 ? null : all[index - 1];
    }

    /// <summary>Next color for the player in the order random, palette; colors of other players are skipped (random never is).</summary>
    private int? NextColor(int player, int step)
    {
        int count = PlayerColors.Palette.Length + 1;
        int index = _colors[player] is { } c ? c + 1 : 0;
        for (int i = 0; i < count; i++)
        {
            index = ((index + step) % count + count) % count;
            if (index == 0)
                return null;
            int color = index - 1;
            if (!Enumerable.Range(0, PlayerCount).Any(other => other != player && _colors[other] == color))
                return color;
        }
        return null;
    }

    /// <summary>The color shown for the player: its chosen one, grey while random.</summary>
    private Color ShownColor(int player) => _colors[player] is { } c ? PlayerColors.Palette[c] : Color.Gray;

    /// <summary>Black or white, whichever reads better on <paramref name="background"/>.</summary>
    private static Color TextOn(Color background) =>
        background.R * 0.3 + background.G * 0.59 + background.B * 0.11 > 140 ? Color.Black : Color.White;

    public void Draw(SpriteBatch batch, Texture2D pixel, UiFont font, Rectangle view)
    {
        var hover = Mouse.GetState().Position;
        var dim = new Color(150, 150, 150);
        font.Draw(batch, "Homestead", new Vector2(Margin, 28), Color.Gold, 34);

        // Category buttons and map list.
        for (int category = 0; category < Categories.Length; category++)
        {
            var tab = Tab(view, category);
            bool current = category == _category;
            batch.Draw(pixel, tab, current ? new Color(70, 60, 30) : tab.Contains(hover) ? new Color(55, 50, 42) : new Color(35, 33, 30));
            if (current)
                batch.Draw(pixel, new Rectangle(tab.X, tab.Bottom - 2, tab.Width, 2), Color.Gold);
            var size = font.Measure(Categories[category], 16);
            font.Draw(batch, Categories[category], new Vector2(tab.Center.X - size.X / 2, tab.Center.Y - size.Y / 2 - 1),
                current ? Color.White : Color.LightGray, 16);
        }
        var list = ListArea(view);
        batch.Draw(pixel, list, new Color(25, 25, 30));
        int rows = VisibleRows(view);
        for (int r = 0; r < rows && _scroll + r < _shown.Length; r++)
        {
            int index = _scroll + r;
            var entry = _shown[index];
            var row = new Rectangle(list.X, list.Y + r * RowHeight, list.Width, RowHeight);
            if (index == _selected)
                batch.Draw(pixel, row, new Color(70, 60, 30));
            else if (row.Contains(hover))
                batch.Draw(pixel, row, new Color(45, 45, 50));
            var color = index == _selected ? Color.White : Color.LightGray;
            font.Draw(batch, entry.Name, new Vector2(row.X + 8, row.Y + 3), color);
            string players = entry.Players > 0 ? $"{entry.Players} Spieler" : "?";
            font.Draw(batch, players, new Vector2(row.Right - 14 - font.Measure(players).X, row.Y + 3), index == _selected ? color : dim);
        }
        if (_shown.Length > rows)
        {
            // Scroll bar.
            int height = Math.Max(16, list.Height * rows / _shown.Length);
            int y = list.Y + (list.Height - height) * _scroll / Math.Max(1, _shown.Length - rows);
            batch.Draw(pixel, new Rectangle(list.Right - 4, y, 4, height), dim);
        }

        // Selected map: preview with its start positions, facts.
        int x = PanelX(view);
        var preview = PreviewArea(view);
        if (_preview is not null && _map is not null)
        {
            batch.Draw(_preview, preview, Color.White);
            for (int position = 0; position < _map.Players.Count; position++)
            {
                // A drawn position is a black box with "?"; an assigned one shows its player's color and number.
                var mark = MarkAt(view, position);
                int? player = RandomPositions ? _positions[position] : position;
                bool hot = RandomPositions && (mark.Contains(hover) || _positionMenu == position);
                batch.Draw(pixel, new Rectangle(mark.X - 1, mark.Y - 1, mark.Width + 2, mark.Height + 2), hot ? Color.Gold : Color.Black);
                var fill = player is { } p ? ShownColor(p) : Color.Black;
                batch.Draw(pixel, new Rectangle(mark.X + 1, mark.Y + 1, mark.Width - 2, mark.Height - 2), fill);
                string label = player is { } q ? (q + 1).ToString() : "?";
                var size = font.Measure(label, 13);
                font.Draw(batch, label, new Vector2(mark.Center.X - size.X / 2, mark.Center.Y - size.Y / 2 - 1),
                    player is null ? Color.White : TextOn(fill), 13);
            }
            int textX = preview.Right + 20;
            font.Draw(batch, _map.Name, new Vector2(textX, preview.Y), Color.White, 22);
            font.Draw(batch, $"{(_map.IsSinglePlayer ? "Einzelspieler" : "Mehrspieler")} · {_map.Players.Count} Spieler",
                new Vector2(textX, preview.Y + 34), Color.LightGray);
            font.Draw(batch, $"{_map.Area.Size} × {_map.Area.Size} Felder", new Vector2(textX, preview.Y + 56), Color.LightGray);
            if (_map.Buildings.Count > 0)
                font.Draw(batch, $"{_map.Buildings.Count} Gebäude vorgegeben", new Vector2(textX, preview.Y + 78), Color.LightGray);
            if (RandomPositions)
                font.Draw(batch, "Startpositionen: „?“ wird gelost,\nKlick weist einen Spieler zu.", new Vector2(textX, preview.Y + 108), dim, 13);
        }
        else
            font.Draw(batch, $"Karte nicht lesbar: {_mapError}", new Vector2(x, preview.Y), Color.OrangeRed);

        // One row per player: people, color, hardcore.
        int header = HeaderY(view) + 6;
        void Header(string text, Rectangle under)
        {
            var size = font.Measure(text, 13);
            font.Draw(batch, text, new Vector2(under.Center.X - size.X / 2, header), dim, 13);
        }
        Header("Volk", new Rectangle(LeftArrow(view, 0).X, 0, RightArrow(view, 0).Right - LeftArrow(view, 0).X, 0));
        Header("Farbe", ColorBox(view, 0));
        Header("Hardcore", HardcoreBox(view, 0));
        bool hardcoreHot = false;
        for (int player = 0; player < PlayerCount; player++)
        {
            int y = RowY(view, player);
            font.Draw(batch, PlayerName(player) + (player == 0 ? " (Du)" : ""), new Vector2(x, y + 5), Color.White, 16);

            var fixedPeople = FixedPeople(player);
            var people = fixedPeople ?? _peoples[player];
            var left = LeftArrow(view, player);
            var right = RightArrow(view, player);
            if (fixedPeople is null)
            {
                DrawButton(batch, pixel, font, left, "<", left.Contains(hover));
                DrawButton(batch, pixel, font, right, ">", right.Contains(hover));
            }
            string name = NameOf(people) + (fixedPeople is null ? "" : " (Karte)");
            var size = font.Measure(name, 16);
            font.Draw(batch, name, new Vector2((left.Right + right.X - size.X) / 2, left.Y + 4), fixedPeople is null ? Color.White : dim, 16);

            // Color: grey edge (gold under the mouse), "?" while random.
            var swatch = ColorBox(view, player);
            batch.Draw(pixel, new Rectangle(swatch.X - 3, swatch.Y - 3, swatch.Width + 6, swatch.Height + 6),
                swatch.Contains(hover) ? Color.Gold : new Color(90, 90, 95));
            batch.Draw(pixel, new Rectangle(swatch.X - 1, swatch.Y - 1, swatch.Width + 2, swatch.Height + 2), new Color(12, 12, 16));
            if (_colors[player] is { } color)
                batch.Draw(pixel, swatch, PlayerColors.Palette[color]);
            else
            {
                batch.Draw(pixel, swatch, new Color(60, 60, 66));
                var mark = font.Measure("?", 18);
                font.Draw(batch, "?", new Vector2(swatch.Center.X - mark.X / 2, swatch.Center.Y - mark.Y / 2 - 1), Color.White, 18);
            }

            // Hardcore checkbox: grey edge, a gold square inside when on.
            var box = HardcoreBox(view, player);
            bool hot = box.Contains(hover);
            hardcoreHot |= hot;
            batch.Draw(pixel, new Rectangle(box.X - 2, box.Y - 2, box.Width + 4, box.Height + 4), hot ? Color.Gold : new Color(90, 90, 95));
            batch.Draw(pixel, box, new Color(25, 25, 30));
            if (_hardcore[player])
                batch.Draw(pixel, new Rectangle(box.X + 6, box.Y + 6, box.Width - 12, box.Height - 12), Color.Gold);
        }

        var start = StartButton(view);
        string? hint = hardcoreHot ? HardcoreHint
            : RandomPositions && Enumerable.Range(0, PlayerCount).Any(p => FixedPeopleAt(p) is not null)
                ? "Die Karte gibt Völker je Startposition vor: sie gelten für den Spieler, der dort startet."
                : null;
        if (hint is not null)
            font.Draw(batch, hint, new Vector2(x, start.Y - 22), dim, 12);
        DrawButton(batch, pixel, font, start, "Spiel starten", _map is not null && start.Contains(hover), 18);
        font.Draw(batch, "Enter: starten · Esc: beenden · Pfeiltasten/Mausrad: Karte · Tab: Kategorie",
            new Vector2(start.Right + 20, start.Y + 14), dim, 13);

        // The position menu over everything else.
        if (_positionMenu is not null)
        {
            var top = MenuItem(view, 0);
            batch.Draw(pixel, new Rectangle(top.X - 2, top.Y - 2, MenuWidth + 4, (PlayerCount + 1) * MenuItemHeight + 4), Color.Gold);
            for (int item = 0; item <= PlayerCount; item++)
            {
                var entry = MenuItem(view, item);
                batch.Draw(pixel, entry, entry.Contains(hover) ? new Color(70, 60, 30) : new Color(30, 30, 36));
                if (item == 0)
                    font.Draw(batch, "? (Zufall)", new Vector2(entry.X + 8, entry.Y + 3), Color.White, 14);
                else
                {
                    var swatch = new Rectangle(entry.X + 6, entry.Y + 5, 12, 12);
                    batch.Draw(pixel, swatch, ShownColor(item - 1));
                    font.Draw(batch, PlayerName(item - 1), new Vector2(entry.X + 24, entry.Y + 3), Color.White, 14);
                }
            }
        }
    }

    private static void DrawButton(SpriteBatch batch, Texture2D pixel, UiFont font, Rectangle box, string text, bool hot, int size = 16)
    {
        batch.Draw(pixel, box, hot ? new Color(110, 90, 40) : new Color(60, 55, 45));
        batch.Draw(pixel, new Rectangle(box.X, box.Bottom - 2, box.Width, 2), new Color(30, 25, 20));
        var measured = font.Measure(text, size);
        font.Draw(batch, text, new Vector2(box.Center.X - measured.X / 2, box.Center.Y - measured.Y / 2 - 1), Color.White, size);
    }

    public void Dispose() => _preview?.Dispose();
}
