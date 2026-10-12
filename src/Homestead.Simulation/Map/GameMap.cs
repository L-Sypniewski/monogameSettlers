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
    /// <summary>Object per point as original object code (0 = none, see <see cref="MapObjectCodes"/>).</summary>
    private readonly byte[] _objects;

    /// <summary>How many object footprints cover a point (objects can block their neighbors).</summary>
    private readonly byte[] _blockers;

    private readonly bool[] _walkable;

    /// <summary>Resource under a point (fish in water, ores in mountains) and its amount (0-15 in original maps).</summary>
    /// <summary>Owner of each point: player index, or <see cref="NoOwner"/>.</summary>
    private readonly byte[] _owner;

    private readonly ResourceType[] _resourceType;
    private readonly byte[] _resourceAmount;

    public GameMap(int size)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 2);
        Size = size;
        _terrain = new TerrainType[size * size];
        _height = new byte[size * size];
        _objects = new byte[size * size];
        _blockers = new byte[size * size];
        _walkable = new bool[size * size];
        Array.Fill(_walkable, true);
        _owner = new byte[size * size];
        Array.Fill(_owner, NoOwner);
        _resourceType = new ResourceType[size * size];
        _resourceAmount = new byte[size * size];
    }

    public int Size { get; }

    /// <summary>Owner value of land that belongs to nobody (as in the original maps).</summary>
    public const byte NoOwner = 255;

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
            map._owner[i] = area.Owner[i];
            // High nibble type, low nibble amount; 0 means nothing.
            byte resource = area.Resources[i];
            if (resource != 0 && resource >> 4 <= (int)ResourceType.Sulfur)
            {
                map._resourceType[i] = (ResourceType)(resource >> 4);
                map._resourceAmount[i] = (byte)(resource & 0x0f);
            }
        }
        for (int i = 0; i < map.PointCount; i++)
            map.SetObjectCode(map.Point(i), area.Object[i]);
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

    public MapObjectKind ObjectAt(GridPoint p) => MapObjectKinds.FromOriginal(_objects[Index(p)]);

    /// <summary>The object as original object code; decides both rules and graphics.</summary>
    public byte ObjectCodeAt(GridPoint p) => _objects[Index(p)];

    /// <summary>The player owning the point, or null.</summary>
    public int? OwnerAt(GridPoint p) => _owner[Index(p)] is var o && o != NoOwner ? o : null;

    /// <summary>Owner by point index; <see cref="NoOwner"/> for nobody.</summary>
    public byte OwnerAt(int index) => _owner[index];

    public void SetOwner(GridPoint p, int? player) => _owner[Index(p)] = player is { } o ? (byte)o : NoOwner;

    /// <summary>Resource under the point; amount 0 means there is nothing (left).</summary>
    public (ResourceType Type, int Amount) ResourceAt(GridPoint p) => (_resourceType[Index(p)], _resourceAmount[Index(p)]);

    /// <summary>Amount of <paramref name="type"/> under the point, 0 if there is another or no resource.</summary>
    public int ResourceAmount(GridPoint p, ResourceType type) =>
        _resourceType[Index(p)] == type ? _resourceAmount[Index(p)] : 0;

    public void SetResource(GridPoint p, ResourceType type, int amount)
    {
        _resourceType[Index(p)] = type;
        _resourceAmount[Index(p)] = (byte)Math.Clamp(amount, 0, 255);
    }

    public bool IsWalkable(GridPoint p) => Contains(p) && _walkable[Index(p)];

    public bool IsWalkable(int index) => _walkable[index];

    /// <summary>Raised when terrain or height of a point changes after loading (for redrawing).</summary>
    public event Action<GridPoint>? PointChanged;

    public void SetTerrain(GridPoint p, TerrainType terrain)
    {
        _terrain[Index(p)] = terrain;
        UpdateWalkable(Index(p));
        PointChanged?.Invoke(p);
    }

    public void SetHeight(GridPoint p, int height)
    {
        _height[Index(p)] = (byte)Math.Clamp(height, 0, 255);
        PointChanged?.Invoke(p);
    }

    /// <summary>Sets an object by kind, using a representative code (tests, tools).</summary>
    public void SetObject(GridPoint p, MapObjectKind kind) => SetObjectCode(p, kind switch
    {
        MapObjectKind.None => MapObjectCodes.None,
        MapObjectKind.Tree => MapObjectCodes.FirstTree,
        MapObjectKind.ResourceStone => MapObjectCodes.FullStone,
        MapObjectKind.Decoration => MapObjectCodes.FirstPlant,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    });

    public void SetObjectCode(GridPoint p, byte code)
    {
        int index = Index(p);
        var oldKind = MapObjectKinds.FromOriginal(_objects[index]);
        var newKind = MapObjectKinds.FromOriginal(code);
        _objects[index] = code;
        if (oldKind != newKind)
        {
            ChangeFootprint(p, oldKind, -1);
            ChangeFootprint(p, newKind, +1);
        }
        PointChanged?.Invoke(p);
    }

    /// <summary>Marks a point as blocked by something other than a map object (e.g. a building). Counted.</summary>
    public void AddBlocker(GridPoint p) => Cover(p, +1);

    public void RemoveBlocker(GridPoint p) => Cover(p, -1);

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
