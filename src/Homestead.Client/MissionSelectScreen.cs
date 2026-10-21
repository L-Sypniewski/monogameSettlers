using System.Text.Json;
using Homestead.Assets.Gfx;
using Homestead.Simulation.Buildings;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Homestead.Client;

/// <summary>
/// The campaign's missions of one people once one is accomplished, on the original's background (<c>missionScreen</c> in
/// <c>data/ui.json</c>, scaled from 800 × 600, marble beside it): first the intro video, then every accomplished mission
/// and the next, with their names from the mission texts, to play again or on, after the last one the outro video.
/// A click chooses (<see cref="Chosen"/>, <see cref="ChosenVideo"/>); "Zurück" (Esc) goes back to the choice of people.
/// </summary>
public sealed class MissionSelectScreen : IDisposable
{
    /// <summary>A video of the list.</summary>
    public enum Video { Intro, Outro }
    private const int Width = 800, Height = 600;
    private const int RowHeight = 34;

    private static readonly Color Text = new(240, 225, 190);
    private static readonly Color Gold = new(232, 196, 92);
    private static readonly Color Dim = new(175, 175, 185);

    private static readonly Rectangle BackButton = new(680, 555, 97, 25);

    private readonly Texture2D _background, _marble;
    private readonly Rectangle _heading, _list;
    private readonly Civilization _people;
    private readonly IReadOnlyList<string> _names;
    private readonly int _accomplished;

    private float _scale = 1;
    private Vector2 _origin;
    private KeyboardState _previousKeys = Keyboard.GetState();
    private MouseState _previousMouse = Mouse.GetState();

    /// <param name="names">The names of all the campaign's missions in order.</param>
    /// <param name="accomplished">Missions accomplished so far: those and the next one can be chosen.</param>
    public MissionSelectScreen(GraphicsDevice device, GfxDirectory gfx, Civilization people, IReadOnlyList<string> names, int accomplished)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(BuildingCatalog.DefaultDataDirectory, "ui.json")));
        var look = document.RootElement.GetProperty("missionScreen");
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
        _list = Box(look.GetProperty("list"));
        _people = people;
        _names = names;
        _accomplished = accomplished;
    }

    /// <summary>The mission (from 0) clicked in the last <see cref="Update"/>, if any.</summary>
    public int? Chosen { get; private set; }

    /// <summary>The video clicked in the last <see cref="Update"/>, if any.</summary>
    public Video? ChosenVideo { get; private set; }

    /// <summary>The missions to choose from: the accomplished ones and the next (all once every one is).</summary>
    private int Available => Math.Min(_names.Count, _accomplished + 1);

    /// <summary>The rows in order: the intro, the missions to choose from, the outro once every mission is accomplished.</summary>
    private IEnumerable<(int? Mission, Video? Video)> Rows()
    {
        yield return (null, Video.Intro);
        for (int mission = 0; mission < Available; mission++)
            yield return (mission, null);
        if (_accomplished >= _names.Count)
            yield return (null, Video.Outro);
    }

    /// <summary>A mission's name from its text: the first line without its label ("MISSIONSNAME:", "MISSION NAME:").</summary>
    public static string NameOf(string text)
    {
        string line = text.Replace("\r", "").Split('\n')[0];
        int colon = line.IndexOf(':');
        return (colon >= 0 ? line[(colon + 1)..] : line).Trim();
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

    private int Px(float size) => Math.Max(8, (int)MathF.Round(size * _scale));

    private Rectangle Row(int row) => new(_list.X + 12, _list.Y + 14 + row * RowHeight, _list.Width - 24, RowHeight - 4);

    /// <summary>The row under the point (its index), if any.</summary>
    private int? At(Point layout)
    {
        int count = Rows().Count();
        for (int row = 0; row < count; row++)
            if (Row(row).Contains(layout))
                return row;
        return null;
    }

    /// <summary>One frame; true: back to the choice of people. A chosen mission is in <see cref="Chosen"/>.</summary>
    public bool Update(Rectangle view)
    {
        Fit(view);
        var keys = Keyboard.GetState();
        var mouse = Mouse.GetState();
        bool clicked = mouse.LeftButton == ButtonState.Released && _previousMouse.LeftButton == ButtonState.Pressed;
        bool escape = keys.IsKeyDown(Keys.Escape) && !_previousKeys.IsKeyDown(Keys.Escape);
        _previousKeys = keys;
        _previousMouse = mouse;
        var at = ToLayout(mouse.Position);
        var chosen = clicked && At(at) is { } row ? Rows().ElementAt(row) : ((int?)null, (Video?)null);
        (Chosen, ChosenVideo) = chosen;
        return escape || clicked && BackButton.Contains(at);
    }

    /// <summary>Back from a mission's description: keys and buttons still held there do not count here.</summary>
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

        WriteCentered(batch, font, $"{Lang.T("peoples." + _people)} · {Lang.T("campaign.chooseMission")}", _heading, Gold, 13);
        var hover = At(ToLayout(Mouse.GetState().Position));
        int index = 0;
        foreach (var (mission, video) in Rows())
        {
            var row = Row(index);
            bool lit = index++ == hover;
            if (lit)
                batch.Draw(pixel, ToScreen(row), Gold * 0.18f);
            string name = mission is { } m ? $"{m + 1}.  {_names[m]}" : Lang.T(video == Video.Intro ? "campaign.introVideo" : "campaign.outroVideo");
            Write(batch, font, name, row.X + 8, row.Y + 7, lit ? Gold : Text, 14);
            string state = Lang.T(mission is null ? "campaign.video" : mission < _accomplished ? "campaign.missionDone" : "campaign.missionNew");
            float width = font.Measure(state, Px(12)).X / _scale;
            Write(batch, font, state, row.Right - 8 - width, row.Y + 9, mission is null || mission < _accomplished ? Dim : Gold, 12);
        }
        TextButton.Draw(batch, pixel, font, ToScreen(BackButton), Lang.T("campaign.back"), Mouse.GetState().Position.ToVector2(), size: Px(13));
    }

    private void Write(SpriteBatch batch, UiFont font, string text, float x, float y, Color color, int size)
    {
        var at = _origin + new Vector2(x, y) * _scale;
        at = new Vector2(MathF.Round(at.X), MathF.Round(at.Y));
        font.Draw(batch, text, at + new Vector2(1, 1), Color.Black * 0.8f, Px(size));
        font.Draw(batch, text, at, color, Px(size));
    }

    private void WriteCentered(SpriteBatch batch, UiFont font, string text, Rectangle box, Color color, int size)
    {
        var measured = font.Measure(text, Px(size)) / _scale;
        Write(batch, font, text, box.Center.X - measured.X / 2, box.Center.Y - measured.Y / 2, color, size);
    }

    public void Dispose()
    {
        _background.Dispose();
        _marble.Dispose();
    }
}
