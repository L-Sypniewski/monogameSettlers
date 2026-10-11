namespace Homestead.Simulation;

/// <summary>A node of the map grid.</summary>
public readonly record struct GridPoint(int X, int Y)
{
    public static GridPoint operator +(GridPoint a, GridPoint b) => new(a.X + b.X, a.Y + b.Y);

    public GridPoint Neighbor(Direction direction) => this + direction.Delta();
}
