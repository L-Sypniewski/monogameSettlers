using Homestead.Assets.Gfx;
using Homestead.Simulation;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Homestead.Client;

/// <summary>
/// Proof of concept: a settler walks a hexagon on the grid, turning through all six directions.
/// The simulation runs in fixed ticks; rendering interpolates between them.
/// </summary>
/// <remarks>
/// Keys: Left/Right carried good (sequence) · +/- zoom · P player color · G grid · Space pause · Esc quit
/// </remarks>
public sealed class WalkPocGame : Game
{
    private const int SettlerFile = 10;  // Roman settlers
    private const int LandscapeFile = 0;
    private const int GrassTexture = 0;
    private const int HexagonSide = 5;
    private const int StepTicks = 8;     // 0.4 s per grid step at 20 ticks/s

    private static readonly Color[] PlayerColors =
        [Color.Red, Color.RoyalBlue, Color.LimeGreen, Color.Yellow, Color.Magenta, Color.White];

    private readonly GraphicsDeviceManager _graphics;
    private readonly GfxDirectory _gfx;
    private readonly string? _captureDirectory;
    private readonly World _world = new();
    private readonly Walker _walker;
    private readonly GridPoint _center;

    private SpriteBatch _spriteBatch = null!;
    private Texture2D _pixel = null!;
    private Texture2D? _grass;
    private SettlerSprites _sprites = null!;
    private RenderTarget2D? _captureTarget;
    private KeyboardState _previousKeys;

    private TimeSpan _accumulator;
    private int _sequence;
    private float _zoom = 2;
    private int _playerColor;
    private bool _showGrid = true;
    private bool _paused;
    private int _capturedFrames;

    public WalkPocGame(GfxDirectory gfx, string? captureDirectory)
    {
        _gfx = gfx;
        _captureDirectory = captureDirectory;
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1280,
            PreferredBackBufferHeight = 800,
            SynchronizeWithVerticalRetrace = true,
        };
        IsFixedTimeStep = false;
        IsMouseVisible = true;
        Window.AllowUserResizing = true;

        // Start at the hexagon's north-west corner, so the center is HexagonSide steps SE of it.
        var start = new GridPoint(10, 10);
        _center = new GridPoint(start.X + HexagonSide, start.Y + HexagonSide);
        _walker = new Walker(start, Walker.HexagonRoute(HexagonSide), StepTicks);
        _world.Walkers.Add(_walker);
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _pixel = new Texture2D(GraphicsDevice, 1, 1);
        _pixel.SetData([Color.White]);

        var landscape = _gfx.Open(LandscapeFile).ReadLandscape(GrassTexture);
        _grass = new Texture2D(GraphicsDevice, landscape.Width, landscape.Height);
        _grass.SetData(landscape.Pixels);

        _sequence = -1;
        SelectSequence(1);

        if (_captureDirectory is not null)
        {
            Directory.CreateDirectory(_captureDirectory);
            _captureTarget = new RenderTarget2D(GraphicsDevice, 640, 400);
        }
    }

    /// <summary>Switches to the next sequence in <paramref name="step"/> direction that has the walk layout.</summary>
    private void SelectSequence(int step)
    {
        var file = _gfx.Open(SettlerFile);
        int count = file.SettlerCount;
        for (int i = 1; i <= count; i++)
        {
            int candidate = ((_sequence + i * step) % count + count) % count;
            if (file.GetSettlerFrameCount(candidate) == DirectionExtensions.Count * SettlerSprites.FramesPerDirection)
            {
                _sequence = candidate;
                break;
            }
        }
        _sprites?.Dispose();
        _sprites = new SettlerSprites(GraphicsDevice, file, _sequence);
    }

    protected override void Update(GameTime gameTime)
    {
        HandleInput();

        // Fixed-rate simulation, decoupled from the frame rate. A capture run uses exact
        // frame times so the saved images are reproducible.
        var elapsed = _captureDirectory is null ? gameTime.ElapsedGameTime : TimeSpan.FromSeconds(1 / 60.0);
        if (!_paused)
            _accumulator += elapsed;
        while (_accumulator >= World.TickDuration)
        {
            _world.Step();
            _accumulator -= World.TickDuration;
        }

        Window.Title = $"Walk PoC · sequence {SettlerFile}:{_sequence} · tick {_world.Tick} · " +
                       $"{_walker.Facing} · zoom {_zoom}x" + (_paused ? " · PAUSED" : "");
        base.Update(gameTime);
    }

    private void HandleInput()
    {
        var keys = Keyboard.GetState();
        bool Pressed(Keys k) => keys.IsKeyDown(k) && !_previousKeys.IsKeyDown(k);

        if (Pressed(Keys.Escape))
            Exit();
        if (Pressed(Keys.Right))
            SelectSequence(1);
        if (Pressed(Keys.Left))
            SelectSequence(-1);
        if (Pressed(Keys.OemPlus) || Pressed(Keys.Add))
            _zoom = Math.Min(6, _zoom + 1);
        if (Pressed(Keys.OemMinus) || Pressed(Keys.Subtract))
            _zoom = Math.Max(1, _zoom - 1);
        if (Pressed(Keys.P))
            _playerColor = (_playerColor + 1) % PlayerColors.Length;
        if (Pressed(Keys.G))
            _showGrid = !_showGrid;
        if (Pressed(Keys.Space))
            _paused = !_paused;
        _previousKeys = keys;
    }

    protected override void Draw(GameTime gameTime)
    {
        if (_captureTarget is not null)
            GraphicsDevice.SetRenderTarget(_captureTarget);

        var viewport = GraphicsDevice.Viewport;
        GraphicsDevice.Clear(Color.Black);

        // Camera: hexagon center in the middle of the screen.
        var screenCenter = new Vector2(viewport.Width / 2f, viewport.Height / 2f);
        Vector2 ToScreen(Vector2 world) => screenCenter + (world - ScreenProjection.ToWorld(_center)) * _zoom;

        _spriteBatch.Begin(samplerState: SamplerState.PointWrap);
        if (_grass is not null)
        {
            // Tile the grass texture over the whole view, anchored in world space.
            var origin = ToScreen(Vector2.Zero);
            var source = new Rectangle((int)(-origin.X / _zoom), (int)(-origin.Y / _zoom),
                (int)(viewport.Width / _zoom) + 1, (int)(viewport.Height / _zoom) + 1);
            _spriteBatch.Draw(_grass, Vector2.Zero, source, Color.White, 0, Vector2.Zero, _zoom, SpriteEffects.None, 0);
        }
        _spriteBatch.End();

        _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
        if (_showGrid)
            DrawGrid(ToScreen);

        // Interpolate between ticks so movement is smooth at any frame rate.
        float alpha = (float)(_accumulator / World.TickDuration);
        float progress = (_walker.TicksIntoStep + alpha) / _walker.StepTicks;
        var from = ScreenProjection.ToWorld(_walker.Position);
        var to = ScreenProjection.ToWorld(_walker.Target);
        var anchor = ToScreen(Vector2.Lerp(from, to, progress));
        _sprites.Draw(_spriteBatch, SettlerSprites.WalkFrame(_walker.Facing, progress), anchor, _zoom,
            PlayerColors[_playerColor]);
        _spriteBatch.End();

        if (_captureTarget is not null)
            Capture(anchor);

        base.Draw(gameTime);
    }

    private void DrawGrid(Func<Vector2, Vector2> toScreen)
    {
        for (int y = _center.Y - HexagonSide - 2; y <= _center.Y + HexagonSide + 2; y++)
        for (int x = _center.X - HexagonSide - 2; x <= _center.X + HexagonSide + 2; x++)
        {
            var p = toScreen(ScreenProjection.ToWorld(new GridPoint(x, y)));
            _spriteBatch.Draw(_pixel, new Rectangle((int)p.X - 1, (int)p.Y - 1, 3, 3), Color.Black * 0.35f);
        }
    }

    /// <summary>
    /// Headless check: save every 6th frame for one full lap, then quit. The file name carries the
    /// figure's screen position so a review script can crop around it.
    /// </summary>
    private void Capture(Vector2 anchor)
    {
        GraphicsDevice.SetRenderTarget(null);
        if (_capturedFrames % 6 == 0)
        {
            string name = $"frame_{_capturedFrames:D4}_{(int)anchor.X}_{(int)anchor.Y}.png";
            using var stream = File.Create(Path.Combine(_captureDirectory!, name));
            _captureTarget!.SaveAsPng(stream, _captureTarget.Width, _captureTarget.Height);
        }
        int lapFrames = Walker.HexagonRoute(HexagonSide).Count * StepTicks * 60 / World.TicksPerSecond;
        if (++_capturedFrames >= lapFrames)
            Exit();
    }

    protected override void UnloadContent()
    {
        _sprites.Dispose();
        _grass?.Dispose();
        _captureTarget?.Dispose();
        base.UnloadContent();
    }
}
