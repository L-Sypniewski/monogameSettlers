using Microsoft.Xna.Framework;

namespace Homestead.Client.Terrain;

/// <summary>
/// The water lab's depth tint (rings): a per-vertex color factor by water ring, Water1 (shore) to Water8 (open sea),
/// deepening and cooling towards the deep end. Multiplied into the vertex light, never replacing it.
/// </summary>
public static class WaterLabTint
{
    /// <summary>Color factor of the deepest ring (Water8): about 12% darker, slightly cooler than the shore.</summary>
    public static readonly Vector3 Deep = new(0.88f, 0.90f, 0.96f);

    /// <summary>0 (Water1) to 7 (Water8) for the water rings, -1 for anything else (no tint).</summary>
    public static int Ring(TerrainKind kind) => kind >= TerrainKind.Water1 && kind <= TerrainKind.Water8
        ? kind - TerrainKind.Water1
        : -1;

    /// <summary>The tint factor of a terrain kind: 1 everywhere except the water rings, which ramp to <see cref="Deep"/>.</summary>
    public static Vector3 For(TerrainKind kind)
    {
        int ring = Ring(kind);
        if (ring < 0)
            return Vector3.One;
        float depth = ring / 7f;
        return Vector3.Lerp(Vector3.One, Deep, depth);
    }
}
