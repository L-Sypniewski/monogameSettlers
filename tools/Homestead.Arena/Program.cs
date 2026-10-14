using System.Diagnostics;
using Homestead.Assets;
using Homestead.Assets.Maps;
using Homestead.Simulation;
using Homestead.Simulation.Ai;
using Homestead.Simulation.Buildings;
using Homestead.Simulation.Economy;

// AI against AI without a window: every player of a map is an AI player, the game runs as fast as
// it can, and every few game minutes the players' key figures are printed.
//   Homestead.Arena [map names...] [--all] [--minutes N] [--every N] [--peoples amazon,roman] [--characters ,general] [--seed N] [--log] [--notes] [--profile]
// Without map names: the first multiplayer map without start buildings. --all: every such map.
// --peoples: peoples for players the map leaves open (player i: entry i mod count). Default: random,
// with a fixed seed so runs stay comparable (--seed N for another draw).
// --characters: AI character per player (ai.json characters; entry i for player i+1, empty: drawn).
// --notes: every AI decision as it is made (otherwise only the traits' ones).
// --profile: time per part of the simulation step for each report interval.

string[] valueOptions = ["--minutes", "--every", "--peoples", "--characters", "--seed"];
var names = args.Where((a, i) => !a.StartsWith("--") && (i == 0 || !valueOptions.Contains(args[i - 1]))).ToList();
int minutes = IntOption("--minutes", 30);
int every = IntOption("--every", 5);
bool all = args.Contains("--all");
bool showLog = args.Contains("--log");
bool showNotes = args.Contains("--notes");
bool profile = args.Contains("--profile");
int peoplesAt = Array.IndexOf(args, "--peoples");
var peoples = peoplesAt >= 0 && peoplesAt + 1 < args.Length
    ? args[peoplesAt + 1].Split(',').Select(p => Enum.Parse<Civilization>(p.Trim(), ignoreCase: true)).ToArray()
    : null;
int charactersAt = Array.IndexOf(args, "--characters");
var characters = charactersAt >= 0 && charactersAt + 1 < args.Length ? args[charactersAt + 1].Split(',').Select(c => c.Trim()).ToArray() : [];

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
    var random = new Random(IntOption("--seed", 1));
    var (world, report) = OriginalScenario.Load(original, freeChoice: peoples, random: random);
    for (int player = 0; player < report.Catalogs.Count; player++)
    {
        var ai = new AiPlayer(world, report.Catalogs[player], player,
            character: player < characters.Length && characters[player].Length > 0 ? characters[player] : null, random: random);
        // Trait decisions as they happen (the log keeps only the latest).
        ai.Noted += (a, line) =>
        {
            if (showNotes || a.TraitOfNote(line) is not null)
                Console.WriteLine($"   P{a.Player + 1} {a.Character.Id}: {line}");
        };
        world.AiPlayers.Add(ai);
    }
    Console.WriteLine($"== {Path.GetFileNameWithoutExtension(file)}: {report.Catalogs.Count} players " +
                      $"({string.Join(", ", world.AiPlayers.Select(a => $"{report.Catalogs[a.Player].Civilization} {a.Character.Id}"))}), map {world.Map!.Size}");

    var watch = Stopwatch.StartNew();
    long ticks = (long)minutes * 60 * World.TicksPerSecond;
    long interval = every * 60 * World.TicksPerSecond;
    if (profile)
        world.StepProfile = new Dictionary<string, (long, long)>();
    for (long t = 1; t <= ticks; t++)
    {
        world.Step();
        if (t % interval == 0 || t == ticks)
        {
            Print(world, t);
            if (world.StepProfile is { } parts)
            {
                long steps = t % interval == 0 ? interval : t % interval;
                double ms = 1000.0 / Stopwatch.Frequency;
                Console.WriteLine("   ms per tick (longest): " + string.Join(", ", parts.OrderByDescending(p => p.Value.Total)
                    .Select(p => $"{p.Key} {p.Value.Total * ms / steps:F2} ({p.Value.Max * ms:F1})")));
                parts.Clear();
            }
        }
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
        Console.WriteLine($"   P{p + 1}: land {ai.Land} (free {ai.FreeLand}, water {100 * ai.WaterLand / Math.Max(1, ai.Land)}%), {finished} buildings + {own.Count - finished} sites, " +
                          $"settlers {settlers.Count} (carriers {settlers.Count(s => s.Profession == Profession.Carrier)}, soldiers {settlers.Count(s => s.Profession == Profession.Soldier)}), " +
                          $"room {world.Housing.Room(p)} for {world.Housing.Needing(p)} (striking {world.Housing.Striking(p)}), " +
                          $"plank {Stock(Material.Plank)} stone {Stock(Material.Stone)} coal {Stock(Material.Coal)} iron {Stock(Material.Iron)}, " +
                          $"combat strength {world.Military.CombatStrength(p):P0}, gold {Stock(Material.Gold)}, gems {Stock(Material.Gems)}, mana {world.Magic.Mana(p)}, upgrades {world.Magic.Upgrades(p)} (sword {world.Magic.SoldierLevel(p, SoldierType.Swordsman)}, bow {world.Magic.SoldierLevel(p, SoldierType.Bowman)}, pike {world.Magic.SoldierLevel(p, SoldierType.Pikeman)}), priests {settlers.Count(s => s.Profession == Profession.Priest)}");
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
