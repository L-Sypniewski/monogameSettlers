using Homestead.Assets;
using Homestead.Assets.Gfx;
using Homestead.Assets.Maps;
using Homestead.Client.MapObjects;
using Homestead.Client.Terrain;
using Homestead.Simulation;
using Homestead.Simulation.Ai;
using Homestead.Simulation.Buildings;
using Homestead.Simulation.Map;
using Homestead.Simulation.Pathfinding;
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
    private Building? _selectedBuilding;

    /// <summary>Start point of player 1 on the current map.</summary>
    private GridPoint _start;

    /// <summary>Soldiers selected with a selection box; a click then sends them (or attacks).</summary>
    private readonly List<Settler> _group = new();
    private Vector2? _dragStart;
    private Vector2? _rightStart;
    private const float DragThreshold = 6;
    private string _message = "";
    private IReadOnlyList<BuildingCatalog> _playerCatalogs = [];

    /// <summary>The AI playing for player 1 (key I), if on.</summary>
    private AiPlayer? _ownAi;
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
        // Default: the first multiplayer map (equal start equipment for every player).
        _mapIndex = mapName is null
            ? Math.Max(0, Array.FindIndex(_mapFiles, f => OriginalMap.Open(f) is { IsSinglePlayer: false, Buildings.Count: 0 }))
            : Math.Max(0, Array.FindIndex(_mapFiles, f => Path.GetFileNameWithoutExtension(f).Equals(mapName, StringComparison.OrdinalIgnoreCase)));

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
        // Every other player is a computer player.
        _playerCatalogs = report.Catalogs;
        _ownAi = null;
        for (int player = 1; player < report.Catalogs.Count; player++)
            _world.AiPlayers.Add(new AiPlayer(_world, report.Catalogs[player], player));
        _wanderers.Clear();
        _start = _map.Players.Count > 0
            ? new GridPoint(_map.Players[0].StartX, _map.Players[0].StartY)
            : new GridPoint(_map.Area.Size / 2, _map.Area.Size / 2);
        _selected = null;
        _group.Clear();
        _selectedBuilding = null;
        _camera = ToWorld(_start);
    }

    /// <summary>Diggers, woodcutters, stonecutters and foresters change the map: keep terrain and objects in sync.</summary>
    private void OnPointChanged(GridPoint p)
    {
        _terrain.Update(p.X, p.Y, _gameMap.HeightAt(p),
            _gameMap.TerrainAt(p) == TerrainType.Flattened ? TerrainKind.Flattened : _terrain.KindAt(p.X, p.Y));
        _objects?.SetCode(p.X, p.Y, _gameMap.ObjectCodeAt(p));
    }

    /// <summary>
    /// Work area of the selected building as in the original: posts on the borders of four circles at
    /// a quarter, half, three quarters and the full work radius around the work center.
    /// </summary>
    private IEnumerable<(GridPoint Point, int Ring)> WorkAreaMarks()
    {
        if (_selectedBuilding is not { Exists: true } building || building.Definition.WorkRadius is not { } radius || radius <= 0)
            yield break;
        var center = building.Position + (building.Definition.WorkCenter ?? new GridPoint(0, 0));
        for (int ring = 0; ring < 4; ring++)
        {
            int r = Math.Max(1, (int)Math.Round(radius * (ring + 1) / 4.0));
            // Border: inside the circle with a neighbor outside.
            foreach (var p in PathFinder.Circle(center, r))
                if (_gameMap.Contains(p) && Enumerable.Range(0, DirectionExtensions.Count)
                        .Any(d => !PathFinder.InCircle(center, p.Neighbor((Direction)d), r)))
                    yield return (p, ring);
        }
    }

    /// <summary>A small bar above a wounded soldier: green for the health left, red for the rest.</summary>
    private void DrawHealthBar(Settler soldier, SoldierType type, float alpha)
    {
        float left = Math.Clamp(soldier.Health / _world.Military.Config.MaxHealth(type, soldier.Level), 0, 1);
        var anchor = SettlerRenderer.Anchor(soldier, _terrain, alpha);
        var bar = new Rectangle((int)anchor.X - 8, (int)anchor.Y - 34, 16, 2);
        _spriteBatch.Draw(_pixel, bar, Color.DarkRed);
        _spriteBatch.Draw(_pixel, new Rectangle(bar.X, bar.Y, (int)(bar.Width * left), bar.Height), Color.LimeGreen);
    }

    /// <summary>Border posts of grid row <paramref name="y"/>: owned points next to land of another owner.</summary>
    private void DrawBorderRow(int y, RectangleF visible)
    {
        const float margin = 32;
        int firstX = Math.Max(0, (int)((visible.X - margin + y * ScreenProjection.TileWidth / 2f) / ScreenProjection.TileWidth));
        int lastX = Math.Min(_gameMap.Size - 1,
            (int)((visible.X + visible.Width + margin + y * ScreenProjection.TileWidth / 2f) / ScreenProjection.TileWidth));
        for (int x = firstX; x <= lastX; x++)
        {
            var p = new GridPoint(x, y);
            if (_gameMap.OwnerAt(p) is not { } owner)
                continue;
            for (int d = 0; d < DirectionExtensions.Count; d++)
            {
                var n = p.Neighbor((Direction)d);
                if (_gameMap.Contains(n) && _gameMap.OwnerAt(n) != owner)
                {
                    _economyRenderer.DrawBorderPost(_spriteBatch, p, owner, _terrain);
                    break;
                }
            }
        }
    }

    private Vector2 ToWorld(GridPoint p) => ScreenProjection.ToWorld(p.X, p.Y, _terrain.HeightAt(p.X, p.Y));

    protected override void Update(GameTime gameTime)
    {
        // Capture runs ignore input: keyboard and mouse state are global and would leak in from other windows.
        if (_captureDirectory is null)
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
            _message = $"placing {definition.Name} (Q/E change, Shift+click finished, Esc stop){(reason.Length > 0 ? ": " + reason : "")}";
        }
        if (_selectedBuilding is { } selectedBuilding && !_placing)
            _message = $"{selectedBuilding.Definition.Name}: {_world.StatusOf(selectedBuilding)}";
        var production = _world.Production;
        Window.Title = $"{_map.Name} · {_world.Settlers.Count} settlers · {logistics.Jobs.Count} transports, " +
                       $"produced {production.Produced}, trees {production.TreesCut}/{production.TreesPlanted}, stones {production.StonesCut}, " +
                       $"crop {production.CropHarvested}/{production.FieldsSown}, fish {production.FishCaught}, water {production.WaterDrawn}, ore {production.OreMined}, tools {production.ToolsMade} · " +
                       $"{logistics.Delivered} delivered, {logistics.DroppedOnTheWay} dropped · " +
                       $"residents {_world.Housing.Released} · pass-throughs {_world.PassThroughs} · cursor {cursor.X},{cursor.Y} " +
                       $"{_gameMap.TerrainAt(cursor)}{(_gameMap.IsWalkable(cursor) ? "" : " (blocked)")}" +
                       (_ownAi is { Log.Count: > 0 } ? $" · AI: {_ownAi.Log[^1]}" : "") +
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
        if (Pressed(Keys.I))
        {
            if (_ownAi is null && _playerCatalogs.Count > 0)
            {
                _ownAi = new AiPlayer(_world, _playerCatalogs[0], 0);
                _world.AiPlayers.Add(_ownAi);
                _message = "the AI plays for you (I: stop)";
            }
            else if (_ownAi is not null)
            {
                _world.AiPlayers.Remove(_ownAi);
                _ownAi = null;
                _message = "you play again";
            }
        }
        if (Pressed(Keys.L))
        {
            foreach (var ai in _world.AiPlayers.OrderBy(a => a.Player))
            {
                Console.WriteLine($"-- AI player {ai.Player + 1}, land {ai.Land}");
                foreach (string line in ai.Log)
                    Console.WriteLine("   " + line);
            }
            _message = "AI decisions written to the console";
        }
        if (Pressed(Keys.G))
            _message = DemoEconomy.Create(_world, _romans, ScreenToGrid(cursorScreen), _random);
        if (Pressed(Keys.M))
            SpawnWanderers(ScreenToGrid(cursorScreen));
        if (Pressed(Keys.F) && _world.BuildingAt(ScreenToGrid(cursorScreen)) is { } target)
        {
            // F: attack the enemy military building under the cursor with player 1's soldiers nearby.
            int sent = _world.Military.Attack(0, target);
            _message = sent > 0 ? $"{sent} soldiers attack {target.Definition.Name}" : "no soldiers in range or not an enemy military building";
        }
        if (Pressed(Keys.K))
        {
            // Test soldiers: two of each kind, for player 0 (with Shift for player 1).
            int player = keys.IsKeyDown(Keys.LeftShift) || keys.IsKeyDown(Keys.RightShift) ? 1 : 0;
            int soldiers = 0;
            foreach (var type in Enum.GetValues<SoldierType>())
                for (int i = 0; i < 2; i++)
                    if (_world.SpawnSoldier(ScreenToGrid(cursorScreen), type, player: player) is not null)
                        soldiers++;
            _message = $"{soldiers} soldiers for player {player + 1}";
        }

        // Left button: a click on release, or a selection box when dragged.
        if (mouse.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released
            && IsActive && GraphicsDevice.Viewport.Bounds.Contains(mouse.Position))
            _dragStart = cursorScreen;
        if (mouse.LeftButton == ButtonState.Released && _previousMouse.LeftButton == ButtonState.Pressed && _dragStart is { } dragStart)
        {
            if (!_placing && Vector2.Distance(dragStart, cursorScreen) >= DragThreshold)
                SelectInBox(dragStart, cursorScreen);
            else
                Click(cursorScreen);
            _dragStart = null;
        }
        _group.RemoveAll(s => s.IsDead || s.IsInside);

        float speed = 800 / _zoom * (float)gameTime.ElapsedGameTime.TotalSeconds;
        if (keys.IsKeyDown(Keys.Left) || keys.IsKeyDown(Keys.A))
            _camera.X -= speed;
        if (keys.IsKeyDown(Keys.Right) || keys.IsKeyDown(Keys.D))
            _camera.X += speed;
        if (keys.IsKeyDown(Keys.Up) || keys.IsKeyDown(Keys.W))
            _camera.Y -= speed;
        if (keys.IsKeyDown(Keys.Down) || keys.IsKeyDown(Keys.S))
            _camera.Y += speed;

        // Right button: dragging moves the map, a click gives the selected soldiers their order.
        if (mouse.RightButton == ButtonState.Pressed && _previousMouse.RightButton == ButtonState.Released
            && IsActive && GraphicsDevice.Viewport.Bounds.Contains(mouse.Position))
            _rightStart = cursorScreen;
        if (mouse.RightButton == ButtonState.Pressed && _previousMouse.RightButton == ButtonState.Pressed)
            _camera -= (mouse.Position - _previousMouse.Position).ToVector2() / _zoom;
        if (mouse.RightButton == ButtonState.Released && _previousMouse.RightButton == ButtonState.Pressed && _rightStart is { } rightStart)
        {
            if (Vector2.Distance(rightStart, cursorScreen) < DragThreshold)
                Command(cursorScreen);
            _rightStart = null;
        }

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
            // Shift: finished at once (for testing), otherwise a construction site.
            bool instant = Keyboard.GetState().IsKeyDown(Keys.LeftShift) || Keyboard.GetState().IsKeyDown(Keys.RightShift);
            _message = !_world.CanPlace(definition, at, out string reason) ? $"{definition.Name}: {reason}"
                : instant ? _world.PlaceBuilding(definition, 0, at) is not null ? $"{definition.Name} placed" : "could not place"
                : _world.PlaceConstructionSite(definition, 0, at) is not null ? $"{definition.Name} site placed" : "could not place";
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
        if (hit is not null && (_group.Count == 0 || hit.Player == 0))
        {
            _selected = hit;
            _selectedBuilding = null;
            _group.Clear();
            _wanderers.Remove(hit.Id);
            _message = $"settler {hit.Id} selected";
            return;
        }

        // Selected soldiers get their orders by right click; a left click elsewhere deselects them.
        if (SelectedSoldiers().Count > 0)
        {
            _group.Clear();
            _selected = null;
            _message = "";
            return;
        }

        // A building: select it (shows its state and work area). Without a selected settler,
        // clicking elsewhere clears the selection.
        if (_world.BuildingAt(ScreenToGrid(screen)) is { } building)
        {
            _selectedBuilding = building;
            _selected = null;
            return;
        }
        if (_selectedBuilding is not null && _selected is null)
        {
            _selectedBuilding = null;
            _message = "";
            return;
        }

        if (_selected is null)
            return;
        var target = ScreenToGrid(screen);
        _wanderers.Remove(_selected.Id);
        _message = _world.WalkTo(_selected, target) ? $"walking to {target.X},{target.Y}" : "unreachable";
    }

    private List<Settler> SelectedSoldiers() => _group.Count > 0 ? _group.ToList()
        : _selected is { Profession: Profession.Soldier, Player: 0 } soldier ? [soldier] : [];

    /// <summary>
    /// Right click: the selected soldiers attack the enemy military building clicked on, otherwise go
    /// there (fighting enemies they meet on the way).
    /// </summary>
    private void Command(Vector2 screen)
    {
        var soldiers = SelectedSoldiers();
        if (soldiers.Count == 0 || _placing)
            return;
        var clicked = ScreenToGrid(screen);
        if (_world.BuildingAt(clicked) is { } attacked && attacked.Player != 0 && _world.Military.Attack(soldiers, attacked) > 0)
        {
            _message = $"{soldiers.Count} soldiers attack {attacked.Definition.Name}";
            return;
        }
        _world.Military.Move(soldiers, clicked);
        _message = $"{soldiers.Count} soldiers to {clicked.X},{clicked.Y}";
    }

    /// <summary>Selects the own soldiers (outside buildings) inside the box between two screen points.</summary>
    private void SelectInBox(Vector2 from, Vector2 to)
    {
        var inverse = Matrix.Invert(View);
        var a = Vector2.Transform(from, inverse);
        var b = Vector2.Transform(to, inverse);
        var min = Vector2.Min(a, b);
        var max = Vector2.Max(a, b);
        float alpha = TickAlpha;
        _group.Clear();
        _group.AddRange(_world.Settlers.Where(s => s.Profession == Profession.Soldier && s.Player == 0 && !s.IsInside)
            .Where(s => SettlerRenderer.Anchor(s, _terrain, alpha) - new Vector2(0, 10) is var p
                        && p.X >= min.X && p.X <= max.X && p.Y >= min.Y && p.Y <= max.Y));
        _selected = null;
        _selectedBuilding = null;
        _message = _group.Count > 0 ? $"{_group.Count} soldiers selected (right click: go there / attack, left click: deselect)" : "";
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
        var marksByRow = WorkAreaMarks().ToLookup(m => m.Point.Y);
        int rows = _objects.RowCount(visible, out int firstRow);
        for (int y = firstRow; y < firstRow + rows; y++)
        {
            DrawBorderRow(y, visible);
            foreach (var (point, ring) in marksByRow[y])
                _economyRenderer.DrawWorkAreaMark(_spriteBatch, point, ring, _terrain);
            foreach (var pile in pilesByRow[y])
                _economyRenderer.DrawPile(_spriteBatch, pile, _terrain);
            if (_showObjects)
                _objects.DrawRow(_spriteBatch, y, visible, _time);
            foreach (var building in buildingsByRow[y])
            {
                var color = PlayerColors.Of(building.Player);
                _economyRenderer.DrawBuilding(_spriteBatch, building, _terrain, color);
                _economyRenderer.DrawOccupants(_spriteBatch, building, _world.Military.Occupants(building), _settlerRenderer, _terrain);
                _economyRenderer.DrawFlag(_spriteBatch, building, _world.Production.HasWorker(building) || _world.Military.IsOccupied(building),
                    _terrain, color, _time);
            }
            foreach (var settler in settlersByRow[y])
            {
                _settlerRenderer.Draw(_spriteBatch, settler, _terrain, alpha, PlayerColors.Of(settler.Player));
                if (settler.SoldierType is { } type && settler.Health < _world.Military.Config.MaxHealth(type, settler.Level))
                    DrawHealthBar(settler, type, alpha);
            }
        }

        if (_selected is not null)
            DrawSelectionMarker(_selected, alpha);
        foreach (var soldier in _group)
            DrawSelectionMarker(soldier, alpha);
        if (_dragStart is { } boxStart && !_placing)
            DrawSelectionBox(boxStart, Mouse.GetState().Position.ToVector2());
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

    /// <summary>Outline of the selection box being dragged (screen points, drawn in world space).</summary>
    private void DrawSelectionBox(Vector2 from, Vector2 to)
    {
        if (Vector2.Distance(from, to) < DragThreshold)
            return;
        var inverse = Matrix.Invert(View);
        var a = Vector2.Transform(from, inverse);
        var b = Vector2.Transform(to, inverse);
        var min = Vector2.Min(a, b);
        var size = Vector2.Max(a, b) - min;
        float line = 1 / _zoom;
        var color = Color.White * 0.8f;
        _spriteBatch.Draw(_pixel, min, null, color, 0, Vector2.Zero, new Vector2(size.X, line), SpriteEffects.None, 0);
        _spriteBatch.Draw(_pixel, min + new Vector2(0, size.Y), null, color, 0, Vector2.Zero, new Vector2(size.X, line), SpriteEffects.None, 0);
        _spriteBatch.Draw(_pixel, min, null, color, 0, Vector2.Zero, new Vector2(line, size.Y), SpriteEffects.None, 0);
        _spriteBatch.Draw(_pixel, min + new Vector2(size.X, 0), null, color, 0, Vector2.Zero, new Vector2(line, size.Y), SpriteEffects.None, 0);
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
    /// start, run four minutes of game time (long enough for grain to ripen), save a frame every eight game seconds, then quit.
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
                // HOMESTEAD_CAPTURE_ZOOM: closer look at details (flags, marks) in a capture run.
                _zoom = float.TryParse(Environment.GetEnvironmentVariable("HOMESTEAD_CAPTURE_ZOOM"),
                    System.Globalization.CultureInfo.InvariantCulture, out float zoom) ? zoom : 1;
                var start = _start;
                Console.WriteLine(DemoEconomy.Create(_world, _romans, start, _random));
                _selectedBuilding = _world.Buildings.FirstOrDefault(b => b.Definition.Name == (Environment.GetEnvironmentVariable("HOMESTEAD_CAPTURE_FOCUS") ?? "lumberjack"));
                // HOMESTEAD_CAPTURE_BATTLE=1: enemy soldiers attack the start tower.
                if (Environment.GetEnvironmentVariable("HOMESTEAD_CAPTURE_BATTLE") == "1"
                    && _world.Buildings.FirstOrDefault(b => b.Player == 0 && b.Definition.Occupiers.Count > 0) is { } tower)
                {
                    foreach (var type in Enum.GetValues<SoldierType>())
                        for (int i = 0; i < 3; i++)
                            _world.SpawnSoldier(new GridPoint(start.X + 22 + i, start.Y + 4), type, player: 1);
                    _world.Military.Attack(1, tower);
                }
                if (_zoom > 1 && _selectedBuilding is not null)
                    _camera = ToWorld(_selectedBuilding.Position);
                DemoEconomy.PlaceNear(_world, _romans["medium_livinghouse"], new GridPoint(start.X + 10, start.Y - 4), _random);
                foreach (var (name, dx, dy) in new[] { ("lumberjack", -14, 10), ("forester", 8, 16), ("stonecutter", -4, 22) })
                    for (int r = 0; r < 12; r++)
                        if (_world.PlaceConstructionSite(_romans[name], 0, new GridPoint(start.X + dx + r, start.Y + dy)) is not null)
                            break;
                break;
            case 3600:
                var production = _world.Production;
                Console.WriteLine($"buildings: {string.Join(", ", _world.Buildings.Where(b => b.Exists).Select(b => $"{b.Definition.Name}@{b.Position.X},{b.Position.Y}"))}");
                Console.WriteLine($"produced {production.Produced}, crop {production.CropHarvested}/{production.FieldsSown}, fish {production.FishCaught}, " +
                                  $"water {production.WaterDrawn}, ore {production.OreMined}, tools {production.ToolsMade}; " +
                                  $"kills {_world.Military.Kills}, captures {_world.Military.Captures}; " +
                                  $"residents {_world.Housing.Released}, settlers {_world.Settlers.Count}; diggers {_world.Settlers.Count(s => s.Profession == Profession.Digger)}, builders {_world.Settlers.Count(s => s.Profession == Profession.Builder)}; problems: " +
                                  string.Join(", ", production.Problems.Select(kv => $"{kv.Key} {kv.Value}x")));
                var carriers = _world.Settlers.Where(c => c.IsCarrier && !c.IsInside).ToList();
                Console.WriteLine($"carriers {carriers.Count}: with a task {carriers.Count(c => c.HasTask)}, strolling {carriers.Count(c => c.IsStrolling)}, " +
                                  $"idle on building ground {carriers.Count(c => !c.HasTask && !c.IsStrolling && c.Destination is null && !_world.IsFreeGround(c.Position))}");
                foreach (var building in _world.Buildings.Where(b => b.Exists && b.Player == 0))
                    Console.WriteLine($"  {building.Definition.Name}@{building.Position.X},{building.Position.Y}: {_world.StatusOf(building)}");
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
