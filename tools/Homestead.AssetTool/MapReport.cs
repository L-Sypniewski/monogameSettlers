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

    /// <summary>For every resource type (high nibble): how often per landscape group and the amount range.</summary>
    public static void Resources(GameInstallation installation)
    {
        var counts = new SortedDictionary<(int Type, int Group), int>();
        var amounts = new SortedDictionary<(int Type, int Amount), int>();
        foreach (string path in installation.MapFiles)
        {
            var area = OriginalMap.Open(path).Area;
            for (int i = 0; i < area.Resources.Length; i++)
            {
                int value = area.Resources[i];
                if (value == 0)
                    continue;
                counts[(value >> 4, area.Landscape[i] >> 4)] = counts.GetValueOrDefault((value >> 4, area.Landscape[i] >> 4)) + 1;
                amounts[(value >> 4, value & 15)] = amounts.GetValueOrDefault((value >> 4, value & 15)) + 1;
            }
        }
        foreach (var type in counts.Keys.Select(k => k.Type).Distinct())
        {
            Console.WriteLine($"type {type}: groups " + string.Join(", ", counts.Where(kv => kv.Key.Type == type).Select(kv => $"0x{kv.Key.Group:x}0:{kv.Value}")));
            Console.WriteLine($"        amounts " + string.Join(", ", amounts.Where(kv => kv.Key.Type == type).Select(kv => $"{kv.Key.Amount}:{kv.Value}")));
        }
    }

    public static void Minimap(GameInstallation installation, string name, string outPath)
    {
        string path = installation.MapFiles.First(f =>
            Path.GetFileNameWithoutExtension(f).Equals(name, StringComparison.OrdinalIgnoreCase));
        var area = OriginalMap.Open(path).Area;

        Png.Write(outPath, new GfxBitmap(area.Size, area.Size, 0, 0, MapOverview.Pixels(area)));
    }
}
