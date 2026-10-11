namespace Homestead.Client.MapObjects;

/// <summary>Which image of GFX file 01 shows a map object, and whether it animates.</summary>
public readonly record struct MapObjectSprite(int Sequence, int Frame, bool Animated);

/// <summary>
/// Maps the object byte of original maps to graphics in GFX file 01. Object names follow the
/// original editor's naming as listed in JSettlers' EOriginalMapObjectType; the image assignment
/// was worked out by comparing the sequences with that list (see docs/formate/map.md).
/// </summary>
public static class MapObjectCatalog
{
    public const int GfxFile = 1;

    private const int StoneSequence = 29;       // big stones, stones, boundary stones
    private const int SmallStoneSequence = 30;
    private const int WreckSequence = 28;       // wrecks 1-5, grave
    private const int DecorationSequence = 27;  // plants, mushrooms, stumps, cacti, bones, flowers, shrubs, reeds
    private const int SmallTreeSequence = 22;
    private const int ResourceStoneSequence = 31;
    private const int GrainSequence = 23;       // 0-7 growing to ripe (JSettlers' MapObjectDrawer, MIT)

    public static MapObjectSprite? Resolve(byte code) => code switch
    {
        // Confirmed by matching counts and order with the object names.
        >= 1 and <= 20 => new(StoneSequence, code - 1, false),
        >= 29 and <= 34 => new(WreckSequence, code - 29, false),
        >= 35 and <= 67 => new(DecorationSequence, code - 35, false),
        // Trees: pairs of codes map to the two swaying variants of a tree type; each type
        // occupies three sequences (variant 1, variant 2, falling/stump/growing).
        >= 68 and <= 81 => new(1 + (code - 68) / 2 * 3 + (code - 68) % 2, 0, true),
        84 => new(SmallTreeSequence, 0, false),
        // Resource stones: 13 sizes. Which end is "full" is not certain yet;
        // JSettlers reads 115 as 12 stones left (largest image) and 127 as empty.
        >= 115 and <= 127 => new(ResourceStoneSequence, code - 115, false),
        // Plausible but unconfirmed: small stones and reefs share sequence 30 (8 + 4 images).
        >= 21 and <= 28 => new(SmallStoneSequence, code - 21, false),
        >= 111 and <= 114 => new(SmallStoneSequence, code - 111 + 8, false),
        // Grain fields: Homestead's own codes (see MapObjectCodes), one image per growing stage.
        >= 200 and <= 207 => new(GrainSequence, code - 200, false),
        // 192 occurs often but is unknown; 85-110 never occur.
        _ => null,
    };
}
