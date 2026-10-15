using Homestead.Simulation.Buildings;
using Homestead.Simulation.Economy;
using Homestead.Simulation.Map;
using Homestead.Simulation.Pathfinding;

namespace Homestead.Simulation;

/// <summary>Buildings, piles of goods and the logistics that connects them.</summary>
public sealed partial class World
{
    private readonly List<Building> _buildings = new();
    private readonly Dictionary<int, Pile> _pilesByPoint = new();

    /// <summary>Footprint (blocked) points of all buildings and construction sites.</summary>
    private readonly Dictionary<int, Building> _footprints = new();

    /// <summary>Protected points (kept free around buildings) and how many buildings protect them.</summary>
    private readonly Dictionary<int, int> _protected = new();

    private int _nextPileId;

    public IReadOnlyList<Building> Buildings => _buildings;

    public IEnumerable<Pile> Piles => _pilesByPoint.Values;

    public Logistics Logistics { get; }

    public Construction Construction { get; }

    public Production Production { get; }

    public Recruitment Recruitment { get; }

    public Idling Idling { get; }

    public Housing Housing { get; }

    public Geology Geology { get; }

    public Military.Military Military { get; }

    /// <summary>
    /// Buildings may only be placed on the player's own land (worlds from original maps). Off for
    /// test worlds without territory.
    /// </summary>
    public bool TerritoryRules { get; set; }

    /// <summary>Not part of a building, its surroundings or a pile (free for trees, work spots, ...).</summary>
    public bool IsFreeGround(GridPoint p)
    {
        var map = RequireMap();
        if (!map.Contains(p))
            return false;
        int index = map.Index(p);
        return !_footprints.ContainsKey(index) && !_protected.ContainsKey(index) && !_pilesByPoint.ContainsKey(index);
    }

    /// <summary>The building or construction site whose footprint covers the point.</summary>
    public Building? BuildingAt(GridPoint p) =>
        RequireMap().Contains(p) && _footprints.TryGetValue(RequireMap().Index(p), out var building) && building.Exists ? building : null;

    /// <summary>A short description of what a building is doing, for the player.</summary>
    public string StatusOf(Building building)
    {
        if (!building.Exists)
            return "gone";
        switch (building.State)
        {
            case BuildingState.Flattening:
                return $"construction site: levelling the ground, {building.Unlevelled.Count} points left";
            case BuildingState.Constructing:
                bool material = building.Piles.Any(p => p.Role == PileRole.Request && p.Count > 0);
                return $"construction site: {building.BuildStepsDone}/{building.BuildStepsTotal} built" +
                       (material ? "" : ", waiting for material");
        }
        if (Military.Status(building) is { } military)
            return military;
        if (Magic.Status(building) is { } magic)
            return magic;
        if (Production.Status(building) is { } status)
            return status;
        if (Housing.ResidentsLeft(building) is { } left)
            return left > 0 ? $"{left} residents still to come" : "all residents moved out";
        var stored = building.Piles.Where(p => p.Role == PileRole.Storage && p.Count > 0 && p.Material is not null)
            .GroupBy(p => p.Material!.Value).Select(g => $"{g.Sum(p => p.Count)} {g.Key}").ToList();
        if (building.Piles.Any(p => p.Role == PileRole.Storage))
            return stored.Count == 0 ? "storehouse: empty" : "storehouse: " + string.Join(", ", stored);
        return "no function yet";
    }

    /// <summary>
    /// Whether the player may use the goods of a pile: piles of its own buildings, and loose goods on
    /// its own land (anywhere in worlds without territory).
    /// </summary>
    public bool BelongsTo(Pile pile, int player) =>
        pile.Owner is { } owner ? owner.Player == player : !TerritoryRules || RequireMap().OwnerAt(pile.Position) == player;

    public Pile? PileAt(GridPoint p) => _pilesByPoint.GetValueOrDefault(RequireMap().Index(p));

    internal bool HasPile(Pile pile) => _pilesByPoint.TryGetValue(RequireMap().Index(pile.Position), out var at) && at == pile;

    /// <summary>
    /// Checks whether a building fits at <paramref name="position"/>: footprint on the map, on allowed and
    /// walkable ground, not overlapping other buildings or their protected surroundings, and its own
    /// surroundings (door, piles) on the map and not under another building.
    /// </summary>
    public bool CanPlace(BuildingDefinition definition, GridPoint position, out string reason, int player = 0)
    {
        var map = RequireMap();
        foreach (var offset in definition.Blocked)
        {
            var p = position + offset;
            if (!map.Contains(p))
                return Fail("outside the map", out reason);
            int index = map.Index(p);
            if (_footprints.ContainsKey(index) || _protected.ContainsKey(index))
                return Fail("too close to another building", out reason);
            // Extra room between buildings, for the settlers and the looks.
            int margin = Production.Config.BuildingMargin;
            if (margin > 0 && PathFinder.Circle(p, margin).Any(q => map.Contains(q)
                    && (_footprints.ContainsKey(map.Index(q)) || _protected.ContainsKey(map.Index(q)))))
                return Fail("too close to another building", out reason);
            if (!map.IsWalkable(p) || _pilesByPoint.ContainsKey(index) || map.ObjectAt(p) == MapObjectKind.Field)
                return Fail("something is in the way", out reason);
            if (!definition.Ground.Contains(map.TerrainAt(p).GroundName()))
                return Fail($"cannot build on {map.TerrainAt(p)}", out reason);
            if (TerritoryRules && map.OwnerAt(p) != player)
                return Fail("not on own land", out reason);
        }
        foreach (var offset in definition.Protected)
        {
            var p = position + offset;
            if (!map.Contains(p))
                return Fail("too close to the map edge", out reason);
            if (_footprints.ContainsKey(map.Index(p)))
                return Fail("too close to another building", out reason);
        }
        // Door and piles must be reachable, or carriers and workers could never get there.
        foreach (var offset in definition.ConstructionPiles.Concat(definition.RequestPiles).Concat(definition.OfferPiles)
                     .Select(pile => pile.Offset).Append(definition.Door))
        {
            var p = position + offset;
            if (!map.Contains(p) || !map.IsWalkable(p) || _footprints.ContainsKey(map.Index(p)))
                return Fail("door or pile place blocked", out reason);
            // Workers and carriers walk on their own land only.
            if (TerritoryRules && map.OwnerAt(p) != player)
                return Fail("door or pile place not on own land", out reason);
        }
        if (TerritoryRules && definition.BuilderSpots.Any(s => !map.Contains(position + s.Offset) || map.OwnerAt(position + s.Offset) != player))
            return Fail("builders would stand outside own land", out reason);
        if (!DoorLeadsOut(definition, position))
            return Fail("the door would lead nowhere", out reason);
        if (PlanLevelling(definition, position, player) is null)
            return Fail("too steep to level", out reason);
        reason = "";
        return true;

        static bool Fail(string why, out string reason)
        {
            reason = why;
            return false;
        }
    }

    /// <summary>
    /// Whether one can still walk away from the door once the building stands: a small flood fill from
    /// the door around the footprint. Found by the AI: a stonecutter against the blocked ring of a
    /// quarry stone, its door opening into a pocket between the building and the stone.
    /// </summary>
    private bool DoorLeadsOut(BuildingDefinition definition, GridPoint position)
    {
        const int Enough = 60;
        var map = RequireMap();
        var footprint = definition.Blocked.Select(o => position + o).ToHashSet();
        var door = position + definition.Door;
        var seen = new HashSet<GridPoint> { door };
        var queue = new Queue<GridPoint>(seen);
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            for (int d = 0; d < DirectionExtensions.Count; d++)
            {
                var n = p.Neighbor((Direction)d);
                if (!map.IsWalkable(n) || footprint.Contains(n) || _footprints.ContainsKey(map.Index(n)) || !seen.Add(n))
                    continue;
                if (seen.Count >= Enough)
                    return true;
                queue.Enqueue(n);
            }
        }
        return false;
    }

    /// <summary>
    /// Places a finished building. Fails where <see cref="CanPlace"/> fails, unless
    /// <paramref name="force"/> is set (map loading: the editor already decided).
    /// </summary>
    public Building? PlaceBuilding(BuildingDefinition definition, int player, GridPoint position, bool force = false)
    {
        var map = RequireMap();
        if (definition.Blocked.Any(o => !map.Contains(position + o)))
            return null;
        if (!force && !CanPlace(definition, position, out _, player))
            return null;
        // Finished at once: so is the levelling (a loaded map stands as the editor left it).
        if (!force && PlanLevelling(definition, position, player) is { } plan)
            foreach (var (p, target) in plan.Targets)
                map.SetHeight(p, target);

        var building = Register(definition, player, position, BuildingState.Finished);
        foreach (var p in building.BlockedPoints)
            map.AddBlocker(p);
        MoveOffBlocked(building.BlockedPoints.ToList());
        CreateOperatingPiles(building);
        return building;
    }

    /// <summary>Points construction sites still level, by map index, with their target height.</summary>
    private readonly Dictionary<int, int> _pendingLevels = new();

    /// <summary>The height a point will have once the sites levelling it are done.</summary>
    private int PlannedHeight(GridPoint p)
    {
        var map = RequireMap();
        return _pendingLevels.TryGetValue(map.Index(p), out int target) ? target : map.HeightAt(p);
    }

    /// <summary>
    /// What levelling a site at <paramref name="position"/> takes, or null if too much: the footprint goes to its
    /// mean height; ring by ring around it each point is cut or filled just enough that no neighbor
    /// differs by more than <see cref="ProductionConfig.LevellingMaxSlope"/> (the point d rings out at most
    /// d times that from the footprint height). It stops at the first ring left as it is: the ground
    /// beyond is no steeper. Null when the height moved in all exceeds
    /// <see cref="ProductionConfig.LevellingMaxWork"/> or a point to change cannot be: under a building,
    /// levelled by another site, not walkable (water, trees, stones) or not on own land.
    /// </summary>
    internal LevellingPlan? PlanLevelling(BuildingDefinition definition, GridPoint position, int player)
    {
        var map = RequireMap();
        var config = Production.Config;
        var footprint = definition.Blocked.Select(o => position + o).ToHashSet();
        if (footprint.Count == 0)
            return new LevellingPlan(0, new Dictionary<GridPoint, int>(), 0);
        int height = (int)Math.Round(footprint.Average(PlannedHeight));
        var targets = new Dictionary<GridPoint, int>();
        int work = 0;
        foreach (var p in footprint)
        {
            if (_pendingLevels.ContainsKey(map.Index(p)))
                return null;
            int h = map.HeightAt(p);
            if (h == height)
                continue;
            targets[p] = height;
            work += Math.Abs(h - height);
        }
        if (work > config.LevellingMaxWork)
            return null;
        var seen = new HashSet<GridPoint>(footprint);
        var ring = footprint.ToList();
        for (int d = 1; ring.Count > 0; d++)
        {
            int low = height - config.LevellingMaxSlope * d, high = height + config.LevellingMaxSlope * d;
            var next = new List<GridPoint>();
            bool changed = false;
            foreach (var p in ring)
                for (int dir = 0; dir < DirectionExtensions.Count; dir++)
                {
                    var q = p.Neighbor((Direction)dir);
                    if (!map.Contains(q) || !seen.Add(q))
                        continue;
                    next.Add(q);
                    int h = PlannedHeight(q);
                    int target = Math.Clamp(h, low, high);
                    if (target == h)
                        continue;
                    int index = map.Index(q);
                    if (_footprints.ContainsKey(index) || _pendingLevels.ContainsKey(index) || !map.IsWalkable(q)
                        || TerritoryRules && map.OwnerAt(q) != player)
                        return null;
                    targets[q] = target;
                    work += Math.Abs(h - target);
                    changed = true;
                    if (work > config.LevellingMaxWork)
                        return null;
                }
            if (!changed)
                break;
            ring = next;
        }
        return new LevellingPlan(height, targets, work);
    }

    /// <summary>
    /// How much levelling a site at <paramref name="position"/> needs, from 0 (none) to 1 (the most a site may
    /// need, <see cref="ProductionConfig.LevellingMaxWork"/>, or more).
    /// </summary>
    public float LevellingEffort(BuildingDefinition definition, GridPoint position, int player = 0) =>
        PlanLevelling(definition, position, player) is { } plan
            ? Math.Min(1, plan.Work / (float)Math.Max(1, Production.Config.LevellingMaxWork))
            : 1;

    /// <summary>A point of the site reached its target height.</summary>
    internal void Levelled(Building site, GridPoint p)
    {
        site.Unlevelled.Remove(p);
        _pendingLevels.Remove(RequireMap().Index(p));
    }

    /// <summary>The site levels no more (done, or gone): its points are free for other sites.</summary>
    private void DropLevelling(Building site)
    {
        var map = RequireMap();
        foreach (var p in site.Unlevelled.Keys)
            _pendingLevels.Remove(map.Index(p));
        site.Unlevelled.Clear();
    }

    /// <summary>
    /// Starts a construction site: diggers level the ground, carriers bring the construction material,
    /// builders turn it into the building.
    /// </summary>
    public Building? PlaceConstructionSite(BuildingDefinition definition, int player, GridPoint position)
    {
        if (!CanPlace(definition, position, out _, player))
            return null;

        var map = RequireMap();
        var plan = PlanLevelling(definition, position, player)!;
        var building = Register(definition, player, position, BuildingState.Flattening);
        building.TargetHeight = plan.Height;
        foreach (var (p, target) in plan.Targets)
        {
            building.Unlevelled[p] = target;
            _pendingLevels[map.Index(p)] = target;
        }

        foreach (var pile in definition.ConstructionPiles)
        {
            if (pile.Material is not { } material || pile.Required <= 0)
                continue;
            AddPile(new Pile(_nextPileId++, position + pile.Offset, PileRole.Request, material, pile.Required, building), building);
            building.BuildStepsTotal += pile.Required;
        }

        if (building.Unlevelled.Count == 0)
            BeginConstruction(building);
        return building;
    }

    /// <summary>How long the dust cloud of a building coming down lasts (twice as fast as in JSettlers).</summary>
    public const int DustCloudTicks = 3 * TicksPerSecond / 5;

    private readonly List<DustCloud> _dustClouds = new();

    /// <summary>Dust clouds of buildings that came down within the last <see cref="DustCloudTicks"/>.</summary>
    public IReadOnlyList<DustCloud> DustClouds => _dustClouds;

    /// <summary>
    /// Removes a building or site: it comes down in a cloud of dust, goods in its piles stay on the
    /// ground, part of its construction material too (<see cref="ProductionConfig.DemolitionPayback"/>);
    /// workers and carriers react.
    /// </summary>
    public void RemoveBuilding(Building building)
    {
        if (!building.Exists)
            return;
        building.Exists = false;
        var map = RequireMap();
        DropLevelling(building);
        foreach (var p in building.BlockedPoints)
        {
            _footprints.Remove(map.Index(p));
            if (building.State != BuildingState.Flattening)
                map.RemoveBlocker(p);
        }
        foreach (var offset in building.Definition.Protected)
            Unprotect(map, building.Position + offset);

        // Material built in: all of it once finished, else what the builders used up of each pile.
        // (A construction pile's capacity shrinks with every unit built in.)
        var builtIn = building.Definition.ConstructionPiles.Where(p => p.Material is not null)
            .GroupBy(p => p.Material!.Value)
            .Select(g => (Material: g.Key, Count: building.State == BuildingState.Finished
                ? g.Sum(p => p.Required)
                : g.Sum(p => p.Required) - building.Piles.Where(p => p.Role == PileRole.Request && p.AcceptedMaterial == g.Key).Sum(p => p.Capacity)))
            .ToList();
        foreach (var pile in building.Piles.ToList())
            RemovePileDroppingGoods(building, pile);
        foreach (var (material, count) in builtIn)
            for (int i = 0; i < (int)(count * Production.Config.DemolitionPayback); i++)
                DropGoods(building.Position, material);

        _dustClouds.Add(new DustCloud(building.Position, Tick));
    }

    /// <summary>Dust clouds go once they are through.</summary>
    private void ClearDustClouds()
    {
        if (_dustClouds.Count > 0)
            _dustClouds.RemoveAll(c => Tick - c.Tick >= DustCloudTicks);
    }

    /// <summary>Levelling is done: the footprint becomes solid and builders can start.</summary>
    internal void BeginConstruction(Building building)
    {
        var map = RequireMap();
        foreach (var p in building.BlockedPoints)
        {
            map.SetHeight(p, building.TargetHeight);
            map.SetTerrain(p, TerrainType.Flattened);
            map.AddBlocker(p);
        }
        MoveOffBlocked(building.BlockedPoints.ToList());
        DropLevelling(building);
        building.State = BuildingState.Constructing;
        if (building.BuildStepsTotal == 0)
            FinishConstruction(building);
    }

    internal void FinishConstruction(Building building)
    {
        foreach (var pile in building.Piles.ToList())
            RemovePileDroppingGoods(building, pile);
        building.State = BuildingState.Finished;
        CreateOperatingPiles(building);
    }

    /// <summary>A builder uses up one unit of construction material. Returns false if none is there.</summary>
    internal bool ConsumeConstructionMaterial(Building building)
    {
        var pile = building.Piles.FirstOrDefault(p => p.Role == PileRole.Request && p.Count > 0);
        if (pile is null)
            return false;
        pile.Count--;
        pile.Capacity--; // the demand shrinks with every unit built in
        return true;
    }

    /// <summary>How far (in steps) the player may move a work center away from its building.</summary>
    public const int MaxWorkCenterDistance = 20;

    /// <summary>
    /// Moves the work area of <paramref name="building"/> to <paramref name="target"/>. A target farther
    /// than <see cref="MaxWorkCenterDistance"/> from the building (or off the map) is replaced by the
    /// nearest point that is allowed. Returns the new center, or null if the building has no work area.
    /// </summary>
    public GridPoint? SetWorkCenter(Building building, GridPoint target)
    {
        if (!building.Exists || !building.HasWorkArea)
            return null;
        var map = RequireMap();
        var center = target;
        if (PathFinder.Distance(building.Position, target) > MaxWorkCenterDistance || !map.Contains(target))
        {
            // Nearest allowed point by straight-line distance (see PathFinder.InCircle), then by steps.
            int bestSquared = int.MaxValue, bestSteps = int.MaxValue;
            for (int dy = -MaxWorkCenterDistance; dy <= MaxWorkCenterDistance; dy++)
            for (int dx = -MaxWorkCenterDistance; dx <= MaxWorkCenterDistance; dx++)
            {
                var p = new GridPoint(building.Position.X + dx, building.Position.Y + dy);
                if (PathFinder.Distance(building.Position, p) > MaxWorkCenterDistance || !map.Contains(p))
                    continue;
                int ex = target.X - p.X, ey = target.Y - p.Y;
                int squared = ex * ex + ey * ey - ex * ey;
                int steps = PathFinder.Distance(p, target);
                if (squared < bestSquared || squared == bestSquared && steps < bestSteps)
                    (center, bestSquared, bestSteps) = (p, squared, steps);
            }
        }
        building.WorkCenter = center;
        return center;
    }

    /// <summary>Adds goods to a loose pile on the map (map loading, tests).</summary>
    public Pile? AddLooseGoods(GridPoint position, Material material, int count)
    {
        var map = RequireMap();
        if (!map.IsWalkable(position) || _pilesByPoint.ContainsKey(map.Index(position)) || _footprints.ContainsKey(map.Index(position)))
            return null;
        var pile = new Pile(_nextPileId++, position, PileRole.Loose, material, Materials.MaxPileSize, owner: null)
        {
            Count = Math.Clamp(count, 1, Materials.MaxPileSize),
        };
        AddPile(pile, owner: null);
        return pile;
    }

    /// <summary>Puts one unit down at or near <paramref name="near"/>, joining a loose pile of the same material if possible.</summary>
    internal void DropGoods(GridPoint near, Material material)
    {
        var map = RequireMap();
        for (int r = 0; r <= 6; r++)
        for (int dy = -r; dy <= r; dy++)
        for (int dx = -r; dx <= r; dx++)
        {
            var p = new GridPoint(near.X + dx, near.Y + dy);
            if (PathFinder.Distance(near, p) != r || !map.IsWalkable(p) || _footprints.ContainsKey(map.Index(p)))
                continue;
            if (_pilesByPoint.TryGetValue(map.Index(p), out var pile))
            {
                if (pile.Role == PileRole.Loose && pile.Material == material && pile.Count < pile.Capacity)
                {
                    pile.Count++;
                    return;
                }
                continue;
            }
            AddLooseGoods(p, material, 1);
            return;
        }
        // Nowhere to put it: the good is lost. Should not happen on real maps.
    }

    internal void TakeFromPile(Pile pile)
    {
        pile.Count--;
        pile.Reserved = Math.Max(0, pile.Reserved - 1);
        if (pile.Role == PileRole.Loose && pile.Count == 0 && pile.Reserved == 0)
            _pilesByPoint.Remove(RequireMap().Index(pile.Position));
        else
            pile.ResetMaterialIfEmpty();
    }

    private Building Register(BuildingDefinition definition, int player, GridPoint position, BuildingState state)
    {
        var map = RequireMap();
        var building = new Building(_buildings.Count, definition, player, position) { State = state };
        foreach (var p in building.BlockedPoints)
            _footprints[map.Index(p)] = building;
        foreach (var offset in definition.Protected)
        {
            var p = position + offset;
            if (map.Contains(p))
                _protected[map.Index(p)] = _protected.GetValueOrDefault(map.Index(p)) + 1;
        }
        _buildings.Add(building);
        return building;
    }

    private void CreateOperatingPiles(Building building)
    {
        var definition = building.Definition;
        foreach (var pile in definition.RequestPiles)
        {
            var role = pile.Material is null ? PileRole.Storage : PileRole.Request;
            // Mines take only food; each food pile holds a few units, so the food spreads over more mines.
            int capacity = role == PileRole.Storage ? Production.Config.StoragePileCapacity
                : Production.Config.Mines.ContainsKey(definition.Name) ? Production.Config.MineFoodPileCapacity
                : Materials.MaxPileSize;
            AddPile(new Pile(_nextPileId++, building.Position + pile.Offset, role, pile.Material, capacity, building), building);
        }
        foreach (var pile in definition.OfferPiles)
            AddPile(new Pile(_nextPileId++, building.Position + pile.Offset, PileRole.Offer, pile.Material, Materials.MaxPileSize, building), building);
    }

    private void RemovePileDroppingGoods(Building building, Pile pile)
    {
        var map = RequireMap();
        if (_pilesByPoint.TryGetValue(map.Index(pile.Position), out var at) && at == pile)
            _pilesByPoint.Remove(map.Index(pile.Position));
        building.RemovePile(pile);
        if (pile.Material is { } material)
            for (int i = 0; i < pile.Count; i++)
                DropGoods(pile.Position, material);
        pile.Count = 0;
    }

    private void Unprotect(GameMap map, GridPoint p)
    {
        if (!map.Contains(p))
            return;
        int index = map.Index(p);
        if (_protected.TryGetValue(index, out int count) && count > 1)
            _protected[index] = count - 1;
        else
            _protected.Remove(index);
    }

    private void AddPile(Pile pile, Building? owner)
    {
        var map = RequireMap();
        if (!map.Contains(pile.Position))
            return;
        _pilesByPoint[map.Index(pile.Position)] = pile;
        owner?.AddPile(pile);
    }
}

/// <summary>A building came down at <paramref name="Position"/> in tick <paramref name="Tick"/>.</summary>
public sealed record DustCloud(GridPoint Position, long Tick);

/// <summary>
/// The levelling a construction site needs: the footprint's <paramref name="Height"/>, the target height of
/// every point to change (footprint and slope around it) and the height to move in all.
/// </summary>
internal sealed record LevellingPlan(int Height, IReadOnlyDictionary<GridPoint, int> Targets, int Work);
