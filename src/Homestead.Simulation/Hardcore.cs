using Homestead.Simulation.Economy;

namespace Homestead.Simulation;

/// <summary>
/// Head start for hardcore players (chosen for the opponents on the start screen): more combat strength,
/// more residents per living house, tougher soldiers, cheaper buildings, faster production and a richer
/// start (multiplayer start equipment only).
/// </summary>
public static class Hardcore
{
    /// <summary>Added to the combat strength everywhere (flat, may lift it above 100 %).</summary>
    public const float CombatStrengthBonus = 0.1f;

    /// <summary>Residents a new living house releases, as a factor.</summary>
    public const double ResidentsFactor = 1.3;

    /// <summary>Hit points of soldiers, as a factor.</summary>
    public const float HealthFactor = 1.2f;

    /// <summary>Construction material, as a factor.</summary>
    public const double CostFactor = 0.75;

    /// <summary>Work times of production buildings (workshops, mines, smiths, gatherers), as a factor.</summary>
    public const double ProductionFactor = 0.9;

    /// <summary>Extra swordsmen in the start equipment (against an early rush).</summary>
    public const int StartSoldiers = 2;

    /// <summary>Extra goods piles in the start equipment.</summary>
    public static IReadOnlyList<(Material Material, int Count)> StartGoods { get; } = [(Material.Plank, 5), (Material.Trunk, 5)];

    /// <summary>A production work time of <paramref name="ticks"/> for a hardcore player (at least 1).</summary>
    public static int WorkTicks(int ticks) => ticks <= 0 ? ticks : Math.Max(1, (int)Math.Round(ticks * ProductionFactor));

    /// <summary>Residents a living house of <paramref name="total"/> gives a hardcore player.</summary>
    public static int Residents(int total) => (int)Math.Round(total * ResidentsFactor, MidpointRounding.AwayFromZero);

    /// <summary>What a construction pile of <paramref name="required"/> units asks of a hardcore player (at least 1).</summary>
    public static int Cost(int required) =>
        required <= 0 ? required : Math.Max(1, (int)Math.Round(required * CostFactor, MidpointRounding.AwayFromZero));
}
