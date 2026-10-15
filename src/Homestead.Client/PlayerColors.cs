using Microsoft.Xna.Framework;

namespace Homestead.Client;

/// <summary>Colors of the players (tints the player-colored parts of settlers, flags and borders).</summary>
public static class PlayerColors
{
    /// <summary>The colors to choose from on the start screen.</summary>
    public static readonly Color[] Palette =
    [
        Color.Red, Color.RoyalBlue, Color.LimeGreen, Color.Orange, Color.Magenta,
        new Color(255, 175, 205), // light pink
        new Color(255, 238, 200), // cream
        new Color(35, 35, 35), // black: a little light left, so the tinted parts keep their shading
        Color.White,
    ];

    private static Color[] _colors = Palette;

    public static Color Of(int player) => _colors[(player % _colors.Length + _colors.Length) % _colors.Length];

    /// <summary>
    /// Player 1 gets palette entry <paramref name="own"/>, player 2 <paramref name="enemy"/>, further players
    /// the remaining palette colors in order. <c>null</c>: random (see <see cref="Resolve"/>).
    /// </summary>
    public static void Assign(int? own, int? enemy, Random random)
    {
        var colors = Resolve([own, enemy], random);
        _colors = Ordered(colors[0], colors[1]);
    }

    /// <summary>
    /// Palette indices for the chosen colors: the fixed choices first, then each random one (<c>null</c>)
    /// is drawn from the colors still free, so no color comes up twice.
    /// </summary>
    public static int[] Resolve(IReadOnlyList<int?> chosen, Random random)
    {
        var result = new int[chosen.Count];
        var taken = chosen.OfType<int>().ToHashSet();
        for (int i = 0; i < chosen.Count; i++)
        {
            if (chosen[i] is { } fixedColor)
            {
                result[i] = fixedColor;
                continue;
            }
            var free = Enumerable.Range(0, Palette.Length).Where(c => !taken.Contains(c)).ToArray();
            result[i] = free[random.Next(free.Length)];
            taken.Add(result[i]);
        }
        return result;
    }

    /// <summary>The colors per player that <see cref="Assign"/> gives for two chosen colors.</summary>
    public static Color[] Ordered(int own, int enemy) =>
        new[] { Palette[own], Palette[enemy] }.Concat(Palette.Where((_, i) => i != own && i != enemy)).ToArray();
}
