using Homestead.Simulation.Economy;

namespace Homestead.Simulation.Buildings;

public enum BuildingState
{
    /// <summary>Diggers level the ground; the footprint is reserved but still walkable.</summary>
    Flattening,

    /// <summary>Builders turn delivered material into the building.</summary>
    Constructing,

    Finished,
}

/// <summary>A building placed on the map at its anchor point.</summary>
public sealed class Building
{
    private readonly List<Pile> _piles = new();

    internal Building(int id, BuildingDefinition definition, int player, GridPoint position)
    {
        Id = id;
        Definition = definition;
        Player = player;
        Position = position;
        WorkCenter = position + (definition.WorkCenter ?? new GridPoint(0, 0));
    }

    public int Id { get; }

    public BuildingDefinition Definition { get; }

    public int Player { get; internal set; }

    /// <summary>Anchor point; all definition offsets are relative to it.</summary>
    public GridPoint Position { get; }

    public GridPoint Door => Position + Definition.Door;

    /// <summary>Whether the building has a work area its worker gathers in (and the player can move).</summary>
    public bool HasWorkArea => Definition.WorkRadius is > 0;

    /// <summary>The work area is only a gathering point (barracks: new soldiers walk there), as far away as the player likes.</summary>
    public bool HasGatheringPoint => Definition.Name == "barrack";

    /// <summary>Center of the work area: the definition's default until the player moves it (<see cref="World.SetWorkCenter"/>).</summary>
    public GridPoint WorkCenter { get; internal set; }

    public IReadOnlyList<Pile> Piles => _piles;

    /// <summary>Production stopped by the player (<see cref="World.SetPaused"/>): the worker stays inside once its work is done.</summary>
    public bool IsPaused { get; internal set; }

    /// <summary>False once the building was removed; carriers heading here must react.</summary>
    public bool Exists { get; internal set; } = true;

    public BuildingState State { get; internal set; } = BuildingState.Finished;

    /// <summary>Material units the construction needs in total, and how many were built in so far.</summary>
    public int BuildStepsTotal { get; internal set; }

    public int BuildStepsDone { get; internal set; }

    /// <summary>0 at the start of the construction phase, 1 when finished.</summary>
    public float ConstructionProgress => State == BuildingState.Finished ? 1
        : BuildStepsTotal == 0 ? 0 : (float)BuildStepsDone / BuildStepsTotal;

    /// <summary>Height the footprint is levelled to.</summary>
    public int TargetHeight { get; internal set; }

    /// <summary>Points the diggers still have to level (footprint and the slope around it), with their target height.</summary>
    internal Dictionary<GridPoint, int> Unlevelled { get; } = new();

    internal void RemovePile(Pile pile) => _piles.Remove(pile);

    internal void AddPile(Pile pile) => _piles.Add(pile);

    public IEnumerable<GridPoint> BlockedPoints => Definition.Blocked.Select(offset => Position + offset);
}
