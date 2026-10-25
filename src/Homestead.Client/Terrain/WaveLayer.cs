using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Homestead.Client.Terrain;

/// <summary>
/// Waves on open water: long straight swells running diagonally over it. The water's corners rise with them like
/// terrain (<see cref="MaxLift"/> height steps; lit like slopes), the crests show the lighter water texture
/// (<see cref="CrestImage"/>) and their front, the side facing the viewer, glints. How high the waves get grows with
/// the distance from the shore (<see cref="CalmFields"/>, <see cref="RampFields"/>), so they fade out towards the beach
/// and small ponds stay calm. Over the glinting fronts the original's glitter plays (<see cref="DrawGlitter"/>). Ships ride on the same waves (<see cref="LiftAt"/>), only drawn, never moved.
/// </summary>
public sealed class WaveLayer
{
    /// <summary>Landscape image blended in on the crests (the light water of <see cref="TerrainKind.Water2"/>).</summary>
    private const int CrestImage = 16;

    /// <summary>How strongly the highest crest shows the light water.</summary>
    private const float CrestBlend = 0.18f;

    /// <summary>How brightly the front of a wave glints (added light of the crest texture, by the steepness).</summary>
    private const float Glint = 0.6f;

    /// <summary>The glitter on the water: object file sequence 26, frames growing and fading again, laid out as tiles.</summary>
    private const int GlitterFile = 1, GlitterSequence = 26;

    /// <summary>
    /// The part of the swell's phase (radians, see <see cref="SwellPhase"/>) in which its front glints: the phase falls
    /// as the wave passes, so the glitter starts at the upper bound and has faded at the lower one, just past the crest.
    /// </summary>
    private const float GlitterFrom = 3.6f, GlitterTo = 1.75f;

    /// <summary>
    /// How far apart the glitter tiles lie, as a share of a frame: closer than their size, so they overlap; every second
    /// row is shifted by half a step (as in the original).
    /// </summary>
    private const float GlitterStep = 0.75f;

    /// <summary>Fields of calm water along the shore, then over how many the waves grow to full height.</summary>
    private const int CalmFields = 1, RampFields = 14;

    /// <summary>How far the highest wave lifts the water, in height steps.</summary>
    private const float MaxLift = 4;

    /// <summary>Length of the swell in fields (as on the screen), its speed in fields per second.</summary>
    private const float Length = 18, Speed = 1.8f;

    /// <summary>Where the swell runs on the screen: down and to the left, so its crests lie diagonally.</summary>
    private static readonly Vector2 Heading = Vector2.Normalize(new Vector2(-0.5f, 0.87f));

    private readonly TerrainMesh _terrain;
    private readonly int _size;

    /// <summary>Per grid point how high its waves get (0 calm … 1 open sea).</summary>
    private readonly float[] _strength;

    private float[] _lift = [];
    private float[] _glint = [];
    private float[] _crest = [];
    private VertexPositionColorTexture[] _water = [];
    private VertexPositionColorTexture[] _shine = [];
    private Rectangle? _glitterTile;

    public WaveLayer(TerrainMesh terrain)
    {
        _terrain = terrain;
        _size = terrain.Size;
        _strength = new float[_size * _size];
        var distance = ShoreDistances();
        for (int i = 0; i < _strength.Length; i++)
        {
            // Smoothstep, so the calm coast passes into the open sea without an edge. Water without any shore is open sea.
            float s = distance[i] == int.MaxValue ? 1 : MathHelper.Clamp((distance[i] - CalmFields) / (float)RampFields, 0, 1);
            _strength[i] = s * s * (3 - 2 * s);
        }
    }

    private static bool IsWater(TerrainKind kind) => kind is >= TerrainKind.Water1 and <= TerrainKind.Water8;

    /// <summary>Per grid point the steps to the nearest point that is not water (a search from the shore).</summary>
    private int[] ShoreDistances()
    {
        var distance = new int[_size * _size];
        var queue = new Queue<int>();
        for (int y = 0; y < _size; y++)
        for (int x = 0; x < _size; x++)
        {
            int i = y * _size + x;
            if (IsWater(_terrain.KindAt(x, y)))
                distance[i] = int.MaxValue;
            else
                queue.Enqueue(i);
        }
        ReadOnlySpan<(int X, int Y)> neighbors = [(1, 0), (1, 1), (0, 1), (-1, 0), (-1, -1), (0, -1)];
        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            int x = i % _size, y = i / _size;
            foreach (var (dx, dy) in neighbors)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= _size || ny >= _size)
                    continue;
                int n = ny * _size + nx;
                if (distance[n] <= distance[i] + 1)
                    continue;
                distance[n] = distance[i] + 1;
                queue.Enqueue(n);
            }
        }
        return distance;
    }

    /// <summary>
    /// The wave at a grid position (fractional between fields) and time, 0 trough … 1 crest, measured as on the screen
    /// (a row is 9/16 of a field's width): the swell with narrow crests and wide troughs, a little higher and lower
    /// along its crests, and a faint ripple across it.
    /// </summary>
    private static float Wave(float x, float y, float seconds)
    {
        var p = new Vector2(x - y / 2, y * ScreenProjection.RowHeight / ScreenProjection.TileWidth);
        float along = Vector2.Dot(p, Heading), across = Vector2.Dot(p, new Vector2(-Heading.Y, Heading.X));
        float swell = 0.5f + 0.5f * MathF.Sin(SwellPhase(along, seconds));
        swell *= swell * swell * swell;
        float rise = 0.75f + 0.25f * MathF.Sin(across * (MathHelper.TwoPi / 43) + seconds * 0.2f);
        float ripple = 0.5f + 0.5f * MathF.Sin((along * 0.8f + across * 0.6f) * (MathHelper.TwoPi / 5) - seconds * 1.7f);
        return 0.85f * swell * rise + 0.15f * ripple;
    }

    /// <summary>The phase of the swell (radians) at a distance along its heading, in fields as on the screen.</summary>
    private static float SwellPhase(float along, float seconds) => (along - seconds * Speed) * (MathHelper.TwoPi / Length);

    private float StrengthAt(int x, int y) => _strength[Math.Clamp(y, 0, _size - 1) * _size + Math.Clamp(x, 0, _size - 1)];

    /// <summary>How far the waves lift something floating at a grid position (fractional while it moves), in world pixels.</summary>
    public float LiftAt(Vector2 grid, float seconds) =>
        ScreenProjection.HeightStep * MaxLift * StrengthAt((int)MathF.Round(grid.X), (int)MathF.Round(grid.Y)) * Wave(grid.X, grid.Y, seconds);

    /// <summary>
    /// Draws the visible water with waves over the terrain: its triangles again with lifted corners and their own
    /// texture (opaque), then the crests and glints with the light water texture (premultiplied). Expects the terrain
    /// effect (atlas texture, vertex colors).
    /// </summary>
    public void Draw(GraphicsDevice device, BasicEffect effect, RectangleF visible, float seconds)
    {
        // One row more above: the row above a point lights it.
        int y0 = Math.Max(1, (int)(visible.Y / ScreenProjection.RowHeight) - 2);
        int y1 = Math.Min(_size - 1, (int)((visible.Y + visible.Height + 255 * ScreenProjection.HeightStep) / ScreenProjection.RowHeight) + 1);
        int x0 = Math.Max(0, (int)((visible.X + y0 * ScreenProjection.TileWidth / 2f) / ScreenProjection.TileWidth) - 1);
        int x1 = Math.Min(_size - 1, (int)((visible.X + visible.Width + y1 * ScreenProjection.TileWidth / 2f) / ScreenProjection.TileWidth) + 1);
        if (x1 <= x0 || y1 <= y0)
            return;

        int width = x1 - x0 + 1;
        int points = width * (y1 - y0 + 1);
        if (_lift.Length < points)
        {
            _lift = new float[points];
            _glint = new float[points];
            _crest = new float[points];
        }
        for (int y = y0; y <= y1; y++)
        for (int x = x0; x <= x1; x++)
        {
            int i = (y - y0) * width + x - x0;
            float strength = _strength[y * _size + x];
            if (strength <= 0)
            {
                _lift[i] = _glint[i] = _crest[i] = 0;
                continue;
            }
            float wave = Wave(x, y, seconds);
            _lift[i] = MaxLift * strength * wave;
            _crest[i] = CrestBlend * strength * wave;
            // The front faces the viewer: the water a row higher on the screen stands higher.
            float behind = Wave(x - 0.5f, y - 1, seconds);
            _glint[i] = Glint * strength * MathHelper.Clamp((behind - wave) * 3, 0, 1);
        }

        int water = 0, shine = 0;
        Span<Vector2> uv = stackalloc Vector2[3];
        Span<(int X, int Y)> corners = stackalloc (int, int)[3];
        for (int y = y0 + 1; y < y1; y++)
        for (int x = x0; x < x1; x++)
        for (int half = 0; half < 2; half++)
        {
            bool up = half == 0;
            // Same corners as the terrain: up (x,y) (x,y+1) (x+1,y+1), down (x,y) (x+1,y+1) (x+1,y).
            corners[0] = (x, y);
            corners[1] = up ? (x, y + 1) : (x + 1, y + 1);
            corners[2] = up ? (x + 1, y + 1) : (x + 1, y);
            if (StrengthAt(corners[0].X, corners[0].Y) + StrengthAt(corners[1].X, corners[1].Y) + StrengthAt(corners[2].X, corners[2].Y) <= 0)
                continue;
            if (_water.Length < water + 3)
                Array.Resize(ref _water, Math.Max(3072, _water.Length * 2));
            _terrain.TriangleUv(x, y, up, uv);
            for (int c = 0; c < 3; c++)
            {
                var (cx, cy) = corners[c];
                int i = (cy - y0) * width + cx - x0;
                // The light of a slope from the lifted heights; the point above is lifted by its own wave.
                float height = _terrain.HeightAt(cx, cy) + _lift[i];
                float light = TerrainMesh.Light(_terrain.HeightAt(cx, cy - 1) + _lift[i - width], height);
                _water[water++] = Vertex(cx, cy, height, new Color(light, light, light), uv[c]);
            }

            int i0 = (y - y0) * width + x - x0;
            int i1 = (corners[1].Y - y0) * width + corners[1].X - x0;
            int i2 = (corners[2].Y - y0) * width + corners[2].X - x0;
            if (_crest[i0] + _crest[i1] + _crest[i2] + _glint[i0] + _glint[i1] + _glint[i2] < 0.01f)
                continue;
            if (_shine.Length < shine + 3)
                Array.Resize(ref _shine, Math.Max(3072, _shine.Length * 2));
            TerrainMesh.ContinuousUv(CrestImage, x, y, up, uv);
            for (int c = 0; c < 3; c++)
            {
                var (cx, cy) = corners[c];
                int i = (cy - y0) * width + cx - x0;
                // Premultiplied: the light texture times (blend + glint) over what lies below times one minus the blend;
                // the glint only adds.
                float alpha = _crest[i], light = _terrain.LightAt(cx, cy) * (alpha + _glint[i]);
                _shine[shine++] = Vertex(cx, cy, _terrain.HeightAt(cx, cy) + _lift[i], new Color(light, light, light, alpha), uv[c]);
            }
        }

        Draw(device, effect, _water, water, BlendState.Opaque);
        Draw(device, effect, _shine, shine, BlendState.AlphaBlend);
        device.BlendState = BlendState.Opaque;
    }

    /// <summary>
    /// Draws the glitter over the visible water: the glitter frames as overlapping tiles in rows (<see cref="GlitterStep"/>),
    /// each showing the frame that belongs to the swell's phase at its middle, so it plays while the front passes and
    /// several tiles behind each other play at once. The tiles stay on the water's ground, not lifted by the waves, and
    /// fade towards the shore like them. Expects a sprite batch begun in world pixels.
    /// </summary>
    public void DrawGlitter(SpriteBatch batch, SpriteLibrary sprites, RectangleF visible, float seconds)
    {
        var glitter = sprites.Sequence(GlitterFile, GlitterSequence);
        if (_glitterTile is null)
        {
            // The frames' common canvas: the empty ones have no bounds, the others may be cut to their content.
            Rectangle? canvas = null;
            for (int frame = 0; frame < glitter.FrameCount; frame++)
                if (glitter.Bounds(frame) is { } bounds)
                    canvas = canvas is { } c ? Rectangle.Union(c, bounds) : bounds;
            _glitterTile = canvas ?? Rectangle.Empty;
        }
        var tile = _glitterTile.Value;
        if (tile.Width <= 0 || tile.Height <= 0)
            return;

        float stepX = MathF.Round(tile.Width * GlitterStep), stepY = MathF.Round(tile.Height * GlitterStep);
        int tx0 = (int)MathF.Floor((visible.X - tile.Width) / stepX), tx1 = (int)MathF.Floor((visible.X + visible.Width) / stepX);
        // Tiles further down are lifted into view by the height of the water.
        int ty0 = (int)MathF.Floor((visible.Y - tile.Height) / stepY);
        int ty1 = (int)MathF.Floor((visible.Y + visible.Height + 255 * ScreenProjection.HeightStep) / stepY);
        for (int ty = ty0; ty <= ty1; ty++)
        for (int tx = tx0; tx <= tx1; tx++)
        {
            var corner = new Vector2(tx * stepX + ((ty & 1) == 1 ? MathF.Round(stepX / 2) : 0), ty * stepY);
            // Whole tiles on the map only, none sticking out over its edge.
            if (!OnMap(corner.X, corner.Y) || !OnMap(corner.X + tile.Width, corner.Y + tile.Height)
                || !OnMap(corner.X, corner.Y + tile.Height) || !OnMap(corner.X + tile.Width, corner.Y))
                continue;
            var middle = corner + new Vector2(tile.Width, tile.Height) / 2;
            var (x, y) = GridAt(middle.X, middle.Y);
            if (!IsWater(_terrain.KindAt(x, y)))
                continue;
            float strength = _strength[y * _size + x];
            if (strength <= 0)
                continue;
            // The phase as Wave measures it: grid position to screen fields is the world position over the tile width.
            float along = Vector2.Dot(middle / ScreenProjection.TileWidth, Heading);
            float phase = SwellPhase(along, seconds) % MathHelper.TwoPi;
            if (phase < 0)
                phase += MathHelper.TwoPi;
            float progress = (GlitterFrom - phase) / (GlitterFrom - GlitterTo);
            if (progress is < 0 or >= 1)
                continue;
            var anchor = corner - new Vector2(tile.X, tile.Y + _terrain.HeightAt(x, y) * ScreenProjection.HeightStep);
            glitter.Draw(batch, (int)(progress * glitter.FrameCount), anchor, 1, Color.White, tint: Color.White * strength, shadow: false);
        }
    }

    /// <summary>The grid point nearest to a world position at height 0.</summary>
    private static (int X, int Y) GridAt(float worldX, float worldY)
    {
        int y = (int)MathF.Round(worldY / ScreenProjection.RowHeight);
        return ((int)MathF.Round((worldX + y * ScreenProjection.TileWidth / 2f) / ScreenProjection.TileWidth), y);
    }

    private bool OnMap(float worldX, float worldY)
    {
        var (x, y) = GridAt(worldX, worldY);
        return x >= 0 && y >= 0 && x < _size && y < _size;
    }

    private static void Draw(GraphicsDevice device, BasicEffect effect, VertexPositionColorTexture[] vertices, int count, BlendState blend)
    {
        if (count == 0)
            return;
        device.BlendState = blend;
        foreach (var pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            device.DrawUserPrimitives(PrimitiveType.TriangleList, vertices, 0, count / 3);
        }
    }

    private static VertexPositionColorTexture Vertex(int x, int y, float height, Color color, Vector2 uv) =>
        new(new Vector3(x * ScreenProjection.TileWidth - y * ScreenProjection.TileWidth / 2f,
            y * ScreenProjection.RowHeight - height * ScreenProjection.HeightStep, 0), color, uv);
}
