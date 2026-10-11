using Homestead.Simulation.Economy;

namespace Homestead.Simulation.Buildings;

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

    public int Player { get; }

    /// <summary>Anchor point; all definition offsets are relative to it.</summary>
    public GridPoint Position { get; }

    public GridPoint Door => Position + Definition.Door;

    public IReadOnlyList<Pile> Piles => _piles;

    /// <summary>False once the building was removed; carriers heading here must react.</summary>
    public bool Exists { get; internal set; } = true;

    internal void AddPile(Pile pile) => _piles.Add(pile);

    public IEnumerable<GridPoint> BlockedPoints => Definition.Blocked.Select(offset => Position + offset);
}
