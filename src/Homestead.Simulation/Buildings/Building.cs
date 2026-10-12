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
    }

    public int Id { get; }

    public BuildingDefinition Definition { get; }

    public int Player { get; internal set; }

    /// <summary>Anchor point; all definition offsets are relative to it.</summary>
    public GridPoint Position { get; }

    public GridPoint Door => Position + Definition.Door;

    public IReadOnlyList<Pile> Piles => _piles;

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

    /// <summary>Footprint points the diggers still have to level.</summary>
    internal HashSet<GridPoint> Unlevelled { get; } = new();

    internal void RemovePile(Pile pile) => _piles.Remove(pile);

    internal void AddPile(Pile pile) => _piles.Add(pile);

    public IEnumerable<GridPoint> BlockedPoints => Definition.Blocked.Select(offset => Position + offset);
}
