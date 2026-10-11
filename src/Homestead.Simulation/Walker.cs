namespace Homestead.Simulation;

/// <summary>
/// A figure that walks a fixed, looping route of single grid steps.
/// A step takes <see cref="StepTicks"/> ticks; turning happens instantly at the start of a step.
/// </summary>
public sealed class Walker
{
    private readonly IReadOnlyList<Direction> _route;
    private int _routeIndex;

    public Walker(GridPoint start, IReadOnlyList<Direction> route, int stepTicks)
    {
        if (route.Count == 0)
            throw new ArgumentException("Route must not be empty.", nameof(route));
        ArgumentOutOfRangeException.ThrowIfLessThan(stepTicks, 1);

        Position = start;
        _route = route;
        StepTicks = stepTicks;
        Facing = route[0];
    }

    /// <summary>The grid point the current step started from.</summary>
    public GridPoint Position { get; private set; }

    public Direction Facing { get; private set; }

    public GridPoint Target => Position.Neighbor(Facing);

    public int StepTicks { get; }

    /// <summary>Ticks already spent on the current step, 0 to <see cref="StepTicks"/> - 1.</summary>
    public int TicksIntoStep { get; private set; }

    public void Tick()
    {
        if (++TicksIntoStep < StepTicks)
            return;

        TicksIntoStep = 0;
        Position = Target;
        _routeIndex = (_routeIndex + 1) % _route.Count;
        Facing = _route[_routeIndex];
    }

    /// <summary>
    /// A closed hexagon with the given side length: walks every direction once, clockwise.
    /// Start at the hexagon's north-west corner.
    /// </summary>
    public static IReadOnlyList<Direction> HexagonRoute(int sideLength)
    {
        var route = new List<Direction>();
        var direction = Direction.East;
        for (int side = 0; side < DirectionExtensions.Count; side++, direction = direction.Clockwise())
            route.AddRange(Enumerable.Repeat(direction, sideLength));
        return route;
    }
}
