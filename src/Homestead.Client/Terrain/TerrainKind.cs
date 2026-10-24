using Homestead.Simulation.Map;

namespace Homestead.Client.Terrain;

/// <summary>
/// Terrain types as far as rendering is concerned. The value is the landscape image (file 00) of
/// the continuous texture. Names and image numbers from JSettlers' ELandscapeType (MIT).
/// </summary>
public enum TerrainKind
{
    Grass,
    DryGrass,
    Desert,
    Earth,
    Mountain,
    Snow,
    Sand,
    Flattened,
    River1,
    River2,
    River3,
    River4,
    MountainBorder,
    MountainBorderOuter,
    Water1,
    Water2,
    Water3,
    Water4,
    Water5,
    Water6,
    Water7,
    Water8,
    Moor,
    MoorBorder,
    MoorBorderOuter,
    FlattenedDesert,
    SharpFlattenedDesert,
    Gravel,
    DesertBorder,
    DesertBorderOuter,
    SnowBorder,
    SnowBorderOuter,
    Mud,
    MudBorder,
    MudBorderOuter,
    Road,
    DryEarth,
}

public static class TerrainKinds
{
    public static int ImageNumber(this TerrainKind kind) => kind switch
    {
        TerrainKind.Grass => 0,
        TerrainKind.DryGrass => 1,
        TerrainKind.Desert => 18,
        TerrainKind.Earth => 2,
        TerrainKind.Mountain => 21,
        TerrainKind.Snow => 24,
        TerrainKind.Sand => 31,
        TerrainKind.Flattened => 35,
        TerrainKind.River1 or TerrainKind.River2 or TerrainKind.River3 or TerrainKind.River4 => 17,
        TerrainKind.MountainBorder => 22,
        TerrainKind.MountainBorderOuter => 23,
        TerrainKind.Water1 => 17,
        TerrainKind.Water2 => 16,
        TerrainKind.Water3 => 15,
        TerrainKind.Water4 => 14,
        TerrainKind.Water5 => 13,
        TerrainKind.Water6 => 12,
        TerrainKind.Water7 => 11,
        TerrainKind.Water8 => 10,
        TerrainKind.Moor => 7,
        TerrainKind.MoorBorder => 8,
        TerrainKind.MoorBorderOuter => 9,
        TerrainKind.FlattenedDesert or TerrainKind.SharpFlattenedDesert => 217,
        TerrainKind.Gravel => 230,
        TerrainKind.DesertBorder => 19,
        TerrainKind.DesertBorderOuter => 20,
        TerrainKind.SnowBorder => 25,
        TerrainKind.SnowBorderOuter => 26,
        TerrainKind.Mud => 4,
        TerrainKind.MudBorder => 5,
        TerrainKind.MudBorderOuter => 6,
        TerrainKind.Road => 34,
        TerrainKind.DryEarth => 36,
        _ => 0,
    };

    /// <summary>
    /// The kind drawn for a terrain type of the simulation (land spells change it); null for water and rivers, whose depth
    /// and flow only the map knows.
    /// </summary>
    public static TerrainKind? Of(TerrainType terrain) => terrain switch
    {
        TerrainType.Grass => TerrainKind.Grass,
        TerrainType.Sand => TerrainKind.Sand,
        TerrainType.Mountain => TerrainKind.Mountain,
        TerrainType.MountainBorder => TerrainKind.MountainBorder,
        TerrainType.MountainBorderOuter => TerrainKind.MountainBorderOuter,
        TerrainType.Desert => TerrainKind.Desert,
        TerrainType.DesertBorder => TerrainKind.DesertBorder,
        TerrainType.DesertBorderOuter => TerrainKind.DesertBorderOuter,
        TerrainType.Moor => TerrainKind.Moor,
        TerrainType.MoorBorder => TerrainKind.MoorBorder,
        TerrainType.MoorBorderOuter => TerrainKind.MoorBorderOuter,
        TerrainType.Snow => TerrainKind.Snow,
        TerrainType.SnowBorder => TerrainKind.SnowBorder,
        TerrainType.SnowBorderOuter => TerrainKind.SnowBorderOuter,
        TerrainType.Mud => TerrainKind.Mud,
        TerrainType.MudBorder => TerrainKind.MudBorder,
        TerrainType.MudBorderOuter => TerrainKind.MudBorderOuter,
        TerrainType.Flattened => TerrainKind.Flattened,
        _ => null,
    };

    /// <summary>Maps the landscape byte of an original map. Table from JSettlers' OriginalLandscape (MIT).</summary>
    public static TerrainKind FromOriginal(byte value) => value switch
    {
        <= 7 => TerrainKind.Water1 + value,
        16 => TerrainKind.Grass,
        17 => TerrainKind.MountainBorderOuter,
        20 => TerrainKind.DesertBorderOuter,
        21 => TerrainKind.MoorBorderOuter,
        23 => TerrainKind.MudBorderOuter,
        32 => TerrainKind.Mountain,
        33 => TerrainKind.MountainBorder,
        35 => TerrainKind.SnowBorderOuter,
        48 => TerrainKind.Sand,
        64 => TerrainKind.Desert,
        65 => TerrainKind.DesertBorder,
        80 => TerrainKind.Moor,
        81 => TerrainKind.MoorBorder,
        96 => TerrainKind.River1,
        97 => TerrainKind.River2,
        98 => TerrainKind.River3,
        99 => TerrainKind.River4,
        128 => TerrainKind.Snow,
        129 => TerrainKind.SnowBorder,
        144 => TerrainKind.Mud,
        145 => TerrainKind.MudBorder,
        // Not in the JSettlers table (66-71 occur in some maps, probably desert variants): fall back by group.
        _ => (value & 0xf0) switch
        {
            0x40 => TerrainKind.Desert,
            0x20 => TerrainKind.Mountain,
            0x80 => TerrainKind.Snow,
            _ => TerrainKind.Grass,
        },
    };
}
