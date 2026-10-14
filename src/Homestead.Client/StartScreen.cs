using System.Text.Json;
using Homestead.Assets.Maps;
using Homestead.Simulation.Buildings;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Homestead.Client;

/// <summary>
/// Start screen: map list on the left; on the right a preview of the selected map, own people and color,
/// opponents' people and color, and the start button.
/// </summary>
/// <remarks>
/// The list shows one category at a time (buttons above it, as in the original), sorted by player count.
/// Mouse: click selects, wheel scrolls the list, arrows change the people. Keys: up/down/PgUp/PgDn map ·
/// Tab category · Enter start · Esc quit.
/// </remarks>
public sealed class StartScreen : IDisposable
{
    /// <summary>What was chosen: <c>null</c> as people means drawn at random.</summary>
    public sealed record Choice(int MapIndex, Civilization? Own, Civilization? Enemy, int OwnColor, int EnemyColor);

    private const int RowHeight = 22;
    private const int Margin = 40;
    private const int Swatch = 28;
    private const int TabHeight = 30;

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
    private Civilization? _own;
    private Civilization? _enemy;
    private int _ownColor;
    private int _enemyColor = 1;

    private OriginalMap? _map;
    private string? _mapError;
    private Texture2D? _preview;

    private KeyboardState _previousKeys = Keyboard.GetState();
    private MouseState _previousMouse = Mouse.GetState();

    /// <param name="mapDirectory">Map directory of the installation; its first level of folders gives the category.</param>
    /// <param name="peoples">Preset peoples (command line): entry 0 own, entry 1 opponents.</param>
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
        _own = peoples is { Count: > 0 } ? peoples[0] : Civilization.Roman;
        _enemy = peoples is { Count: > 1 } ? peoples[1] : null;
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

    /// <summary>People the map gives <paramref name="player"/>, if it gives one.</summary>
    private Civilization? FixedPeople(int player) =>
        _map is not null && player < _map.Players.Count && _map.Players[player].Tribe is >= 0 and <= 3
            ? (Civilization)_map.Players[player].Tribe
            : null;

    private void Select(int index)
    {
        _selected = Math.Clamp(index, 0, Math.Max(0, _shown.Length - 1));
        _preview?.Dispose();
        _preview = null;
        _map = null;
        _mapError = null;
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
        }
        catch (Exception e) when (e is InvalidDataException or IOException)
        {
            _map = null;
            _mapError = e.Message;
        }
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

    private static Rectangle PreviewArea(Rectangle view)
    {
        int size = Math.Clamp(Math.Min(view.Width - PanelX(view) - Margin, view.Height - 460), 96, 260);
        return new Rectangle(PanelX(view), 90, size, size);
    }

    private static int SettingsY(Rectangle view) => PreviewArea(view).Bottom + 36;

    /// <summary>The row of setting <paramref name="row"/> (0 own people, 1 own color, 2 enemy people, 3 enemy color).</summary>
    private static int RowY(Rectangle view, int row) => SettingsY(view) + row * 44;

    private static Rectangle LeftArrow(Rectangle view, int row) => new(PanelX(view) + 140, RowY(view, row), 28, 28);

    private static Rectangle RightArrow(Rectangle view, int row) => new(PanelX(view) + 140 + 28 + 140, RowY(view, row), 28, 28);

    private static Rectangle SwatchAt(Rectangle view, int row, int index) =>
        new(PanelX(view) + 140 + index * (Swatch + 8), RowY(view, row), Swatch, Swatch);

    private static Rectangle StartButton(Rectangle view) => new(PanelX(view), RowY(view, 4) + 12, 220, 44);

    private int VisibleRows(Rectangle view) => ListArea(view).Height / RowHeight;

    /// <summary>Handles input; returns the choice once the game is started, <c>null</c> otherwise.</summary>
    public Choice? Update(Rectangle view, out bool quit)
    {
        var keys = Keyboard.GetState();
        var mouse = Mouse.GetState();
        bool Pressed(Keys k) => keys.IsKeyDown(k) && !_previousKeys.IsKeyDown(k);
        bool clicked = mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released;
        int wheel = mouse.ScrollWheelValue - _previousMouse.ScrollWheelValue;
        _previousKeys = keys;
        _previousMouse = mouse;
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
            for (int side = 0; side < 2; side++)
            {
                int row = side * 2;
                int step = LeftArrow(view, row).Contains(at) ? -1 : RightArrow(view, row).Contains(at) ? 1 : 0;
                if (step != 0 && FixedPeople(side) is null)
                {
                    if (side == 0)
                        _own = Cycle(_own, step);
                    else
                        _enemy = Cycle(_enemy, step);
                }
                for (int i = 0; i < PlayerColors.Palette.Length; i++)
                    if (SwatchAt(view, row + 1, i).Contains(at))
                        PickColor(side, i);
            }
            if (StartButton(view).Contains(at) && _map is not null)
                return Start();
        }
        if (Pressed(Keys.Enter) && _map is not null)
            return Start();
        return null;
    }

    private Choice Start() => new(_shown[_selected].Index, _own, _enemy, _ownColor, _enemyColor);

    /// <summary>Next people in the order random, Romans, Egyptians, Asians, Amazons.</summary>
    private static Civilization? Cycle(Civilization? people, int step)
    {
        var all = Enum.GetValues<Civilization>();
        int count = all.Length + 1;
        int index = people is { } p ? Array.IndexOf(all, p) + 1 : 0;
        index = ((index + step) % count + count) % count;
        return index == 0 ? null : all[index - 1];
    }

    /// <summary>Picks a color for one side; taking the other side's color swaps the two.</summary>
    private void PickColor(int side, int color)
    {
        if (side == 0)
        {
            if (color == _enemyColor)
                _enemyColor = _ownColor;
            _ownColor = color;
        }
        else
        {
            if (color == _ownColor)
                _ownColor = _enemyColor;
            _enemyColor = color;
        }
    }

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

        // Selected map: preview with start positions in the chosen colors, facts.
        int x = PanelX(view);
        var preview = PreviewArea(view);
        if (_preview is not null && _map is not null)
        {
            batch.Draw(_preview, preview, Color.White);
            float scale = preview.Width / (float)_map.Area.Size;
            var colors = PlayerColors.Ordered(_ownColor, _enemyColor);
            for (int player = 0; player < _map.Players.Count; player++)
            {
                var home = _map.Players[player];
                var mark = new Rectangle(preview.X + (int)(home.StartX * scale) - 4, preview.Y + (int)(home.StartY * scale) - 4, 9, 9);
                batch.Draw(pixel, mark, Color.Black);
                batch.Draw(pixel, new Rectangle(mark.X + 2, mark.Y + 2, 5, 5), colors[player % colors.Length]);
            }
            int textX = preview.Right + 20;
            font.Draw(batch, _map.Name, new Vector2(textX, preview.Y), Color.White, 22);
            font.Draw(batch, $"{(_map.IsSinglePlayer ? "Einzelspieler" : "Mehrspieler")} · {_map.Players.Count} Spieler",
                new Vector2(textX, preview.Y + 34), Color.LightGray);
            font.Draw(batch, $"{_map.Area.Size} × {_map.Area.Size} Felder", new Vector2(textX, preview.Y + 56), Color.LightGray);
            if (_map.Buildings.Count > 0)
                font.Draw(batch, $"{_map.Buildings.Count} Gebäude vorgegeben", new Vector2(textX, preview.Y + 78), Color.LightGray);
        }
        else
            font.Draw(batch, $"Karte nicht lesbar: {_mapError}", new Vector2(x, preview.Y), Color.OrangeRed);

        // People and color per side.
        string[] titles = ["Dein Volk", "Deine Farbe", "Gegnervolk", "Gegnerfarbe"];
        for (int side = 0; side < 2; side++)
        {
            int row = side * 2;
            font.Draw(batch, titles[row], new Vector2(x, RowY(view, row) + 5), Color.White, 16);
            font.Draw(batch, titles[row + 1], new Vector2(x, RowY(view, row + 1) + 5), Color.White, 16);

            var fixedPeople = FixedPeople(side);
            var people = fixedPeople ?? (side == 0 ? _own : _enemy);
            var left = LeftArrow(view, row);
            var right = RightArrow(view, row);
            if (fixedPeople is null)
            {
                DrawButton(batch, pixel, font, left, "<", left.Contains(hover));
                DrawButton(batch, pixel, font, right, ">", right.Contains(hover));
            }
            string name = NameOf(people) + (fixedPeople is null ? "" : " (Karte)");
            var size = font.Measure(name, 16);
            font.Draw(batch, name, new Vector2((left.Right + right.X - size.X) / 2, left.Y + 4), fixedPeople is null ? Color.White : dim, 16);

            int chosen = side == 0 ? _ownColor : _enemyColor;
            for (int i = 0; i < PlayerColors.Palette.Length; i++)
            {
                var swatch = SwatchAt(view, row + 1, i);
                if (i == chosen)
                    batch.Draw(pixel, new Rectangle(swatch.X - 3, swatch.Y - 3, swatch.Width + 6, swatch.Height + 6), Color.White);
                batch.Draw(pixel, swatch, PlayerColors.Palette[i]);
            }
        }
        if (_map is { Players.Count: > 2 })
            font.Draw(batch, "Weitere Gegner bekommen das Gegnervolk und die übrigen Farben.",
                new Vector2(x, RowY(view, 4) - 10), dim, 12);

        var start = StartButton(view);
        DrawButton(batch, pixel, font, start, "Spiel starten", _map is not null && start.Contains(hover), 18);
        font.Draw(batch, "Enter: starten · Esc: beenden · Pfeiltasten/Mausrad: Karte · Tab: Kategorie",
            new Vector2(start.Right + 20, start.Y + 14), dim, 13);
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
