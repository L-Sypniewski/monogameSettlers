using Homestead.Simulation.Economy;

namespace Homestead.Simulation.Tests;

internal static class TestConfig
{
    /// <summary>The real balancing, but every trade works without a tool (tests that are not about tools).</summary>
    public static ProductionConfig NoTools { get; } = ProductionConfig.Load() with { Tools = new Dictionary<string, Material>() };
}
