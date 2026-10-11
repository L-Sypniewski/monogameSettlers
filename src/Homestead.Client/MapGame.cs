using Homestead.Assets;
using Homestead.Assets.Gfx;
using Homestead.Assets.Maps;
using Homestead.Client.MapObjects;
using Homestead.Client.Terrain;
using Homestead.Simulation;
using Homestead.Simulation.Buildings;
using Homestead.Simulation.Map;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Homestead.Client;

/// <summary>
/// An original map with terrain, objects and settlers that walk where they are sent.
/// </summary>
/// <remarks>
/// Mouse: left click on a settler selects it, left click elsewhere sends the selected settler there ·
/// right drag pan · wheel zoom.
/// Keys: arrows/WASD scroll · N new carrier at cursor · M 50 wandering settlers ·
/// G demo economy at cursor · P placement mode (Q/E building type, click places a construction site) ·
/// PgUp/PgDn map ·
/// O objects · B blocked fields · Esc quit
/// </remarks>
public sealed class MapGame : Game
{
    private const int WanderRadius = 15;
    private const int WandererBatch = 50;

    private static readonly Color PlayerColor = Color.Red;

    private readonly GraphicsDeviceManager _graphics;
    private readonly GfxDirectory _gfx;
    private readonly string[] _mapFiles;
    private readonly string? _captureDirectory;
    private readonly Random _random = new(1);
    private readonly BuildingCatalog _romans = BuildingCatalog.Load(Civilization.Roman);
    private BuildingDefinition[] _placeable = [];
    private int _placeIndex;
    private bool _placing;

    private int _mapIndex;
    private OriginalMap _map = null!;
    private TerrainMesh _terrain = null!;
    private MapObjectLayer _objects = null!;
    private GameMap _gameMap = null!;
    private Texture2D _atlas = null!;
    private BasicEffect _effect = null!;
    private SpriteBatch _spriteBatch = null!;
    private Texture2D _pixel = null!;
    private SettlerRenderer _settlerRenderer = null!;
    private EconomyRenderer _economyRenderer = null!;

    private World _world = null!;
    private readonly HashSet<int> _wanderers = new();
    private Settler? _selected;
    private string _message = "";
    private TimeSpan _accumulator;
    private TimeSpan _time;

    private bool _showBlocked;
    private bool _showObjects = true;
    private Vector2 _camera; // world position shown at the screen center
    private float _zoom = 2;
    private KeyboardState _previousKeys;
    private MouseState _previousMouse;
    private int _capturedFrames;

    public MapGame(GameInstallation installation, GfxDirectory gfx, string? mapName, string? captureDirectory)
    {
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
        if (captureDirectory is not null)
        {
            // Offscreen runs must not wait for the screen: a covered window gets throttled to
            // about one frame per second by the window system.
            _graphics.SynchronizeWithVerticalRetrace = false;
            IsFixedTimeStep = false;
        }
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
        _settlerRenderer = new SettlerRenderer(GraphicsDevice, _gfx);
        _placeable = _romans.All.Where(b => b.ConstructionPiles.Count > 0 && b.ConstructionImage is not null)
            .OrderBy(b => b.Name).ToArray();
        _placeIndex = Math.Max(0, Array.FindIndex(_placeable, b => b.Name == "lumberjack"));
        _economyRenderer = new EconomyRenderer(GraphicsDevice, _gfx);
        LoadMap(_mapIndex);
    }

    private void LoadMap(int index)
    {
        _mapIndex = (index % _mapFiles.Length + _mapFiles.Length) % _mapFiles.Length;
        _terrain?.Dispose();
        _objects?.Dispose();
        _map = OriginalMap.Open(_mapFiles[_mapIndex]);
        _terrain = new TerrainMesh(GraphicsDevice, _map.Area);
        (_world, var report) = OriginalScenario.Load(_map);
        _gameMap = _world.Map!;
        _gameMap.PointChanged += OnPointChanged;
        _objects = new MapObjectLayer(GraphicsDevice, _gfx.Open(MapObjectCatalog.GfxFile), _map.Area, _terrain);
        _message = $"{report.Buildings} buildings, {report.Piles} piles, {report.Settlers} settlers from the map";
        _wanderers.Clear();
        var start = _map.Players.Count > 0
            ? new GridPoint(_map.Players[0].StartX, _map.Players[0].StartY)
            : new GridPoint(_map.Area.Size / 2, _map.Area.Size / 2);
        _selected = _world.Spawn(start, Profession.Carrier);
        _camera = ToWorld(start);
    }

    /// <summary>Diggers, woodcutters, stonecutters and foresters change the map: keep terrain and objects in sync.</summary>
    private void OnPointChanged(GridPoint p)
    {
        _terrain.Update(p.X, p.Y, _gameMap.HeightAt(p),
            _gameMap.TerrainAt(p) == TerrainType.Flattened ? TerrainKind.Flattened : _terrain.KindAt(p.X, p.Y));
        _objects?.SetCode(p.X, p.Y, _gameMap.ObjectCodeAt(p));
    }

    private Vector2 ToWorld(GridPoint p) => ScreenProjection.ToWorld(p.X, p.Y, _terrain.HeightAt(p.X, p.Y));

    protected override void Update(GameTime gameTime)
    {
        HandleInput(gameTime);

        // Capture runs use fixed frame times at 4x speed, so long processes fit into a short run.
        var elapsed = _captureDirectory is null ? gameTime.ElapsedGameTime : TimeSpan.FromSeconds(4 / 60.0);
        _accumulator += elapsed;
        _time += elapsed;
        while (_accumulator >= World.TickDuration)
        {
            GiveWanderersNewDestinations();
            _world.Step();
            _accumulator -= World.TickDuration;
        }

        var cursor = ScreenToGrid(Mouse.GetState().Position.ToVector2());
        var logistics = _world.Logistics;
        if (_placing)
        {
            var definition = _placeable[_placeIndex];
            _world.CanPlace(definition, cursor, out string reason);
            _message = $"placing {definition.Name} (Q/E change, Esc stop){(reason.Length > 0 ? ": " + reason : "")}";
        }
        var production = _world.Production;
        Window.Title = $"{_map.Name} · {_world.Settlers.Count} settlers · {logistics.Jobs.Count} transports, " +
                       $"produced {production.Produced}, trees {production.TreesCut}/{production.TreesPlanted}, stones {production.StonesCut} · " +
                       $"{logistics.Delivered} delivered, {logistics.DroppedOnTheWay} dropped · " +
                       $"pass-throughs {_world.PassThroughs} · cursor {cursor.X},{cursor.Y} " +
                       $"{_gameMap.TerrainAt(cursor)}{(_gameMap.IsWalkable(cursor) ? "" : " (blocked)")}" +
                       (_message.Length > 0 ? $" · {_message}" : "");
        base.Update(gameTime);
    }

    private void GiveWanderersNewDestinations()
    {
        foreach (int id in _wanderers)
        {
            var settler = _world.Settlers[id];
            if (settler.Destination is not null || settler.State == SettlerState.Stepping)
                continue;
            for (int attempt = 0; attempt < 10; attempt++)
            {
                var target = new GridPoint(
                    settler.Position.X + _random.Next(-WanderRadius, WanderRadius + 1),
                    settler.Position.Y + _random.Next(-WanderRadius, WanderRadius + 1));
                if (_gameMap.IsWalkable(target) && _world.WalkTo(settler, target))
                    break;
            }
        }
    }

    private void HandleInput(GameTime gameTime)
    {
        var keys = Keyboard.GetState();
        var mouse = Mouse.GetState();
        bool Pressed(Keys k) => keys.IsKeyDown(k) && !_previousKeys.IsKeyDown(k);
        var cursorScreen = mouse.Position.ToVector2();

        if (Pressed(Keys.Escape))
        {
            if (_placing)
                _placing = false;
            else
                Exit();
        }
        if (Pressed(Keys.P))
            _placing = !_placing;
        if (_placing && Pressed(Keys.E))
            _placeIndex = (_placeIndex + 1) % _placeable.Length;
        if (_placing && Pressed(Keys.Q))
            _placeIndex = (_placeIndex - 1 + _placeable.Length) % _placeable.Length;
        if (Pressed(Keys.PageDown))
            LoadMap(_mapIndex + 1);
        if (Pressed(Keys.PageUp))
            LoadMap(_mapIndex - 1);
        if (Pressed(Keys.B))
            _showBlocked = !_showBlocked;
        if (Pressed(Keys.O))
            _showObjects = !_showObjects;
        if (Pressed(Keys.N) && _world.Spawn(ScreenToGrid(cursorScreen), Profession.Carrier) is { } spawned)
            _selected = spawned;
        if (Pressed(Keys.G))
            _message = DemoEconomy.Create(_world, _romans, ScreenToGrid(cursorScreen), _random);
        if (Pressed(Keys.M))
            SpawnWanderers(ScreenToGrid(cursorScreen));

        if (mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released
            && IsActive && GraphicsDevice.Viewport.Bounds.Contains(mouse.Position))
            Click(cursorScreen);

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

    /// <summary>
    /// Placement mode: place a construction site. Otherwise select the settler under the cursor, or
    /// send the selected one to the clicked point.
    /// </summary>
    private void Click(Vector2 screen)
    {
        if (_placing)
        {
            var definition = _placeable[_placeIndex];
            var at = ScreenToGrid(screen);
            _message = _world.CanPlace(definition, at, out string reason)
                ? _world.PlaceConstructionSite(definition, 0, at) is not null ? $"{definition.Name} site placed" : "could not place"
                : $"{definition.Name}: {reason}";
            return;
        }

        float alpha = TickAlpha;
        var world = Vector2.Transform(screen, Matrix.Invert(View));
        // Settler sprites stand above their anchor; compare with the body center.
        var hit = _world.Settlers.Where(s => !s.IsInside)
            .Select(s => (Settler: s, Distance: Vector2.Distance(world, SettlerRenderer.Anchor(s, _terrain, alpha) - new Vector2(0, 10))))
            .Where(t => t.Distance < 10)
            .OrderBy(t => t.Distance)
            .Select(t => t.Settler)
            .FirstOrDefault();
        if (hit is not null)
        {
            _selected = hit;
            _wanderers.Remove(hit.Id);
            _message = $"settler {hit.Id} selected";
            return;
        }

        if (_selected is null)
            return;
        var target = ScreenToGrid(screen);
        _wanderers.Remove(_selected.Id);
        _message = _world.WalkTo(_selected, target) ? $"walking to {target.X},{target.Y}" : "unreachable";
    }

    private void SpawnWanderers(GridPoint near)
    {
        for (int i = 0; i < WandererBatch; i++)
        {
            var at = new GridPoint(near.X + _random.Next(-8, 9), near.Y + _random.Next(-8, 9));
            if (_world.Spawn(at) is { } settler)
                _wanderers.Add(settler.Id);
        }
    }

    private float TickAlpha => (float)(_accumulator / World.TickDuration);

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

        _spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: View);
        if (_selected is not null)
            DrawPath(_selected);

        // Back to front by grid row, objects and settlers interleaved.
        float alpha = TickAlpha;
        var settlersByRow = _world.Settlers.Where(s => !s.IsInside).ToLookup(SettlerRenderer.DepthRow);
        var buildingsByRow = _world.Buildings.Where(b => b.Exists).ToLookup(EconomyRenderer.DepthRow);
        var pilesByRow = _world.Piles.ToLookup(p => p.Position.Y);
        int rows = _objects.RowCount(visible, out int firstRow);
        for (int y = firstRow; y < firstRow + rows; y++)
        {
            foreach (var pile in pilesByRow[y])
                _economyRenderer.DrawPile(_spriteBatch, pile, _terrain);
            if (_showObjects)
                _objects.DrawRow(_spriteBatch, y, visible, _time);
            foreach (var building in buildingsByRow[y])
                _economyRenderer.DrawBuilding(_spriteBatch, building, _terrain, PlayerColor);
            foreach (var settler in settlersByRow[y])
                _settlerRenderer.Draw(_spriteBatch, settler, _terrain, alpha, PlayerColor);
        }

        if (_selected is not null)
            DrawSelectionMarker(_selected, alpha);
        if (_placing)
        {
            var at = ScreenToGrid(Mouse.GetState().Position.ToVector2());
            var definition = _placeable[_placeIndex];
            _economyRenderer.DrawPreview(_spriteBatch, definition, at, _terrain, _world.CanPlace(definition, at, out _));
        }
        // Debug overlay on top of everything, so markers are not hidden behind trees.
        if (_showBlocked)
            for (int y = firstRow; y < firstRow + rows; y++)
                DrawBlockedRow(y, visible);
        _spriteBatch.End();

        if (target is not null)
            Capture(target);

        base.Draw(gameTime);
    }

    /// <summary>Dots along the remaining path of a settler.</summary>
    private void DrawPath(Settler settler)
    {
        var p = settler.StepTarget;
        foreach (var step in settler.RemainingPath)
        {
            p = p.Neighbor(step);
            var position = ToWorld(p);
            _spriteBatch.Draw(_pixel, new Rectangle((int)position.X - 1, (int)position.Y - 1, 3, 3), Color.Yellow * 0.8f);
        }
    }

    private void DrawSelectionMarker(Settler settler, float alpha)
    {
        var anchor = SettlerRenderer.Anchor(settler, _terrain, alpha);
        _spriteBatch.Draw(_pixel, new Rectangle((int)anchor.X - 1, (int)anchor.Y - 32, 3, 3), Color.Yellow);
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

    /// <summary>
    /// Headless check: set up the demo economy and three construction sites at the first player's
    /// start, run two minutes of game time, save a frame every eight game seconds, then quit.
    /// </summary>
    private void Capture(RenderTarget2D target)
    {
        GraphicsDevice.SetRenderTarget(null);
        if (_capturedFrames % 120 == 119)
        {
            using var stream = File.Create(Path.Combine(_captureDirectory!, $"{_map.Name}_{_capturedFrames:D4}.png"));
            target.SaveAsPng(stream, target.Width, target.Height);
        }
        target.Dispose();

        switch (++_capturedFrames)
        {
            case 1:
                _zoom = 1;
                var start = _selected!.Position;
                DemoEconomy.Create(_world, _romans, start, _random);
                foreach (var (name, dx, dy) in new[] { ("lumberjack", -14, 10), ("forester", 8, 16), ("stonecutter", -4, 22) })
                    for (int r = 0; r < 12; r++)
                        if (_world.PlaceConstructionSite(_romans[name], 0, new GridPoint(start.X + dx + r, start.Y + dy)) is not null)
                            break;
                break;
            case 1800:
                Exit();
                break;
        }
    }

    protected override void UnloadContent()
    {
        _terrain.Dispose();
        _objects.Dispose();
        _settlerRenderer.Dispose();
        _economyRenderer.Dispose();
        _atlas.Dispose();
        _effect.Dispose();
        base.UnloadContent();
    }
}
