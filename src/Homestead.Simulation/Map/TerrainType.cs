namespace Homestead.Simulation.Map;

/// <summary>
/// Terrain as the game rules see it. "Border" types are the transition zone on the inner side
/// (e.g. mountain foot), "BorderOuter" the zone on the grass side.
/// </summary>
public enum TerrainType : byte
{
    Grass,
    Water,
    River,
    Sand,
    Mountain,
    MountainBorder,
    MountainBorderOuter,
    Desert,
    DesertBorder,
    DesertBorderOuter,
    Moor,
    MoorBorder,
    MoorBorderOuter,
    Snow,
    SnowBorder,
    SnowBorderOuter,
    Mud,
    MudBorder,
    MudBorderOuter,

    /// <summary>Ground levelled for a building.</summary>
    Flattened,
}

public static class TerrainTypes
{
    /// <summary>
    /// Whether settlers can walk on the terrain. Matches the accessibility flags stored in the
    /// original maps: water, moor and snow block, rivers, mud and all outer borders do not.
    /// </summary>
    public static bool IsWalkable(this TerrainType terrain) => terrain switch
    {
        TerrainType.Water or TerrainType.Moor or TerrainType.MoorBorder or TerrainType.Snow or TerrainType.SnowBorder => false,
        _ => true,
    };

    /// <summary>Ground type name as used in the building data (<c>ground</c> lists).</summary>
    public static string GroundName(this TerrainType terrain) => terrain switch
    {
        TerrainType.River => "RIVER1",
        _ => terrain.ToString().ToUpperInvariant(),
    };

    /// <summary>Landscape byte of original maps. Unknown values fall back by their group (upper nibble).</summary>
    public static TerrainType FromOriginal(byte value) => value switch
    {
        <= 7 => TerrainType.Water,
        16 => TerrainType.Grass,
        17 => TerrainType.MountainBorderOuter,
        20 => TerrainType.DesertBorderOuter,
        21 => TerrainType.MoorBorderOuter,
        23 => TerrainType.MudBorderOuter,
        32 => TerrainType.Mountain,
        33 => TerrainType.MountainBorder,
        35 => TerrainType.SnowBorderOuter,
        48 => TerrainType.Sand,
        64 => TerrainType.Desert,
        65 => TerrainType.DesertBorder,
        80 => TerrainType.Moor,
        81 => TerrainType.MoorBorder,
        >= 96 and <= 99 => TerrainType.River,
        128 => TerrainType.Snow,
        129 => TerrainType.SnowBorder,
        144 => TerrainType.Mud,
        145 => TerrainType.MudBorder,
        _ => (value & 0xf0) switch
        {
            0x20 => TerrainType.Mountain,
            0x40 => TerrainType.Desert,
            0x80 => TerrainType.Snow,
            _ => TerrainType.Grass,
        },
    };
}
