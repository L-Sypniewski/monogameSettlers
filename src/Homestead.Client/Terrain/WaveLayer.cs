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

    /// <summary>
    /// Depth shading (HOMESTEAD_TERRAIN=depth): how much light open water loses per unit of <see cref="_strength"/>,
    /// per color channel (red most, green and blue a little less), so the sea gets darker and a touch cooler offshore.
    /// </summary>
    private const float DepthDark = 0.10f;

    /// <summary>Foam (HOMESTEAD_TERRAIN=foam): the shore band of <see cref="_strength"/> it shows in and its peak alpha.</summary>
    private const float FoamRange = 0.18f, FoamAlpha = 0.15f;

    /// <summary>Softlight (HOMESTEAD_TERRAIN=softlight): the share of the water pass's remaining darkness it
    /// leaves — masks residual faceting.</summary>
    private const float SoftLight = 0.6f;

    /// <summary>How far the highest wave lifts the water, in height steps.</summary>
    internal const float MaxLift = 4;

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
    private float[] _foamAlpha = [];
    /// <summary>Per sub-corner of the water pass, a grid finer by the tessellation factor over the visible
    /// window: wave lift and strength. tess2/tess4 re-sample the analytic field at every subdivision point;
    /// at factor 1 the grids alias the field-corner values (index-identical to <see cref="_lift"/>).</summary>
    private float[] _subLift = [];
    private float[] _subStrength = [];
    private readonly TerrainExperimentSession? _experiments;
    private VertexPositionColorTexture[] _water = [];
    private VertexPositionColorTexture[] _shine = [];
    private VertexPositionColorTexture[] _foam = [];
    private Rectangle? _glitterTile;

    public WaveLayer(TerrainMesh terrain, TerrainExperimentSession? experiments = null)
    {
        _terrain = terrain;
        _size = terrain.Size;
        _experiments = experiments;
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
    internal static float Wave(float x, float y, float seconds)
    {
        var p = SwellPosition(x, y);
        float along = Vector2.Dot(p, Heading), across = Vector2.Dot(p, new Vector2(-Heading.Y, Heading.X));
        float swell = 0.5f + 0.5f * MathF.Sin(SwellPhase(along, seconds));
        swell *= swell * swell * swell;
        float rise = 0.75f + 0.25f * MathF.Sin(across * (MathHelper.TwoPi / 43) + seconds * 0.2f);
        float ripple = 0.5f + 0.5f * MathF.Sin((along * 0.8f + across * 0.6f) * (MathHelper.TwoPi / 5) - seconds * 1.7f);
        return 0.85f * swell * rise + 0.15f * ripple;
    }

    /// <summary>The phase of the swell (radians) at a distance along its heading, in fields as on the screen.</summary>
    private static float SwellPhase(float along, float seconds) => (along - seconds * Speed) * (MathHelper.TwoPi / Length);

    /// <summary>A grid position in the fields the swell measures it in (fractional between grid points).</summary>
    private static Vector2 SwellPosition(float x, float y) =>
        new(x - y / 2, y * ScreenProjection.RowHeight / ScreenProjection.TileWidth);

    private float StrengthAt(int x, int y) => _strength[Math.Clamp(y, 0, _size - 1) * _size + Math.Clamp(x, 0, _size - 1)];

    /// <summary>Bilinear sample of a per-grid-point field at a fractional position; indices clamp at the edges
    /// and grid points give the exact value (weights 0 stay exact in float arithmetic).</summary>
    internal static float Bilinear(float[] values, int size, float x, float y)
    {
        x = Math.Clamp(x, 0, size - 1);
        y = Math.Clamp(y, 0, size - 1);
        int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y);
        int x1 = Math.Min(x0 + 1, size - 1), y1 = Math.Min(y0 + 1, size - 1);
        float tx = x - x0, ty = y - y0;
        return (1 - tx) * (1 - ty) * values[y0 * size + x0]
            + tx * (1 - ty) * values[y0 * size + x1]
            + (1 - tx) * ty * values[y1 * size + x0]
            + tx * ty * values[y1 * size + x1];
    }

    /// <summary>The wave strength at a fractional grid position, bilinear between the per-grid-point values
    /// (the strength field is smooth by construction, unlike the stepped heights).</summary>
    internal float StrengthBilinear(float x, float y) => Bilinear(_strength, _size, x, y);

    /// <summary>
    /// The foam band over the wave strength: smoothsteps in over the first half of <see cref="FoamRange"/> and
    /// out over the second, so neither the shore nor the open sea end of the band shows an edge.
    /// </summary>
    private static float FoamBand(float strength)
    {
        if (strength <= 0 || strength >= FoamRange)
            return 0;
        float half = FoamRange / 2, rise = Math.Min(strength / half, 1), fall = Math.Min((FoamRange - strength) / half, 1);
        rise *= rise * (3 - 2 * rise);
        fall *= fall * (3 - 2 * fall);
        return rise * fall;
    }

    /// <summary>Per color channel the share of the light the depth shading leaves at a wave strength.</summary>
    private static Vector3 DepthFactors(float strength) =>
        new(1 - DepthDark * strength, 1 - DepthDark * 0.99f * strength, 1 - DepthDark * 0.97f * strength);

    /// <summary>How far the waves lift something floating at a grid position (fractional while it moves), in world pixels.</summary>
    public float LiftAt(Vector2 grid, float seconds) =>
        ScreenProjection.HeightStep * MaxLift * StrengthAt((int)MathF.Round(grid.X), (int)MathF.Round(grid.Y)) * Wave(grid.X, grid.Y, seconds);

    /// <summary>
    /// Draws the visible water with waves over the terrain: its triangles again with lifted corners and their own
    /// texture (opaque; tess2/tess4 subdivide this pass, every sub-corner re-sampling the wave field), then the
    /// crests and glints with the light water texture (premultiplied). Expects the terrain effect (atlas texture,
    /// vertex colors).
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

        // depth/foam/tess2/tess4/softlight (HOMESTEAD_TERRAIN, F5): read per draw so a step of the ring applies without a restart.
        bool depthShading = _experiments?.Active.Depth == true, foamOn = _experiments?.Active.Foam == true;
        int tess = Math.Max(1, _experiments?.Active.Tessellation ?? 1);
        bool softLight = _experiments?.Active.SoftLight == true;
        int width = x1 - x0 + 1;
        Precompute(x0, x1, y0, y1, width, seconds, tess);
        int water = EmitWater(x0, x1, y0, y1, depthShading, softLight, tess);

        int shine = 0, foam = 0;
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
            int i0 = (y - y0) * width + x - x0;
            int i1 = (corners[1].Y - y0) * width + corners[1].X - x0;
            int i2 = (corners[2].Y - y0) * width + corners[2].X - x0;
            if (foamOn && _foamAlpha[i0] + _foamAlpha[i1] + _foamAlpha[i2] >= 0.01f)
            {
                if (_foam.Length < foam + 3)
                    Array.Resize(ref _foam, Math.Max(3072, _foam.Length * 2));
                TerrainMesh.ContinuousUv(CrestImage, x, y, up, uv);
                for (int c = 0; c < 3; c++)
                {
                    var (cx, cy) = corners[c];
                    int i = (cy - y0) * width + cx - x0;
                    // Premultiplied like the shine pass: the rasterizer interpolates the corners' alphas, so the foam stays continuous across triangles.
                    float alpha = _foamAlpha[i], light = _terrain.LightAt(cx, cy) * alpha;
                    _foam[foam++] = Vertex(cx, cy, _terrain.HeightAt(cx, cy) + _lift[i],
                        new Color(light, light, light, alpha), uv[c]);
                }
            }
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
                var depth = DepthFactors(StrengthAt(cx, cy));
                _shine[shine++] = Vertex(cx, cy, _terrain.HeightAt(cx, cy) + _lift[i],
                    depthShading ? new Color(light * depth.X, light * depth.Y, light * depth.Z, alpha) : new Color(light, light, light, alpha), uv[c]);
            }
        }

        Draw(device, effect, _water, water, BlendState.Opaque);
        Draw(device, effect, _shine, shine, BlendState.AlphaBlend);
        if (foamOn)
            Draw(device, effect, _foam, foam, BlendState.AlphaBlend);
        device.BlendState = BlendState.Opaque;
    }

    /// <summary>
    /// The visible window's per-grid-point values — wave lift, crest, glint and foam, which stay at field
    /// granularity — and the water pass's sub-corner grids: at factor 1 they alias the field-corner values,
    /// above it every subdivision point re-samples the analytic wave field with bilinear
    /// <see cref="StrengthBilinear"/> strength. Re-sampling instead of interpolating the corner values is
    /// the point: the swell keeps its curvature between fields.
    /// </summary>
    internal void Precompute(int x0, int x1, int y0, int y1, int width, float seconds, int tess)
    {
        int points = width * (y1 - y0 + 1);
        if (_lift.Length < points)
        {
            _lift = new float[points];
            _glint = new float[points];
            _crest = new float[points];
            _foamAlpha = new float[points];
        }
        int cols = (x1 - x0) * tess + 1, rows = (y1 - y0) * tess + 1;
        if (_subLift.Length < rows * cols)
        {
            _subLift = new float[rows * cols];
            _subStrength = new float[rows * cols];
        }
        for (int y = y0; y <= y1; y++)
        for (int x = x0; x <= x1; x++)
        {
            int i = (y - y0) * width + x - x0;
            float strength = _strength[y * _size + x];
            if (strength <= 0)
            {
                _lift[i] = _glint[i] = _crest[i] = _foamAlpha[i] = 0;
                if (tess == 1)
                    _subLift[i] = _subStrength[i] = 0;
                continue;
            }
            float wave = Wave(x, y, seconds);
            _lift[i] = MaxLift * strength * wave;
            _crest[i] = CrestBlend * strength * wave;
            // The front faces the viewer: the water a row higher on the screen stands higher.
            float behind = Wave(x - 0.5f, y - 1, seconds);
            _glint[i] = Glint * strength * MathHelper.Clamp((behind - wave) * 3, 0, 1);
            // Per corner like the values above, so the foam over a corner shared by triangles has no edge.
            float along = Vector2.Dot(SwellPosition(x, y), Heading);
            _foamAlpha[i] = FoamAlpha * FoamBand(strength) * (0.5f + 0.5f * MathF.Sin(SwellPhase(along, seconds)));
            if (tess == 1)
            {
                _subLift[i] = _lift[i];
                _subStrength[i] = strength;
            }
        }
        if (tess > 1)
        {
            for (int row = 0; row < rows; row++)
            {
                float fy = y0 + row / (float)tess;
                for (int col = 0; col < cols; col++)
                {
                    int g = row * cols + col;
                    float fx = x0 + col / (float)tess;
                    float strength = StrengthBilinear(fx, fy);
                    if (strength <= 0)
                    {
                        _subLift[g] = _subStrength[g] = 0;
                        continue;
                    }
                    _subStrength[g] = strength;
                    _subLift[g] = MaxLift * strength * Wave(fx, fy, seconds);
                }
            }
        }
    }

    /// <summary>
    /// The opaque water pass: the field cells' triangles with lifted corners and their own light and texture.
    /// tess2/tess4 (HOMESTEAD_TERRAIN) emit n×n sub-cells of two triangles per field cell instead — the same
    /// diagonal pattern at a finer scale — with every sub-corner's height, light and lift from its own sample
    /// of the wave field. Only the terrain height stays that of the nearest grid point, so the shore silhouette
    /// keeps the land mesh's steps (the stepped heights are never interpolated). softlight compresses the light
    /// toward white, masking residual faceting. At n=1 the sub-corners are the field corners and the emission is
    /// the classic one, value for value and in the same order.
    /// </summary>
    internal int EmitWater(int x0, int x1, int y0, int y1, bool depthShading, bool softLight, int tess)
    {
        int cols = (x1 - x0) * tess + 1;
        int water = 0;
        Span<Vector2> uvUp = stackalloc Vector2[3], uvDown = stackalloc Vector2[3];
        Span<(float U, float V)> corner = stackalloc (float U, float V)[3];
        Span<int> grid = stackalloc int[3];
        for (int y = y0 + 1; y < y1; y++)
        for (int x = x0; x < x1; x++)
        {
            _terrain.TriangleUv(x, y, true, uvUp);
            _terrain.TriangleUv(x, y, false, uvDown);
            int gx = (x - x0) * tess, gy = (y - y0) * tess;
            for (int sy = 0; sy < tess; sy++)
            for (int sx = 0; sx < tess; sx++)
            for (int half = 0; half < 2; half++)
            {
                bool up = half == 0;
                var (a, b, c) = SubCellCorners(sx, sy, tess, up);
                corner[0] = a;
                corner[1] = b;
                corner[2] = c;
                grid[0] = (gy + sy) * cols + gx + sx;
                grid[1] = (gy + sy + 1) * cols + gx + (up ? sx : sx + 1);
                grid[2] = (gy + (up ? sy + 1 : sy)) * cols + gx + sx + 1;
                if (_subStrength[grid[0]] + _subStrength[grid[1]] + _subStrength[grid[2]] <= 0)
                    continue;
                if (_water.Length < water + 3)
                    Array.Resize(ref _water, Math.Max(3072, _water.Length * 2));
                bool parentUp = ParentIsUp(sx, sy, up);
                for (int k = 0; k < 3; k++)
                {
                    float fx = x + corner[k].U, fy = y + corner[k].V;
                    // The light of a slope from the lifted heights; the point above is lifted by its own wave.
                    float height = _terrain.HeightAt((int)MathF.Round(fx), (int)MathF.Round(fy)) + _subLift[grid[k]];
                    float above = _terrain.HeightAt((int)MathF.Round(fx), (int)MathF.Round(fy - 1)) + _subLift[grid[k] - cols];
                    float light = TerrainMesh.Light(above, height);
                    if (softLight)
                        light = 1 - (1 - light) * SoftLight;
                    var depth = DepthFactors(_subStrength[grid[k]]);
                    var parent = parentUp ? uvUp : uvDown;
                    var (w0, w1, w2) = ParentUvWeights(corner[k].U, corner[k].V, parentUp);
                    var uv = parent[0] * w0 + parent[1] * w1 + parent[2] * w2;
                    _water[water++] = Vertex(fx, fy, height,
                        depthShading ? new Color(light * depth.X, light * depth.Y, light * depth.Z) : new Color(light, light, light), uv);
                }
            }
        }
        return water;
    }

    /// <summary>
    /// The corners of sub-cell (sx, sy) of a field cell as shares of it (0…1), in the terrain's up/down triangle
    /// order: up (0,0) (0,1) (1,1), down (0,0) (1,1) (1,0) — the cell's diagonal pattern at the finer scale.
    /// </summary>
    internal static ((float U, float V) A, (float U, float V) B, (float U, float V) C) SubCellCorners(int sx, int sy, int tess, bool up)
    {
        float u0 = sx / (float)tess, u1 = (sx + 1) / (float)tess;
        float v0 = sy / (float)tess, v1 = (sy + 1) / (float)tess;
        return up ? ((u0, v0), (u0, v1), (u1, v1)) : ((u0, v0), (u1, v1), (u1, v0));
    }

    /// <summary>
    /// Which of the field cell's two triangles a sub-triangle takes its texture from. A sub-cell's diagonal runs
    /// parallel to the cell's, so every sub-triangle lies in one closed half of the cell: those on or above the
    /// cell diagonal (up: sx ≤ sy; down: sx &lt; sy) in the up triangle's half, the rest in the down's. On the
    /// diagonal itself a corner follows its own sub-triangle's orientation, as the field corners do at n=1.
    /// </summary>
    internal static bool ParentIsUp(int sx, int sy, bool up) => up ? sx <= sy : sx < sy;

    /// <summary>
    /// Barycentric weight of each parent-triangle corner (up: (0,0) (0,1) (1,1); down: (0,0) (1,1) (1,0)) at a
    /// sub-corner of the cell. At the cell corners exactly one weight is 1, so the classic corner UVs come out
    /// bit for bit (multiplying by 1 and adding 0 keeps float values).
    /// </summary>
    internal static (float W0, float W1, float W2) ParentUvWeights(float u, float v, bool parentUp) =>
        parentUp ? (1 - v, v - u, u) : (1 - u, v, u - v);

    /// <summary>The last water pass's vertex buffer; headless tests compare it against the classic emission.</summary>
    internal VertexPositionColorTexture[] WaterBuffer => _water;

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

    private static VertexPositionColorTexture Vertex(float x, float y, float height, Color color, Vector2 uv) =>
        new(new Vector3(x * ScreenProjection.TileWidth - y * ScreenProjection.TileWidth / 2f,
            y * ScreenProjection.RowHeight - height * ScreenProjection.HeightStep, 0), color, uv);
}
