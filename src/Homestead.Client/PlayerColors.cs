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

    /// <summary>The colors in use, as text for a saved game (<see cref="Restore"/>).</summary>
    public static string Saved() => string.Join(',', _colors.Select(c => c.PackedValue.ToString("x8")));

    /// <summary>The colors of a saved game (<see cref="Saved"/>); unreadable text leaves them as they are.</summary>
    public static void Restore(string? saved)
    {
        var colors = saved?.Split(',').Select(c => uint.TryParse(c, System.Globalization.NumberStyles.HexNumber, null, out uint v) ? new Color(v) : (Color?)null).ToArray();
        if (colors is { Length: > 0 } && colors.All(c => c is not null))
            _colors = colors.Select(c => c!.Value).ToArray();
    }

    /// <summary>
    /// Player i gets palette entry <paramref name="chosen"/>[i] (<c>null</c>: random, see <see cref="Resolve"/>),
    /// further players the remaining palette colors in order.
    /// </summary>
    public static void Assign(IReadOnlyList<int?> chosen, Random random)
    {
        var colors = Resolve(chosen, random);
        _colors = colors.Select(c => Palette[c]).Concat(Palette.Where((_, i) => !colors.Contains(i))).ToArray();
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
            if (free.Length == 0)
                free = Enumerable.Range(0, Palette.Length).ToArray(); // more players than colors
            result[i] = free[random.Next(free.Length)];
            taken.Add(result[i]);
        }
        return result;
    }
}
