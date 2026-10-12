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

    /// <summary>Field of a farm, vine, rice or beehive (walkable, but nothing else may be put there).</summary>
    Field,

    /// <summary>Codes we do not know yet.</summary>
    Unknown,
}

/// <summary>Original object codes the simulation creates or changes at runtime.</summary>
public static class MapObjectCodes
{
    public const byte None = 0;
    public const byte FirstPlant = 35;
    public const byte TreeStump = 41;

    /// <summary>Adult trees: 68-81, pairs of codes per tree type.</summary>
    public const byte FirstTree = 68;
    public const byte LastTree = 81;

    /// <summary>Young tree, planted by a forester.</summary>
    public const byte Sapling = 84;

    /// <summary>Quarry stone: 115 is full, each unit cut off moves one code up, 127 is the empty rubble.</summary>
    public const byte FullStone = 115;
    public const byte StoneRubble = 127;

    /// <summary>
    /// Grain: Homestead's own codes (unused in original maps). 200-206 growing stages, 207 ripe;
    /// harvesting clears the field.
    /// </summary>
    public const byte FirstGrain = 200;
    public const byte RipeGrain = 207;

    /// <summary>Vines (winegrower): 208-209 growing, 210 ripe.</summary>
    public const byte FirstWine = 208;
    public const byte RipeWine = 210;

    /// <summary>Rice (rice farmer, on swampy ground): 211-213 growing, 214 ripe.</summary>
    public const byte FirstRice = 211;
    public const byte RipeRice = 214;

    /// <summary>Beehives (beekeeper): 215-217 filling, 218 full of honey.</summary>
    public const byte FirstHive = 215;
    public const byte FullHive = 218;

    /// <summary>Last of Homestead's own field codes.</summary>
    public const byte LastField = FullHive;

    public static bool IsAdultTree(byte code) => code is >= FirstTree and <= LastTree;

    /// <summary>Stone units left in a quarry stone (0 for anything else).</summary>
    public static int StoneLeft(byte code) => code is >= FullStone and < StoneRubble ? StoneRubble - code : 0;
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
        >= MapObjectCodes.FirstGrain and <= MapObjectCodes.LastField => MapObjectKind.Field,
        _ => MapObjectKind.Unknown,
    };
}
