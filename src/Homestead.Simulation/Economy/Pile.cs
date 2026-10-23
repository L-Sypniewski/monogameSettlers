using Homestead.Simulation.Buildings;

namespace Homestead.Simulation.Economy;

public enum PileRole
{
    /// <summary>Goods lying around (from the map or dropped by a carrier). Offered to anyone.</summary>
    Loose,

    /// <summary>Input pile of a building or construction site: carriers fill it.</summary>
    Request,

    /// <summary>Output pile of a building: carriers empty it.</summary>
    Offer,

    /// <summary>Storehouse pile: takes any material (one at a time) and hands it out again.</summary>
    Storage,

    /// <summary>
    /// Market pile: takes the goods the player sends along the market's trade route (one material at a time); only
    /// donkeys take them away (see <see cref="Economy.Trade"/>).
    /// </summary>
    Trade,
}

/// <summary>
/// A pile of one material on a grid point. Reservations keep carriers from being sent to the same
/// unit twice: <see cref="Reserved"/> units are promised to carriers on their way to pick them up,
/// <see cref="Incoming"/> units are on their way here.
/// </summary>
public sealed class Pile
{
    private readonly Material? _accepted;

    internal Pile(int id, GridPoint position, PileRole role, Material? material, int capacity, Building? owner)
    {
        Id = id;
        Position = position;
        Role = role;
        Material = material;
        _accepted = role == PileRole.Request ? material : null;
        Capacity = capacity;
        Owner = owner;
    }

    public int Id { get; }

    public GridPoint Position { get; }

    public PileRole Role { get; }

    /// <summary>Current material. Null for an empty storehouse pile.</summary>
    public Material? Material { get; internal set; }

    /// <summary>For request piles the material they were defined for; null for storehouse piles (any).</summary>
    public Material? AcceptedMaterial => Role switch
    {
        PileRole.Request => _accepted,
        PileRole.Storage or PileRole.Trade => null,
        _ => Material,
    };

    public int Count { get; internal set; }

    /// <summary>Most units the pile takes. Construction piles count down as material is used up.</summary>
    public int Capacity { get; internal set; }

    public int Reserved { get; internal set; }

    public int Incoming { get; internal set; }

    public Building? Owner { get; }

    private bool Receives => Role is PileRole.Request or PileRole.Storage or PileRole.Trade;

    /// <summary>Units carriers may still be sent to pick up.</summary>
    public int Available => Role is PileRole.Request or PileRole.Trade ? 0 : Count - Reserved;

    /// <summary>Units carriers may still bring.</summary>
    public int Space => Receives ? Capacity - Count - Incoming : 0;

    public bool Accepts(Material material) => Receives && Space > 0 && (Material ?? _accepted ?? material) == material;

    /// <summary>
    /// Whether the pile may accept the material at any time during one dispatch of the carriers: a storehouse
    /// pile that is empty may take it again once a job to it is called off (<see cref="ResetMaterialIfEmpty"/>).
    /// </summary>
    internal bool MayAccept(Material material) => Role switch
    {
        PileRole.Request => (_accepted ?? material) == material,
        PileRole.Storage or PileRole.Trade => Material is null || Material == material || Count == 0,
        _ => false,
    };

    /// <summary>A storehouse pile that ran empty takes any material again.</summary>
    internal void ResetMaterialIfEmpty()
    {
        if (Receives && Count == 0 && Incoming == 0 && Reserved == 0)
            Material = AcceptedMaterial;
    }
}
