using Homestead.Simulation;
using Microsoft.Xna.Framework;

namespace Homestead.Client;

/// <summary>
/// Grid to world-pixel mapping: 16 px between columns, 9 px between rows,
/// each row shifted half a tile to the left of the one above.
/// </summary>
public static class ScreenProjection
{
    public const int TileWidth = 16;
    public const int RowHeight = 9;

    public static Vector2 ToWorld(GridPoint p) => new(p.X * TileWidth - p.Y * TileWidth / 2f, p.Y * RowHeight);
}
