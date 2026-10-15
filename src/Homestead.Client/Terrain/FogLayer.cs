using Homestead.Simulation.Map;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Homestead.Client.Terrain;

/// <summary>
/// Fog of war drawn over the world: per grid point a brightness (visible 1, explored <see cref="Explored"/>,
/// unexplored 0), as black triangles on the terrain grid whose alpha is interpolated between the points.
/// Edges are soft over <see cref="SoftFields"/> fields: brightness fades out beyond seen land, so the
/// view reaches a little further than the vision itself. New targets come from a <see cref="Vision"/> now and then; the shown brightness
/// follows them over <see cref="FadeSeconds"/>. Triangles are kept per terrain chunk and rebuilt when a
/// point in them changes.
/// </summary>
public sealed class FogLayer : IDisposable
{
    /// <summary>Brightness of land seen before but not now.</summary>
    public const float Explored = 0.55f;

    public const float FadeSeconds = 1f;

    /// <summary>Over how many fields beyond seen (or explored) land the brightness fades out.</summary>
    public const int SoftFields = 3;

    /// <summary>Neighbor offsets of a grid point (as <c>Direction.Delta</c>).</summary>
    private static readonly (int X, int Y)[] Neighbors = [(1, 0), (1, 1), (0, 1), (-1, 0), (-1, -1), (0, -1)];

    private readonly Queue<int> _spread = new();

    private readonly GraphicsDevice _device;
    private readonly TerrainMesh _terrain;
    private readonly int _size;
    private readonly float[] _shown;
    private readonly float[] _target;

    /// <summary>Points whose shown brightness has not reached the target yet.</summary>
    private readonly List<int> _fading = new();
    private readonly bool[] _isFading;

    private readonly int _chunksPerRow;
    private readonly VertexBuffer?[] _chunks;
    private readonly bool[] _built;
    private bool _first = true;

    public FogLayer(GraphicsDevice device, TerrainMesh terrain)
    {
        _device = device;
        _terrain = terrain;
        _size = terrain.Size;
        _shown = new float[_size * _size];
        _target = new float[_size * _size];
        _isFading = new bool[_size * _size];
        _chunksPerRow = (_size - 1 + TerrainMesh.ChunkSize - 1) / TerrainMesh.ChunkSize;
        _chunks = new VertexBuffer?[_chunksPerRow * _chunksPerRow];
        _built = new bool[_chunks.Length];
    }

    /// <summary>Brightness shown at a point (0 unexplored … 1 visible).</summary>
    public float BrightnessAt(int x, int y) => _shown[Math.Clamp(y, 0, _size - 1) * _size + Math.Clamp(x, 0, _size - 1)];

    /// <summary>Takes the new targets from <paramref name="vision"/>; the first time without fading.</summary>
    public void SetTargets(Vision vision)
    {
        for (int i = 0; i < _target.Length; i++)
            _target[i] = vision.IsVisible(i) ? 1 : vision.IsExplored(i) ? Explored : 0;
        Soften();
        for (int i = 0; i < _target.Length; i++)
        {
            float target = _target[i];
            if (_first)
                _shown[i] = target;
            else if (_shown[i] != target && !_isFading[i])
            {
                _isFading[i] = true;
                _fading.Add(i);
            }
        }
        if (_first)
            Array.Clear(_built);
        _first = false;
    }

    /// <summary>
    /// Lets brightness spill over to darker neighbors, losing 1 / (<see cref="SoftFields"/> + 1) per field,
    /// so it fades out over <see cref="SoftFields"/> fields: a search from the points brighter than a neighbor.
    /// </summary>
    private void Soften()
    {
        const float Loss = 1f / (SoftFields + 1);
        for (int y = 0; y < _size; y++)
        for (int x = 0; x < _size; x++)
        {
            float spill = _target[y * _size + x] - Loss;
            foreach (var (dx, dy) in Neighbors)
            {
                int nx = x + dx, ny = y + dy;
                if (nx >= 0 && ny >= 0 && nx < _size && ny < _size && _target[ny * _size + nx] < spill)
                {
                    _spread.Enqueue(y * _size + x);
                    break;
                }
            }
        }
        while (_spread.Count > 0)
        {
            int i = _spread.Dequeue();
            int x = i % _size, y = i / _size;
            float spill = _target[i] - Loss;
            if (spill <= 0)
                continue;
            foreach (var (dx, dy) in Neighbors)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= _size || ny >= _size)
                    continue;
                int n = ny * _size + nx;
                if (_target[n] >= spill)
                    continue;
                _target[n] = spill;
                _spread.Enqueue(n);
            }
        }
    }

    /// <summary>Moves the shown brightness toward the targets.</summary>
    public void Advance(float seconds)
    {
        float step = seconds / FadeSeconds;
        for (int n = _fading.Count - 1; n >= 0; n--)
        {
            int i = _fading[n];
            float shown = _shown[i], target = _target[i];
            shown = shown < target ? Math.Min(target, shown + step) : Math.Max(target, shown - step);
            _shown[i] = shown;
            Touch(i % _size, i / _size);
            if (shown == target)
            {
                _isFading[i] = false;
                _fading[n] = _fading[^1];
                _fading.RemoveAt(_fading.Count - 1);
            }
        }
    }

    /// <summary>A point changed (brightness or terrain height): the chunks with triangles touching it are rebuilt.</summary>
    public void Touch(int x, int y)
    {
        for (int dy = -1; dy <= 0; dy++)
        for (int dx = -1; dx <= 0; dx++)
        {
            int cx = x + dx, cy = y + dy;
            if (cx < 0 || cy < 0 || cx >= _size - 1 || cy >= _size - 1)
                continue;
            _built[cy / TerrainMesh.ChunkSize * _chunksPerRow + cx / TerrainMesh.ChunkSize] = false;
        }
    }

    public void Draw(BasicEffect effect, RectangleF visibleWorld)
    {
        for (int cy = 0; cy < _chunksPerRow; cy++)
        for (int cx = 0; cx < _chunksPerRow; cx++)
        {
            if (!TerrainMesh.ChunkBounds(cx, cy).Intersects(visibleWorld))
                continue;
            int index = cy * _chunksPerRow + cx;
            if (!_built[index])
            {
                _chunks[index]?.Dispose();
                _chunks[index] = BuildChunk(cx, cy);
                _built[index] = true;
            }
            if (_chunks[index] is not { } buffer)
                continue;
            _device.SetVertexBuffer(buffer);
            foreach (var pass in effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                _device.DrawPrimitives(PrimitiveType.TriangleList, 0, buffer.VertexCount / 3);
            }
        }
    }

    /// <summary>The fog triangles of a chunk; none where all three corners are fully visible.</summary>
    private VertexBuffer? BuildChunk(int cx, int cy)
    {
        var vertices = new List<VertexPositionColor>();
        int xEnd = Math.Min((cx + 1) * TerrainMesh.ChunkSize, _size - 1);
        int yEnd = Math.Min((cy + 1) * TerrainMesh.ChunkSize, _size - 1);
        for (int y = cy * TerrainMesh.ChunkSize; y < yEnd; y++)
        for (int x = cx * TerrainMesh.ChunkSize; x < xEnd; x++)
        {
            // Same triangles as the terrain: up (x,y) (x,y+1) (x+1,y+1), down (x,y) (x+1,y+1) (x+1,y).
            AddTriangle(vertices, (x, y), (x, y + 1), (x + 1, y + 1));
            AddTriangle(vertices, (x, y), (x + 1, y + 1), (x + 1, y));
        }
        if (vertices.Count == 0)
            return null;
        var buffer = new VertexBuffer(_device, VertexPositionColor.VertexDeclaration, vertices.Count, BufferUsage.WriteOnly);
        buffer.SetData(vertices.ToArray());
        return buffer;
    }

    private void AddTriangle(List<VertexPositionColor> vertices, (int X, int Y) a, (int X, int Y) b, (int X, int Y) c)
    {
        if (BrightnessAt(a.X, a.Y) >= 1 && BrightnessAt(b.X, b.Y) >= 1 && BrightnessAt(c.X, c.Y) >= 1)
            return;
        AddVertex(vertices, a);
        AddVertex(vertices, b);
        AddVertex(vertices, c);
    }

    private void AddVertex(List<VertexPositionColor> vertices, (int X, int Y) p)
    {
        var position = ScreenProjection.ToWorld(p.X, p.Y, _terrain.HeightAt(p.X, p.Y));
        // Premultiplied black: only the alpha darkens what lies below.
        vertices.Add(new VertexPositionColor(new Vector3(position, 0), Color.Black * (1 - BrightnessAt(p.X, p.Y))));
    }

    public void Dispose()
    {
        foreach (var chunk in _chunks)
            chunk?.Dispose();
    }
}
