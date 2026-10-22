using System.Text.Json;
using Homestead.Assets.Gfx;
using Homestead.Simulation.Buildings;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Homestead.Client;

/// <summary>
/// The original's loading screen (the three helmets) shown while a game is loaded, so the window does not hang on
/// the menu: per campaign its own colour (<c>loadingScreen</c> in <c>data/ui.json</c>; free games gold). Scaled to the
/// window's height, its outermost columns stretched to the sides. It stays at least <see cref="MinSeconds"/>, then fades
/// out over the running game in <see cref="FadeSeconds"/>. Timed in real time: the loading itself stalls the game's clock.
/// Once loaded, the game is already drawn beneath it (still covered) for <see cref="WarmFrames"/> frames: the first
/// frames of the first game fill sprite atlases and compile code, which would stutter the fade.
/// </summary>
public sealed class LoadingScreen : IDisposable
{
    public const double MinSeconds = 1, FadeSeconds = 0.3;
    public const int WarmFrames = 3;

    private readonly Texture2D _image;
    private readonly System.Diagnostics.Stopwatch _clock = new();
    private double _loadedAt;
    private int _framesOverGame;
    /// <summary>When the first fading frame was drawn: the fade counts from there, not from a slow first update.</summary>
    private double? _fadeStart;

    /// <summary>Whether it has been drawn; the game loads only then.</summary>
    public bool Shown => _clock.IsRunning;

    public bool Loaded { get; private set; }

    /// <summary>Loaded, shown long enough and the game drawn beneath it a few times: the game runs, the screen fades.</summary>
    public bool Fading => Loaded && _framesOverGame >= WarmFrames && _clock.Elapsed.TotalSeconds >= Math.Max(MinSeconds, _loadedAt);

    /// <summary>Faded out completely.</summary>
    public bool Gone => Opacity <= 0;

    private float Opacity => _fadeStart is { } start
        ? (float)Math.Clamp(1 - (_clock.Elapsed.TotalSeconds - start) / FadeSeconds, 0, 1)
        : 1;

    /// <summary>The game is loaded (from now on the fade may begin).</summary>
    public void MarkLoaded()
    {
        Loaded = true;
        _loadedAt = _clock.Elapsed.TotalSeconds;
    }

    /// <param name="campaign">The campaign of the game (<c>settlers</c>, <c>missionCd</c>, <c>amazons</c>), null for a free game.</param>
    /// <param name="theme">Other than normal, the theme's picture whatever the campaign.</param>
    public LoadingScreen(GraphicsDevice device, GfxDirectory gfx, string? campaign, Theme theme)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(BuildingCatalog.DefaultDataDirectory, "ui.json")));
        var look = document.RootElement.GetProperty("loadingScreen");
        campaign = theme switch
        {
            Theme.Gold => null,
            Theme.Classic => "settlers",
            Theme.Amazons => "amazons",
            _ => campaign,
        };
        if (campaign is null || !look.TryGetProperty(campaign, out var image))
            image = look.GetProperty("game");
        var bitmap = gfx.Open(image[0].GetInt32()).ReadGui(image[1].GetInt32());
        _image = new Texture2D(device, bitmap.Width, bitmap.Height);
        _image.SetData(bitmap.Pixels);
    }

    /// <param name="overGame">Drawn over the loaded game (covering it until <see cref="Fading"/>).</param>
    public void Draw(SpriteBatch batch, Rectangle view, bool overGame = false)
    {
        _clock.Start();
        if (overGame && !Fading)
            _framesOverGame++;
        else if (overGame && _fadeStart is null)
            _fadeStart = _clock.Elapsed.TotalSeconds;
        var color = Color.White * Opacity;
        float scale = Math.Min((float)view.Width / _image.Width, (float)view.Height / _image.Height);
        int width = (int)(_image.Width * scale), height = (int)(_image.Height * scale);
        var picture = new Rectangle(view.X + (view.Width - width) / 2, view.Y + (view.Height - height) / 2, width, height);
        // Black where the picture does not reach (a window narrower than 4:3), so the game does not show there either.
        batch.Draw(_image, view, Color.Black * Opacity);
        batch.Draw(_image, new Rectangle(view.X, picture.Y, picture.X - view.X, height), new Rectangle(0, 0, 1, _image.Height), color);
        batch.Draw(_image, new Rectangle(picture.Right, picture.Y, view.Right - picture.Right, height),
            new Rectangle(_image.Width - 1, 0, 1, _image.Height), color);
        batch.Draw(_image, picture, color);
    }

    public void Dispose() => _image.Dispose();
}
