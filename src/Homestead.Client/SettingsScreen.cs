using System.Text.Json;
using Homestead.Assets.Gfx;
using Homestead.Simulation;
using Homestead.Simulation.Buildings;
using Homestead.Simulation.Economy;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Homestead.Client;

/// <summary>
/// The settings screen from the title screen, on a background of the original's menus (<c>settingsScreen</c> in
/// <c>data/ui.json</c>, scaled from 800 × 600, marble beside it): one section per topic in the large dark area, one
/// row of choices per setting (room for more below). "Ok" (Enter) writes the <see cref="GameSettings"/> into
/// <c>settings.json</c>, "Abbrechen" (Esc) drops the changes, "Standard" goes back to <c>production.json</c>.
/// </summary>
public sealed class SettingsScreen : IDisposable
{
    private const int Width = 800, Height = 600;

    private static readonly Color Text = new(240, 225, 190);
    private static readonly Color Gold = new(232, 196, 92);
    private static readonly Color Dim = new(175, 175, 185);
    private static readonly Color Error = new(240, 120, 100);

    /// <summary>One setting: a row of percentages to choose from.</summary>
    private sealed record Choice(string Label, string Hint, Func<GameSettings, int> Get, Func<GameSettings, int, GameSettings> Set);

    private static readonly int[] DepletionValues = [100, 75, 50, 25, 10, 0];

    private static readonly Choice[] Depletion =
    [
        new("Steinbrüche", "Steinmetze: Anteil der gehauenen Steine, der den Felsen kleiner macht. 100 % wie im Original, ∞: Felsen bleiben.",
            s => s.Depletion.Stone, (s, v) => s with { Depletion = s.Depletion with { Stone = v } }),
        new("Bergwerke", "Minen (Kohle, Erze, Gold, Edelsteine, Schwefel): Anteil der Funde, der das Vorkommen senkt. ∞: Berge erschöpfen nie.",
            s => s.Depletion.Mountain, (s, v) => s with { Depletion = s.Depletion with { Mountain = v } }),
        new("Fischgründe", "Fischer: Anteil der Fänge, der den Fischbestand senkt. ∞: Fische gehen nie aus.",
            s => s.Depletion.Fish, (s, v) => s with { Depletion = s.Depletion with { Fish = v } }),
    ];

    // Layout in the 800 × 600 space of the background, measured on it.
    private static readonly Rectangle TitleField = new(500, 20, 280, 44);
    private static readonly Rectangle Content = new(20, 88, 760, 452);
    private static readonly Rectangle NoteField = new(22, 556, 338, 22);
    private static readonly Rectangle DefaultButton = new(400, 555, 116, 30);
    private static readonly Rectangle OkButton = new(528, 555, 116, 30);
    private static readonly Rectangle CancelButton = new(656, 555, 116, 30);

    private const int RowTop = 150, RowHeight = 40;

    private static Rectangle Row(int row) => new(Content.X + 16, RowTop + row * RowHeight, Content.Width - 32, RowHeight - 6);

    private static Rectangle ValueBox(int row, int value)
    {
        const int left = 300, width = 460;
        int count = DepletionValues.Length;
        return new Rectangle(left + value * width / count, Row(row).Y, width / count - 4, 28);
    }

    private readonly Texture2D _background, _marble;
    private readonly ProductionConfig _production;
    private GameSettings _settings;
    private string? _error;

    private float _scale = 1;
    private Vector2 _origin;
    private KeyboardState _previousKeys = Keyboard.GetState();
    private MouseState _previousMouse = Mouse.GetState();

    public SettingsScreen(GraphicsDevice device, GfxDirectory gfx)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(BuildingCatalog.DefaultDataDirectory, "ui.json")));
        var look = document.RootElement.GetProperty("settingsScreen");
        var file = gfx.Open(look.GetProperty("file").GetInt32());
        Texture2D Image(string name)
        {
            var bitmap = file.ReadGui(look.GetProperty(name).GetInt32());
            var texture = new Texture2D(device, bitmap.Width, bitmap.Height);
            texture.SetData(bitmap.Pixels);
            return texture;
        }
        _background = Image("background");
        _marble = Image("marble");
        _production = ProductionConfig.Load();
        _settings = GameSettings.Load(_production);
    }

    private void Fit(Rectangle view)
    {
        _scale = Math.Min(view.Width / (float)Width, view.Height / (float)Height);
        _origin = new Vector2(view.X + (view.Width - Width * _scale) / 2, view.Y + (view.Height - Height * _scale) / 2);
    }

    private Rectangle ToScreen(Rectangle r)
    {
        var a = _origin + new Vector2(r.X, r.Y) * _scale;
        var b = _origin + new Vector2(r.Right, r.Bottom) * _scale;
        return new Rectangle((int)MathF.Round(a.X), (int)MathF.Round(a.Y), (int)MathF.Round(b.X) - (int)MathF.Round(a.X),
            (int)MathF.Round(b.Y) - (int)MathF.Round(a.Y));
    }

    private Point ToLayout(Point screen) => ((screen.ToVector2() - _origin) / _scale).ToPoint();

    private int Px(int size) => Math.Max(8, (int)MathF.Round(size * _scale));

    /// <summary>True once the screen is closed (saved or cancelled).</summary>
    public bool Update(Rectangle view)
    {
        Fit(view);
        var keys = Keyboard.GetState();
        var mouse = Mouse.GetState();
        bool escape = keys.IsKeyDown(Keys.Escape) && !_previousKeys.IsKeyDown(Keys.Escape);
        bool enter = keys.IsKeyDown(Keys.Enter) && !_previousKeys.IsKeyDown(Keys.Enter);
        bool clicked = mouse.LeftButton == ButtonState.Released && _previousMouse.LeftButton == ButtonState.Pressed;
        _previousKeys = keys;
        _previousMouse = mouse;
        var at = ToLayout(mouse.Position);

        if (escape || clicked && CancelButton.Contains(at))
            return true;
        if (enter || clicked && OkButton.Contains(at))
            return Save();
        if (!clicked)
            return false;
        if (DefaultButton.Contains(at))
            _settings = GameSettings.Defaults(_production);
        for (int row = 0; row < Depletion.Length; row++)
            for (int value = 0; value < DepletionValues.Length; value++)
                if (ValueBox(row, value).Contains(at))
                    _settings = Depletion[row].Set(_settings, DepletionValues[value]);
        return false;
    }

    /// <summary>Writes the settings; on failure the screen stays open with the reason.</summary>
    private bool Save()
    {
        try
        {
            _settings.Save();
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            _error = $"Nicht gespeichert: {e.Message}";
            return false;
        }
    }

    public void Draw(SpriteBatch batch, Texture2D pixel, UiFont font, Rectangle view)
    {
        Fit(view);
        var mouse = Mouse.GetState().Position;
        var hover = ToLayout(mouse);
        var page = ToScreen(new Rectangle(0, 0, Width, Height));
        if (page.X > view.X || page.Y > view.Y)
            batch.Draw(_marble, view, new Rectangle(14, 0, _marble.Width - 28, _marble.Height), Color.White);
        batch.Draw(_background, page, Color.White);

        var title = font.Measure("Einstellungen", Px(18)) / _scale;
        Write(batch, font, "Einstellungen", TitleField.Center.X - title.X / 2, TitleField.Center.Y - title.Y / 2, Gold, 18);

        Write(batch, font, "Rohstoffe", Content.X + 20, 104, Gold, 15);
        Write(batch, font, "Wie viel jeder Abbau vom Vorkommen verbraucht", Content.X + 20, 124, Dim, 11);
        string? hint = null;
        for (int row = 0; row < Depletion.Length; row++)
        {
            var choice = Depletion[row];
            var rect = Row(row);
            Write(batch, font, choice.Label, rect.X + 4, rect.Y + 6, Text, 13);
            if (rect.Contains(hover))
                hint = choice.Hint;
            int chosen = choice.Get(_settings);
            for (int value = 0; value < DepletionValues.Length; value++)
            {
                int percent = DepletionValues[value];
                TextButton.Draw(batch, pixel, font, ToScreen(ValueBox(row, value)), percent == 0 ? "∞" : $"{percent} %",
                    mouse.ToVector2(), size: Px(percent == 0 ? 18 : 12), selected: percent == chosen);
            }
        }
        if (hint is not null)
        {
            float y = Content.Bottom - 46;
            foreach (string line in font.Wrap(hint, Px(Content.Width - 40), Px(11)))
            {
                Write(batch, font, line, Content.X + 20, y, Dim, 11);
                y += 15;
            }
        }

        Write(batch, font, _error ?? "Gilt für neue Partien, nicht für Spielstände.", NoteField.X + 6, NoteField.Y + 4,
            _error is null ? Dim : Error, 11);
        TextButton.Draw(batch, pixel, font, ToScreen(DefaultButton), "Standard", mouse.ToVector2(), size: Px(13));
        TextButton.Draw(batch, pixel, font, ToScreen(OkButton), "Ok", mouse.ToVector2(), size: Px(13));
        TextButton.Draw(batch, pixel, font, ToScreen(CancelButton), "Abbrechen", mouse.ToVector2(), size: Px(13));
    }

    /// <summary>Text at a point of the 800 × 600 space, with a shadow.</summary>
    private void Write(SpriteBatch batch, UiFont font, string text, float x, float y, Color color, int size)
    {
        var at = _origin + new Vector2(x, y) * _scale;
        at = new Vector2(MathF.Round(at.X), MathF.Round(at.Y));
        font.Draw(batch, text, at + new Vector2(1, 1), Color.Black * 0.8f, Px(size));
        font.Draw(batch, text, at, color, Px(size));
    }

    public void Dispose()
    {
        _background.Dispose();
        _marble.Dispose();
    }
}
