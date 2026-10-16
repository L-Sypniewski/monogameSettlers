using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Homestead.Client;

/// <summary>
/// The share bars of the goods menus (Herstellung, Verteilung) like the original's: a dark box with a
/// frame, filled cyan by the share (0–1). A click sets it, dragging moves it, in 5 % steps; near either
/// end (or beyond) it snaps to 0 or 100 %, so both are easy to hit. Panel space.
/// </summary>
public static class ShareBar
{
    private static readonly Color Fill = new(20, 190, 200);
    private static readonly Color FrameColor = new(170, 185, 195);

    /// <summary>A press this close to (or beyond) a bar's end sets 0 or 100 %.</summary>
    private const int SnapPixels = 6;

    public const int Height = 18;

    /// <summary>The bar's inner part, 0 to 100 %.</summary>
    private static Rectangle Inner(Rectangle bar) => new(bar.X + 2, bar.Y + 2, bar.Width - 4, bar.Height - 4);

    public static void Draw(SpriteBatch batch, Texture2D pixel, Rectangle bar, double share)
    {
        batch.Draw(pixel, bar, Color.Black * 0.45f);
        batch.Draw(pixel, new Rectangle(bar.X, bar.Y, bar.Width, 1), FrameColor);
        batch.Draw(pixel, new Rectangle(bar.X, bar.Bottom - 1, bar.Width, 1), FrameColor);
        batch.Draw(pixel, new Rectangle(bar.X, bar.Y, 1, bar.Height), FrameColor);
        batch.Draw(pixel, new Rectangle(bar.Right - 1, bar.Y, 1, bar.Height), FrameColor);
        var inner = Inner(bar);
        int filled = (int)Math.Round(Math.Clamp(share, 0, 1) * inner.Width);
        if (filled > 0)
            batch.Draw(pixel, new Rectangle(inner.X, inner.Y, filled, inner.Height), Fill);
    }

    /// <summary>The share at a cursor x on the bar (also beyond it while dragging).</summary>
    public static double ValueAt(Rectangle bar, float x)
    {
        var inner = Inner(bar);
        return x <= inner.X + SnapPixels ? 0
            : x >= inner.Right - SnapPixels ? 1
            : Math.Round((x - inner.X) / inner.Width * 20) / 20;
    }
}
