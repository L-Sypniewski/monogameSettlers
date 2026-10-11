namespace Homestead.Simulation.Map;

/// <summary>Static objects on a map field, grouped by how the game rules treat them.</summary>
public enum MapObjectKind : byte
{
    None,

    /// <summary>Plants, flowers, mushrooms, decorative stones, wrecks: purely visual.</summary>
    Decoration,

    Tree,

    /// <summary>Quarry stone that stonecutters can mine.</summary>
    ResourceStone,

    /// <summary>Codes we do not know yet.</summary>
    Unknown,
}

public static class MapObjectKinds
{
    public static bool BlocksWalking(this MapObjectKind kind) => kind is MapObjectKind.Tree or MapObjectKind.ResourceStone;

    /// <summary>
    /// Whether the object also blocks its six neighbors. Quarry stones do: the original maps mark
    /// the ring around every stone as not accessible.
    /// </summary>
    public static bool BlocksNeighbors(this MapObjectKind kind) => kind is MapObjectKind.ResourceStone;

    /// <summary>Object byte of original maps (see docs/formate/map.md).</summary>
    public static MapObjectKind FromOriginal(byte code) => code switch
    {
        0 => MapObjectKind.None,
        >= 1 and <= 67 => MapObjectKind.Decoration,
        >= 68 and <= 84 => MapObjectKind.Tree,
        >= 111 and <= 114 => MapObjectKind.Decoration, // reefs (in water anyway)
        // 127 is the depleted stone (rubble): the maps never mark it as blocking.
        >= 115 and <= 126 => MapObjectKind.ResourceStone,
        127 => MapObjectKind.Decoration,
        _ => MapObjectKind.Unknown,
    };
}
