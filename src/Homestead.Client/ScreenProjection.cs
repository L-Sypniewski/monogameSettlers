using Homestead.Simulation;
using Microsoft.Xna.Framework;

namespace Homestead.Client;

/// <summary>
/// Grid to world-pixel mapping: 16 px between columns, 9 px between rows,
/// each row shifted half a tile to the left of the one above. Terrain height lifts a point
/// by <see cref="HeightStep"/> px per height unit.
/// </summary>
public static class ScreenProjection
{
    public const int TileWidth = 16;
    public const int RowHeight = 9;
    /// <summary>Pixels per height step. The original uses 2; we halve the relief (gentler hills).</summary>
    public const int HeightStep = 1;

    public static Vector2 ToWorld(GridPoint p) => ToWorld(p.X, p.Y, 0);

    public static Vector2 ToWorld(int x, int y, int height) =>
        new(x * TileWidth - y * TileWidth / 2f, y * RowHeight - height * HeightStep);
}

/// <summary>Axis-aligned rectangle in world pixels.</summary>
public readonly record struct RectangleF(float X, float Y, float Width, float Height)
{
    public bool Intersects(RectangleF other) =>
        X < other.X + other.Width && other.X < X + Width && Y < other.Y + other.Height && other.Y < Y + Height;
}
