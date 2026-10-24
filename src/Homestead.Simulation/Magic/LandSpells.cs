using Homestead.Simulation.Map;
using Homestead.Simulation.Pathfinding;

namespace Homestead.Simulation.Magic;

/// <summary>
/// Spells on the land: they change the ground (desert, swamp, snow), what grows or lies on it (trees, stones, crops) and
/// what lies under it (ores, fish).
/// </summary>
public sealed partial class Magic
{
    /// <summary>How long the fire takes to pass from a burning tree to the trees next to it.</summary>
    public const int FireSpreadTicks = 2 * World.TicksPerSecond;

    /// <summary>Trees that catch fire from a burning neighbor, and when (forest fire).</summary>
    private readonly List<(GridPoint At, long Tick)> _fires = new();

    /// <summary>The ground classes the land spells turn into each other; their borders follow (<see cref="Paint"/>).</summary>
    private enum Ground { Other, Grass, Desert, Moor, Mud, Snow, Mountain }

    private static Ground ClassOf(TerrainType terrain) => terrain switch
    {
        TerrainType.Grass or TerrainType.MountainBorderOuter or TerrainType.DesertBorderOuter or TerrainType.MoorBorderOuter
            or TerrainType.MudBorderOuter => Ground.Grass,
        TerrainType.Desert or TerrainType.DesertBorder => Ground.Desert,
        TerrainType.Moor or TerrainType.MoorBorder => Ground.Moor,
        TerrainType.Mud or TerrainType.MudBorder => Ground.Mud,
        TerrainType.Snow or TerrainType.SnowBorder => Ground.Snow,
        TerrainType.Mountain or TerrainType.MountainBorder or TerrainType.SnowBorderOuter => Ground.Mountain,
        _ => Ground.Other,
    };

    /// <summary>What a ground class turns into under the spell (null: it stays).</summary>
    private static Ground? Turned(SpellKind kind, Ground ground) => (kind, ground) switch
    {
        (SpellKind.Grassland, Ground.Desert) => Ground.Grass,
        (SpellKind.Desertify, Ground.Grass) => Ground.Desert,
        (SpellKind.DrainSwamp, Ground.Moor or Ground.Mud) => Ground.Grass,
        (SpellKind.MeltSnow, Ground.Snow) => Ground.Mountain,
        _ => null,
    };

    private IEnumerable<GridPoint> Area(GridPoint target, int radius)
    {
        var map = _world.Map!;
        return PathFinder.Circle(target, radius).Where(p => map.Contains(p) && PathFinder.InCircle(target, p, radius));
    }

    /// <summary>Whether the land spell finds something to work on in its area.</summary>
    private bool HasLandTargets(Spell spell, GridPoint target)
    {
        var map = _world.Map!;
        return spell.Kind switch
        {
            SpellKind.Grassland or SpellKind.Desertify or SpellKind.DrainSwamp or SpellKind.MeltSnow =>
                Area(target, spell.Radius).Any(p => Turned(spell.Kind, ClassOf(map.TerrainAt(p))) is not null && Paintable(p)),
            SpellKind.ShrinkResources => Area(target, spell.Radius).Any(p => map.ResourceAt(p).Amount > 0),
            SpellKind.FishFlood => Area(target, spell.Radius).Any(p => map.TerrainAt(p) == TerrainType.Water),
            SpellKind.ForestFire => Area(target, spell.Radius).Any(p => MapObjectCodes.IsAdultTree(map.ObjectCodeAt(p))),
            SpellKind.Growth => Area(target, spell.Radius).Any(p => _world.Production.GrowsSlowerThan(p, spell.Ticks)),
            SpellKind.GrowForest => Area(target, spell.Radius).Any(p => Plantable(p, trees: true)),
            SpellKind.StoneCurse => Area(target, spell.Radius).Any(p => Plantable(p, trees: false)),
            _ => true,
        };
    }

    /// <summary>The land spell takes effect; returns the points to mark (flames of a forest fire).</summary>
    private IReadOnlyList<GridPoint> ApplyToLand(Spell spell, GridPoint target)
    {
        var map = _world.Map!;
        var area = Area(target, spell.Radius).OrderBy(p => PathFinder.Distance(target, p)).ThenBy(p => p.Y).ThenBy(p => p.X).ToList();
        switch (spell.Kind)
        {
            case SpellKind.Grassland or SpellKind.Desertify or SpellKind.DrainSwamp or SpellKind.MeltSnow:
                Paint(area.Where(p => Turned(spell.Kind, ClassOf(map.TerrainAt(p))) is not null && Paintable(p))
                    .ToDictionary(p => p, p => Turned(spell.Kind, ClassOf(map.TerrainAt(p)))!.Value));
                break;
            case SpellKind.ShrinkResources:
                foreach (var p in area)
                    if (map.ResourceAt(p) is { Amount: > 0 } resource)
                        map.SetResource(p, resource.Type, (int)(resource.Amount * spell.Factor));
                break;
            case SpellKind.FishFlood:
                foreach (var p in area.Where(p => map.TerrainAt(p) == TerrainType.Water))
                {
                    var (type, amount) = map.ResourceAt(p);
                    int before = type == ResourceType.Fish ? amount : 0;
                    map.SetResource(p, ResourceType.Fish, Math.Min(MaxFish, before + _random.Next(spell.Piles.Min, spell.Piles.Max + 1)));
                }
                break;
            case SpellKind.ForestFire:
                var burning = area.Where(p => MapObjectCodes.IsAdultTree(map.ObjectCodeAt(p))).ToList();
                foreach (var p in burning)
                    _world.Production.BurnTree(p);
                // The fire passes on to the trees right next to the area's, once.
                foreach (var p in burning.SelectMany(Neighbors).Distinct().Where(p => !PathFinder.InCircle(target, p, spell.Radius)))
                    if (MapObjectCodes.IsAdultTree(map.ObjectCodeAt(p)))
                        _fires.Add((p, _world.Tick + FireSpreadTicks));
                return burning;
            case SpellKind.Growth:
                foreach (var p in area)
                    _world.Production.Hasten(p, spell.Ticks);
                break;
            case SpellKind.GrowForest:
                foreach (var p in area.OrderBy(_ => _random.Next()))
                    if (Plantable(p, trees: true))
                        map.SetObjectCode(p, (byte)(MapObjectCodes.FirstTree + _random.Next(MapObjectCodes.LastTree - MapObjectCodes.FirstTree + 1)));
                break;
            case SpellKind.StoneCurse:
                // A few stones of all sizes, spread over the area (piles: how many; amount: stone left in each).
                int stones = _random.Next(spell.Piles.Min, spell.Piles.Max + 1);
                foreach (var p in area.OrderBy(_ => _random.Next()))
                {
                    if (stones == 0)
                        break;
                    if (!Plantable(p, trees: false))
                        continue;
                    map.SetObjectCode(p, (byte)(MapObjectCodes.StoneRubble - _random.Next(spell.Amount.Min, spell.Amount.Max + 1)));
                    stones--;
                }
                break;
        }
        return [];
    }

    /// <summary>Most fish a point of water holds (the original's maps: 15).</summary>
    public const int MaxFish = 15;

    private IEnumerable<GridPoint> Neighbors(GridPoint p)
    {
        var map = _world.Map!;
        for (int d = 0; d < DirectionExtensions.Count; d++)
            if (p.Neighbor((Direction)d) is var n && map.Contains(n))
                yield return n;
    }

    /// <summary>The fire reaches the next trees.</summary>
    private void SpreadFire()
    {
        var map = _world.Map!;
        var burnt = new List<GridPoint>();
        foreach (var fire in _fires.Where(f => f.Tick <= _world.Tick).ToList())
        {
            _fires.Remove(fire);
            if (!MapObjectCodes.IsAdultTree(map.ObjectCodeAt(fire.At)))
                continue;
            _world.Production.BurnTree(fire.At);
            burnt.Add(fire.At);
        }
        if (burnt.Count > 0)
            _effects.Add(new SpellEffect(SpellKind.ForestFire, burnt[0], 0, _world.Tick) { Points = burnt });
    }

    /// <summary>Ground a spell may change: not under a building, its surroundings or a pile.</summary>
    private bool Paintable(GridPoint p) => _world.IsFreeGround(p);

    /// <summary>
    /// A free point for a tree (on grass) or a stone (on grass or desert): nothing on it, nobody standing there, walkable,
    /// trees not next to another tree or stone, stones (which block their neighbors) with free neighbors and nothing blocking
    /// within two fields.
    /// </summary>
    private bool Plantable(GridPoint p, bool trees)
    {
        var map = _world.Map!;
        if (!map.IsWalkable(p) || map.ObjectCodeAt(p) != MapObjectCodes.None || !_world.IsFreeGround(p) || _world.IsOccupied(p)
            || map.CoverAt(p) != GroundCover.None)
            return false;
        var ground = ClassOf(map.TerrainAt(p));
        if (ground != Ground.Grass && !(ground == Ground.Desert && !trees))
            return false;
        int reach = trees ? 1 : 2;
        foreach (var q in PathFinder.Circle(p, reach))
        {
            if (q == p || !map.Contains(q))
                continue;
            if (MapObjectKinds.FromOriginal(map.ObjectCodeAt(q)) is MapObjectKind.Tree or MapObjectKind.ResourceStone)
                return false;
            if (!trees && PathFinder.Distance(p, q) == 1 && (!map.IsWalkable(q) || !_world.IsFreeGround(q) || _world.IsOccupied(q)))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Turns the points into the ground classes given and lays the borders around them as the original's maps have them
    /// (one field on each side: the class's own border inside, the outer border on the grass), so every triangle finds its
    /// transition tile. Farmland and trails that no longer fit next to the new ground are gone.
    /// </summary>
    private void Paint(IReadOnlyDictionary<GridPoint, Ground> changes)
    {
        if (changes.Count == 0)
            return;
        var map = _world.Map!;
        var ground = new Dictionary<GridPoint, Ground>(changes);
        Ground GroundAt(GridPoint p) => ground.TryGetValue(p, out var g) ? g : ClassOf(map.TerrainAt(p));
        var touched = changes.Keys.SelectMany(p => Neighbors(p).Append(p)).Distinct().ToList();
        foreach (var p in touched)
        {
            var g = GroundAt(p);
            var around = Neighbors(p).Select(GroundAt).ToList();
            TerrainType? terrain = g switch
            {
                Ground.Desert => around.Any(n => n != Ground.Desert) ? TerrainType.DesertBorder : TerrainType.Desert,
                Ground.Moor => around.Any(n => n != Ground.Moor) ? TerrainType.MoorBorder : TerrainType.Moor,
                Ground.Mud => around.Any(n => n != Ground.Mud) ? TerrainType.MudBorder : TerrainType.Mud,
                Ground.Snow => around.Any(n => n != Ground.Snow) ? TerrainType.SnowBorder : TerrainType.Snow,
                Ground.Mountain => around.Any(n => n is not (Ground.Mountain or Ground.Snow)) ? TerrainType.MountainBorder
                    : around.Contains(Ground.Snow) ? TerrainType.SnowBorderOuter : TerrainType.Mountain,
                Ground.Grass => around.Any(n => n is Ground.Mountain or Ground.Snow) ? TerrainType.MountainBorderOuter
                    : around.Contains(Ground.Desert) ? TerrainType.DesertBorderOuter
                    : around.Contains(Ground.Mud) ? TerrainType.MudBorderOuter
                    : around.Contains(Ground.Moor) ? TerrainType.MoorBorderOuter
                    : TerrainType.Grass,
                _ => null,
            };
            if (terrain is { } t && map.TerrainAt(p) != t)
                map.SetTerrain(p, t);
        }
        foreach (var p in touched.SelectMany(p => Neighbors(p).Append(p)).Distinct())
            if (map.CoverAt(p) is var cover && cover != GroundCover.None && !map.CoverFits(p, cover))
                map.SetCover(p, GroundCover.None);
    }
}
