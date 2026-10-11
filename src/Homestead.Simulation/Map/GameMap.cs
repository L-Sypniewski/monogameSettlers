using Homestead.Assets.Maps;

namespace Homestead.Simulation.Map;

/// <summary>
/// The playing field: a square grid of points with terrain, height and an optional object.
/// Points are addressed by <see cref="GridPoint"/> or by index (<c>y * Size + x</c>).
/// </summary>
public sealed class GameMap
{
    private readonly TerrainType[] _terrain;
    private readonly byte[] _height;
    private readonly MapObjectKind[] _objects;

    /// <summary>How many object footprints cover a point (objects can block their neighbors).</summary>
    private readonly byte[] _blockers;

    private readonly bool[] _walkable;

    public GameMap(int size)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 2);
        Size = size;
        _terrain = new TerrainType[size * size];
        _height = new byte[size * size];
        _objects = new MapObjectKind[size * size];
        _blockers = new byte[size * size];
        _walkable = new bool[size * size];
        Array.Fill(_walkable, true);
    }

    public int Size { get; }

    public int PointCount => Size * Size;

    /// <summary>Incremented whenever walkability changes, so caches (e.g. path search regions) can notice.</summary>
    public int WalkabilityVersion { get; private set; }

    public static GameMap FromOriginal(MapArea area)
    {
        var map = new GameMap(area.Size);
        for (int i = 0; i < map.PointCount; i++)
        {
            map._terrain[i] = TerrainTypes.FromOriginal(area.Landscape[i]);
            map._height[i] = area.Height[i];
        }
        for (int i = 0; i < map.PointCount; i++)
            map.SetObject(map.Point(i), MapObjectKinds.FromOriginal(area.Object[i]));
        for (int i = 0; i < map.PointCount; i++)
            map._walkable[i] = map.ComputeWalkable(i);
        map.WalkabilityVersion = 0;
        return map;
    }

    public bool Contains(GridPoint p) => (uint)p.X < (uint)Size && (uint)p.Y < (uint)Size;

    public int Index(GridPoint p) => p.Y * Size + p.X;

    public GridPoint Point(int index) => new(index % Size, index / Size);

    public TerrainType TerrainAt(GridPoint p) => _terrain[Index(p)];

    public int HeightAt(GridPoint p) => _height[Index(p)];

    public MapObjectKind ObjectAt(GridPoint p) => _objects[Index(p)];

    public bool IsWalkable(GridPoint p) => Contains(p) && _walkable[Index(p)];

    public bool IsWalkable(int index) => _walkable[index];

    public void SetTerrain(GridPoint p, TerrainType terrain)
    {
        _terrain[Index(p)] = terrain;
        UpdateWalkable(Index(p));
    }

    public void SetObject(GridPoint p, MapObjectKind kind)
    {
        int index = Index(p);
        ChangeFootprint(p, _objects[index], -1);
        _objects[index] = kind;
        ChangeFootprint(p, kind, +1);
    }

    private void ChangeFootprint(GridPoint p, MapObjectKind kind, int delta)
    {
        if (!kind.BlocksWalking())
            return;
        Cover(p, delta);
        if (kind.BlocksNeighbors())
            for (int d = 0; d < DirectionExtensions.Count; d++)
                Cover(p.Neighbor((Direction)d), delta);
    }

    private void Cover(GridPoint p, int delta)
    {
        if (!Contains(p))
            return;
        int index = Index(p);
        _blockers[index] = (byte)(_blockers[index] + delta);
        UpdateWalkable(index);
    }

    private void UpdateWalkable(int index)
    {
        bool walkable = ComputeWalkable(index);
        if (walkable == _walkable[index])
            return;
        _walkable[index] = walkable;
        WalkabilityVersion++;
    }

    private bool ComputeWalkable(int index) => _terrain[index].IsWalkable() && _blockers[index] == 0;
}
