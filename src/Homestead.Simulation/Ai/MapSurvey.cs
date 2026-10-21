using Homestead.Simulation.Map;

namespace Homestead.Simulation.Ai;

/// <summary>
/// What an AI player knows about the map, in coarse cells: trees, quarry stone, ore, water and fish,
/// and who owns the land. The AI knows the whole map (no scouting), like the original's AI.
/// </summary>
internal sealed class MapSurvey
{
    public const int CellSize = 8;
    private static readonly int ResourceTypes = Enum.GetValues<ResourceType>().Length;

    private readonly World _world;
    private readonly GameMap _map;
    private readonly int _player;

    /// <summary>Ground rice grows on (see the crops in production.json), by terrain type; built when first needed (saved games).</summary>
    [NonSerialized] private bool[]? _swamp;

    public MapSurvey(World world, int player)
    {
        _world = world;
        var map = _map = world.Map!;
        _player = player;
        CellsPerSide = (map.Size + CellSize - 1) / CellSize;
        Cells = new Cell[CellsPerSide * CellsPerSide];
        for (int i = 0; i < Cells.Length; i++)
        {
            var center = new GridPoint(i % CellsPerSide * CellSize + CellSize / 2, i / CellsPerSide * CellSize + CellSize / 2);
            Cells[i] = new Cell(center) { OwnPart = new Cell(center) };
        }
    }

    public int CellsPerSide { get; }

    public Cell[] Cells { get; }

    /// <summary>Totals on the player's own land.</summary>
    public Cell Own { get; private set; } = new(default);

    public void Update()
    {
        var own = new Cell(default);
        foreach (var cell in Cells)
        {
            cell.Clear();
            cell.OwnPart!.Clear();
        }
        // By index, and free ground looked up for the few occupied points afterwards rather than for every
        // point: a 640 map has 409600 of them, surveyed by every AI.
        byte self = (byte)_player;
        var swamp = _swamp ??= SwampGround();
        for (int i = 0, y = 0; y < _map.Size; y++)
        for (int x = 0; x < _map.Size; x++, i++)
        {
            var cell = Cells[y / CellSize * CellsPerSide + x / CellSize];
            byte owner = _map.OwnerAt(i);
            bool mine = owner == self;
            if (mine)
            {
                cell.Own++;
                own.Own++;
            }
            else if (owner != GameMap.NoOwner)
                cell.Foreign++;

            byte code = _map.ObjectCodeAt(i);
            var terrain = _map.TerrainAt(i);
            bool water = terrain is TerrainType.Water or TerrainType.River;
            var (type, amount) = _map.ResourceAt(i);
            int trees = MapObjectCodes.IsAdultTree(code) ? 1 : 0;
            int stone = MapObjectCodes.StoneLeft(code);
            int fish = water && type == ResourceType.Fish ? amount : 0;
            int ore = !water && type != ResourceType.Fish ? amount : 0;
            bool land = !water && _map.IsWalkable(i);
            // Free building land: grass without anything on it (buildings around: taken off below).
            bool buildable = land && IsBuildable(terrain, code);
            bool freeSwamp = swamp[(int)terrain] && code == MapObjectCodes.None;

            Add(cell);
            if (mine)
            {
                Add(cell.OwnPart!);
                Add(own);
            }

            void Add(Cell target)
            {
                target.Trees += trees;
                target.StoneUnits += stone;
                target.Fish += fish;
                if (water)
                    target.Water++;
                if (terrain == TerrainType.River)
                    target.River++;
                if (freeSwamp)
                    target.Swamp++;
                if (ore > 0)
                    target.Ore[(int)type] += ore;
                if (land)
                {
                    target.Walkable++;
                    target.Anchor ??= new GridPoint(x, y);
                    if (buildable)
                        target.Buildable++;
                }
            }
        }
        foreach (int i in _world.OccupiedPoints())
        {
            if (!_map.IsWalkable(i) || !IsBuildable(_map.TerrainAt(i), _map.ObjectCodeAt(i)))
                continue;
            var p = _map.Point(i);
            var cell = Cells[p.Y / CellSize * CellsPerSide + p.X / CellSize];
            cell.Buildable--;
            if (_map.OwnerAt(i) == self)
            {
                cell.OwnPart!.Buildable--;
                own.Buildable--;
            }
        }
        Own = own;
    }

    private bool[] SwampGround()
    {
        var swamp = new bool[256];
        if (_world.Production.Config.Crops.TryGetValue("rice", out var rice))
            foreach (var terrain in rice.Ground)
                swamp[(int)terrain] = true;
        return swamp;
    }

    private static bool IsBuildable(TerrainType terrain, byte code) => terrain == TerrainType.Grass && code == MapObjectCodes.None;

    public sealed class Cell(GridPoint center)
    {
        public GridPoint Center { get; } = center;

        /// <summary>The same counts for the player's own points of the cell only.</summary>
        public Cell? OwnPart { get; init; }

        /// <summary>A walkable point of the cell (to check whether it can be reached).</summary>
        public GridPoint? Anchor { get; set; }

        public int Own { get; set; }
        public int Foreign { get; set; }
        public int Walkable { get; set; }

        /// <summary>Free building land: grass with nothing on it and no building around.</summary>
        public int Buildable { get; set; }
        public int Trees { get; set; }
        public int StoneUnits { get; set; }
        /// <summary>Water of any kind (sea, lakes and rivers).</summary>
        public int Water { get; set; }

        /// <summary>River points: waterworks draw from these only.</summary>
        public int River { get; set; }

        /// <summary>Free swampy ground where rice grows.</summary>
        public int Swamp { get; set; }
        public int Fish { get; set; }
        public int[] Ore { get; } = new int[ResourceTypes];

        public bool IsOwn => Own > CellSize * CellSize / 2;

        public void Clear()
        {
            Anchor = null;
            Own = Foreign = Walkable = Buildable = Trees = StoneUnits = Water = River = Swamp = Fish = 0;
            Array.Clear(Ore);
        }
    }
}
