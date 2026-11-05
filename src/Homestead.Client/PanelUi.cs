using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Homestead.Client;

/// <summary>
/// The pieces the panel menus share: a frame, the hover overlay, the spinner halves and a heading — beside
/// <see cref="ShareBar"/> and <see cref="Charts"/>, whose own one-pixel frame stays as it is.
/// </summary>
public static class PanelUi
{
    private static readonly Color Text = new(240, 225, 190);

    /// <summary>A frame <paramref name="width"/> pixels wide around the rectangle.</summary>
    public static void Frame(SpriteBatch batch, Texture2D pixel, Rectangle rect, Color color, int width = 1)
    {
        batch.Draw(pixel, new Rectangle(rect.X, rect.Y, rect.Width, width), color);
        batch.Draw(pixel, new Rectangle(rect.X, rect.Bottom - width, rect.Width, width), color);
        batch.Draw(pixel, new Rectangle(rect.X, rect.Y, width, rect.Height), color);
        batch.Draw(pixel, new Rectangle(rect.Right - width, rect.Y, width, rect.Height), color);
    }

    /// <summary>The light veil over the element under the cursor.</summary>
    public static void HoverOverlay(SpriteBatch batch, Texture2D pixel, Rectangle rect) => batch.Draw(pixel, rect, Color.White * 0.15f);

    /// <summary>The upper or lower half of an up/down spinner.</summary>
    public static Rectangle SpinnerHalf(Rectangle spinner, bool upper) =>
        new(spinner.X, upper ? spinner.Y : spinner.Center.Y, spinner.Width, spinner.Height / 2);

    /// <summary>The half a change of <paramref name="delta"/> belongs to (up: the upper one).</summary>
    public static Rectangle SpinnerHalf(Rectangle spinner, int delta) =>
        new(spinner.X, delta > 0 ? spinner.Y : spinner.Center.Y, spinner.Width, spinner.Height / 2);

    /// <summary>A heading centered over <paramref name="width"/> pixels starting at <paramref name="x"/>.</summary>
    public static void Heading(SpriteBatch batch, UiFont font, string text, int x, int width, int y, int size = 16)
    {
        var measured = font.Measure(text, size);
        SelectionPanel.Shadowed(batch, font, text, new Vector2(x + (width - measured.X) / 2, y), size, Text);
    }
}
