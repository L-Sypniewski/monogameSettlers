using System.Diagnostics;
using Homestead.Assets;
using Homestead.Assets.Maps;
using Homestead.Simulation.Map;
using Homestead.Simulation.Pathfinding;
using Xunit.Abstractions;

namespace Homestead.Simulation.Tests;

/// <summary>
/// Checks against the maps of the local original installation. Without an installation these
/// tests pass without doing anything (assets are never part of the repository).
/// </summary>
public class OriginalMapTests(ITestOutputHelper output)
{
    private static GameInstallation? TryLocate()
    {
        try { return GameInstallation.Locate(); }
        catch (Exception) { return null; }
    }

    [Fact]
    public void Walkability_rules_match_the_accessibility_flags_of_the_maps()
    {
        if (TryLocate() is not { } installation)
        {
            output.WriteLine("No installation found, skipped.");
            return;
        }

        long total = 0, agree = 0;
        var disagreements = new Dictionary<string, long>();
        foreach (string path in installation.MapFiles)
        {
            var area = OriginalMap.Open(path).Area;
            var map = GameMap.FromOriginal(area);
            for (int i = 0; i < map.PointCount; i++)
            {
                bool flaggedBlocked = (area.Accessibility[i] & 1) != 0;
                bool ruleBlocked = !map.IsWalkable(i);
                total++;
                if (flaggedBlocked == ruleBlocked)
                {
                    agree++;
                    continue;
                }
                var p = map.Point(i);
                string key = $"{(flaggedBlocked ? "flag blocks" : "flag free")}: {map.TerrainAt(p)}/{map.ObjectAt(p)}";
                disagreements[key] = disagreements.GetValueOrDefault(key) + 1;
            }
        }

        double share = (double)agree / total;
        output.WriteLine($"Agreement: {share:P3} of {total} points");
        foreach (var (key, count) in disagreements.OrderByDescending(kv => kv.Value).Take(12))
            output.WriteLine($"  {count,9}  {key}");
        Assert.True(share > 0.995, $"Only {share:P2} agreement.");
    }

    [Fact]
    public void Path_search_on_large_maps_is_fast_and_correct()
    {
        if (TryLocate() is not { } installation)
        {
            output.WriteLine("No installation found, skipped.");
            return;
        }

        var largest = installation.MapFiles
            .Select(OriginalMap.Open)
            .OrderByDescending(m => m.Area.Size)
            .Take(3);

        var random = new Random(42);
        foreach (var original in largest)
        {
            var map = GameMap.FromOriginal(original.Area);
            var finder = new PathFinder(map);
            var walkable = Enumerable.Range(0, map.PointCount).Where(map.IsWalkable).ToArray();
            finder.FindPath(map.Point(walkable[0]), map.Point(walkable[1])); // builds regions

            int found = 0, unreachable = 0, expanded = 0;
            var watch = Stopwatch.StartNew();
            const int queries = 300;
            for (int q = 0; q < queries; q++)
            {
                var start = map.Point(walkable[random.Next(walkable.Length)]);
                var target = map.Point(walkable[random.Next(walkable.Length)]);
                var path = finder.FindPath(start, target);
                expanded += finder.LastExpandedNodes;
                if (path is null)
                {
                    unreachable++;
                    continue;
                }
                found++;
                if (q % 30 == 0)
                {
                    // Spot-check optimality against the reference search.
                    Assert.Equal(Reference.Distances(map, start)[map.Index(target)], path.Count);
                    PathFinderTests.AssertValid(map, start, target, path);
                }
            }
            watch.Stop();

            double msPerQuery = watch.Elapsed.TotalMilliseconds / queries;
            output.WriteLine($"{original.Name} ({map.Size}): {found} paths, {unreachable} unreachable, " +
                             $"{msPerQuery:F2} ms/query, {expanded / queries} nodes/query");
            Assert.True(msPerQuery < 50, $"{msPerQuery:F1} ms per query is too slow.");
        }
    }
}
