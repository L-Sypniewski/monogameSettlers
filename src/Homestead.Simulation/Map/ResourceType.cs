namespace Homestead.Simulation.Map;

/// <summary>
/// Resources under the ground, in the order of the original maps' type nibble
/// (fish only in water, the others in mountains).
/// </summary>
public enum ResourceType : byte
{
    Fish,
    Coal,
    IronOre,
    GoldOre,
    Gems,
    Sulfur,
}
