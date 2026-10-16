namespace Homestead.Simulation.Economy;

/// <summary>
/// How many units of each good a player produced over time (Siedler › Produktion): every unit a producer
/// brings out of its building (workshops, mines, smiths, gatherers) counts, in periods of
/// <see cref="PeriodTicks"/>.
/// </summary>
public sealed class ProductionStatistics
{
    /// <summary>Length of one period: 30 seconds.</summary>
    public const int PeriodTicks = 30 * World.TicksPerSecond;

    private readonly World _world;
    private readonly Dictionary<(int Player, Material Material), List<int>> _periods = new();

    internal ProductionStatistics(World world)
    {
        _world = world;
    }

    /// <summary>The period the current tick lies in (0 at the start of the game).</summary>
    public int CurrentPeriod => (int)(_world.Tick / PeriodTicks);

    internal void Record(int player, Material material)
    {
        if (!_periods.TryGetValue((player, material), out var periods))
            _periods[(player, material)] = periods = new List<int>();
        int period = CurrentPeriod;
        while (periods.Count <= period)
            periods.Add(0);
        periods[period]++;
    }

    /// <summary>Units of the good the player produced in a period (0 for periods not reached or before the start).</summary>
    public int Produced(int player, Material material, int period) =>
        period >= 0 && _periods.TryGetValue((player, material), out var periods) && period < periods.Count ? periods[period] : 0;

    /// <summary>Units of the good the player produced in all.</summary>
    public int Total(int player, Material material) =>
        _periods.TryGetValue((player, material), out var periods) ? periods.Sum() : 0;
}
