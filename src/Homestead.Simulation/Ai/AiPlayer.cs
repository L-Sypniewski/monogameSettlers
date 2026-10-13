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
/// of <c>data/ai.json</c>. Each building goes next to what it works on (trees, stones, ore, water),
/// otherwise into the middle of its production chain, otherwise near the home tower.</item>
/// <item>Expansion: when the own land lacks quarry stone, ore, trees, water or room, a tower goes to the
/// own-land spot nearest the closest unclaimed place with it, so a game cannot stall on a start area
/// without mountains or stones.</item>
/// </list>
/// </remarks>
public sealed partial class AiPlayer
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
        _config = config ?? AiConfig.Load(civilization: catalog.Civilization);
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

    /// <summary>Water (sea, lakes, rivers) on the own land at the last survey.</summary>
    public int WaterLand => _survey.Own.Water;

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
        TearDownSpareHouse(mine);
        mine.RemoveAll(b => !b.Exists);
        FollowStrikes(mine);
        FollowCampaign();
        LeadPriests();
        UpgradeSoldiers();
        if (_world.Tick >= _nextMilitary)
        {
            Command(mine);
            _nextMilitary = _world.Tick + _config.Military.ThinkTicks;
        }
        Expand(mine);
        BuildEconomy(mine);
    }

    // ---- economy ----

    private void BuildEconomy(List<Building> mine)
    {
        if (mine.Count(b => b.State != BuildingState.Finished && !IsMilitary(b)) >= _config.MaxSites)
            return;
        int planks = Free(Material.Plank, mine), stones = Free(Material.Stone, mine);
        // Everything but the basic producers leaves enough for a new stonecutter: a player out of
        // stones with no stonecutter it can afford would be stuck for good.
        var cutter = _catalog.Find("stonecutter");
        int keepPlanks = cutter is null ? 0 : Cost(cutter, Material.Plank), keepStones = cutter is null ? 0 : Cost(cutter, Material.Stone);
        foreach (var (name, count) in Wishes(mine))
        {
            if (mine.Count(b => b.Definition.Name == name) >= count || _catalog.Find(name) is not { } definition)
                continue;
            if (LacksOre(definition, mine))
                continue;
            bool basic = Basics.Contains(name);
            if (!Enough(planks, Cost(definition, Material.Plank) + (basic ? 0 : keepPlanks))
                || !Enough(stones, Cost(definition, Material.Stone) + (basic ? 0 : keepStones)))
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
            if ((Place(definition, anchor) ?? PlaceWhereRoomy(definition)) is { } site)
            {
                _reported.Remove(name);
                Note($"builds {name} at {site.Position.X},{site.Position.Y}");
                return;
            }
            NoteOnce(name, $"cannot build {name}: no spot near {anchor.X},{anchor.Y}");
            if (!IsResourceBound(name))
                _lacksRoom = true;
        }
    }

    /// <summary>
    /// A living house comes first when carriers run short: fewer free ones than freeCarriers, no more
    /// carriers in all than carriersPerProductionBuilding per production building (sites included),
    /// or the carrier reserve (nearly) stops recruiting. Waiting until no carrier may convert any more
    /// is too late: the plank and stone makers may then lack workers for good, and the house its material.
    /// Residents still to come count as carriers.
    /// </summary>
    private bool HouseWanted(List<Building> mine)
    {
        if (mine.Any(b => IsLivingHouse(b.Definition.Name) && b.State != BuildingState.Finished))
            return false;
        int coming = mine.Sum(b => _world.Housing.ResidentsLeft(b) ?? 0);
        int free = _world.Settlers.Count(s => s.Player == Player && s.IsAvailableCarrier) + coming;
        int carriers = _world.Settlers.Count(s => s.Player == Player && s.IsCarrier) + coming;
        int producers = mine.Count(b => b.Definition.Worker is not null);
        return free < _config.FreeCarriers
               || carriers <= producers * _config.CarriersPerProductionBuilding
               || _world.Recruitment.Convertible(Player) < _config.HouseWhenConvertible;
    }

    /// <summary>
    /// Tears down a living house no one needs: one that has released all its residents, while without
    /// it at least spareRoom places would still be free and no house is wanted. Its material comes back
    /// in part. The biggest such house first.
    /// </summary>
    private void TearDownSpareHouse(List<Building> mine)
    {
        if (_world.Housing.Room(Player) is not { } room || HouseWanted(mine))
            return;
        int spare = room - _world.Housing.Needing(Player);
        var house = mine.Where(b => b.State == BuildingState.Finished && _world.Housing.ResidentsLeft(b) == 0
                                    && spare - _world.Housing.RoomOf(b) >= _config.SpareRoom)
            .MaxBy(b => _world.Housing.RoomOf(b));
        if (house is null)
            return;
        Note($"tears down {house.Definition.Name} at {house.Position.X},{house.Position.Y}: {spare} places spare");
        _world.RemoveBuilding(house);
    }

    private bool IsLivingHouse(string name) => name == _config.House || _config.HouseSizes.Any(h => h.Building == name);

    /// <summary>
    /// The living house to build: the biggest one the wood and stone production can afford (enough
    /// finished sawmills and stonecutters, per people in ai.json), otherwise the small one.
    /// </summary>
    private BuildingDefinition? HouseToBuild(List<Building> mine)
    {
        int sawmills = mine.Count(b => b.Definition.Name == "sawmill" && b.State == BuildingState.Finished);
        int stonecutters = mine.Count(b => b.Definition.Name == "stonecutter" && b.State == BuildingState.Finished);
        foreach (var size in _config.HouseSizes)
            if (sawmills >= size.Sawmills && stonecutters >= size.Stonecutters && _catalog.Find(size.Building) is { } house)
                return house;
        return _catalog.Find(_config.House);
    }

    private IEnumerable<(string Name, int Count)> Wishes(List<Building> mine)
    {
        if (HouseWanted(mine) && HouseToBuild(mine) is { } house)
            yield return (house.Name, mine.Count(b => b.Definition.Name == house.Name) + 1);
        // A storehouse only once goods worth storing (tools, weapons, gold, gems) are made in numbers and
        // pile up, and the storehouses there are have no room left for them.
        var storage = mine.Where(b => b.State == BuildingState.Finished).SelectMany(b => b.Piles).Where(p => p.Role == PileRole.Storage).ToList();
        bool stockComing = mine.Any(b => b.Definition.Name == "stock" && b.State != BuildingState.Finished);
        var config = _world.Production.Config;
        int waiting = _world.Piles.Where(p => p.Role is PileRole.Loose or PileRole.Offer && p.Material is { } m && config.StoredMaterials?.Contains(m) == true
                                              && _world.BelongsTo(p, Player)).Sum(p => p.Count);
        bool massProduction = mine.Any(b => b.State == BuildingState.Finished
                                            && (config.Smiths.ContainsKey(b.Definition.Name) || b.Definition.Name is "goldmelt" or "gemsmine"));
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

    /// <summary>
    /// A building that processes a mine's output (iron melt, gold melt) while no own building (sites
    /// included) yields it: it would only stand idle.
    /// </summary>
    private bool LacksOre(BuildingDefinition definition, List<Building> mine)
    {
        var mines = _world.Production.Config.Mines;
        var mined = mines.Values.Select(m => m.Output).ToHashSet();
        return Inputs(definition).Any(m => mined.Contains(m) && !mine.Any(b => Outputs(b.Definition).Contains(m)
                                                                             || mines.TryGetValue(b.Definition.Name, out var r) && r.Output == m));
    }

    /// <summary>
    /// Whether the player has every build order entry before the first one for <paramref name="building"/>;
    /// entries with nothing to work on in the own land do not count.
    /// </summary>
    private bool ReachedBefore(string building, List<Building> mine)
    {
        foreach (var (name, count) in _config.BuildOrder)
        {
            if (name == building)
                return true;
            if (mine.Count(b => b.Definition.Name == name) < count && _catalog.Find(name) is { } definition
                && AnchorFor(definition, mine) is not null)
                return false;
        }
        return false;
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
    /// Whether <paramref name="free"/> units cover <paramref name="needed"/> (cost plus what is kept back),
    /// asking for at most one full pile: a producer keeps no more than that and then waits (only tools,
    /// weapons and gold are stored), so with a single sawmill an Asian farm (9 planks) would never be
    /// started. The rest comes while the site is levelled.
    /// </summary>
    private static bool Enough(int free, int needed) => free >= Math.Min(needed, Materials.MaxPileSize);

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
            "waterworks" => BestOwnCell(c => c.River, mine, name, spacing: 6),
            "rice_farm" => BestOwnCell(c => c.Swamp, mine, name, spacing: 12),
            "forester" => mine.Where(b => b.Definition.Name == "lumberjack")
                              .OrderBy(l => mine.Count(f => f.Definition.Name == "forester" && PathFinder.Distance(f.Position, l.Position) < 12))
                              .Select(l => (GridPoint?)l.Position).FirstOrDefault() ?? _home!.Position,
            _ => ChainCenter(definition, mine) ?? _home!.Position,
        };
    }

    /// <summary>
    /// Where the building's production chain is: the middle of the own buildings that supply what it
    /// needs or need what it makes (iron mine and coal mine for the iron melt, mill and waterworks for
    /// the baker). Keeps transport ways short and spreads the economy over the land. A further one of
    /// a kind goes to the part of the chain the existing ones do not serve yet (a second sawmill to the
    /// woodcutters at the new wood). Null if there are none yet.
    /// </summary>
    private GridPoint? ChainCenter(BuildingDefinition definition, List<Building> mine)
    {
        var inputs = Inputs(definition);
        var outputs = Outputs(definition);
        var related = mine.Where(b => !IsMilitary(b) && !b.Piles.Any(p => p.Role == PileRole.Storage) && b.Definition.Name != definition.Name
                                      && (Outputs(b.Definition).Overlaps(inputs) || Inputs(b.Definition).Overlaps(outputs)))
            .ToList();
        var same = mine.Where(b => b.Definition.Name == definition.Name).Select(b => b.Position).ToList();
        var unserved = related.Where(b => same.All(s => PathFinder.Distance(s, b.Position) > ServedDistance)).ToList();
        if (same.Count > 0 && unserved.Count > 0)
            related = unserved;
        if (related.Count == 0)
            return null;
        return new GridPoint((int)Math.Round(related.Average(b => b.Position.X)), (int)Math.Round(related.Average(b => b.Position.Y)));
    }

    /// <summary>A building of the chain this close to one of the kind counts as served by it.</summary>
    private const int ServedDistance = 12;

    private static HashSet<Material> Inputs(BuildingDefinition definition) =>
        definition.RequestPiles.Where(p => p.Material is not null).Select(p => p.Material!.Value).ToHashSet();

    private HashSet<Material> Outputs(BuildingDefinition definition)
    {
        var outputs = definition.OfferPiles.Where(p => p.Material is not null).Select(p => p.Material!.Value).ToHashSet();
        if (_world.Production.Config.Smiths.TryGetValue(definition.Name, out var smith))
            outputs.UnionWith(smith.Products);
        return outputs;
    }

    /// <summary>Buildings that go where their resource is, not where their chain is.</summary>
    private bool IsResourceBound(string name) =>
        _world.Production.Config.Mines.ContainsKey(name) || name is "lumberjack" or "stonecutter" or "fisher" or "waterworks" or "forester" or "rice_farm";

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

    private readonly Dictionary<string, long> _nextRoomySearch = new();

    /// <summary>
    /// No spot near the chain (big buildings in a crowded town): the own cells with the most free land
    /// are tried, at most once a minute per building, as it takes a while.
    /// </summary>
    private Building? PlaceWhereRoomy(BuildingDefinition definition)
    {
        if (IsResourceBound(definition.Name) || _world.Tick < _nextRoomySearch.GetValueOrDefault(definition.Name))
            return null;
        _nextRoomySearch[definition.Name] = _world.Tick + 60 * World.TicksPerSecond;
        foreach (var cell in _survey.Cells.Where(c => c.Own > 0).OrderByDescending(c => c.OwnPart!.Buildable).Take(5))
            if (Place(definition, cell.Center) is { } site)
                return site;
        return null;
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
                || !_world.Reachable(Player, homeDoor, p + definition.Door))
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
            "waterworks" => Around(p, 6, q => map.TerrainAt(q) == TerrainType.River ? 1 : 0) > 0,
            "farm" => Around(p + (definition.WorkCenter ?? default), 6,
                q => map.TerrainAt(q) == TerrainType.Grass && map.ObjectCodeAt(q) == MapObjectCodes.None && _world.IsFreeGround(q) ? 1 : 0) >= 20,
            // Vines, rice and beehives: enough free ground of their kind around.
            _ when _world.Production.Config.Gatherers.TryGetValue(definition.Name, out var gatherer) && gatherer.Action == GatherAction.Farm
                   && _world.Production.Config.Crops.TryGetValue(gatherer.Crop, out var crop) =>
                Around(p + (definition.WorkCenter ?? default), definition.WorkRadius ?? 6,
                    q => crop.Ground.Contains(map.TerrainAt(q)) && map.ObjectCodeAt(q) == MapObjectCodes.None && _world.IsFreeGround(q) ? 1 : 0) >= 10,
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

    /// <summary>
    /// Builds a tower towards what the land lacks, if its material is on hand. Nothing is kept back
    /// for a tower that cannot be afforded yet: planks and stones are made again.
    /// </summary>
    private void Expand(List<Building> mine)
    {
        if (_catalog.Find(_config.ExpansionBuilding) is not { } tower)
            return;
        int towerSites = mine.Count(b => IsMilitary(b) && b.State != BuildingState.Finished);
        if (towerSites >= _config.MaxTowerSites)
            return;
        // Somebody has to occupy it.
        int spareSoldiers = _world.Settlers.Count(s => s.Player == Player && s.Profession == Profession.Soldier && !s.HasTask && !s.IsInside);
        int emptyTowers = mine.Count(b => IsMilitary(b) && b.State == BuildingState.Finished && !_world.Military.IsOccupied(b));
        spareSoldiers -= emptyTowers + towerSites;
        if (spareSoldiers <= 0)
            return;
        (int Planks, int Stones) cost = (Cost(tower, Material.Plank), Cost(tower, Material.Stone));
        int planks = Free(Material.Plank, mine), stones = Free(Material.Stone, mine);
        // A wanted living house comes before towers: they leave its material.
        (int Planks, int Stones) keep = HouseWanted(mine) && HouseToBuild(mine) is { } house
            ? (Cost(house, Material.Plank), Cost(house, Material.Stone))
            : default;
        if (!Enough(planks, cost.Planks + keep.Planks) || !Enough(stones, cost.Stones + keep.Stones))
            return;

        // Places an own tower (or tower site) will cover anyway do not need another.
        var military = mine.Where(IsMilitary).Select(b => b.Position).ToList();
        int covered = (int)(_world.Military.Config.Territory.GetValueOrDefault(tower.Name, 20) * 0.7);
        bool Uncovered(MapSurvey.Cell c) => military.All(m => PathFinder.Distance(m, c.Center) > covered);

        foreach (var (need, wanted) in Needs(spareSoldiers, mine))
        {
            // Plain land only from a full store: what the economy needs comes first.
            if (need == "land" && (planks - keep.Planks < cost.Planks + _config.LandStock || stones - keep.Stones < cost.Stones + _config.LandStock))
                continue;
            if (NearestUnclaimed(c => wanted(c) && Uncovered(c)) is not { } target)
                continue;
            if (PlaceTower(tower, target, mine, resource: need is not ("land" or "room")) is { } site)
            {
                Note($"expands towards {need} at {target.X},{target.Y}: tower at {site.Position.X},{site.Position.Y}");
                if (need == "room")
                    _lacksRoom = false;
                return;
            }
        }
    }

    /// <summary>What the own land lacks, most urgent first, with how to recognize a cell that has it.</summary>
    private IEnumerable<(string Need, Func<MapSurvey.Cell, bool> Has)> Needs(int spareSoldiers, List<Building> mine)
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
        if (own.River == 0)
            yield return ("water", c => c.River > 0);
        // Rice wants swampy ground (Asians' mana).
        if (own.Swamp < 20 && _config.BuildOrder.Any(b => b.Building == "rice_farm"))
            yield return ("swamp", c => c.Swamp >= 20);
        if (_lacksRoom)
            yield return ("room", roomy);
        // Gold (gems) for the combat strength: late, once the build order up to its mine is done.
        foreach (var ore in _config.LateOres)
            if (own.Ore[(int)ore] == 0 && _world.Production.Config.Mines.FirstOrDefault(m => m.Value.Resource == ore).Key is { } oreMine
                && ReachedBefore(oreMine, mine))
                yield return (ore.ToString(), c => c.Ore[(int)ore] > 0);
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

    /// <summary>
    /// A tower on the own-land spot nearest the target, if that gets the land closer to it. Spots whose new
    /// land would be mostly water are passed over (<see cref="AiConfig.MaxWaterShare"/>); towards a
    /// <paramref name="resource"/> the spot with the least water is taken if every spot has too much.
    /// </summary>
    private Building? PlaceTower(BuildingDefinition tower, GridPoint target, List<Building> mine, bool resource)
    {
        var map = _world.Map!;
        int closest = mine.Where(IsMilitary).Select(b => PathFinder.Distance(b.Position, target)).DefaultIfEmpty(int.MaxValue).Min();
        int radius = (int)_world.Military.Config.Territory.GetValueOrDefault(tower.Name, 20);
        GridPoint? leastWater = null;
        double leastShare = double.MaxValue;
        foreach (var offset in Spiral)
        {
            var p = target + offset;
            if (PathFinder.Distance(p, target) + 6 > closest)
                break;
            if (!map.Contains(p) || map.OwnerAt(p) != Player || !_world.CanPlace(tower, p, out _, Player)
                || !_world.Reachable(Player, _home!.Door, p + tower.Door))
                continue;
            double share = WaterShare(p, radius);
            if (share <= _config.MaxWaterShare)
                return _world.PlaceConstructionSite(tower, Player, p);
            if (share < leastShare)
                (leastWater, leastShare) = (p, share);
        }
        if (resource && leastWater is { } spot)
        {
            Note($"tower at {spot.X},{spot.Y} takes {leastShare:P0} water");
            return _world.PlaceConstructionSite(tower, Player, spot);
        }
        return null;
    }

    /// <summary>Share of water among the unclaimed points within <paramref name="radius"/> (every other point sampled).</summary>
    private double WaterShare(GridPoint center, int radius)
    {
        var map = _world.Map!;
        int unclaimed = 0, water = 0;
        foreach (var offset in TerritorySample(radius))
        {
            var p = center + offset;
            if (!map.Contains(p) || map.OwnerAt(p) is not null)
                continue;
            unclaimed++;
            if (IsWater(p))
                water++;
        }
        return unclaimed == 0 ? 0 : (double)water / unclaimed;
    }

    private static readonly Dictionary<int, GridPoint[]> TerritorySamples = new();

    private static GridPoint[] TerritorySample(int radius)
    {
        lock (TerritorySamples)
        {
            if (!TerritorySamples.TryGetValue(radius, out var sample))
                TerritorySamples[radius] = sample = PathFinder.Circle(new GridPoint(0, 0), radius)
                    .Where(o => (o.X & 1) == 0 && (o.Y & 1) == 0).ToArray();
            return sample;
        }
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
