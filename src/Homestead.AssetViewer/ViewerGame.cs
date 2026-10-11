using Homestead.Assets;
using Homestead.Assets.Gfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Homestead.AssetViewer;

/// <summary>
/// Browses the sprites of the original GFX files. Status is shown in the window title.
/// </summary>
/// <remarks>
/// Keys: PgUp/PgDn file · Tab kind · Left/Right entry (Shift = 10) · Up/Down frame ·
/// Space play/pause · +/- zoom · P player color · S shadow · T torso · Esc quit
/// </remarks>
public sealed class ViewerGame : Game
{
    private static readonly Color[] PlayerColors =
        [Color.Red, Color.RoyalBlue, Color.LimeGreen, Color.Yellow, Color.Magenta, Color.Orange, Color.Cyan, Color.White];

    private static readonly SpriteKind[] Kinds = Enum.GetValues<SpriteKind>();

    private readonly GraphicsDeviceManager _graphics;
    private readonly GfxDirectory _gfx;
    private readonly int[] _fileNumbers;
    private readonly Dictionary<(int File, SpriteKind Kind, int Entry, int Frame, Layer Layer), Sprite?> _cache = new();

    private SpriteBatch _spriteBatch = null!;
    private Texture2D _pixel = null!;
    private KeyboardState _previousKeys;

    private int _fileIndex;
    private SpriteKind _kind = SpriteKind.Settler;
    private int _entry;
    private int _frame;
    private bool _playing = true;
    private double _frameTimer;
    private int _zoom = 2;
    private int _playerColor;
    private bool _showShadow = true;
    private bool _showTorso = true;
    private string? _error;

    public ViewerGame(GfxDirectory gfx)
    {
        _gfx = gfx;
        _fileNumbers = gfx.FileNumbers.ToArray();
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1280,
            PreferredBackBufferHeight = 800,
        };
        Window.AllowUserResizing = true;
        IsMouseVisible = true;

        // Start on a file that has settlers to look at.
        _fileIndex = Math.Max(0, Array.IndexOf(_fileNumbers, 11));
    }

    private GfxFile File => _gfx.Open(_fileNumbers[_fileIndex]);

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _pixel = new Texture2D(GraphicsDevice, 1, 1);
        _pixel.SetData([Color.White]);
    }

    protected override void Update(GameTime gameTime)
    {
        var keys = Keyboard.GetState();
        bool Pressed(Keys k) => keys.IsKeyDown(k) && !_previousKeys.IsKeyDown(k);
        int step = keys.IsKeyDown(Keys.LeftShift) || keys.IsKeyDown(Keys.RightShift) ? 10 : 1;

        if (Pressed(Keys.Escape))
            Exit();
        if (Pressed(Keys.PageDown))
            SelectFile(_fileIndex + 1);
        if (Pressed(Keys.PageUp))
            SelectFile(_fileIndex - 1);
        if (Pressed(Keys.Tab))
            SelectKind(Kinds[(Array.IndexOf(Kinds, _kind) + 1) % Kinds.Length]);
        if (Pressed(Keys.Right))
            SelectEntry(_entry + step);
        if (Pressed(Keys.Left))
            SelectEntry(_entry - step);
        if (Pressed(Keys.Up))
            _frame = Wrap(_frame + 1, FrameCount());
        if (Pressed(Keys.Down))
            _frame = Wrap(_frame - 1, FrameCount());
        if (Pressed(Keys.Space))
            _playing = !_playing;
        if (Pressed(Keys.OemPlus) || Pressed(Keys.Add))
            _zoom = Math.Min(8, _zoom + 1);
        if (Pressed(Keys.OemMinus) || Pressed(Keys.Subtract))
            _zoom = Math.Max(1, _zoom - 1);
        if (Pressed(Keys.P))
            _playerColor = (_playerColor + 1) % PlayerColors.Length;
        if (Pressed(Keys.S))
            _showShadow = !_showShadow;
        if (Pressed(Keys.T))
            _showTorso = !_showTorso;
        _previousKeys = keys;

        if (_playing)
        {
            _frameTimer += gameTime.ElapsedGameTime.TotalSeconds;
            if (_frameTimer >= 0.1)
            {
                _frameTimer = 0;
                _frame = Wrap(_frame + 1, FrameCount());
            }
        }

        Window.Title = $"Asset Viewer · file {_fileNumbers[_fileIndex]} · {_kind} {_entry}/{EntryCount()} · " +
                       $"frame {_frame}/{FrameCount()} · zoom {_zoom}x" + LayerInfo() +
                       (_error is null ? "" : $" · ERROR: {_error}");
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(new Color(48, 56, 48));
        var viewport = GraphicsDevice.Viewport;
        var anchor = new Vector2(viewport.Width / 2, viewport.Height / 2);

        _spriteBatch.Begin(samplerState: SamplerState.PointClamp);

        if (EntryCount() > 0 && FrameCount() > 0)
        {
            bool displaced = _kind is SpriteKind.Settler or SpriteKind.Torso or SpriteKind.Shadow;
            if (_kind == SpriteKind.Settler)
            {
                if (_showShadow)
                    DrawSprite(GetSprite(Layer.Shadow), anchor, Color.White);
                DrawSprite(GetSprite(Layer.Main), anchor, Color.White);
                if (_showTorso)
                    DrawSprite(GetSprite(Layer.Torso), anchor, PlayerColors[_playerColor]);
            }
            else
            {
                var sprite = GetSprite(Layer.Main);
                // Non-displaced images have no anchor offset; center them instead.
                var position = displaced || sprite is null
                    ? anchor
                    : anchor - new Vector2(sprite.Texture.Width, sprite.Texture.Height) * _zoom / 2;
                DrawSprite(sprite, position, _kind == SpriteKind.Torso ? PlayerColors[_playerColor] : Color.White);
            }

            // Anchor cross for judging sprite offsets.
            _spriteBatch.Draw(_pixel, new Rectangle((int)anchor.X - 6, (int)anchor.Y, 13, 1), Color.Yellow * 0.6f);
            _spriteBatch.Draw(_pixel, new Rectangle((int)anchor.X, (int)anchor.Y - 6, 1, 13), Color.Yellow * 0.6f);
        }

        _spriteBatch.End();
        base.Draw(gameTime);
    }

    private void DrawSprite(Sprite? sprite, Vector2 position, Color tint)
    {
        if (sprite is null)
            return;
        _spriteBatch.Draw(sprite.Texture, position + sprite.Offset * _zoom, null, tint, 0, Vector2.Zero, _zoom,
            SpriteEffects.None, 0);
    }

    private Sprite? GetSprite(Layer layer)
    {
        var key = (_fileNumbers[_fileIndex], _kind, _entry, _frame, layer);
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        Sprite? sprite = null;
        try
        {
            var bitmap = ReadBitmap(layer);
            if (bitmap is { Width: > 0, Height: > 0 })
            {
                var texture = new Texture2D(GraphicsDevice, bitmap.Width, bitmap.Height);
                texture.SetData(bitmap.Pixels);
                sprite = new Sprite(texture, new Vector2(bitmap.OffsetX, bitmap.OffsetY));
            }
        }
        catch (Exception e)
        {
            _error = e.Message;
        }
        _cache[key] = sprite;
        return sprite;
    }

    private GfxBitmap? ReadBitmap(Layer layer)
    {
        var file = File;
        return (_kind, layer) switch
        {
            (SpriteKind.Settler, Layer.Main) => file.ReadSettler(_entry, _frame),
            (SpriteKind.Settler, Layer.Torso) => file.ReadSettlerTorso(_entry, _frame),
            (SpriteKind.Settler, Layer.Shadow) => file.ReadSettlerShadow(_entry, _frame),
            (SpriteKind.Torso, Layer.Main) => file.ReadTorso(_entry, _frame),
            (SpriteKind.Shadow, Layer.Main) => file.ReadShadow(_entry, _frame),
            (SpriteKind.Landscape, Layer.Main) => file.ReadLandscape(_entry),
            (SpriteKind.Gui, Layer.Main) => file.ReadGui(_entry),
            _ => null,
        };
    }

    private string LayerInfo()
    {
        if (_kind != SpriteKind.Settler || _entry >= EntryCount())
            return "";
        static string Name(int seq) => seq >= 0 ? seq.ToString() : "-";
        return $" · torso {Name(File.TorsoSequenceFor(_entry))} · shadow {Name(File.ShadowSequenceFor(_entry))}";
    }

    private int EntryCount() => _kind switch
    {
        SpriteKind.Settler => File.SettlerCount,
        SpriteKind.Torso => File.TorsoCount,
        SpriteKind.Shadow => File.ShadowCount,
        SpriteKind.Landscape => File.LandscapeCount,
        SpriteKind.Gui => File.GuiCount,
        _ => 0,
    };

    private int FrameCount()
    {
        if (_entry >= EntryCount())
            return 0;
        return _kind switch
        {
            SpriteKind.Settler => File.GetSettlerFrameCount(_entry),
            SpriteKind.Torso => File.GetTorsoFrameCount(_entry),
            SpriteKind.Shadow => File.GetShadowFrameCount(_entry),
            _ => 1,
        };
    }

    private void SelectFile(int index)
    {
        _fileIndex = Wrap(index, _fileNumbers.Length);
        // Keep the current kind if the new file has it, otherwise pick the first kind it has.
        if (EntryCount() == 0)
        {
            var current = _kind;
            foreach (var kind in Kinds)
            {
                _kind = kind;
                if (EntryCount() > 0)
                    break;
            }
            if (EntryCount() == 0)
                _kind = current;
        }
        SelectEntry(0);
    }

    private void SelectKind(SpriteKind kind)
    {
        _kind = kind;
        SelectEntry(0);
    }

    private void SelectEntry(int entry)
    {
        _entry = Wrap(entry, EntryCount());
        _frame = 0;
        _error = null;
    }

    private static int Wrap(int value, int count) => count == 0 ? 0 : ((value % count) + count) % count;

    private enum SpriteKind
    {
        Settler,
        Torso,
        Shadow,
        Landscape,
        Gui,
    }

    private enum Layer
    {
        Main,
        Torso,
        Shadow,
    }

    private sealed record Sprite(Texture2D Texture, Vector2 Offset);
}
