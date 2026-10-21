using System.Text.Json;
using Homestead.Assets.Gfx;
using Homestead.Simulation.Buildings;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Homestead.Client;

/// <summary>
/// The campaign's first page: choosing the people on the original's background (<c>campaignScreen</c> in
/// <c>data/ui.json</c>, scaled from 800 × 600, marble beside it). A click on a people's picture or name chooses it
/// (<see cref="Chosen"/>), Esc goes back to the title screen.
/// </summary>
public sealed class CampaignScreen : IDisposable
{
    private const int Width = 800, Height = 600;

    private static readonly Color Text = new(240, 225, 190);
    private static readonly Color Gold = new(232, 196, 92);

    private readonly Texture2D _background, _marble;
    private readonly Rectangle _heading;
    private readonly (Civilization People, Rectangle Picture, Rectangle Name)[] _peoples;

    private float _scale = 1;
    private Vector2 _origin;
    private KeyboardState _previousKeys = Keyboard.GetState();
    private MouseState _previousMouse = Mouse.GetState();

    public CampaignScreen(GraphicsDevice device, GfxDirectory gfx)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(BuildingCatalog.DefaultDataDirectory, "ui.json")));
        var look = document.RootElement.GetProperty("campaignScreen");
        var file = gfx.Open(look.GetProperty("file").GetInt32());
        Texture2D Image(string name)
        {
            var bitmap = file.ReadGui(look.GetProperty(name).GetInt32());
            var texture = new Texture2D(device, bitmap.Width, bitmap.Height);
            texture.SetData(bitmap.Pixels);
            return texture;
        }
        static Rectangle Box(JsonElement e) => new(e[0].GetInt32(), e[1].GetInt32(), e[2].GetInt32(), e[3].GetInt32());
        _background = Image("background");
        _marble = Image("marble");
        _heading = Box(look.GetProperty("heading"));
        _peoples = look.GetProperty("peoples").EnumerateObject()
            .Select(p => (Enum.Parse<Civilization>(p.Name), Box(p.Value.GetProperty("picture")), Box(p.Value.GetProperty("name"))))
            .ToArray();
    }

    /// <summary>The people clicked in the last <see cref="Update"/>, if any.</summary>
    public Civilization? Chosen { get; private set; }

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

    private int Px(float size) => Math.Max(8, (int)MathF.Round(size * _scale));

    /// <summary>The people under the mouse (its picture or its name).</summary>
    private Civilization? At(Point layout) =>
        _peoples.FirstOrDefault(p => p.Picture.Contains(layout) || p.Name.Contains(layout)) is { Picture.Width: > 0 } hit ? hit.People : null;

    /// <summary>One frame; true: back to the title screen. A chosen people is in <see cref="Chosen"/>.</summary>
    public bool Update(Rectangle view)
    {
        Fit(view);
        var keys = Keyboard.GetState();
        var mouse = Mouse.GetState();
        bool escape = keys.IsKeyDown(Keys.Escape) && !_previousKeys.IsKeyDown(Keys.Escape);
        bool clicked = mouse.LeftButton == ButtonState.Released && _previousMouse.LeftButton == ButtonState.Pressed;
        _previousKeys = keys;
        _previousMouse = mouse;
        Chosen = clicked ? At(ToLayout(mouse.Position)) : null;
        if (Chosen is not null)
            UiSounds.Toggle();
        return escape;
    }

    /// <summary>Back from the video: keys and buttons still held there do not count here.</summary>
    public void Resume()
    {
        _previousKeys = Keyboard.GetState();
        _previousMouse = Mouse.GetState();
    }

    public void Draw(SpriteBatch batch, Texture2D pixel, UiFont font, Rectangle view)
    {
        Fit(view);
        var page = ToScreen(new Rectangle(0, 0, Width, Height));
        if (page.X > view.X || page.Y > view.Y)
            batch.Draw(_marble, view, new Rectangle(14, 0, _marble.Width - 28, _marble.Height), Color.White);
        batch.Draw(_background, page, Color.White);

        WriteCentered(batch, font, Lang.T("campaign.choosePeople"), _heading, Gold, 15);
        var hover = At(ToLayout(Mouse.GetState().Position));
        foreach (var (people, picture, name) in _peoples)
        {
            bool lit = people == hover;
            if (lit)
                Outline(batch, pixel, ToScreen(new Rectangle(picture.X - 3, picture.Y - 3, picture.Width + 6, picture.Height + 6)), Gold);
            WriteCentered(batch, font, Lang.T("peoples." + people), name, lit ? Gold : Text, 16);
        }
    }

    private void Outline(SpriteBatch batch, Texture2D pixel, Rectangle r, Color color)
    {
        int line = Math.Max(2, (int)MathF.Round(2 * _scale));
        batch.Draw(pixel, new Rectangle(r.X, r.Y, r.Width, line), color);
        batch.Draw(pixel, new Rectangle(r.X, r.Bottom - line, r.Width, line), color);
        batch.Draw(pixel, new Rectangle(r.X, r.Y, line, r.Height), color);
        batch.Draw(pixel, new Rectangle(r.Right - line, r.Y, line, r.Height), color);
    }

    private void WriteCentered(SpriteBatch batch, UiFont font, string text, Rectangle box, Color color, int size)
    {
        var measured = font.Measure(text, Px(size)) / _scale;
        var at = _origin + new Vector2(box.Center.X - measured.X / 2, box.Center.Y - measured.Y / 2) * _scale;
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
