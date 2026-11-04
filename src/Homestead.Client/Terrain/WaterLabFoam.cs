using Microsoft.Xna.Framework;

namespace Homestead.Client.Terrain;

/// <summary>
/// The water lab's shoreline foam, per grid point: strongest in a band along the shore (a smoothstep in and out over
/// the wave strength, which is 0 at the beach and 1 on the open sea), washed in and out with the swell's phase at
/// that point, and dithered by a cheap deterministic hash of the point so the band is not a perfect ring.
/// Pure math, headless-testable.
/// </summary>
public static class WaterLabFoam
{
    /// <summary>The foam's peak opacity.</summary>
    public const float FoamAlpha = 0.35f;

    /// <summary>The band over the wave strength: rises from the beach, falls back before the open sea.</summary>
    public const float RiseFrom = 0, RiseTo = 0.08f, FallFrom = 0.12f, FallTo = 0.2f;

    /// <summary>How much the hash may scale the alpha up and down: breaks up the ring shape.</summary>
    public const float DitherSpread = 0.25f;

    /// <summary>The band factor of a wave strength: 0 at the beach, a plateau of 1 shortly before it, 0 on the open sea.</summary>
    public static float Band(float strength) =>
        SmoothStep(RiseFrom, RiseTo, strength) * (1 - SmoothStep(FallFrom, FallTo, strength));

    /// <summary>Deterministic value noise in [0, 1) from the point's coordinates.</summary>
    public static float Hash01(int x, int y)
    {
        uint h = (uint)(x * 374761393 + y * 668265263);
        h = (h ^ (h >> 13)) * 1274126177;
        h ^= h >> 16;
        return h / 4294967296f;
    }

    /// <summary>The foam's alpha at a grid point: the band times the swell's pulse there, dithered per point.</summary>
    public static float Alpha(float strength, float swellPhase, int x, int y)
    {
        float band = Band(strength);
        if (band <= 0)
            return 0;
        float pulse = 0.5f + 0.5f * MathF.Sin(swellPhase);
        float dither = 1 + DitherSpread * (2 * Hash01(x, y) - 1);
        return Math.Clamp(FoamAlpha * band * pulse * dither, 0, 1);
    }

    private static float SmoothStep(float from, float to, float value)
    {
        float t = MathHelper.Clamp((value - from) / (to - from), 0, 1);
        return t * t * (3 - 2 * t);
    }
}
