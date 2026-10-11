using Homestead.Assets;
using Homestead.Assets.Gfx;
using Homestead.Assets.Maps;
using Homestead.Client.MapObjects;
using Homestead.Client.Terrain;
using Homestead.Simulation;
using Homestead.Simulation.Map;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Homestead.Client;

/// <summary>
/// Shows an original map's terrain with a camera, and a settler walking near the first player's start.
/// </summary>
/// <remarks>
/// Keys: arrows/WASD scroll · right mouse drag pan · wheel zoom · PgUp/PgDn map · H walker to cursor area ·
/// O objects on/off · B blocked fields · Esc quit
/// </remarks>
public sealed class MapGame : Game
{
    private const int SettlerFile = 10;
    private const int HexagonSide = 4;
    private const int StepTicks = 8;

    private readonly GraphicsDeviceManager _graphics;
    private readonly GameInstallation _installation;
    private readonly GfxDirectory _gfx;
    private readonly string[] _mapFiles;
    private readonly string? _captureDirectory;

    private int _mapIndex;
    private OriginalMap _map = null!;
    private TerrainMesh _terrain = null!;
    private MapObjectLayer _objects = null!;
    private GameMap _gameMap = null!;
    private Texture2D _pixel = null!;
    private bool _showBlocked;
    private bool _showObjects = true;
    private TimeSpan _time;
    private Texture2D _atlas = null!;
    private BasicEffect _effect = null!;
    private SpriteBatch _spriteBatch = null!;
    private SettlerSprites _sprites = null!;

    private World _world = new();
    private Walker _walker = null!;
    private TimeSpan _accumulator;

    private Vector2 _camera; // world position shown at the screen center
    private float _zoom = 2;
    private KeyboardState _previousKeys;
    private MouseState _previousMouse;
    private int _capturedFrames;

    public MapGame(GameInstallation installation, GfxDirectory gfx, string? mapName, string? captureDirectory)
    {
        _installation = installation;
        _gfx = gfx;
        _captureDirectory = captureDirectory;
        _mapFiles = installation.MapFiles.ToArray();
        _mapIndex = Math.Max(0, Array.FindIndex(_mapFiles, f =>
            Path.GetFileNameWithoutExtension(f).Equals(mapName ?? "Italy", StringComparison.OrdinalIgnoreCase)));

        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1280,
            PreferredBackBufferHeight = 800,
        };
        IsMouseVisible = true;
        Window.AllowUserResizing = true;
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _pixel = new Texture2D(GraphicsDevice, 1, 1);
        _pixel.SetData([Color.White]);
        _atlas = TerrainAtlas.Build(GraphicsDevice, _gfx.Open(0));
        _effect = new BasicEffect(GraphicsDevice)
        {
            TextureEnabled = true,
            Texture = _atlas,
            VertexColorEnabled = true,
            LightingEnabled = false,
        };
        _sprites = new SettlerSprites(GraphicsDevice, _gfx.Open(SettlerFile), 0);
        LoadMap(_mapIndex);

        if (_captureDirectory is not null)
        {
            using var stream = File.Create(Path.Combine(_captureDirectory, "atlas.png"));
            _atlas.SaveAsPng(stream, _atlas.Width, _atlas.Height);
        }
    }

    private void LoadMap(int index)
    {
        _mapIndex = (index % _mapFiles.Length + _mapFiles.Length) % _mapFiles.Length;
        _terrain?.Dispose();
        _objects?.Dispose();
        _map = OriginalMap.Open(_mapFiles[_mapIndex]);
        _terrain = new TerrainMesh(GraphicsDevice, _map.Area);
        _gameMap = GameMap.FromOriginal(_map.Area);
        _objects = new MapObjectLayer(GraphicsDevice, _gfx.Open(MapObjectCatalog.GfxFile), _map.Area, _terrain);

        var start = _map.Players.Count > 0
            ? new GridPoint(_map.Players[0].StartX, _map.Players[0].StartY)
            : new GridPoint(_map.Area.Size / 2, _map.Area.Size / 2);
        PlaceWalker(start);
        _camera = ToWorld(start);
    }

    /// <summary>The walker's hexagon is centered on <paramref name="center"/>.</summary>
    private void PlaceWalker(GridPoint center)
    {
        _world = new World();
        _walker = new Walker(new GridPoint(center.X - HexagonSide, center.Y - HexagonSide),
            Walker.HexagonRoute(HexagonSide), StepTicks);
        _world.Walkers.Add(_walker);
    }

    private Vector2 ToWorld(GridPoint p) => ScreenProjection.ToWorld(p.X, p.Y, _terrain.HeightAt(p.X, p.Y));

    protected override void Update(GameTime gameTime)
    {
        HandleInput(gameTime);

        var elapsed = _captureDirectory is null ? gameTime.ElapsedGameTime : TimeSpan.FromSeconds(1 / 60.0);
        _accumulator += elapsed;
        _time += elapsed;
        while (_accumulator >= World.TickDuration)
        {
            _world.Step();
            _accumulator -= World.TickDuration;
        }

        var cursor = ScreenToGrid(Mouse.GetState().Position.ToVector2());
        Window.Title = $"{_map.Name} ({_map.Area.Size}) · zoom {_zoom}x · cursor {cursor.X},{cursor.Y} " +
                       $"{_terrain.KindAt(cursor.X, cursor.Y)} h={_terrain.HeightAt(cursor.X, cursor.Y)} " +
                       $"object={_map.Area.Object[_map.Area.Index(cursor.X, cursor.Y)]}";
        base.Update(gameTime);
    }

    private void HandleInput(GameTime gameTime)
    {
        var keys = Keyboard.GetState();
        var mouse = Mouse.GetState();
        bool Pressed(Keys k) => keys.IsKeyDown(k) && !_previousKeys.IsKeyDown(k);

        if (Pressed(Keys.Escape))
            Exit();
        if (Pressed(Keys.PageDown))
            LoadMap(_mapIndex + 1);
        if (Pressed(Keys.PageUp))
            LoadMap(_mapIndex - 1);
        if (Pressed(Keys.B))
            _showBlocked = !_showBlocked;
        if (Pressed(Keys.O))
            _showObjects = !_showObjects;
        if (Pressed(Keys.H))
            PlaceWalker(ScreenToGrid(mouse.Position.ToVector2()));

        float speed = 800 / _zoom * (float)gameTime.ElapsedGameTime.TotalSeconds;
        if (keys.IsKeyDown(Keys.Left) || keys.IsKeyDown(Keys.A))
            _camera.X -= speed;
        if (keys.IsKeyDown(Keys.Right) || keys.IsKeyDown(Keys.D))
            _camera.X += speed;
        if (keys.IsKeyDown(Keys.Up) || keys.IsKeyDown(Keys.W))
            _camera.Y -= speed;
        if (keys.IsKeyDown(Keys.Down) || keys.IsKeyDown(Keys.S))
            _camera.Y += speed;

        if (mouse.RightButton == ButtonState.Pressed && _previousMouse.RightButton == ButtonState.Pressed)
            _camera -= (mouse.Position - _previousMouse.Position).ToVector2() / _zoom;

        int wheel = mouse.ScrollWheelValue - _previousMouse.ScrollWheelValue;
        if (wheel != 0)
            _zoom = Math.Clamp(_zoom * (wheel > 0 ? 1.25f : 0.8f), 0.25f, 6f);

        _previousKeys = keys;
        _previousMouse = mouse;
    }

    private Matrix View
    {
        get
        {
            var viewport = GraphicsDevice.Viewport;
            return Matrix.CreateTranslation(-_camera.X, -_camera.Y, 0)
                   * Matrix.CreateScale(_zoom)
                   * Matrix.CreateTranslation(MathF.Round(viewport.Width / 2f), MathF.Round(viewport.Height / 2f), 0);
        }
    }

    /// <summary>Screen position to the nearest grid point, ignoring terrain height.</summary>
    private GridPoint ScreenToGrid(Vector2 screen)
    {
        var world = Vector2.Transform(screen, Matrix.Invert(View));
        int y = (int)MathF.Round(world.Y / ScreenProjection.RowHeight);
        int x = (int)MathF.Round((world.X + y * ScreenProjection.TileWidth / 2f) / ScreenProjection.TileWidth);
        return new GridPoint(Math.Clamp(x, 0, _map.Area.Size - 1), Math.Clamp(y, 0, _map.Area.Size - 1));
    }

    protected override void Draw(GameTime gameTime)
    {
        RenderTarget2D? target = null;
        if (_captureDirectory is not null)
        {
            target = new RenderTarget2D(GraphicsDevice, 960, 600);
            GraphicsDevice.SetRenderTarget(target);
        }

        GraphicsDevice.Clear(Color.Black);
        var viewport = GraphicsDevice.Viewport;

        _effect.World = Matrix.Identity;
        _effect.View = View;
        _effect.Projection = Matrix.CreateOrthographicOffCenter(0, viewport.Width, viewport.Height, 0, -1, 1);
        GraphicsDevice.RasterizerState = RasterizerState.CullNone;
        // Point sampling: linear filtering would bleed neighboring atlas cells into the texture edges.
        GraphicsDevice.SamplerStates[0] = SamplerState.PointClamp;
        GraphicsDevice.BlendState = BlendState.Opaque;
        GraphicsDevice.DepthStencilState = DepthStencilState.None;

        var visible = new RectangleF(
            _camera.X - viewport.Width / 2f / _zoom, _camera.Y - viewport.Height / 2f / _zoom,
            viewport.Width / _zoom, viewport.Height / _zoom);
        _terrain.Draw(_effect, visible);

        // Walker: interpolate position and terrain height between the two grid points.
        float alpha = (float)(_accumulator / World.TickDuration);
        float progress = (_walker.TicksIntoStep + alpha) / _walker.StepTicks;
        var anchor = Vector2.Lerp(ToWorld(_walker.Position), ToWorld(_walker.Target), progress);

        // Back to front by grid row; the walker belongs to the row it is leaving or entering, whichever is lower.
        int walkerRow = Math.Max(_walker.Position.Y, _walker.Target.Y);
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: View);
        int rows = _objects.RowCount(visible, out int firstRow);
        bool walkerDrawn = false;
        for (int y = firstRow; y < firstRow + rows; y++)
        {
            if (_showObjects)
                _objects.DrawRow(_spriteBatch, y, visible, _time);
            if (y == walkerRow)
            {
                _sprites.Draw(_spriteBatch, SettlerSprites.WalkFrame(_walker.Facing, progress), anchor, 1, Color.Red);
                walkerDrawn = true;
            }
        }
        if (!walkerDrawn)
            _sprites.Draw(_spriteBatch, SettlerSprites.WalkFrame(_walker.Facing, progress), anchor, 1, Color.Red);
        // Debug overlay on top of everything, so markers are not hidden behind trees.
        if (_showBlocked)
            for (int y = firstRow; y < firstRow + rows; y++)
                DrawBlockedRow(y, visible);
        _spriteBatch.End();

        if (target is not null)
            Capture(target);

        base.Draw(gameTime);
    }

    /// <summary>Debug overlay: a red marker on every point settlers cannot walk on.</summary>
    private void DrawBlockedRow(int y, RectangleF visible)
    {
        int firstX = Math.Max(0, (int)((visible.X + y * ScreenProjection.TileWidth / 2f) / ScreenProjection.TileWidth) - 1);
        int lastX = Math.Min(_gameMap.Size - 1,
            (int)((visible.X + visible.Width + y * ScreenProjection.TileWidth / 2f) / ScreenProjection.TileWidth) + 1);
        for (int x = firstX; x <= lastX; x++)
        {
            var p = new GridPoint(x, y);
            if (_gameMap.IsWalkable(p))
                continue;
            var position = ToWorld(p);
            _spriteBatch.Draw(_pixel, new Rectangle((int)position.X - 2, (int)position.Y - 2, 4, 4), Color.Red * 0.7f);
        }
    }

    /// <summary>Headless check: a few views at different zoom levels and maps, then quit.</summary>
    private void Capture(RenderTarget2D target)
    {
        GraphicsDevice.SetRenderTarget(null);
        if (_capturedFrames is 2 or 4 or 6 or 8 or 10 or 12)
        {
            using var stream = File.Create(Path.Combine(_captureDirectory!, $"{_map.Name}_z{_zoom}_{_capturedFrames:D2}.png"));
            target.SaveAsPng(stream, target.Width, target.Height);
        }
        target.Dispose();

        // Script: overview, then close-ups around the walker, then the next map.
        switch (++_capturedFrames)
        {
            case 1: _zoom = 0.5f; break;
            case 3: _zoom = 1; break;
            case 5: _zoom = 2; break;
            case 7: _zoom = 4; break;
            case 9: _zoom = 1; _camera += new Vector2(300, 150); _showBlocked = true; break;
            case 11: LoadMap(_mapIndex + 7); _zoom = 1; break;
            case 13: Exit(); break;
        }
    }

    protected override void UnloadContent()
    {
        _terrain.Dispose();
        _objects.Dispose();
        _sprites.Dispose();
        _atlas.Dispose();
        _effect.Dispose();
        base.UnloadContent();
    }
}
