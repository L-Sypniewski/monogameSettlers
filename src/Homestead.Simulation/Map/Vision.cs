namespace Homestead.Simulation.Map;

/// <summary>
/// What one player sees of the map (fog of war): everything within sight of their settlers outside the buildings
/// (donkeys hardly) and of buildings with a sight (<see cref="Military.Military.SightOfBuilding"/>: the lookout tower, occupied
/// military buildings a little beyond their land, every other finished building a little) is visible – the own land itself sees nothing; what was visible once stays explored. Recomputed on demand (<see cref="Update"/>),
/// not every tick. The simulation itself does not use it: computer players know the whole map (scouts stay revealed
/// while in sight, <see cref="Military.Military.Sees"/>).
/// </summary>
public sealed class Vision
{
    /// <summary>Sight of settlers other than soldiers (soldiers see as far as they notice enemies).</summary>
    public const int SettlerSight = 8;

    private readonly int _size;
    private readonly bool[] _visible;
    private readonly bool[] _explored;

    public Vision(int size, int player)
    {
        _size = size;
        Player = player;
        _visible = new bool[size * size];
        _explored = new bool[size * size];
    }

    public int Player { get; }

    public int Size => _size;

    public bool IsVisible(int index) => _visible[index];

    public bool IsExplored(int index) => _explored[index];

    public bool IsVisible(GridPoint p) => (uint)p.X < (uint)_size && (uint)p.Y < (uint)_size && _visible[p.Y * _size + p.X];

    /// <summary>Recomputes what is visible now; all of it counts as explored from now on.</summary>
    public void Update(World world)
    {
        Array.Clear(_visible);
        foreach (var settler in world.Settlers)
        {
            if (settler.Player != Player || settler.IsDead || settler.IsInside)
                continue;
            See(settler.Position, world.Military.SightOf(settler));
        }
        foreach (var ship in world.Shipping.Ships)
            if (ship.Player == Player)
                See(ship.Position, world.Shipping.Rules.ShipSight);
        foreach (var building in world.Buildings)
            if (building.Player == Player && world.Military.SightOfBuilding(building) is { } sight)
                See(building.Position, sight);
        foreach (var sight in world.Magic.Sights)
            if (sight.Player == Player && sight.Until > world.Tick)
            {
                if (sight.Radius == 0)
                    Array.Fill(_visible, true);
                else
                    See(sight.Center, sight.Radius);
            }
        for (int i = 0; i < _visible.Length; i++)
            _explored[i] |= _visible[i];
    }

    /// <summary>Marks the circle of <see cref="Pathfinding.PathFinder.InCircle"/> as visible.</summary>
    private void See(GridPoint center, int radius)
    {
        // Row by row instead of PathFinder.Circle: large radii (lookout tower) would test four times the area.
        // The circle reaches 2r/√3 rows up and down.
        int reach = (int)Math.Ceiling(radius * 2 / Math.Sqrt(3));
        for (int dy = -reach; dy <= reach; dy++)
        {
            int y = center.Y + dy;
            if ((uint)y >= (uint)_size)
                continue;
            // InCircle: dx² - dx·dy + dy² <= r², solved for dx.
            double discriminant = 4.0 * radius * radius - 3.0 * dy * dy;
            if (discriminant < 0)
                continue;
            double root = Math.Sqrt(discriminant);
            int from = Math.Max(0, center.X + (int)Math.Ceiling((dy - root) / 2 - 1e-9));
            int to = Math.Min(_size - 1, center.X + (int)Math.Floor((dy + root) / 2 + 1e-9));
            for (int x = from; x <= to; x++)
                _visible[y * _size + x] = true;
        }
    }
}
