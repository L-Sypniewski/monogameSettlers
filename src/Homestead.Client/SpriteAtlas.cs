using Homestead.Assets.Gfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Homestead.Client;

/// <summary>An image packed into an atlas page: the page texture and the image's area on it.</summary>
public readonly record struct AtlasImage(Texture2D Texture, Rectangle Source);

/// <summary>
/// Packs many small images into a few large textures, so SpriteBatch can draw sprites of different
/// images in one draw call (it starts a new one whenever the texture changes). Images are added
/// as they are first needed, shelf by shelf; a full page starts the next one.
/// </summary>
public sealed class SpriteAtlas : IDisposable
{
    public const int PageSize = 4096;

    /// <summary>Transparent border around every image, so sampling never picks up a neighbor.</summary>
    private const int Padding = 1;

    private readonly GraphicsDevice _device;
    private readonly List<Texture2D> _pages = new();
    private readonly List<Texture2D> _single = new();
    private int _x, _y, _shelfHeight;

    public SpriteAtlas(GraphicsDevice device)
    {
        _device = device;
    }

    public int PageCount => _pages.Count;

    public AtlasImage Add(GfxBitmap bitmap)
    {
        int width = bitmap.Width + 2 * Padding, height = bitmap.Height + 2 * Padding;
        if (width > PageSize || height > PageSize)
        {
            // Larger than a page (does not occur in the original's sprites): a texture of its own.
            var texture = new Texture2D(_device, bitmap.Width, bitmap.Height);
            texture.SetData(bitmap.Pixels);
            _single.Add(texture);
            return new AtlasImage(texture, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
        }

        if (_x + width > PageSize)
        {
            _x = 0;
            _y += _shelfHeight;
            _shelfHeight = 0;
        }
        if (_pages.Count == 0 || _y + height > PageSize)
            NewPage();

        var source = new Rectangle(_x + Padding, _y + Padding, bitmap.Width, bitmap.Height);
        _pages[^1].SetData(0, source, bitmap.Pixels, 0, bitmap.Pixels.Length);
        _x += width;
        _shelfHeight = Math.Max(_shelfHeight, height);
        return new AtlasImage(_pages[^1], source);
    }

    private void NewPage()
    {
        // Cleared explicitly: the padding must be transparent, new GL textures are undefined.
        var page = new Texture2D(_device, PageSize, PageSize);
        page.SetData(new uint[PageSize * PageSize]);
        _pages.Add(page);
        _x = _y = _shelfHeight = 0;
    }

    public void Dispose()
    {
        foreach (var texture in _pages.Concat(_single))
            texture.Dispose();
    }
}

/// <summary>
/// The sprite sequences of the GFX files, loaded on first use into one shared <see cref="SpriteAtlas"/>.
/// Shared by all renderers, so each sequence is loaded once and sprites of all kinds batch together.
/// </summary>
public sealed class SpriteLibrary : IDisposable
{
    private readonly SpriteAtlas _atlas;
    private readonly Dictionary<(int File, int Sequence), SettlerSprites> _sequences = new();

    public SpriteLibrary(GraphicsDevice device, GfxDirectory gfx)
    {
        _atlas = new SpriteAtlas(device);
        Gfx = gfx;
    }

    public GfxDirectory Gfx { get; }

    public int PageCount => _atlas.PageCount;

    public SettlerSprites Sequence(int file, int sequence)
    {
        if (!_sequences.TryGetValue((file, sequence), out var sprites))
            _sequences[(file, sequence)] = sprites = new SettlerSprites(_atlas, Gfx.Open(file), sequence);
        return sprites;
    }

    public void Dispose() => _atlas.Dispose();
}
