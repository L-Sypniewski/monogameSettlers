using Homestead.Simulation.Pathfinding;

namespace Homestead.Simulation.Map;

/// <summary>
/// What one player sees of the map (fog of war): their own land and everything within sight of their
/// settlers is visible; what was visible once stays explored. Recomputed on demand (<see cref="Update"/>),
/// not every tick. The simulation itself does not use it: computer players know the whole map.
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
        var map = world.Map!;
        for (int i = 0; i < _visible.Length; i++)
            _visible[i] = map.OwnerAt(i) == Player;
        foreach (var settler in world.Settlers)
        {
            if (settler.Player != Player || settler.IsDead || settler.IsInside)
                continue;
            int sight = settler.SoldierType is { } type ? world.Military.Config.Sight(type) : SettlerSight;
            foreach (var p in PathFinder.Circle(settler.Position, sight))
                if (map.Contains(p))
                    _visible[map.Index(p)] = true;
        }
        for (int i = 0; i < _visible.Length; i++)
            _explored[i] |= _visible[i];
    }
}
