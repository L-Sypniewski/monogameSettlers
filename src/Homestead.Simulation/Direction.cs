namespace Homestead.Simulation;

/// <summary>
/// The six neighbor directions of the map grid. Rows are shifted by half a tile, so on screen
/// the neighbors form a hexagon (E/W horizontal, the others diagonal).
/// </summary>
public enum Direction
{
    East,
    SouthEast,
    SouthWest,
    West,
    NorthWest,
    NorthEast,
}

public static class DirectionExtensions
{
    public const int Count = 6;

    public static GridPoint Delta(this Direction direction) => direction switch
    {
        Direction.East => new(1, 0),
        Direction.SouthEast => new(1, 1),
        Direction.SouthWest => new(0, 1),
        Direction.West => new(-1, 0),
        Direction.NorthWest => new(-1, -1),
        Direction.NorthEast => new(0, -1),
        _ => throw new ArgumentOutOfRangeException(nameof(direction)),
    };

    /// <summary>Next direction clockwise (as seen on screen).</summary>
    public static Direction Clockwise(this Direction direction) => (Direction)(((int)direction + 1) % Count);
}
