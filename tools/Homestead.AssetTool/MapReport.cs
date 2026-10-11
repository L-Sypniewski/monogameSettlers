using Homestead.Assets;
using Homestead.Assets.Gfx;
using Homestead.Assets.Maps;

namespace Homestead.AssetTool;

internal static class MapReport
{
    // Landscape values known from JSettlers' OriginalLandscape table.
    private static readonly HashSet<int> KnownLandscapes =
        [0, 1, 2, 3, 4, 5, 6, 7, 16, 17, 20, 21, 23, 32, 33, 35, 48, 64, 65, 80, 81, 96, 97, 98, 99, 128, 129, 144, 145];

    public static void Scan(GameInstallation installation)
    {
        int ok = 0, failed = 0;
        var unknown = new SortedDictionary<int, int>();
        var objects = new SortedDictionary<int, int>();
        foreach (string path in installation.MapFiles)
        {
            try
            {
                var map = OriginalMap.Open(path);
                foreach (byte value in map.Area.Object)
                    objects[value] = objects.GetValueOrDefault(value) + 1;
                foreach (byte value in map.Area.Landscape)
                    if (!KnownLandscapes.Contains(value))
                        unknown[value] = unknown.GetValueOrDefault(value) + 1;
                Console.WriteLine($"{map.Name,-40} {map.Area.Size,4}  players {map.PlayerCount}  " +
                                  $"{(map.IsSinglePlayer ? "single" : "multi ")}  v{map.Version:x}");
                ok++;
            }
            catch (Exception e)
            {
                Console.WriteLine($"{Path.GetFileName(path),-40} FAILED: {e.Message}");
                failed++;
            }
        }
        Console.WriteLine($"Total: {ok} ok, {failed} failed");
        Console.WriteLine("Object values: " + string.Join(", ", objects.Select(kv => $"{kv.Key}:{kv.Value}")));
        Console.WriteLine("Unknown landscape values: " +
                          (unknown.Count == 0 ? "none" : string.Join(", ", unknown.Select(kv => $"{kv.Key} ({kv.Value}x)"))));
    }

    public static void Minimap(GameInstallation installation, string name, string outPath)
    {
        string path = installation.MapFiles.First(f =>
            Path.GetFileNameWithoutExtension(f).Equals(name, StringComparison.OrdinalIgnoreCase));
        var area = OriginalMap.Open(path).Area;

        var pixels = new uint[area.Size * area.Size];
        for (int y = 0; y < area.Size; y++)
        for (int x = 0; x < area.Size; x++)
        {
            int i = area.Index(x, y);
            uint color = BaseColor(area.Landscape[i]);
            // Simple relief shading from the height difference to the point above.
            int dh = y > 0 ? area.Height[i] - area.Height[area.Index(x, y - 1)] : 0;
            float light = Math.Clamp(1f + dh * 0.12f, 0.5f, 1.4f);
            pixels[i] = Shade(color, light);
        }
        Png.Write(outPath, new GfxBitmap(area.Size, area.Size, 0, 0, pixels));
    }

    private static uint BaseColor(byte landscape) => (landscape & 0xf0) switch
    {
        0x00 => Rgb(30, 80, 200),    // water
        0x10 => Rgb(40, 120, 30),    // grass and borders
        0x20 => Rgb(110, 105, 100),  // mountain
        0x30 => Rgb(200, 180, 90),   // sand / beach
        0x40 => Rgb(210, 170, 80),   // desert
        0x50 => Rgb(30, 60, 40),     // moor
        0x60 => Rgb(60, 120, 220),   // river
        0x80 => Rgb(235, 240, 245),  // snow
        0x90 => Rgb(90, 60, 40),     // mud
        _ => Rgb(255, 0, 255),       // unknown
    };

    private static uint Rgb(uint r, uint g, uint b) => 0xff000000 | (b << 16) | (g << 8) | r;

    private static uint Shade(uint c, float f)
    {
        uint Ch(int shift) => (uint)Math.Clamp((int)(((c >> shift) & 0xff) * f), 0, 255) << shift;
        return 0xff000000 | Ch(16) | Ch(8) | Ch(0);
    }
}
