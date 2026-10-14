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
//                   [--until-decided] [--csv file] [--tag text]
// Without map names: the first multiplayer map without start buildings. --all: every such map.
// --peoples: peoples for players the map leaves open (player i: entry i mod count). Default: random,
// with a fixed seed so runs stay comparable (--seed N for another draw; it also seeds the world's chance).
// --characters: AI character per player (ai.json characters; entry i for player i+1, empty: drawn).
// --notes: every AI decision as it is made (otherwise only the traits' ones).
// --profile: time per part of the simulation step for each report interval.
// --characters also takes traits joined by '+' (e.g. pincer+guardByThreat: a character of just those traits).
// --until-decided: stop as soon as only one player has military buildings (or sites) or soldiers left.
// --csv: append one row per player and map to the file (header if it is new); --tag fills its first column.

string[] valueOptions = ["--minutes", "--every", "--peoples", "--characters", "--seed", "--csv", "--tag"];
var names = args.Where((a, i) => !a.StartsWith("--") && (i == 0 || !valueOptions.Contains(args[i - 1]))).ToList();
int minutes = IntOption("--minutes", 30);
int every = IntOption("--every", 5);
bool all = args.Contains("--all");
bool showLog = args.Contains("--log");
bool showNotes = args.Contains("--notes");
bool profile = args.Contains("--profile");
bool untilDecided = args.Contains("--until-decided");
string? csv = StringOption("--csv");
string tag = StringOption("--tag") ?? "";
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
    var (world, report) = OriginalScenario.Load(original, freeChoice: peoples, random: random, seed: IntOption("--seed", 1));
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
    long end = ticks;
    for (long t = 1; t <= ticks; t++)
    {
        world.Step();
        bool decided = untilDecided && t % (10 * World.TicksPerSecond) == 0 && Alive(world).Count(a => a) <= 1;
        if (decided)
        {
            Print(world, t);
            Console.WriteLine($"   decided after {t / World.TicksPerSecond / 60} min");
            end = t;
            break;
        }
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
    if (csv is not null)
        WriteCsv(csv, Path.GetFileNameWithoutExtension(file), world, report.Catalogs, end, ticks, watch.Elapsed.TotalSeconds);
    Console.WriteLine($"   {watch.Elapsed.TotalSeconds:F1} s real time, {watch.Elapsed.TotalMilliseconds / end:F2} ms per tick, " +
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

// Fields owned by each player (index: player).
int[] Lands(World world)
{
    var map = world.Map!;
    var lands = new int[world.AiPlayers.Count];
    for (int i = 0; i < map.PointCount; i++)
        if (map.OwnerAt(i) is var owner && owner < lands.Length)
            lands[owner]++;
    return lands;
}

// Whether each player (index) can still fight: has a military building or site, or a soldier.
bool[] Alive(World world)
{
    var alive = new bool[world.AiPlayers.Count];
    foreach (var b in world.Buildings)
        if (b.Exists && b.Definition.Occupiers.Count > 0 && b.Player < alive.Length)
            alive[b.Player] = true;
    foreach (var s in world.Settlers)
        if (s.Profession == Profession.Soldier && s.Player < alive.Length)
            alive[s.Player] = true;
    return alive;
}

// One row per player: result "win"/"loss" once only one player is alive, otherwise "open"
// (the land share then tells who was ahead).
void WriteCsv(string path, string map, World world, IReadOnlyList<BuildingCatalog> catalogs, long end, long ticks, double seconds)
{
    var lands = Lands(world);
    int total = Math.Max(1, lands.Sum());
    var alive = Alive(world);
    bool decided = alive.Count(a => a) <= 1;
    var invariant = System.Globalization.CultureInfo.InvariantCulture;
    var lines = new List<string>();
    if (!File.Exists(path) || new FileInfo(path).Length == 0)
        lines.Add("tag,map,seed,minutes,players,player,people,character,opponents,result,end_minute,land,land_share,buildings,military_buildings," +
                  "soldiers,settlers,combat_strength,mana,upgrades,gold,real_seconds");
    foreach (var ai in world.AiPlayers)
    {
        int p = ai.Player;
        var own = world.Buildings.Where(b => b.Exists && b.Player == p && b.State == BuildingState.Finished).ToList();
        var settlers = world.Settlers.Where(s => s.Player == p).ToList();
        string result = !decided ? "open" : alive[p] ? "win" : "loss";
        string opponents = string.Join("|", world.AiPlayers.Where(a => a != ai).Select(a => a.Character.Id));
        lines.Add(string.Join(",", tag, map, IntOption("--seed", 1), (ticks / World.TicksPerSecond / 60).ToString(invariant),
            world.AiPlayers.Count, p + 1, catalogs[p].Civilization, ai.Character.Id, opponents, result,
            (end / (double)World.TicksPerSecond / 60).ToString("F1", invariant), lands[p], (lands[p] / (double)total).ToString("F3", invariant),
            own.Count, own.Count(world.Military.IsOccupied), settlers.Count(s => s.Profession == Profession.Soldier), settlers.Count,
            world.Military.CombatStrength(p).ToString("F2", invariant), world.Magic.Mana(p), world.Magic.Upgrades(p),
            world.Piles.Where(x => x.Material == Material.Gold && x.Role != PileRole.Request && world.BelongsTo(x, p)).Sum(x => x.Count),
            seconds.ToString("F1", invariant)));
    }
    File.AppendAllText(path, string.Concat(lines.Select(l => l + "\n")));
}

string? StringOption(string name)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

int IntOption(string name, int fallback)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out int value) ? value : fallback;
}
