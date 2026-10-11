namespace Homestead.Simulation;

/// <summary>
/// Simulation state, advanced in fixed ticks independent of the frame rate.
/// </summary>
public sealed class World
{
    public const int TicksPerSecond = 20;

    public static TimeSpan TickDuration { get; } = TimeSpan.FromSeconds(1.0 / TicksPerSecond);

    public List<Walker> Walkers { get; } = new();

    public long Tick { get; private set; }

    public void Step()
    {
        foreach (var walker in Walkers)
            walker.Tick();
        Tick++;
    }
}
