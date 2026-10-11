using Homestead.Assets.Gfx;
using Microsoft.Xna.Framework.Graphics;

namespace Homestead.Client.Terrain;

/// <summary>
/// One texture holding all landscape images of file 00, laid out on a grid of 32 px cells.
/// Continuous textures get a cell block one cell larger than their repeat size and are filled
/// by repeating the image, so triangles can sample across the repeat seam without wrapping.
/// Layout from JSettlers' Background (MIT).
/// </summary>
public static class TerrainAtlas
{
    public const int Size = 1024;
    public const int Cell = 32;

    /// <summary>Per landscape image: cell x, cell y, cell count (1 = single tile, n = (n-1) cells repeat size).</summary>
    public static readonly (int X, int Y, int Cells)[] Positions =
    [
        (0, 0, 5), (5, 0, 5), (10, 0, 5), (15, 0, 5), (20, 0, 5), (30, 0, 1), (31, 0, 1), (25, 0, 5),
        (30, 1, 1), (31, 1, 1), (0, 5, 5), (0, 20, 2), (2, 20, 2), (4, 20, 2), (6, 20, 2), (8, 20, 2),
        (10, 20, 2), (12, 20, 2), (5, 5, 5), (31, 5, 1), (30, 6, 1), (10, 5, 5), (31, 6, 1), (30, 7, 1),
        (15, 5, 5), (31, 7, 1), (30, 8, 1), (31, 8, 1), (30, 9, 1), (31, 9, 1), (30, 10, 1), (20, 5, 5),
        (31, 10, 1), (30, 11, 1), (31, 11, 1), (25, 5, 5), (0, 10, 5), (30, 13, 1), (31, 13, 1), (30, 14, 1),
        (31, 14, 1), (0, 15, 1), (1, 15, 1), (2, 15, 1), (3, 15, 1), (4, 15, 1), (5, 15, 1), (6, 15, 1),
        (7, 15, 1), (8, 15, 1), (9, 15, 1), (10, 15, 1), (11, 15, 1), (12, 15, 1), (13, 15, 1), (14, 15, 1),
        (15, 15, 1), (16, 15, 1), (17, 15, 1), (18, 15, 1), (19, 15, 1), (20, 15, 1), (21, 15, 1), (22, 15, 1),
        (23, 15, 1), (24, 15, 1), (25, 15, 1), (26, 15, 1), (27, 15, 1), (28, 15, 1), (29, 15, 1), (30, 15, 1),
        (31, 15, 1), (0, 16, 1), (1, 16, 1), (2, 16, 1), (3, 16, 1), (4, 16, 1), (5, 16, 1), (6, 16, 1),
        (7, 16, 1), (8, 16, 1), (9, 16, 1), (10, 16, 1), (11, 16, 1), (12, 16, 1), (13, 16, 1), (14, 16, 1),
        (15, 16, 1), (16, 16, 1), (17, 16, 1), (18, 16, 1), (19, 16, 1), (20, 16, 1), (21, 16, 1), (22, 16, 1),
        (23, 16, 1), (24, 16, 1), (30, 16, 1), (31, 12, 1), (25, 12, 1), (26, 16, 1), (27, 16, 1), (28, 16, 1),
        (29, 16, 1), (30, 16, 1), (31, 16, 1), (0, 17, 1), (1, 17, 1), (2, 17, 1), (3, 17, 1), (4, 17, 1),
        (5, 17, 1), (6, 17, 1), (7, 17, 1), (8, 17, 1), (9, 17, 1), (10, 17, 1), (11, 17, 1), (12, 17, 1),
        (13, 17, 1), (14, 17, 1), (15, 17, 1), (16, 17, 1), (17, 17, 1), (18, 17, 1), (19, 17, 1), (20, 17, 1),
        (21, 17, 1), (22, 17, 1), (23, 17, 1), (24, 17, 1), (25, 17, 1), (26, 17, 1), (27, 17, 1), (28, 17, 1),
        (29, 17, 1), (30, 17, 1), (31, 17, 1), (0, 18, 1), (1, 18, 1), (2, 18, 1), (3, 18, 1), (4, 18, 1),
        (5, 18, 1), (6, 18, 1), (7, 18, 1), (8, 18, 1), (9, 18, 1), (10, 18, 1), (11, 18, 1), (12, 18, 1),
        (13, 18, 1), (14, 18, 1), (15, 18, 1), (16, 18, 1), (17, 18, 1), (18, 18, 1), (19, 18, 1), (20, 18, 1),
        (21, 18, 1), (22, 18, 1), (23, 18, 1), (24, 18, 1), (25, 18, 1), (26, 18, 1), (27, 18, 1), (28, 18, 1),
        (29, 18, 1), (30, 18, 1), (31, 18, 1), (0, 19, 1), (1, 19, 1), (2, 19, 1), (3, 19, 1), (4, 19, 1),
        (5, 10, 5), (6, 19, 1), (7, 19, 1), (8, 19, 1), (9, 19, 1), (10, 19, 1), (11, 19, 1), (12, 19, 1),
        (13, 19, 1), (14, 19, 1), (15, 19, 1), (16, 19, 1), (17, 19, 1), (18, 19, 1), (19, 19, 1), (20, 19, 1),
        (21, 19, 1), (22, 19, 1), (23, 19, 1), (24, 19, 1), (25, 19, 1), (26, 19, 1), (27, 19, 1), (28, 19, 1),
        (29, 19, 1), (30, 19, 1), (31, 19, 1), (0, 22, 1), (1, 22, 1), (2, 22, 1), (3, 22, 1), (4, 22, 1),
        (5, 22, 1), (6, 22, 1), (7, 22, 1), (8, 22, 1), (9, 22, 1), (10, 22, 1), (11, 22, 1), (12, 22, 1),
        (13, 22, 1), (14, 20, 5), (1, 23, 1), (2, 23, 1), (3, 23, 1), (4, 23, 1), (5, 23, 1), (6, 23, 1),
        (7, 23, 1), (8, 23, 1), (9, 23, 1), (10, 23, 1), (11, 23, 1), (12, 23, 1), (19, 20, 5), (13, 23, 1),
        (0, 24, 1), (1, 24, 1), (2, 24, 1),
    ];

    public static Texture2D Build(GraphicsDevice device, GfxFile landscapeFile)
    {
        var data = new uint[Size * Size];
        int count = Math.Min(Positions.Length, landscapeFile.LandscapeCount);
        for (int index = 0; index < count; index++)
        {
            var bitmap = landscapeFile.ReadLandscape(index);
            if (bitmap.Width == 0 || bitmap.Height == 0)
                continue;

            var (cellX, cellY, cells) = Positions[index];
            int left = cellX * Cell, top = cellY * Cell, extent = cells * Cell;
            for (int y = 0; y < extent && top + y < Size; y++)
            {
                int srcRow = y % bitmap.Height * bitmap.Width;
                int dstRow = (top + y) * Size + left;
                for (int x = 0; x < extent && left + x < Size; x++)
                    data[dstRow + x] = bitmap.Pixels[srcRow + x % bitmap.Width];
            }
        }

        var texture = new Texture2D(device, Size, Size);
        texture.SetData(data);
        return texture;
    }
}
