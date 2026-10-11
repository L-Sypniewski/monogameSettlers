using Homestead.Simulation.Buildings;
using Homestead.Simulation.Economy;
using Homestead.Simulation.Pathfinding;

namespace Homestead.Simulation;

/// <summary>Buildings, piles of goods and the logistics that connects them.</summary>
public sealed partial class World
{
    private readonly List<Building> _buildings = new();
    private readonly Dictionary<int, Pile> _pilesByPoint = new();
    private int _nextPileId;

    public IReadOnlyList<Building> Buildings => _buildings;

    public IEnumerable<Pile> Piles => _pilesByPoint.Values;

    public Logistics Logistics { get; }

    public Pile? PileAt(GridPoint p) => _pilesByPoint.GetValueOrDefault(RequireMap().Index(p));

    internal bool HasPile(Pile pile) => _pilesByPoint.TryGetValue(RequireMap().Index(pile.Position), out var at) && at == pile;

    /// <summary>
    /// Places a finished building. Fails if a blocked field is outside the map or not walkable
    /// (water, another building, a tree, ...), unless <paramref name="force"/> is set (map loading).
    /// </summary>
    public Building? PlaceBuilding(BuildingDefinition definition, int player, GridPoint position, bool force = false)
    {
        var map = RequireMap();
        var blocked = definition.Blocked.Select(offset => position + offset).ToList();
        if (blocked.Any(p => !map.Contains(p)))
            return null;
        if (!force && blocked.Any(p => !map.IsWalkable(p) || _occupant[map.Index(p)] != NoOccupant))
            return null;

        var building = new Building(_buildings.Count, definition, player, position);
        foreach (var p in blocked)
            map.AddBlocker(p);
        foreach (var pile in definition.RequestPiles)
            AddPile(Pile.CreateRequest(_nextPileId++, position + pile.Offset, pile.Material, Materials.MaxPileSize, building), building);
        foreach (var pile in definition.OfferPiles)
            AddPile(new Pile(_nextPileId++, position + pile.Offset, PileRole.Offer, pile.Material, Materials.MaxPileSize, building), building);
        _buildings.Add(building);
        return building;
    }

    /// <summary>Removes a building; carriers on their way to it notice and drop their goods.</summary>
    public void RemoveBuilding(Building building)
    {
        if (!building.Exists)
            return;
        building.Exists = false;
        var map = RequireMap();
        foreach (var p in building.BlockedPoints)
            map.RemoveBlocker(p);
        foreach (var pile in building.Piles)
        {
            _pilesByPoint.Remove(map.Index(pile.Position));
            // Goods in the piles stay on the ground.
            for (int i = 0; i < pile.Count; i++)
                DropGoods(pile.Position, pile.Material!.Value);
        }
    }

    /// <summary>Adds goods to a loose pile on the map (map loading, tests).</summary>
    public Pile? AddLooseGoods(GridPoint position, Material material, int count)
    {
        var map = RequireMap();
        if (!map.IsWalkable(position) || _pilesByPoint.ContainsKey(map.Index(position)))
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
        for (int r = 0; r <= 4; r++)
        for (int dy = -r; dy <= r; dy++)
        for (int dx = -r; dx <= r; dx++)
        {
            var p = new GridPoint(near.X + dx, near.Y + dy);
            if (PathFinder.Distance(near, p) != r || !map.IsWalkable(p))
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
        else if (pile.Role == PileRole.Request && pile.Count == 0 && pile.Incoming == 0)
            pile.Material = pile.AcceptedMaterial;
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
