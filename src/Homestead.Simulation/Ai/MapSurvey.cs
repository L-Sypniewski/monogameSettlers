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

    private readonly GameMap _map;
    private readonly int _player;

    public MapSurvey(GameMap map, int player)
    {
        _map = map;
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
        for (int y = 0; y < _map.Size; y++)
        for (int x = 0; x < _map.Size; x++)
        {
            var p = new GridPoint(x, y);
            var cell = Cells[y / CellSize * CellsPerSide + x / CellSize];
            int? owner = _map.OwnerAt(p);
            bool mine = owner == _player;
            if (mine)
            {
                cell.Own++;
                own.Own++;
            }
            else if (owner is not null)
                cell.Foreign++;

            byte code = _map.ObjectCodeAt(p);
            var terrain = _map.TerrainAt(p);
            bool water = terrain is TerrainType.Water or TerrainType.River;
            var (type, amount) = _map.ResourceAt(p);
            int trees = MapObjectCodes.IsAdultTree(code) ? 1 : 0;
            int stone = MapObjectCodes.StoneLeft(code);
            int fish = water && type == ResourceType.Fish ? amount : 0;
            int ore = !water && type != ResourceType.Fish ? amount : 0;

            foreach (var target in mine ? [cell, cell.OwnPart!, own] : new[] { cell })
            {
                target.Trees += trees;
                target.StoneUnits += stone;
                target.Fish += fish;
                if (water)
                    target.Water++;
                if (ore > 0)
                    target.Ore[(int)type] += ore;
                if (_map.IsWalkable(p) && !water)
                {
                    target.Walkable++;
                    target.Anchor ??= p;
                }
            }
        }
        Own = own;
    }

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
        public int Trees { get; set; }
        public int StoneUnits { get; set; }
        public int Water { get; set; }
        public int Fish { get; set; }
        public int[] Ore { get; } = new int[ResourceTypes];

        public bool IsOwn => Own > CellSize * CellSize / 2;

        public void Clear()
        {
            Anchor = null;
            Own = Foreign = Walkable = Trees = StoneUnits = Water = Fish = 0;
            Array.Clear(Ore);
        }
    }
}
