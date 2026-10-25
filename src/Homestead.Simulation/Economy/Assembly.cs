using Homestead.Simulation.Buildings;

namespace Homestead.Simulation.Economy;

/// <summary>
/// Something a building's worker puts together outside, unit by unit: he takes a unit from the building's piles, carries
/// it to the work spot and hammers it in (<see cref="Production"/>). The shipyard's ships (<see cref="Shipping"/>) and the
/// war machines and their ammunition (<see cref="Military.WarMachines"/>).
/// </summary>
internal interface IAssembly
{
    /// <summary>A pile of the building holding a unit the current work still needs, or null.</summary>
    Pile? NextUnit(Building building);

    /// <summary>The worker took a unit (or, <paramref name="back"/>, lost it on the way).</summary>
    void Take(Building building, Material material, bool back = false);

    /// <summary>The worker hammered a unit in; with the last one the work is done.</summary>
    void Build(Building building, Material material);

    /// <summary>Where the worker stands to work and which way he looks; null while there is none.</summary>
    (GridPoint Spot, Direction Facing)? WorkSpot(Building building);

    /// <summary>Ticks of hammering per unit.</summary>
    int BuildTicks { get; }

    /// <summary>Why the worker has nothing to do (shown to the player).</summary>
    string Idle(Building building);

    /// <summary>What the worker is putting together, for the status.</summary>
    string Work(Building building);
}
