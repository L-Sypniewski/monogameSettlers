using Microsoft.Xna.Framework;

namespace Homestead.Client;

/// <summary>Colors of the players (tints the player-colored parts of settlers, flags and borders).</summary>
public static class PlayerColors
{
    /// <summary>The colors to choose from on the start screen.</summary>
    public static readonly Color[] Palette =
        [Color.Red, Color.RoyalBlue, Color.LimeGreen, Color.Yellow, Color.Magenta, Color.Orange, Color.Cyan, Color.White];

    private static Color[] _colors = Palette;

    public static Color Of(int player) => _colors[(player % _colors.Length + _colors.Length) % _colors.Length];

    /// <summary>
    /// Player 1 gets palette entry <paramref name="own"/>, player 2 <paramref name="enemy"/>, further players
    /// the remaining palette colors in order.
    /// </summary>
    public static void Assign(int own, int enemy) => _colors = Ordered(own, enemy);

    /// <summary>The colors per player that <see cref="Assign"/> would give.</summary>
    public static Color[] Ordered(int own, int enemy) =>
        new[] { Palette[own], Palette[enemy] }.Concat(Palette.Where((_, i) => i != own && i != enemy)).ToArray();
}
