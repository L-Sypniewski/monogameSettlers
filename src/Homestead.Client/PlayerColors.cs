using Microsoft.Xna.Framework;

namespace Homestead.Client;

/// <summary>Colors of the players (tints the player-colored parts of settlers, flags and borders).</summary>
public static class PlayerColors
{
    private static readonly Color[] Colors =
        [Color.Red, Color.RoyalBlue, Color.LimeGreen, Color.Yellow, Color.Magenta, Color.Orange, Color.Cyan, Color.White];

    public static Color Of(int player) => Colors[(player % Colors.Length + Colors.Length) % Colors.Length];
}
