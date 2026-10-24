using Homestead.Assets.Maps;
using Homestead.Client.Terrain;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Homestead.Client.MapObjects;

/// <summary>
/// Draws the static objects of a map (trees, stones, plants, ...) back to front, row by row,
/// so that other figures can be interleaved at the right depth.
/// </summary>
public sealed class MapObjectLayer
{
    private const float TreeFramesPerSecond = 8;

    /// <summary>Extra rows below the visible area: high terrain lifts points up into view.</summary>
    private const int HeightMarginRows = 255 * ScreenProjection.HeightStep / ScreenProjection.RowHeight + 1;

    /// <summary>Extra rows above the visible area: tall sprites (trees) reach up from below.</summary>
    private const int SpriteMarginRows = 12;

    private readonly SpriteLibrary _library;
    private readonly TerrainMesh _terrain;
    private readonly MapObjectSprite?[] _objects;
    private readonly int _size;

    public MapObjectLayer(SpriteLibrary library, MapArea area, TerrainMesh terrain)
    {
        _library = library;
        _terrain = terrain;
        _size = area.Size;
        _objects = area.Object.Select(MapObjectCatalog.Resolve).ToArray();
    }

    /// <summary>The object of a point changed in the simulation (tree cut, sapling, stone cut off).</summary>
    public void SetCode(int x, int y, byte code) => _objects[y * _size + x] = MapObjectCatalog.Resolve(code);

    public int RowCount(RectangleF visibleWorld, out int firstRow)
    {
        firstRow = Math.Max(0, (int)(visibleWorld.Y / ScreenProjection.RowHeight) - SpriteMarginRows);
        int lastRow = Math.Min(_size - 1,
            (int)((visibleWorld.Y + visibleWorld.Height) / ScreenProjection.RowHeight) + HeightMarginRows);
        return Math.Max(0, lastRow - firstRow + 1);
    }

    /// <summary>Draws all objects of grid row <paramref name="y"/> that can be visible.</summary>
    public void DrawRow(SpriteBatch batch, int y, RectangleF visibleWorld, TimeSpan time)
    {
        // Columns whose screen x falls into the view (plus a margin for wide sprites).
        const float margin = 64;
        int firstX = Math.Max(0, (int)((visibleWorld.X - margin + y * ScreenProjection.TileWidth / 2f) / ScreenProjection.TileWidth));
        int lastX = Math.Min(_size - 1,
            (int)((visibleWorld.X + visibleWorld.Width + margin + y * ScreenProjection.TileWidth / 2f) / ScreenProjection.TileWidth));

        for (int x = firstX; x <= lastX; x++)
        {
            if (_objects[y * _size + x] is not { } sprite)
                continue;

            var sprites = Sequence(sprite.Sequence);
            int frame = sprite.Frame;
            if (sprite.Animated && sprites.FrameCount > 0)
            {
                // Per-position phase offset so neighboring trees do not sway in lockstep.
                int phase = (x * 7 + y * 13) % sprites.FrameCount;
                frame = ((int)(time.TotalSeconds * TreeFramesPerSecond) + phase) % sprites.FrameCount;
            }

            var anchor = ScreenProjection.ToWorld(x, y, _terrain.HeightAt(x, y));
            sprites.Draw(batch, frame, anchor, 1, Color.White);
        }
    }

    /// <summary>Draws the (still image of the) object of <paramref name="code"/> at a point, e.g. a preview in a tint.</summary>
    public void DrawCode(SpriteBatch batch, int x, int y, byte code, Color color)
    {
        if (MapObjectCatalog.Resolve(code) is { } sprite)
            Sequence(sprite.Sequence).Draw(batch, sprite.Frame, ScreenProjection.ToWorld(x, y, _terrain.HeightAt(x, y)), 1, color);
    }

    private SettlerSprites Sequence(int sequence) => _library.Sequence(MapObjectCatalog.GfxFile, sequence);
}
