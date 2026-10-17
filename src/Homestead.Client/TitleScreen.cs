using System.Globalization;
using System.Text.Json;
using Homestead.Assets.Gfx;
using Homestead.Simulation.Buildings;
using Homestead.Simulation.Persistence;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Homestead.Client;

/// <summary>What the title screen asks the game to do.</summary>
public enum TitleAction
{
    None,
    /// <summary>Open the map selection (<see cref="StartScreen"/>).</summary>
    SinglePlayer,
    /// <summary>Load the save game in <see cref="TitleScreen.ChosenSlot"/>.</summary>
    Load,
    Quit,
}

/// <summary>
/// The title screen: the Gold Edition's main menu picture (<c>titleScreen</c> in <c>data/ui.json</c>, scaled from
/// 800 × 600 to the window, plain gold beside it) with a column of engraved buttons on its left side as in the
/// original, "Beenden" set apart at the bottom. "Spiel laden" lists the save slots below the buttons; settings,
/// contribution and licenses open a page there too (content still to come). The open page's button stays pressed;
/// clicking it again or Esc closes the page, Esc on the main page quits.
/// </summary>
public sealed class TitleScreen : IDisposable
{
    private const int Width = 800, Height = 600;

    private enum Page { Main, Load, Settings, Contributing, Licenses }

    private static readonly (string Label, Page Opens)[] Buttons =
    [
        ("Einzelspieler", Page.Main),
        ("Spiel laden", Page.Load),
        ("Einstellungen", Page.Settings),
        ("Contribution", Page.Contributing),
        ("Lizenzen", Page.Licenses),
    ];

    // The original's engraved buttons: a fill a little darker than the gold, a dark line around it and a light
    // one just outside to the lower right.
    private static readonly Color Fill = new(146, 132, 58);
    private static readonly Color PressedFill = new(128, 116, 46);
    private static readonly Color Dark = new(100, 90, 26);
    private static readonly Color Light = new(186, 170, 108);
    private static readonly Color Text = new(250, 246, 225);
    private static readonly Color HoverText = new(255, 236, 140);
    private static readonly Color Version = new(240, 222, 70);

    private readonly Texture2D _background, _plain;
    private readonly Rectangle _plainStrip;
    private readonly Dictionary<string, string> _peoples = new();
    private Page _page = Page.Main;
    private SaveInfo?[] _slots = [];

    private float _scale = 1;
    private Vector2 _origin;
    private KeyboardState _previousKeys = Keyboard.GetState();
    private MouseState _previousMouse = Mouse.GetState();

    /// <summary>The slot to load after <see cref="TitleAction.Load"/>.</summary>
    public int ChosenSlot { get; private set; }

    public TitleScreen(GraphicsDevice device, GfxDirectory gfx)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(BuildingCatalog.DefaultDataDirectory, "ui.json")));
        var look = document.RootElement.GetProperty("titleScreen");
        var file = gfx.Open(look.GetProperty("file").GetInt32());
        Texture2D Image(string name)
        {
            var bitmap = file.ReadGui(look.GetProperty(name).GetInt32());
            var texture = new Texture2D(device, bitmap.Width, bitmap.Height);
            texture.SetData(bitmap.Pixels);
            return texture;
        }
        _background = Image("background");
        _plain = Image("plain");
        var strip = look.GetProperty("plainStrip");
        _plainStrip = new Rectangle(strip[0].GetInt32(), strip[1].GetInt32(), strip[2].GetInt32(), strip[3].GetInt32());
        if (document.RootElement.TryGetProperty("peoples", out var names))
            foreach (var name in names.EnumerateObject())
                _peoples[name.Name] = name.Value.GetString()!;
    }

    // Layout in the 800 × 600 space of the picture, measured on the original menu.
    private static Rectangle Button(int index) => new(74, 19 + index * 40, 175, 31);

    private static readonly Rectangle QuitButton = new(74, 540, 175, 31);

    /// <summary>The open page, below the buttons.</summary>
    private static readonly Rectangle PagePanel = new(36, 226, 260, 296);

    private static Rectangle Slot(int slot) => new(PagePanel.X + 8, PagePanel.Y + 30 + slot * 33, PagePanel.Width - 16, 31);

    private void Fit(Rectangle view)
    {
        _scale = Math.Min(view.Width / (float)Width, view.Height / (float)Height);
        _origin = new Vector2(view.X + (view.Width - Width * _scale) / 2, view.Y + (view.Height - Height * _scale) / 2);
    }

    private Rectangle ToScreen(Rectangle r)
    {
        var a = _origin + new Vector2(r.X, r.Y) * _scale;
        var b = _origin + new Vector2(r.Right, r.Bottom) * _scale;
        return new Rectangle((int)MathF.Round(a.X), (int)MathF.Round(a.Y), (int)MathF.Round(b.X - a.X), (int)MathF.Round(b.Y - a.Y));
    }

    private Point ToLayout(Point screen) => ((screen.ToVector2() - _origin) / _scale).ToPoint();

    private int Px(float size) => Math.Max(8, (int)MathF.Round(size * _scale));

    public TitleAction Update(Rectangle view)
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

        if (escape)
        {
            if (_page == Page.Main)
                return TitleAction.Quit;
            _page = Page.Main;
        }
        if (enter && _page == Page.Main)
            return TitleAction.SinglePlayer;
        if (!clicked)
            return TitleAction.None;
        if (QuitButton.Contains(at))
            return TitleAction.Quit;
        for (int index = 0; index < Buttons.Length; index++)
            if (Button(index).Contains(at))
            {
                var opens = Buttons[index].Opens;
                if (opens == Page.Main)
                    return TitleAction.SinglePlayer;
                _page = _page == opens ? Page.Main : opens;
                if (_page == Page.Load)
                    _slots = Enumerable.Range(0, SaveSlots.Count).Select(SaveSlots.Info).ToArray();
            }
        if (_page == Page.Load)
            for (int slot = 0; slot < _slots.Length; slot++)
                if (_slots[slot] is not null && Slot(slot).Contains(at))
                {
                    ChosenSlot = slot;
                    return TitleAction.Load;
                }
        return TitleAction.None;
    }

    public void Draw(SpriteBatch batch, Texture2D pixel, UiFont font, Rectangle view)
    {
        Fit(view);
        var hover = ToLayout(Mouse.GetState().Position);
        bool held = Mouse.GetState().LeftButton == ButtonState.Pressed;
        batch.Draw(_plain, view, _plainStrip, Color.White);
        batch.Draw(_background, ToScreen(new Rectangle(0, 0, Width, Height)), Color.White);

        for (int index = 0; index < Buttons.Length; index++)
            Engraved(batch, pixel, font, Button(index), Buttons[index].Label, hover, held,
                _page != Page.Main && Buttons[index].Opens == _page);
        Engraved(batch, pixel, font, QuitButton, "Beenden", hover, held, false);

        if (_page != Page.Main)
        {
            var panel = ToScreen(PagePanel);
            batch.Draw(pixel, panel, PressedFill * 0.92f);
            Frame(batch, pixel, PagePanel, Dark, Light);
            Write(batch, font, Buttons.First(b => b.Opens == _page).Label, PagePanel.X + 10, PagePanel.Y + 7, Text, 14);
            if (_page == Page.Load)
                DrawSlots(batch, font, hover);
            else
                Write(batch, font, "Inhalt folgt.", PagePanel.X + 10, PagePanel.Y + 36, Text, 12);
        }
        Write(batch, font, "monoSettlers · Fan-Projekt, nicht verbunden mit Ubisoft oder Blue Byte", 27, 577, Version, 10);
    }

    /// <summary>The save slots: map, date and people; empty ones dashed.</summary>
    private void DrawSlots(SpriteBatch batch, UiFont font, Point hover)
    {
        for (int slot = 0; slot < _slots.Length; slot++)
        {
            var rect = Slot(slot);
            if (_slots[slot] is not { } info)
            {
                Write(batch, font, $"{slot + 1}.  ------------", rect.X + 4, rect.Y + 8, Text * 0.6f, 11);
                continue;
            }
            var color = rect.Contains(hover) ? HoverText : Text;
            Write(batch, font, $"{slot + 1}.  {Shorten(font, info.Map, rect.Width - 24, 12)}", rect.X + 4, rect.Y + 1, color, 12);
            string when = info.SavedAt.ToString("dd.MM.yy HH:mm", CultureInfo.InvariantCulture);
            Write(batch, font, $"{when} · {_peoples.GetValueOrDefault(info.People, info.People)}", rect.X + 22, rect.Y + 16, color * 0.85f, 10);
        }
    }

    private string Shorten(UiFont font, string text, int width, int size)
    {
        if (font.Measure(text, Px(size)).X <= width * _scale)
            return text;
        while (text.Length > 1 && font.Measure(text + "…", Px(size)).X > width * _scale)
            text = text[..^1];
        return text + "…";
    }

    /// <summary>One of the original's engraved buttons: pressed (darker) while open or held, the text lit under the mouse.</summary>
    private void Engraved(SpriteBatch batch, Texture2D pixel, UiFont font, Rectangle rect, string label, Point hover, bool held, bool open)
    {
        bool over = rect.Contains(hover);
        bool down = open || over && held;
        batch.Draw(pixel, ToScreen(rect), down ? PressedFill : Fill);
        Frame(batch, pixel, rect, Dark, Light);
        var size = font.Measure(label, Px(12)) / _scale;
        Write(batch, font, label, rect.Center.X - size.X / 2 + (down ? 1 : 0), rect.Center.Y - size.Y / 2 + (down ? 1 : 0),
            over || open ? HoverText : Text, 12);
    }

    /// <summary>An engraved frame: the dark line on the edge, the light one a pixel outside to the lower right.</summary>
    private void Frame(SpriteBatch batch, Texture2D pixel, Rectangle rect, Color dark, Color light)
    {
        var outer = ToScreen(rect);
        int line = Math.Max(1, (int)MathF.Round(_scale));
        void Box(Rectangle r, Color color)
        {
            batch.Draw(pixel, new Rectangle(r.X, r.Y, r.Width, line), color);
            batch.Draw(pixel, new Rectangle(r.X, r.Bottom - line, r.Width, line), color);
            batch.Draw(pixel, new Rectangle(r.X, r.Y, line, r.Height), color);
            batch.Draw(pixel, new Rectangle(r.Right - line, r.Y, line, r.Height), color);
        }
        Box(new Rectangle(outer.X + line, outer.Y + line, outer.Width, outer.Height), light);
        Box(outer, dark);
    }

    /// <summary>Text at a point of the 800 × 600 space with a dark shadow, as on the original's buttons.</summary>
    private void Write(SpriteBatch batch, UiFont font, string text, float x, float y, Color color, int size)
    {
        var at = _origin + new Vector2(x, y) * _scale;
        at = new Vector2(MathF.Round(at.X), MathF.Round(at.Y));
        font.Draw(batch, text, at + new Vector2(1, 1), new Color(60, 52, 12) * 0.9f, Px(size));
        font.Draw(batch, text, at, color, Px(size));
    }

    public void Dispose()
    {
        _background.Dispose();
        _plain.Dispose();
    }
}
