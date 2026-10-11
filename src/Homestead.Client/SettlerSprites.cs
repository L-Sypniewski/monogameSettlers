using Homestead.Assets.Gfx;
using Homestead.Simulation;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Homestead.Client;

/// <summary>
/// Textures of one settler sequence (body, torso, shadow per frame), plus the knowledge how
/// walk sequences are laid out: six blocks of 12 frames (SW, W, NW, NE, E, SE), frame 0 of a
/// block is standing, frames 1-11 are the walk cycle.
/// </summary>
public sealed class SettlerSprites : IDisposable
{
    public const int FramesPerDirection = 12;
    public const int WalkCycleFrames = FramesPerDirection - 1;

    private readonly Layer?[] _body;
    private readonly Layer?[] _torso;
    private readonly Layer?[] _shadow;

    public SettlerSprites(GraphicsDevice device, GfxFile file, int sequence)
    {
        int frames = file.GetSettlerFrameCount(sequence);
        _body = new Layer?[frames];
        _torso = new Layer?[frames];
        _shadow = new Layer?[frames];
        for (int f = 0; f < frames; f++)
        {
            _body[f] = Layer.From(device, file.ReadSettler(sequence, f));
            _torso[f] = Layer.From(device, file.ReadSettlerTorso(sequence, f));
            _shadow[f] = Layer.From(device, file.ReadSettlerShadow(sequence, f));
        }
    }

    public int FrameCount => _body.Length;

    public static int WalkFrame(Direction facing, float stepProgress)
    {
        int cycle = Math.Clamp((int)(stepProgress * WalkCycleFrames), 0, WalkCycleFrames - 1);
        return Block(facing) * FramesPerDirection + 1 + cycle;
    }

    public static int StandFrame(Direction facing) => Block(facing) * FramesPerDirection;

    /// <summary>Draws shadow, body and player-colored torso with the frame's anchor at <paramref name="anchor"/>.</summary>
    public void Draw(SpriteBatch batch, int frame, Vector2 anchor, float scale, Color playerColor)
    {
        if ((uint)frame >= (uint)FrameCount)
            return;
        _shadow[frame]?.Draw(batch, anchor, scale, Color.White);
        _body[frame]?.Draw(batch, anchor, scale, Color.White);
        _torso[frame]?.Draw(batch, anchor, scale, playerColor);
    }

    public void Dispose()
    {
        foreach (var layer in _body.Concat(_torso).Concat(_shadow))
            layer?.Texture.Dispose();
    }

    private static int Block(Direction facing) => facing switch
    {
        Direction.SouthWest => 0,
        Direction.West => 1,
        Direction.NorthWest => 2,
        Direction.NorthEast => 3,
        Direction.East => 4,
        Direction.SouthEast => 5,
        _ => throw new ArgumentOutOfRangeException(nameof(facing)),
    };

    private sealed record Layer(Texture2D Texture, Vector2 Offset)
    {
        public static Layer? From(GraphicsDevice device, GfxBitmap? bitmap)
        {
            if (bitmap is not { Width: > 0, Height: > 0 })
                return null;
            var texture = new Texture2D(device, bitmap.Width, bitmap.Height);
            texture.SetData(bitmap.Pixels);
            return new Layer(texture, new Vector2(bitmap.OffsetX, bitmap.OffsetY));
        }

        public void Draw(SpriteBatch batch, Vector2 anchor, float scale, Color tint) =>
            batch.Draw(Texture, anchor + Offset * scale, null, tint, 0, Vector2.Zero, scale, SpriteEffects.None, 0);
    }
}
