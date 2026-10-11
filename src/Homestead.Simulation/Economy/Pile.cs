using Homestead.Simulation.Buildings;

namespace Homestead.Simulation.Economy;

public enum PileRole
{
    /// <summary>Goods lying around (from the map or dropped by a carrier). Offered to anyone.</summary>
    Loose,

    /// <summary>Input pile of a building: carriers fill it.</summary>
    Request,

    /// <summary>Output pile of a building: carriers empty it.</summary>
    Offer,
}

/// <summary>
/// A pile of one material on a grid point. Reservations keep carriers from being sent to the same
/// unit twice: <see cref="Reserved"/> units are promised to carriers on their way to pick them up,
/// <see cref="Incoming"/> units are on their way here.
/// </summary>
public sealed class Pile
{
    internal Pile(int id, GridPoint position, PileRole role, Material? material, int capacity, Building? owner)
    {
        Id = id;
        Position = position;
        Role = role;
        Material = material;
        Capacity = capacity;
        Owner = owner;
    }

    public int Id { get; }

    public GridPoint Position { get; }

    public PileRole Role { get; }

    /// <summary>Current material. Null for an empty request pile that accepts any material (storehouse).</summary>
    public Material? Material { get; internal set; }

    /// <summary>For request piles: the material it was defined for, null = any.</summary>
    public Material? AcceptedMaterial => Role == PileRole.Request ? _accepted : Material;

    private Material? _accepted;

    public int Count { get; internal set; }

    public int Capacity { get; }

    public int Reserved { get; internal set; }

    public int Incoming { get; internal set; }

    public Building? Owner { get; }

    /// <summary>Units carriers may still be sent to pick up.</summary>
    public int Available => Role == PileRole.Request ? 0 : Count - Reserved;

    /// <summary>Units carriers may still bring.</summary>
    public int Space => Role == PileRole.Request ? Capacity - Count - Incoming : 0;

    public bool Accepts(Material material) =>
        Role == PileRole.Request && Space > 0 && (Material ?? _accepted ?? material) == material;

    internal static Pile CreateRequest(int id, GridPoint position, Material? accepted, int capacity, Building owner) =>
        new(id, position, PileRole.Request, accepted, capacity, owner) { _accepted = accepted };
}
