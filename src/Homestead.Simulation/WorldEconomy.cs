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

    /// <summary>Not part of a building, its surroundings or a pile (free for trees, work spots, ...).</summary>
    public bool IsFreeGround(GridPoint p)
    {
        var map = RequireMap();
        if (!map.Contains(p))
            return false;
        int index = map.Index(p);
        return !_footprints.ContainsKey(index) && !_protected.ContainsKey(index) && !_pilesByPoint.ContainsKey(index);
    }

    public Pile? PileAt(GridPoint p) => _pilesByPoint.GetValueOrDefault(RequireMap().Index(p));

    internal bool HasPile(Pile pile) => _pilesByPoint.TryGetValue(RequireMap().Index(pile.Position), out var at) && at == pile;

    /// <summary>
    /// Checks whether a building fits at <paramref name="position"/>: footprint on the map, on allowed and
    /// walkable ground, not overlapping other buildings or their protected surroundings, and its own
    /// surroundings (door, piles) on the map and not under another building.
    /// </summary>
    public bool CanPlace(BuildingDefinition definition, GridPoint position, out string reason)
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
            if (!map.IsWalkable(p) || _pilesByPoint.ContainsKey(index) || map.ObjectAt(p) == MapObjectKind.Field)
                return Fail("something is in the way", out reason);
            if (!definition.Ground.Contains(map.TerrainAt(p).GroundName()))
                return Fail($"cannot build on {map.TerrainAt(p)}", out reason);
        }
        foreach (var offset in definition.Protected)
        {
            var p = position + offset;
            if (!map.Contains(p))
                return Fail("too close to the map edge", out reason);
            if (_footprints.ContainsKey(map.Index(p)))
                return Fail("too close to another building", out reason);
        }
        reason = "";
        return true;

        static bool Fail(string why, out string reason)
        {
            reason = why;
            return false;
        }
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
        if (!force && !CanPlace(definition, position, out _))
            return null;

        var building = Register(definition, player, position, BuildingState.Finished);
        foreach (var p in building.BlockedPoints)
            map.AddBlocker(p);
        CreateOperatingPiles(building);
        return building;
    }

    /// <summary>
    /// Starts a construction site: diggers level the ground, carriers bring the construction material,
    /// builders turn it into the building.
    /// </summary>
    public Building? PlaceConstructionSite(BuildingDefinition definition, int player, GridPoint position)
    {
        if (!CanPlace(definition, position, out _))
            return null;

        var map = RequireMap();
        var building = Register(definition, player, position, BuildingState.Flattening);
        var footprint = building.BlockedPoints.ToList();
        building.TargetHeight = (int)Math.Round(footprint.Average(map.HeightAt));
        foreach (var p in footprint)
            if (map.HeightAt(p) != building.TargetHeight)
                building.Unlevelled.Add(p);

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

    /// <summary>Removes a building or site; goods in its piles stay on the ground, workers and carriers react.</summary>
    public void RemoveBuilding(Building building)
    {
        if (!building.Exists)
            return;
        building.Exists = false;
        var map = RequireMap();
        foreach (var p in building.BlockedPoints)
        {
            _footprints.Remove(map.Index(p));
            if (building.State != BuildingState.Flattening)
                map.RemoveBlocker(p);
        }
        foreach (var offset in building.Definition.Protected)
            Unprotect(map, building.Position + offset);
        foreach (var pile in building.Piles.ToList())
            RemovePileDroppingGoods(building, pile);
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
        building.Unlevelled.Clear();
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
            AddPile(new Pile(_nextPileId++, building.Position + pile.Offset, role, pile.Material, Materials.MaxPileSize, building), building);
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
