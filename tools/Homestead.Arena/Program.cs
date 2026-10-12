using System.Diagnostics;
using Homestead.Assets;
using Homestead.Assets.Maps;
using Homestead.Simulation;
using Homestead.Simulation.Ai;
using Homestead.Simulation.Buildings;
using Homestead.Simulation.Economy;

// AI against AI without a window: every player of a map is an AI player, the game runs as fast as
// it can, and every few game minutes the players' key figures are printed.
//   Homestead.Arena [map names...] [--all] [--minutes N] [--every N] [--peoples amazon,roman] [--log]
// Without map names: the first multiplayer map without start buildings. --all: every such map.
// --peoples: peoples for players the map leaves open (player i: entry i mod count; default Romans).

string[] valueOptions = ["--minutes", "--every", "--peoples"];
var names = args.Where((a, i) => !a.StartsWith("--") && (i == 0 || !valueOptions.Contains(args[i - 1]))).ToList();
int minutes = IntOption("--minutes", 30);
int every = IntOption("--every", 5);
bool all = args.Contains("--all");
bool showLog = args.Contains("--log");
int peoplesAt = Array.IndexOf(args, "--peoples");
var peoples = peoplesAt >= 0 && peoplesAt + 1 < args.Length
    ? args[peoplesAt + 1].Split(',').Select(p => Enum.Parse<Civilization>(p.Trim(), ignoreCase: true)).ToArray()
    : null;

var installation = GameInstallation.Locate();
var maps = installation.MapFiles
    .Where(f => names.Count == 0 ? true : names.Any(n => Path.GetFileNameWithoutExtension(f).Equals(n, StringComparison.OrdinalIgnoreCase)))
    .Select(f => (File: f, Map: OriginalMap.Open(f)))
    .Where(m => names.Count > 0 || m.Map is { IsSinglePlayer: false, Buildings.Count: 0 })
    .ToList();
if (!all && names.Count == 0)
    maps = maps.Take(1).ToList();

var siteSince = new Dictionary<int, long>();
foreach (var (file, original) in maps)
{
    siteSince.Clear();
    var (world, report) = OriginalScenario.Load(original, freeChoice: peoples);
    for (int player = 0; player < report.Catalogs.Count; player++)
        world.AiPlayers.Add(new AiPlayer(world, report.Catalogs[player], player));
    Console.WriteLine($"== {Path.GetFileNameWithoutExtension(file)}: {report.Catalogs.Count} players " +
                      $"({string.Join(", ", report.Catalogs.Select(c => c.Civilization))}), map {world.Map!.Size}");

    var watch = Stopwatch.StartNew();
    long ticks = (long)minutes * 60 * World.TicksPerSecond;
    for (long t = 1; t <= ticks; t++)
    {
        world.Step();
        if (t % (every * 60 * World.TicksPerSecond) == 0 || t == ticks)
            Print(world, t);
    }
    Console.WriteLine($"   {watch.Elapsed.TotalSeconds:F1} s real time, {watch.Elapsed.TotalMilliseconds / ticks:F2} ms per tick, " +
                      $"pass-throughs {world.PassThroughs}, problems: {string.Join(", ", world.Production.Problems.Select(p => $"{p.Key} {p.Value}"))}");
    if (showLog)
        foreach (var ai in world.AiPlayers)
        {
            Console.WriteLine($"   -- player {ai.Player + 1} buildings");
            foreach (var b in world.Buildings.Where(b => b.Exists && b.Player == ai.Player))
                Console.WriteLine($"      {b.Definition.Name} at {b.Position.X},{b.Position.Y}: {world.StatusOf(b)}");
            Console.WriteLine($"   -- player {ai.Player + 1} log");
            foreach (string line in ai.Log)
                Console.WriteLine("      " + line);
        }
}

void Print(World world, long tick)
{
    foreach (var site in world.Buildings.Where(b => b.Exists && b.State != BuildingState.Finished))
        siteSince.TryAdd(site.Id, tick);
    long seconds = tick / World.TicksPerSecond;
    Console.WriteLine($"-- {seconds / 60} min");
    foreach (var ai in world.AiPlayers)
    {
        int p = ai.Player;
        var own = world.Buildings.Where(b => b.Exists && b.Player == p).ToList();
        int finished = own.Count(b => b.State == BuildingState.Finished);
        var settlers = world.Settlers.Where(s => s.Player == p).ToList();
        int Stock(Material m) => world.Piles.Where(x => x.Material == m && x.Role != PileRole.Request && world.BelongsTo(x, p)).Sum(x => x.Count);
        var kinds = own.Where(b => b.State == BuildingState.Finished).GroupBy(b => b.Definition.Name)
            .OrderBy(g => g.Key).Select(g => g.Count() > 1 ? $"{g.Key} {g.Count()}" : g.Key);
        Console.WriteLine($"   P{p + 1}: land {ai.Land} (free {ai.FreeLand}), {finished} buildings + {own.Count - finished} sites, " +
                          $"settlers {settlers.Count} (carriers {settlers.Count(s => s.Profession == Profession.Carrier)}, soldiers {settlers.Count(s => s.Profession == Profession.Soldier)}), " +
                          $"plank {Stock(Material.Plank)} stone {Stock(Material.Stone)} coal {Stock(Material.Coal)} iron {Stock(Material.Iron)}");
        Console.WriteLine($"       {string.Join(", ", kinds)}");
        foreach (var site in own.Where(b => b.State != BuildingState.Finished && tick - siteSince[b.Id] >= 10 * 60 * World.TicksPerSecond))
            Console.WriteLine($"       STUCK for {(tick - siteSince[site.Id]) / World.TicksPerSecond / 60} min: {site.Definition.Name} at {site.Position.X},{site.Position.Y}: {world.StatusOf(site)}");
    }
}

int IntOption(string name, int fallback)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out int value) ? value : fallback;
}
