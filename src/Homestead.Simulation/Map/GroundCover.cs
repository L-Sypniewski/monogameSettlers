namespace Homestead.Simulation.Map;

/// <summary>What lies on the ground of a grass point, on top of its terrain. Settlers walk on it as on grass.</summary>
public enum GroundCover : byte
{
    None,

    /// <summary>Sown by a farm (see <c>farmland</c> in the crops of production.json); stays until the farm is gone.</summary>
    Farmland,

    /// <summary>Trodden by carriers at work (see <see cref="Trails"/>); settlers walk faster on it, it fades when unused.</summary>
    Trail,
}
