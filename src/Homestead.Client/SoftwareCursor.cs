using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Homestead.Client;

/// <summary>
/// The game's own mouse pointer while the hardware cursor is off (<see cref="DisplaySettings.HardwareCursor"/>): a
/// gold arrow with a dark edge, drawn from the pattern below (the original's pointers live in its program, which we
/// do not touch), larger on large windows. Drawn last, over everything.
/// </summary>
public sealed class SoftwareCursor : IDisposable
{
    // X: edge, o: fill; the tip is the top left pixel.
    private static readonly string[] Pattern =
    [
        "X",
        "XX",
        "XoX",
        "XooX",
        "XoooX",
        "XooooX",
        "XoooooX",
        "XooooooX",
        "XoooooooX",
        "XooooooooX",
        "XoooooXXXXX",
        "XooXooX",
        "XoX XooX",
        "XX  XooX",
        "X    XooX",
        "     XooX",
        "      XX",
    ];

    private static readonly Color Edge = new(40, 30, 10);
    private static readonly Color Fill = new(250, 222, 130);

    private readonly Texture2D _texture;

    public SoftwareCursor(GraphicsDevice device)
    {
        int width = Pattern.Max(row => row.Length), height = Pattern.Length;
        var pixels = new Color[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < Pattern[y].Length; x++)
                pixels[y * width + x] = Pattern[y][x] switch { 'X' => Edge, 'o' => Fill, _ => Color.Transparent };
        _texture = new Texture2D(device, width, height);
        _texture.SetData(pixels);
    }

    /// <summary>Draws the arrow with its tip at <paramref name="at"/>; inside a begun batch with point sampling.</summary>
    public void Draw(SpriteBatch batch, Point at, int windowHeight)
    {
        int scale = Math.Max(1, (int)MathF.Round(windowHeight / 720f));
        batch.Draw(_texture, new Rectangle(at.X, at.Y, _texture.Width * scale, _texture.Height * scale), Color.White);
    }

    public void Dispose() => _texture.Dispose();
}
