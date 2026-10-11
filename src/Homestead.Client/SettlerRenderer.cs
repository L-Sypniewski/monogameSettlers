using Homestead.Assets.Gfx;
using Homestead.Client.Terrain;
using Homestead.Simulation;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Homestead.Client;

/// <summary>
/// Draws simulation settlers: interpolates between ticks, follows terrain height and picks the
/// walk or stand frame. Each settler gets one of the carrier animations (with different goods).
/// </summary>
public sealed class SettlerRenderer : IDisposable
{
    private const int SettlerFile = 10; // Roman settlers

    private readonly GraphicsDevice _device;
    private readonly GfxFile _gfx;
    private readonly int[] _walkSequences;
    private readonly Dictionary<int, SettlerSprites> _sprites = new();

    public SettlerRenderer(GraphicsDevice device, GfxDirectory gfx)
    {
        _device = device;
        _gfx = gfx.Open(SettlerFile);
        // Carriers: the leading sequences with the 6 x 12 walk layout (empty-handed and with goods).
        _walkSequences = Enumerable.Range(0, _gfx.SettlerCount)
            .TakeWhile(s => _gfx.GetSettlerFrameCount(s) == DirectionExtensions.Count * SettlerSprites.FramesPerDirection)
            .ToArray();
    }

    /// <summary>Screen anchor (feet) of a settler in world pixels.</summary>
    public static Vector2 Anchor(Settler settler, TerrainMesh terrain, float tickAlpha)
    {
        var from = ToWorld(settler.Position, terrain);
        if (settler.State != SettlerState.Stepping)
            return from;
        var to = ToWorld(settler.StepTarget, terrain);
        return Vector2.Lerp(from, to, StepProgress(settler, tickAlpha));
    }

    /// <summary>Grid row used for depth sorting: the lower of the two rows a step connects.</summary>
    public static int DepthRow(Settler settler) => Math.Max(settler.Position.Y, settler.StepTarget.Y);

    public void Draw(SpriteBatch batch, Settler settler, TerrainMesh terrain, float tickAlpha, Color playerColor)
    {
        var sprites = SpritesFor(settler);
        int frame = settler.State == SettlerState.Stepping
            ? SettlerSprites.WalkFrame(settler.Facing, StepProgress(settler, tickAlpha))
            : SettlerSprites.StandFrame(settler.Facing);
        sprites.Draw(batch, frame, Anchor(settler, terrain, tickAlpha), 1, playerColor);
    }

    private static float StepProgress(Settler settler, float tickAlpha) =>
        Math.Clamp((settler.TicksIntoStep + tickAlpha) / World.StepTicks, 0, 1);

    private static Vector2 ToWorld(GridPoint p, TerrainMesh terrain) =>
        ScreenProjection.ToWorld(p.X, p.Y, terrain.HeightAt(p.X, p.Y));

    private SettlerSprites SpritesFor(Settler settler)
    {
        int sequence = _walkSequences[settler.Id % _walkSequences.Length];
        if (!_sprites.TryGetValue(sequence, out var sprites))
            _sprites[sequence] = sprites = new SettlerSprites(_device, _gfx, sequence);
        return sprites;
    }

    public void Dispose()
    {
        foreach (var sprites in _sprites.Values)
            sprites.Dispose();
    }
}
