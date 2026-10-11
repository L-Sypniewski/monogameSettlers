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

    /// <summary>
    /// Draws shadow, body and player-colored torso with the frame's anchor at <paramref name="anchor"/>.
    /// <paramref name="visibleFromBottom"/> &lt; 1 shows only the lower part (growing construction sites);
    /// the shadow is left out then. <paramref name="tint"/> multiplies all layers (placement preview).
    /// </summary>
    public void Draw(SpriteBatch batch, int frame, Vector2 anchor, float scale, Color playerColor,
        float visibleFromBottom = 1, Color? tint = null)
    {
        if ((uint)frame >= (uint)FrameCount || visibleFromBottom <= 0)
            return;
        var white = tint ?? Color.White;
        if (visibleFromBottom >= 1)
            _shadow[frame]?.Draw(batch, anchor, scale, white, 1);
        _body[frame]?.Draw(batch, anchor, scale, white, visibleFromBottom);
        _torso[frame]?.Draw(batch, anchor, scale, Multiply(playerColor, white), visibleFromBottom);
    }

    private static Color Multiply(Color a, Color b) =>
        new(a.R * b.R / 255, a.G * b.G / 255, a.B * b.B / 255, a.A * b.A / 255);

    public void Dispose()
    {
        foreach (var layer in _body.Concat(_torso).Concat(_shadow))
            layer?.Texture.Dispose();
    }

    /// <summary>Index of a direction in the original's order (SW, W, NW, NE, E, SE).</summary>
    public static int Block(Direction facing) => facing switch
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

        public void Draw(SpriteBatch batch, Vector2 anchor, float scale, Color tint, float visibleFromBottom)
        {
            if (visibleFromBottom >= 1)
            {
                batch.Draw(Texture, anchor + Offset * scale, null, tint, 0, Vector2.Zero, scale, SpriteEffects.None, 0);
                return;
            }
            int visible = (int)(Texture.Height * visibleFromBottom);
            int hidden = Texture.Height - visible;
            var source = new Rectangle(0, hidden, Texture.Width, visible);
            batch.Draw(Texture, anchor + (Offset + new Vector2(0, hidden)) * scale, source, tint, 0, Vector2.Zero, scale,
                SpriteEffects.None, 0);
        }
    }
}
