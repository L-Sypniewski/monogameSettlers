using Homestead.Assets.Maps;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Homestead.Client.Terrain;

/// <summary>
/// Terrain geometry: two textured triangles per grid point, split into chunks so only visible
/// parts are drawn. Texture selection (continuous textures, transition tiles per terrain pair and
/// their orientation) ported from JSettlers' Background and ETextureOrientation (MIT).
/// </summary>
public sealed class TerrainMesh : IDisposable
{
    public const int ChunkSize = 32;

    /// <summary>Screen pixels a terrain triangle covers in texture space (wider/taller than on screen).</summary>
    private const float TexUnit = 16;
    private const float BorderOffset = 0.5f;

    // Texture positions of the three vertices (u0, v0, u1, v1, u2, v2) inside a 32x32 tile.
    // Up triangles: apex, lower left, lower right. Down triangles: upper left, bottom, upper right.
    private static readonly float[] ContinuousUp = [TexUnit / 2, 0, 0, TexUnit, TexUnit, TexUnit];
    private static readonly float[] ContinuousDown = [TexUnit / 2, 0, TexUnit, TexUnit, TexUnit * 3 / 2, 0];
    private static readonly float[] TopLeft = [TexUnit / 2 + BorderOffset, 0, BorderOffset, TexUnit, TexUnit, TexUnit];
    private static readonly float[] TopRight =
        [TexUnit * 3 / 2 - BorderOffset, 0, TexUnit - BorderOffset, TexUnit, TexUnit * 2 - BorderOffset, TexUnit];
    private static readonly float[] Bottom = [TexUnit, TexUnit, TexUnit / 2, TexUnit * 2, TexUnit * 3 / 2, TexUnit * 2];
    private static readonly float[] Top = [TexUnit / 2, 0, TexUnit, TexUnit, TexUnit * 3 / 2, 0];
    private static readonly float[] BottomLeft =
        [BorderOffset, TexUnit, TexUnit / 2 + BorderOffset, TexUnit * 2, TexUnit + BorderOffset, TexUnit];
    private static readonly float[] BottomRight =
        [TexUnit - BorderOffset, TexUnit, TexUnit * 3 / 2 - BorderOffset, TexUnit * 2, TexUnit * 2 - BorderOffset, TexUnit];

    /// <summary>
    /// Transition tiles: four consecutive images starting at BaseImage. +2 if Outer covers two corners,
    /// +1 alternates by position for variety. InnerAlt is accepted in place of Inner on at most one corner.
    /// </summary>
    private static readonly (TerrainKind Inner, TerrainKind InnerAlt, TerrainKind Outer, int BaseImage)[] Transitions =
    [
        (TerrainKind.Sand, TerrainKind.Sand, TerrainKind.Water1, 37),
        (TerrainKind.Grass, TerrainKind.Sand, TerrainKind.River1, 52),
        (TerrainKind.Grass, TerrainKind.Sand, TerrainKind.River2, 56),
        (TerrainKind.Grass, TerrainKind.Sand, TerrainKind.River3, 60),
        (TerrainKind.Grass, TerrainKind.Sand, TerrainKind.River4, 64),
        (TerrainKind.Sand, TerrainKind.Sand, TerrainKind.River1, 68),
        (TerrainKind.Sand, TerrainKind.Sand, TerrainKind.River2, 72),
        (TerrainKind.Sand, TerrainKind.Sand, TerrainKind.River3, 76),
        (TerrainKind.Sand, TerrainKind.Sand, TerrainKind.River4, 80),
        (TerrainKind.Water1, TerrainKind.Water1, TerrainKind.Water2, 84),
        (TerrainKind.Water2, TerrainKind.Water2, TerrainKind.Water3, 88),
        (TerrainKind.Water3, TerrainKind.Water3, TerrainKind.Water4, 92),
        (TerrainKind.Water4, TerrainKind.Water4, TerrainKind.Water5, 96),
        (TerrainKind.Water5, TerrainKind.Water5, TerrainKind.Water6, 100),
        (TerrainKind.Water6, TerrainKind.Water6, TerrainKind.Water7, 104),
        (TerrainKind.Water7, TerrainKind.Water7, TerrainKind.Water8, 108),
        (TerrainKind.Sand, TerrainKind.Sand, TerrainKind.Grass, 112),
        (TerrainKind.Grass, TerrainKind.Grass, TerrainKind.MountainBorderOuter, 116),
        (TerrainKind.MountainBorderOuter, TerrainKind.MountainBorderOuter, TerrainKind.MountainBorder, 120),
        (TerrainKind.Mountain, TerrainKind.Mountain, TerrainKind.MountainBorder, 124),
        (TerrainKind.Grass, TerrainKind.Grass, TerrainKind.DesertBorderOuter, 128),
        (TerrainKind.DesertBorderOuter, TerrainKind.DesertBorderOuter, TerrainKind.DesertBorder, 132),
        (TerrainKind.Desert, TerrainKind.Desert, TerrainKind.DesertBorder, 136),
        (TerrainKind.Grass, TerrainKind.Grass, TerrainKind.MudBorderOuter, 140),
        (TerrainKind.MudBorderOuter, TerrainKind.MudBorderOuter, TerrainKind.MudBorder, 144),
        (TerrainKind.Mud, TerrainKind.Mud, TerrainKind.MudBorder, 148),
        (TerrainKind.Mountain, TerrainKind.Mountain, TerrainKind.SnowBorderOuter, 156),
        (TerrainKind.SnowBorderOuter, TerrainKind.SnowBorderOuter, TerrainKind.SnowBorder, 160),
        (TerrainKind.Snow, TerrainKind.Snow, TerrainKind.SnowBorder, 164),
        (TerrainKind.Mountain, TerrainKind.Mountain, TerrainKind.Snow, 156),
        (TerrainKind.Grass, TerrainKind.Grass, TerrainKind.Earth, 168),
        (TerrainKind.Grass, TerrainKind.Grass, TerrainKind.Flattened, 172),
        (TerrainKind.Grass, TerrainKind.Grass, TerrainKind.Road, 185),
        (TerrainKind.Grass, TerrainKind.Grass, TerrainKind.DryGrass, 193),
        (TerrainKind.Grass, TerrainKind.Grass, TerrainKind.DryEarth, 197),
        (TerrainKind.Grass, TerrainKind.Grass, TerrainKind.MoorBorderOuter, 201),
        (TerrainKind.MoorBorderOuter, TerrainKind.MoorBorderOuter, TerrainKind.MoorBorder, 205),
        (TerrainKind.Moor, TerrainKind.Moor, TerrainKind.MoorBorder, 209),
        (TerrainKind.Desert, TerrainKind.Desert, TerrainKind.SharpFlattenedDesert, 218),
        (TerrainKind.Desert, TerrainKind.Desert, TerrainKind.FlattenedDesert, 222),
        (TerrainKind.Mountain, TerrainKind.Mountain, TerrainKind.Gravel, 231),
    ];

    private readonly GraphicsDevice _device;
    private readonly int _size;
    private readonly TerrainKind[] _kinds;
    private readonly TerrainKind[] _originalKinds;
    private readonly byte[] _heights;
    private readonly VertexBuffer?[] _chunks;
    private readonly int _chunksPerRow;

    public TerrainMesh(GraphicsDevice device, MapArea area)
    {
        _device = device;
        _size = area.Size;
        _heights = (byte[])area.Height.Clone();
        _kinds = area.Landscape.Select(TerrainKinds.FromOriginal).ToArray();
        _originalKinds = (TerrainKind[])_kinds.Clone();
        _chunksPerRow = (_size - 1 + ChunkSize - 1) / ChunkSize;
        _chunks = new VertexBuffer?[_chunksPerRow * _chunksPerRow];
    }

    public int Size => _size;

    public int HeightAt(int x, int y) => _heights[Math.Clamp(y, 0, _size - 1) * _size + Math.Clamp(x, 0, _size - 1)];

    public TerrainKind KindAt(int x, int y) => _kinds[Math.Clamp(y, 0, _size - 1) * _size + Math.Clamp(x, 0, _size - 1)];

    /// <summary>The terrain of the point as the map was loaded (what farmland or a trail on it leaves behind).</summary>
    public TerrainKind OriginalKindAt(int x, int y) => _originalKinds[Math.Clamp(y, 0, _size - 1) * _size + Math.Clamp(x, 0, _size - 1)];

    /// <summary>
    /// Changes height and terrain of a point at runtime (e.g. levelled by diggers). The chunks with
    /// triangles touching the point are rebuilt on the next draw; lighting depends on the neighbor
    /// below as well, so that chunk is included.
    /// </summary>
    public void Update(int x, int y, int height, TerrainKind kind)
    {
        int index = y * _size + x;
        _heights[index] = (byte)height;
        _kinds[index] = kind;
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 0; dx++)
            Invalidate(x + dx, y + dy);
    }

    private void Invalidate(int x, int y)
    {
        if (x < 0 || y < 0 || x >= _size - 1 || y >= _size - 1)
            return;
        ref var chunk = ref _chunks[y / ChunkSize * _chunksPerRow + x / ChunkSize];
        chunk?.Dispose();
        chunk = null;
    }

    /// <summary>Draws all chunks that intersect the visible world rectangle. Chunks are built on first use.</summary>
    public void Draw(BasicEffect effect, RectangleF visibleWorld)
    {
        for (int cy = 0; cy < _chunksPerRow; cy++)
        for (int cx = 0; cx < _chunksPerRow; cx++)
        {
            if (!ChunkBounds(cx, cy).Intersects(visibleWorld))
                continue;

            ref var buffer = ref _chunks[cy * _chunksPerRow + cx];
            buffer ??= BuildChunk(cx, cy);
            if (buffer.VertexCount == 0)
                continue;

            _device.SetVertexBuffer(buffer);
            foreach (var pass in effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                _device.DrawPrimitives(PrimitiveType.TriangleList, 0, buffer.VertexCount / 3);
            }
        }
    }

    /// <summary>World area a chunk can cover (also used by the fog layer, which shares the chunks).</summary>
    public static RectangleF ChunkBounds(int cx, int cy)
    {
        int x0 = cx * ChunkSize, y0 = cy * ChunkSize;
        int x1 = x0 + ChunkSize, y1 = y0 + ChunkSize;
        // Rows shift left by half a tile, heights lift points up by up to 255 * HeightStep.
        float left = x0 * ScreenProjection.TileWidth - y1 * ScreenProjection.TileWidth / 2f;
        float right = x1 * ScreenProjection.TileWidth - y0 * ScreenProjection.TileWidth / 2f;
        float top = y0 * ScreenProjection.RowHeight - 255 * ScreenProjection.HeightStep;
        float bottom = y1 * ScreenProjection.RowHeight;
        return new RectangleF(left, top, right - left, bottom - top);
    }

    private VertexBuffer BuildChunk(int cx, int cy)
    {
        var vertices = new List<VertexPositionColorTexture>(ChunkSize * ChunkSize * 6);
        int xEnd = Math.Min((cx + 1) * ChunkSize, _size - 1);
        int yEnd = Math.Min((cy + 1) * ChunkSize, _size - 1);
        for (int y = cy * ChunkSize; y < yEnd; y++)
        for (int x = cx * ChunkSize; x < xEnd; x++)
        {
            AddTriangle(vertices, x, y, up: true);
            AddTriangle(vertices, x, y, up: false);
        }

        var buffer = new VertexBuffer(_device, VertexPositionColorTexture.VertexDeclaration,
            Math.Max(1, vertices.Count), BufferUsage.WriteOnly);
        if (vertices.Count > 0)
            buffer.SetData(vertices.ToArray());
        return buffer;
    }

    /// <summary>
    /// Up triangle: apex (x, y), lower left (x, y+1), lower right (x+1, y+1).
    /// Down triangle: upper left (x, y), bottom (x+1, y+1), upper right (x+1, y).
    /// </summary>
    private void AddTriangle(List<VertexPositionColorTexture> vertices, int x, int y, bool up)
    {
        Span<Vector2> uv = stackalloc Vector2[3];
        TriangleUv(x, y, up, uv);
        AddVertex(vertices, (x, y), uv[0]);
        AddVertex(vertices, up ? (x, y + 1) : (x + 1, y + 1), uv[1]);
        AddVertex(vertices, up ? (x + 1, y + 1) : (x + 1, y), uv[2]);
    }

    /// <summary>
    /// Atlas coordinates (0–1) of the corners of a terrain triangle, in the order (x, y), then
    /// (up ? (x, y+1) : (x+1, y+1)), then (up ? (x+1, y+1) : (x+1, y)): the continuous texture or the transition tile
    /// with its orientation. "left", "a" and "right" follow JSettlers' naming for the corners used in the texture rules.
    /// </summary>
    public void TriangleUv(int x, int y, bool up, Span<Vector2> uv)
    {
        (int X, int Y) left = up ? (x, y + 1) : (x, y);
        (int X, int Y) a = up ? (x, y) : (x + 1, y + 1);
        (int X, int Y) right = up ? (x + 1, y + 1) : (x + 1, y);

        var leftKind = KindAt(left.X, left.Y);
        var aKind = KindAt(a.X, a.Y);
        var rightKind = KindAt(right.X, right.Y);

        if (aKind == leftKind && aKind == rightKind)
        {
            Uv(aKind.ImageNumber(), up ? ContinuousUp : ContinuousDown, x, y, uv);
            return;
        }
        int variant = up ? x * 37 + y * 17 : x;
        int image = TransitionImage(leftKind, aKind, rightKind, variant) ?? leftKind.ImageNumber();
        float[] corners;
        if (leftKind == rightKind)
            corners = up ? Bottom : Top;
        else if (leftKind == aKind)
            corners = up ? TopLeft : BottomLeft;
        else
            corners = up ? TopRight : BottomRight;
        Uv(image, corners, x, y, uv);
    }

    /// <summary>
    /// Atlas coordinates of a triangle (corner order as <see cref="TriangleUv"/>) showing the continuous texture of
    /// landscape <paramref name="image"/> as if all its corners were of that kind.
    /// </summary>
    public static void ContinuousUv(int image, int x, int y, bool up, Span<Vector2> uv) =>
        Uv(image, up ? ContinuousUp : ContinuousDown, x, y, uv);

    private static void Uv(int image, float[] corners, int x, int y, Span<Vector2> uv)
    {
        var (cellX, cellY, cells) = TerrainAtlas.Positions[image];
        float offsetU = cellX * TerrainAtlas.Cell, offsetV = cellY * TerrainAtlas.Cell;
        if (cells >= 2)
        {
            // Continuous textures: pick the part of the repeating texture under this triangle.
            int period = (cells - 1) * TerrainAtlas.Cell;
            offsetU += Modulo(x * ScreenProjection.TileWidth - y * ScreenProjection.TileWidth / 2, period);
            offsetV += Modulo(y * (int)TexUnit, period);
        }

        // Coordinates exactly on a cell edge (v = 0 every few rows, v = 32 for some tile orientations)
        // sample the neighboring atlas cell and show up as thin lines. Keep half a texel inside.
        float cellLeft = cellX * TerrainAtlas.Cell, cellTop = cellY * TerrainAtlas.Cell;
        float minU = cellLeft + BorderOffset, minV = cellTop + BorderOffset;
        float maxU = cellLeft + cells * TerrainAtlas.Cell - BorderOffset;
        float maxV = cellTop + cells * TerrainAtlas.Cell - BorderOffset;
        // Corner order: first = (up ? apex : upper left), second = (up ? lower left : bottom), third = right.
        for (int i = 0; i < 3; i++)
            uv[i] = new Vector2(
                Math.Clamp(corners[i * 2] + offsetU, minU, maxU),
                Math.Clamp(corners[i * 2 + 1] + offsetV, minV, maxV)) / TerrainAtlas.Size;
    }

    private static int? TransitionImage(TerrainKind left, TerrainKind a, TerrainKind right, int variant)
    {
        foreach (var (inner, innerAlt, outer, baseImage) in Transitions)
        {
            int innerCount = 0, altCount = 0, outerCount = 0;
            foreach (var kind in (ReadOnlySpan<TerrainKind>)[left, a, right])
            {
                if (kind == inner)
                    innerCount++;
                else if (kind == innerAlt)
                    altCount++;
                if (kind == outer)
                    outerCount++;
            }
            if (innerCount + altCount + outerCount != 3 || altCount == 2 || outerCount == 0)
                continue;
            return baseImage + (outerCount == 2 ? 2 : 0) + (variant & 1);
        }
        return null;
    }

    /// <summary>Slopes facing the viewer are lit, those facing away darker.</summary>
    public float LightAt(int x, int y) => Light(HeightAt(x, y - 1), HeightAt(x, y));

    /// <summary>The light of a point from its height and that of the point above it (x, y-1).</summary>
    public static float Light(float heightAbove, float height) =>
        Math.Clamp(0.875f + (heightAbove - height) * 0.0625f, 0.4f, 1f); // halved with the relief

    private void AddVertex(List<VertexPositionColorTexture> vertices, (int X, int Y) p, Vector2 uv)
    {
        int height = HeightAt(p.X, p.Y);
        float light = LightAt(p.X, p.Y);

        var position = ScreenProjection.ToWorld(p.X, p.Y, height);
        vertices.Add(new VertexPositionColorTexture(
            new Vector3(position, 0),
            new Color(light, light, light),
            uv));
    }

    private static int Modulo(int value, int modulo) => (value % modulo + modulo) % modulo;

    public void Dispose()
    {
        foreach (var chunk in _chunks)
            chunk?.Dispose();
    }
}
