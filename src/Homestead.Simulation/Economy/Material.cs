namespace Homestead.Simulation.Economy;

/// <summary>Goods that settlers carry and that lie in piles.</summary>
public enum Material : byte
{
    Plank,
    Stone,
    Trunk,
    Bread,
    Coal,
    GoldOre,
    IronOre,
    Fish,
    Crop,
    Gold,
    Iron,
    Blade,
    Hammer,
    Axe,
    Pick,
    Saw,
    FishingRod,
    Sword,
    Bow,
    Spear,
    Wine,
    Flour,
    Pig,
    Meat,
    Sulfur,
    Water,
    Rice,
    Gems,
    Keg,
    Scythe,
    Honey,
    Mead,
    Liquor,
    GunPowder,
}

public static class Materials
{
    /// <summary>Most units a single pile holds.</summary>
    public const int MaxPileSize = 8;

    /// <summary>
    /// Parses names as used in the data files (upper snake case, e.g. <c>GOLDORE</c>, <c>GUN_POWDER</c>).
    /// Returns null for <c>NO_MATERIAL</c>, which in pile definitions means "any material".
    /// </summary>
    public static Material? Parse(string name)
    {
        if (name == "NO_MATERIAL")
            return null;
        if (Enum.TryParse<Material>(name.Replace("_", ""), ignoreCase: true, out var material))
            return material;
        throw new FormatException($"Unknown material '{name}'.");
    }

    /// <summary>Pile type byte of original maps (chunk 9). Table from JSettlers' EOriginalMapStackType (MIT).</summary>
    public static Material? FromOriginalStack(int code) => code switch
    {
        1 => Material.Plank,
        2 => Material.Stone,
        3 => Material.Trunk,
        4 => Material.Bread,
        5 => Material.Coal,
        6 => Material.GoldOre,
        7 => Material.IronOre,
        8 => Material.Fish,
        9 => Material.Crop,
        10 => Material.Gold,
        11 => Material.Iron,
        12 => Material.Blade,
        13 => Material.Hammer,
        14 => Material.Axe,
        15 => Material.Pick,
        16 => Material.Saw,
        17 => Material.FishingRod,
        18 => Material.Sword,
        19 => Material.Bow,
        20 => Material.Spear,
        21 => Material.Wine,
        22 => Material.Flour,
        23 => Material.Pig,
        24 => Material.Meat,
        25 => Material.Sulfur,
        26 => Material.Water,
        27 => Material.Rice,
        28 => Material.Gems,
        29 => Material.Keg,
        30 => Material.Scythe,
        31 => Material.Honey,
        _ => null,
    };
}
