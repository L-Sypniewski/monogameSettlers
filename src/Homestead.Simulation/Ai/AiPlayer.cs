using Homestead.Simulation.Buildings;
using Homestead.Simulation.Economy;
using Homestead.Simulation.Map;
using Homestead.Simulation.Pathfinding;

namespace Homestead.Simulation.Ai;

/// <summary>
/// A computer player. It uses the same orders as a human player (construction sites; the rest is
/// done by the settlers) and knows the whole map.
/// </summary>
/// <remarks>
/// Every <see cref="AiConfig.ThinkTicks"/> it does two things:
/// <list type="bullet">
/// <item>Economy: starts the first building of its wishes it has not got yet and can afford: a living
/// house when carriers run short, the toolsmith when a worker waits for a tool, then the build order
/// of <c>data/ai.json</c>. Each building goes next to what it works on (trees, stones, ore, water) or
/// near the home tower.</item>
/// <item>Expansion: when the own land lacks quarry stone, ore, trees, water or room, a tower goes to the
/// own-land spot nearest the closest unclaimed place with it, so a game cannot stall on a start area
/// without mountains or stones.</item>
/// </list>
/// </remarks>
public sealed class AiPlayer
{
    private const int SurveyTicks = 200;
    private const int SearchRadius = 30;
    private const int TowerSearchRadius = 60;
    private const int MaxLog = 40;

    /// <summary>How long a building may stand without anything left to work on.</summary>
    private const int UselessTicks = 3 * 60 * World.TicksPerSecond;

    /// <summary>Offsets sorted by distance, for searching spots nearest a point.</summary>
    private static readonly GridPoint[] Spiral = PathFinder.Circle(new GridPoint(0, 0), TowerSearchRadius)
        .OrderBy(o => PathFinder.Distance(new GridPoint(0, 0), o)).ToArray();

    private static readonly string[] Basics = ["lumberjack", "sawmill", "stonecutter"];

    private readonly World _world;
    private readonly BuildingCatalog _catalog;
    private readonly AiConfig _config;
    private readonly MapSurvey _survey;
    private readonly List<string> _log = new();
    private long _nextSurvey;
    private bool _lacksRoom;
    private readonly Dictionary<int, long> _uselessSince = new();
    private readonly HashSet<string> _reported = new();
    private Building? _home;

    public AiPlayer(World world, BuildingCatalog catalog, int player, AiConfig? config = null)
    {
        _world = world;
        _catalog = catalog;
        _config = config ?? AiConfig.Load();
        Player = player;
        _survey = new MapSurvey(world, player);
    }

    public int Player { get; }

    /// <summary>The latest decisions, oldest first (for watching and debugging).</summary>
    public IReadOnlyList<string> Log => _log;

    /// <summary>Points of land the player owned at the last survey.</summary>
    public int Land => _survey.Own.Own;

    /// <summary>Free building land on the own land at the last survey.</summary>
    public int FreeLand => _survey.Own.Buildable;

    internal void Tick()
    {
        if ((_world.Tick + Player * 7) % _config.ThinkTicks != 0)
            return;
        if (_world.Tick >= _nextSurvey)
        {
            _survey.Update();
            _nextSurvey = _world.Tick + SurveyTicks;
        }
        var mine = _world.Buildings.Where(b => b.Exists && b.Player == Player).ToList();
        if (_home is not { Exists: true } || _home.Player != Player)
            _home = mine.Where(IsMilitary).OrderBy(b => b.State == BuildingState.Finished ? 0 : 1).FirstOrDefault()
                    ?? mine.FirstOrDefault();
        if (_home is null)
            return;
        TearDownUseless(mine);
        mine.RemoveAll(b => !b.Exists);
        var reserve = Expand(mine);
        BuildEconomy(mine, reserve);
    }

    // ---- economy ----

    /// <param name="reserve">Material kept back for a tower the expansion wants but cannot afford yet.</param>
    private void BuildEconomy(List<Building> mine, (int Planks, int Stones) reserve)
    {
        if (mine.Count(b => b.State != BuildingState.Finished && !IsMilitary(b)) >= _config.MaxSites)
            return;
        int planks = Free(Material.Plank, mine) - reserve.Planks, stones = Free(Material.Stone, mine) - reserve.Stones;
        // Everything but the basic producers leaves enough for a new stonecutter: a player out of
        // stones with no stonecutter it can afford would be stuck for good.
        var cutter = _catalog.Find("stonecutter");
        int keepPlanks = cutter is null ? 0 : Cost(cutter, Material.Plank), keepStones = cutter is null ? 0 : Cost(cutter, Material.Stone);
        foreach (var (name, count) in Wishes(mine))
        {
            if (mine.Count(b => b.Definition.Name == name) >= count || _catalog.Find(name) is not { } definition)
                continue;
            bool basic = Basics.Contains(name);
            if (planks - (basic ? 0 : keepPlanks) < Cost(definition, Material.Plank) || stones - (basic ? 0 : keepStones) < Cost(definition, Material.Stone))
            {
                // Wait for the material rather than skip ahead, unless nothing would ever bring it.
                bool noSupply = Basics.Contains(name) && !mine.Any(b => b.Definition.Name == name);
                if (!noSupply)
                    return;
            }
            if (AnchorFor(definition, mine) is not { } anchor)
            {
                NoteOnce(name, $"cannot build {name}: nothing to work on in own land");
                continue; // the expansion goes for it
            }
            if (Place(definition, anchor) is { } site)
            {
                _reported.Remove(name);
                Note($"builds {name} at {site.Position.X},{site.Position.Y}");
                return;
            }
            NoteOnce(name, $"cannot build {name}: no spot near {anchor.X},{anchor.Y}");
            if (anchor == _home!.Position)
                _lacksRoom = true;
        }
    }

    private IEnumerable<(string Name, int Count)> Wishes(List<Building> mine)
    {
        int carriers = _world.Settlers.Count(s => s.Player == Player && s.IsAvailableCarrier)
                       + mine.Sum(b => _world.Housing.ResidentsLeft(b) ?? 0);
        bool houseComing = mine.Any(b => b.Definition.Name == _config.House && b.State != BuildingState.Finished);
        if (carriers < _config.FreeCarriers && !houseComing)
            yield return (_config.House, mine.Count(b => b.Definition.Name == _config.House) + 1);
        // A storehouse only once goods worth storing (tools, weapons, gold) are made in numbers and
        // pile up, and the storehouses there are have no room left for them.
        var storage = mine.Where(b => b.State == BuildingState.Finished).SelectMany(b => b.Piles).Where(p => p.Role == PileRole.Storage).ToList();
        bool stockComing = mine.Any(b => b.Definition.Name == "stock" && b.State != BuildingState.Finished);
        var config = _world.Production.Config;
        int waiting = _world.Piles.Where(p => p.Role is PileRole.Loose or PileRole.Offer && p.Material is { } m && config.StoredMaterials?.Contains(m) == true
                                              && _world.BelongsTo(p, Player)).Sum(p => p.Count);
        bool massProduction = mine.Any(b => b.State == BuildingState.Finished
                                            && (config.Smiths.ContainsKey(b.Definition.Name) || b.Definition.Name == "goldmelt"));
        if (massProduction && !stockComing && waiting >= _config.StockWhenWaiting && storage.Count(p => p.Material is null) < 2)
            yield return ("stock", mine.Count(b => b.Definition.Name == "stock") + 1);
        // A stonecutter or woodcutter with nothing left in range: a new one where there is some.
        foreach (var (producer, idle) in new[] { ("stonecutter", "no stone in range"), ("lumberjack", "no tree in range") })
        {
            var all = mine.Where(b => b.Definition.Name == producer).ToList();
            if (all.Count > 0 && all.All(b => b.State == BuildingState.Finished && _world.StatusOf(b) == idle))
                yield return (producer, all.Count + 1);
        }
        if (mine.Any(b => b.State == BuildingState.Finished && _world.StatusOf(b).Contains("tool missing")))
            yield return ("toolsmith", 1);
        // Mines without food: more food, one building at a time.
        bool hungry = mine.Any(b => b.State == BuildingState.Finished && _world.StatusOf(b) == "waiting for food");
        if (hungry && !mine.Any(b => b.State != BuildingState.Finished && b.Definition.Name is "fisher" or "farm" or "mill" or "baker"))
        {
            int fishers = mine.Count(b => b.Definition.Name == "fisher"), farms = mine.Count(b => b.Definition.Name == "farm");
            // Bread: two farms per mill and baker.
            int bakeries = (farms + 1) / 2;
            yield return ("mill", bakeries);
            yield return ("baker", bakeries);
            if (fishers <= farms)
                yield return ("fisher", fishers + 1);
            yield return ("farm", farms + 1);
        }
        foreach (var wish in _config.BuildOrder)
            yield return wish;
    }

    /// <summary>Planks or stones the player has beyond what its construction sites still need.</summary>
    private int Free(Material material, List<Building> mine)
    {
        int stock = _world.Piles.Where(p => p.Material == material && p.Role != PileRole.Request && _world.BelongsTo(p, Player))
            .Sum(p => p.Available);
        int demand = mine.Where(b => b.State != BuildingState.Finished).SelectMany(b => b.Piles)
            .Where(p => p.Role == PileRole.Request && p.Material == material).Sum(p => Math.Max(0, p.Space));
        return stock - demand;
    }

    private static int Cost(BuildingDefinition definition, Material material) =>
        definition.ConstructionPiles.Where(p => p.Material == material).Sum(p => p.Required);

    /// <summary>
    /// Where a building should go: next to what it works on, otherwise near home. Null if that is
    /// missing on own land.
    /// </summary>
    private GridPoint? AnchorFor(BuildingDefinition definition, List<Building> mine)
    {
        string name = definition.Name;
        if (_world.Production.Config.Mines.TryGetValue(name, out var mineRecipe))
            return BestOwnCell(c => c.Ore[(int)mineRecipe.Resource], mine, name, spacing: 6);
        return name switch
        {
            "lumberjack" => BestOwnCell(c => c.Trees, mine, name, spacing: 10),
            "stonecutter" => BestOwnCell(c => c.StoneUnits, mine, name, spacing: 10),
            "fisher" => BestOwnCell(c => c.Fish, mine, name, spacing: 10),
            "waterworks" => BestOwnCell(c => c.Water, mine, name, spacing: 6),
            "forester" => mine.Where(b => b.Definition.Name == "lumberjack")
                              .OrderBy(l => mine.Count(f => f.Definition.Name == "forester" && PathFinder.Distance(f.Position, l.Position) < 12))
                              .Select(l => (GridPoint?)l.Position).FirstOrDefault() ?? _home!.Position,
            _ => _home!.Position,
        };
    }

    /// <summary>
    /// Own cell with the most of something (closer to home counts more); cells next to a building of
    /// the same kind count less, so a second woodcutter goes to another wood.
    /// </summary>
    private GridPoint? BestOwnCell(Func<MapSurvey.Cell, int> amount, List<Building> mine, string name, int spacing)
    {
        var home = _home!.Position;
        var best = _survey.Cells
            .Where(c => c.Own > 0 && amount(c.OwnPart!) > 0)
            .Select(c => (Cell: c, Score: amount(c.OwnPart!) / (1.0 + PathFinder.Distance(home, c.Center) / 20.0)
                                          / (1 + mine.Count(b => b.Definition.Name == name && PathFinder.Distance(b.Position, c.Center) < spacing))))
            .OrderByDescending(x => x.Score)
            .FirstOrDefault();
        return best.Cell?.Center;
    }

    /// <summary>A construction site on the free own-land spot nearest the anchor that suits the building.</summary>
    private Building? Place(BuildingDefinition definition, GridPoint anchor)
    {
        var map = _world.Map!;
        var pathFinder = _world.PathFinder!;
        var homeDoor = _home!.Door;
        bool isMine = _world.Production.Config.Mines.TryGetValue(definition.Name, out var mineRecipe);
        GridPoint? bestMine = null;
        int bestOre = 0, checkedMines = 0;
        foreach (var offset in Spiral)
        {
            if (PathFinder.Distance(default, offset) > SearchRadius)
                break;
            var p = anchor + offset;
            if (!map.Contains(p) || map.OwnerAt(p) != Player || !_world.CanPlace(definition, p, out _, Player)
                || !pathFinder.AreConnected(homeDoor, p + definition.Door))
                continue;
            if (isMine)
            {
                // The richest of the nearest fitting spots.
                int ore = Around(p, mineRecipe!.Radius, q => map.ResourceAmount(q, mineRecipe.Resource));
                if (ore > bestOre)
                    (bestMine, bestOre) = (p, ore);
                if (++checkedMines >= 25)
                    break;
                continue;
            }
            if (Suits(definition, p))
                return _world.PlaceConstructionSite(definition, Player, p);
        }
        return bestMine is { } spot ? _world.PlaceConstructionSite(definition, Player, spot) : null;
    }

    private bool Suits(BuildingDefinition definition, GridPoint p)
    {
        var map = _world.Map!;
        return definition.Name switch
        {
            "lumberjack" => Around(p, 10, q => MapObjectCodes.IsAdultTree(map.ObjectCodeAt(q)) ? 1 : 0) >= 3,
            "stonecutter" => Around(p, 10, q => MapObjectCodes.StoneLeft(map.ObjectCodeAt(q))) > 0,
            "fisher" => Around(p, 8, q => IsWater(q) ? map.ResourceAmount(q, ResourceType.Fish) : 0) > 0,
            "waterworks" => Around(p, 6, q => IsWater(q) ? 1 : 0) > 0,
            "farm" => Around(p + (definition.WorkCenter ?? default), 6,
                q => map.TerrainAt(q) == TerrainType.Grass && map.ObjectCodeAt(q) == MapObjectCodes.None && _world.IsFreeGround(q) ? 1 : 0) >= 20,
            _ => true,
        };
    }

    private bool IsWater(GridPoint p) => _world.Map!.TerrainAt(p) is TerrainType.Water or TerrainType.River;

    private int Around(GridPoint center, int radius, Func<GridPoint, int> value)
    {
        var map = _world.Map!;
        int sum = 0;
        foreach (var q in PathFinder.Circle(center, radius))
            if (map.Contains(q))
                sum += value(q);
        return sum;
    }

    /// <summary>
    /// Buildings that have run out of work for good (quarry stones cut, fish caught, mine exhausted) go
    /// after a while: the worker keeps its trade and takes the job at the new building that goes where
    /// there is some left.
    /// </summary>
    private void TearDownUseless(List<Building> mine)
    {
        foreach (var building in mine)
        {
            if (building.State != BuildingState.Finished
                || _world.StatusOf(building) is not ("no stone in range" or "no fish in range" or "mine is exhausted"))
            {
                _uselessSince.Remove(building.Id);
                continue;
            }
            long since = _uselessSince.TryGetValue(building.Id, out long t) ? t : _uselessSince[building.Id] = _world.Tick;
            if (_world.Tick - since < UselessTicks)
                continue;
            _uselessSince.Remove(building.Id);
            Note($"tears down {building.Definition.Name} at {building.Position.X},{building.Position.Y}: {_world.StatusOf(building)}");
            _world.RemoveBuilding(building);
        }
    }

    // ---- expansion ----

    /// <summary>Builds a tower towards what the land lacks. Returns the material to keep back for it if it cannot be afforded yet.</summary>
    private (int Planks, int Stones) Expand(List<Building> mine)
    {
        if (_catalog.Find(_config.ExpansionBuilding) is not { } tower)
            return default;
        int towerSites = mine.Count(b => IsMilitary(b) && b.State != BuildingState.Finished);
        if (towerSites >= _config.MaxTowerSites)
            return default;
        // Somebody has to occupy it.
        int spareSoldiers = _world.Settlers.Count(s => s.Player == Player && s.Profession == Profession.Soldier && !s.HasTask && !s.IsInside);
        int emptyTowers = mine.Count(b => IsMilitary(b) && b.State == BuildingState.Finished && !_world.Military.IsOccupied(b));
        spareSoldiers -= emptyTowers + towerSites;
        if (spareSoldiers <= 0)
            return default;
        (int Planks, int Stones) cost = (Cost(tower, Material.Plank), Cost(tower, Material.Stone));
        bool affordable = Free(Material.Plank, mine) >= cost.Planks && Free(Material.Stone, mine) >= cost.Stones;

        // Places an own tower (or tower site) will cover anyway do not need another.
        var military = mine.Where(IsMilitary).Select(b => b.Position).ToList();
        int covered = (int)(_world.Military.Config.Territory.GetValueOrDefault(tower.Name, 20) * 0.7);
        bool Uncovered(MapSurvey.Cell c) => military.All(m => PathFinder.Distance(m, c.Center) > covered);
        int planks = Free(Material.Plank, mine), stones = Free(Material.Stone, mine);

        foreach (var (need, wanted) in Needs(spareSoldiers))
        {
            // Plain land only from a full store: what the economy needs comes first.
            if (need == "land" && (planks < cost.Planks + _config.LandStock || stones < cost.Stones + _config.LandStock))
                continue;
            if (NearestUnclaimed(c => wanted(c) && Uncovered(c)) is not { } target)
                continue;
            if (!affordable)
                return need == "land" ? default : cost; // keep the material back for it
            if (PlaceTower(tower, target, mine) is { } site)
            {
                Note($"expands towards {need} at {target.X},{target.Y}: tower at {site.Position.X},{site.Position.Y}");
                if (need == "room")
                    _lacksRoom = false;
                return default;
            }
        }
        return default;
    }

    /// <summary>What the own land lacks, most urgent first, with how to recognize a cell that has it.</summary>
    private IEnumerable<(string Need, Func<MapSurvey.Cell, bool> Has)> Needs(int spareSoldiers)
    {
        var own = _survey.Own;
        // Stones almost gone first, then the ores, then more stones for later.
        if (own.StoneUnits < _config.MinStoneUnits)
            yield return ("stone", c => c.StoneUnits >= 10);
        foreach (var ore in _config.Ores)
            if (own.Ore[(int)ore] == 0)
                yield return (ore.ToString(), c => c.Ore[(int)ore] > 0);
        // Free building land is a resource too: towards the cells with the most of it.
        bool roomy(MapSurvey.Cell c) => c.Buildable >= MapSurvey.CellSize * MapSurvey.CellSize * 2 / 3;
        if (own.Buildable < _config.MinFreeLand)
            yield return ("room", roomy);
        if (own.StoneUnits >= _config.MinStoneUnits && own.StoneUnits < _config.WantedStoneUnits)
            yield return ("stone", c => c.StoneUnits >= 10);
        if (own.Trees < _config.MinTrees)
            yield return ("trees", c => c.Trees >= 10);
        if (own.Water == 0)
            yield return ("water", c => c.Water > 0);
        if (_lacksRoom)
            yield return ("room", roomy);
        // Nothing lacking: soldiers to spare still take land, as a player would.
        if (spareSoldiers >= _config.SpareSoldiersForLand)
            yield return ("land", roomy);
    }

    /// <summary>Center of the nearest cell nobody owns that has what is wanted and can be walked to.</summary>
    private GridPoint? NearestUnclaimed(Func<MapSurvey.Cell, bool> wanted)
    {
        var home = _home!;
        var pathFinder = _world.PathFinder!;
        return _survey.Cells
            .Where(c => c.Own == 0 && c.Foreign == 0 && c.Anchor is not null && wanted(c))
            .OrderBy(c => PathFinder.Distance(home.Position, c.Center))
            .Where(c => pathFinder.AreConnected(home.Door, c.Anchor!.Value))
            .Select(c => (GridPoint?)c.Center)
            .FirstOrDefault();
    }

    /// <summary>A tower on the own-land spot nearest the target, if that gets the land closer to it.</summary>
    private Building? PlaceTower(BuildingDefinition tower, GridPoint target, List<Building> mine)
    {
        var map = _world.Map!;
        var pathFinder = _world.PathFinder!;
        int closest = mine.Where(IsMilitary).Select(b => PathFinder.Distance(b.Position, target)).DefaultIfEmpty(int.MaxValue).Min();
        foreach (var offset in Spiral)
        {
            var p = target + offset;
            if (PathFinder.Distance(p, target) + 6 > closest)
                return null;
            if (map.Contains(p) && map.OwnerAt(p) == Player && _world.CanPlace(tower, p, out _, Player)
                && pathFinder.AreConnected(_home!.Door, p + tower.Door))
                return _world.PlaceConstructionSite(tower, Player, p);
        }
        return null;
    }

    private static bool IsMilitary(Building b) => b.Definition.Occupiers.Count > 0;

    /// <summary>Notes a problem once, until the building can be built again.</summary>
    private void NoteOnce(string key, string text)
    {
        if (_reported.Add(key))
            Note(text);
    }

    private void Note(string text)
    {
        long seconds = _world.Tick / World.TicksPerSecond;
        _log.Add($"{seconds / 60}:{seconds % 60:00} {text}");
        if (_log.Count > MaxLog)
            _log.RemoveAt(0);
    }
}
