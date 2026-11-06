using Homestead.Simulation.Buildings;
using Homestead.Simulation.Map;
using Homestead.Simulation.Pathfinding;

namespace Homestead.Simulation.Economy;

/// <summary>
/// Gatherers' outdoor cycle: find a target in the work radius (<see cref="FindTarget"/>), walk there,
/// work and bring the result home (<see cref="StartGathering"/>, <see cref="FinishGathering"/>). Also
/// the field and farmland bookkeeping: fields of crops with a keeper (beehives, <see cref="_fields"/>)
/// and farmland that goes with its farm (<see cref="_farmland"/>, <see cref="RemoveFarmland"/>).
/// </summary>
public sealed partial class Production
{
    /// <summary>Farmland (by point index) and the farm that sowed it last; it goes with that farm.</summary>
    private readonly Dictionary<int, Building> _farmland = new();

    /// <summary>Fields of crops with a field limit (beehives, by point index) and the building that keeps them.</summary>
    private readonly Dictionary<int, Building> _fields = new();

    private CropRecipe CropOf(Workplace w) => _config.Crops[w.Gatherer!.Crop];

    /// <summary>A field of the building's own, not ripe, of a crop that needs trees, with no adult tree left around.</summary>
    private bool IsBarren(Workplace w, GridPoint p)
    {
        var map = _world.Map!;
        var crop = CropOf(w);
        byte code = map.ObjectCodeAt(p);
        return crop.Contains(code) && code != crop.Ripe && _fields.TryGetValue(map.Index(p), out var keeper) && keeper == w.Building
               && TreeShare(crop, p) == 0;
    }

    /// <summary>A field is cleared (taken home, its building gone): free ground again.</summary>
    private void RemoveField(GridPoint p)
    {
        var map = _world.Map!;
        int index = map.Index(p);
        map.SetObjectCode(p, MapObjectCodes.None);
        _growing.Remove(index);
        _fields.Remove(index);
    }

    /// <summary>
    /// Fields the building keeps of a crop with a field limit. Fields in its work area nobody keeps (games saved before fields
    /// had a keeper) it takes over; fields that are gone (built over, spells) are forgotten.
    /// </summary>
    private int FieldsOf(Workplace w)
    {
        var map = _world.Map!;
        var crop = CropOf(w);
        foreach (var p in Area(w.Building.WorkCenter, w.Building.Definition.WorkRadius ?? DefaultRadius))
            if (crop.Contains(map.ObjectCodeAt(p)) && !_fields.ContainsKey(map.Index(p)))
                _fields[map.Index(p)] = w.Building;
        int count = 0;
        foreach (var (index, building) in _fields.ToList())
        {
            if (!crop.Contains(map.ObjectCodeAt(map.Point(index))))
                _fields.Remove(index);
            else if (building == w.Building)
                count++;
        }
        return count;
    }

    private void StartGathering(Workplace w, GathererRecipe recipe)
    {
        // Full output pile: wait instead of starting a new pile next to it.
        if (ProductOf(recipe) is { } product
            && w.Building.Piles.FirstOrDefault(p => p.Role == PileRole.Offer && p.Material == product) is { } output
            && output.Count >= output.Capacity)
        {
            w.Waiting = "output pile is full";
            w.TicksLeft = RetryTicks;
            return;
        }
        var target = FindTarget(w, recipe.Action);
        w.FindsNothing = target is null;
        if (target is not { } t)
        {
            w.Waiting = recipe.Action switch
            {
                GatherAction.CutTree => "no tree in range",
                GatherAction.CutStone => "no stone in range",
                GatherAction.PlantTree => "no free ground to plant on",
                GatherAction.Fish => "no fish in range",
                GatherAction.DrawWater => "no river in range",
                _ => "no field to sow or harvest",
            };
            w.TicksLeft = RetryTicks;
            return;
        }
        w.Waiting = null;
        w.Target = t.Target;
        w.Facing = t.Facing;
        _reservedTargets.Add(_world.Map!.Index(t.Target));
        _world.LeaveBuilding(w.Worker!);
        if (!WalkTo(w, t.Spot, Phase.ToSpot))
        {
            _unreachableUntil[_world.Map.Index(t.Target)] = _world.Tick + UnreachableForgetTicks;
            GiveUp(w, "work spot unreachable");
        }
    }

    /// <summary>What a gatherer brings home (null: nothing, e.g. a forester).</summary>
    private static Material? ProductOf(GathererRecipe recipe) => recipe.Action switch
    {
        GatherAction.CutTree => Material.Trunk,
        GatherAction.CutStone => Material.Stone,
        GatherAction.Fish => Material.Fish,
        GatherAction.DrawWater => Material.Water,
        GatherAction.PlantTree => null,
        _ => recipe.Output ?? Material.Crop,
    };

    private void FinishGathering(Workplace w)
    {
        var map = _world.Map!;
        _reservedTargets.Remove(map.Index(w.Target));
        byte code = map.ObjectCodeAt(w.Target);
        var worker = w.Worker!;
        switch (w.Gatherer!.Action)
        {
            case GatherAction.CutTree when MapObjectCodes.IsAdultTree(code):
                map.SetObjectCode(w.Target, MapObjectCodes.TreeStump);
                _growing[map.Index(w.Target)] = _world.Tick + _config.StumpTicks; // the stump rots away
                TreesCut++;
                _world.Cue(new SoundCue(SoundCueKind.TreeFalls, w.Target, w.Building.Player));
                if (w.Gatherer.PickupTicks > 0)
                {
                    // The tree has fallen: a moment before the trunk is picked up.
                    w.Phase = Phase.AfterFelling;
                    w.TicksLeft = WorkTicks(w, w.Gatherer.PickupTicks);
                    return;
                }
                worker.Carrying = Material.Trunk;
                DeliverOutput(w);
                return;
            case GatherAction.CutStone when MapObjectCodes.StoneLeft(code) > 0:
                // Each of the 13 sizes yields StonesPerCut stones before the rock shrinks.
                int index = map.Index(w.Target);
                int cuts = _stoneCuts.GetValueOrDefault(index) + (UsesUp(0, _config.Depletion.Stone) ? 1 : 0);
                if (cuts >= _config.StonesPerCut)
                {
                    map.SetObjectCode(w.Target, (byte)(code + 1));
                    _stoneCuts.Remove(index);
                }
                else
                {
                    _stoneCuts[index] = cuts;
                }
                StonesCut++;
                worker.Carrying = Material.Stone;
                DeliverOutput(w);
                return;
            case GatherAction.PlantTree when CanPlant(w.Target):
                map.SetObjectCode(w.Target, MapObjectCodes.Sapling);
                _growing[map.Index(w.Target)] = _world.Tick + _config.TreeGrowTicks;
                TreesPlanted++;
                break;
            case GatherAction.Fish when map.ResourceAmount(w.Target, ResourceType.Fish) > 0:
                if (UsesUp(2, _config.Depletion.Fish))
                    TakeResource(w.Target, _config.FishPerAmount);
                FishCaught++;
                worker.Carrying = Material.Fish;
                DeliverOutput(w);
                return;
            case GatherAction.DrawWater when IsRiver(w.Target):
                WaterDrawn++;
                worker.Carrying = Material.Water;
                DeliverOutput(w);
                return;
            case GatherAction.Farm when code == CropOf(w).Ripe:
                // Vines and beehives start over, grain and rice leave free ground.
                var crop = CropOf(w);
                map.SetObjectCode(w.Target, crop.Regrows ? crop.First : MapObjectCodes.None);
                if (crop.Regrows)
                    _growing[map.Index(w.Target)] = _world.Tick + StageTicks(crop, w.Target);
                CropHarvested++;
                worker.Carrying = w.Gatherer.Output ?? Material.Crop;
                DeliverOutput(w);
                return;
            case GatherAction.Farm when IsBarren(w, w.Target):
                // A beehive without trees around: taken home (another one goes up where there are trees, if any).
                RemoveField(w.Target);
                break;
            case GatherAction.Farm when CanSow(w.Target, CropOf(w), ignoreWorker: worker):
                map.SetObjectCode(w.Target, CropOf(w).First);
                if (CropOf(w).Farmland)
                    MakeFarmland(w.Building, w.Target, CropOf(w).FarmlandRadius);
                _growing[map.Index(w.Target)] = _world.Tick + StageTicks(CropOf(w), w.Target);
                if (CropOf(w).MaxFields > 0)
                    _fields[map.Index(w.Target)] = w.Building;
                FieldsSown++;
                break;
            default:
                Report("target gone before the work was done");
                break;
        }
        WalkTo(w, w.Building.Door, Phase.ToDoorToRest);
    }

    /// <summary>
    /// Usable target in the work radius nearest to its center (then to the door): an adult tree with a free neighbor to stand on,
    /// a quarry stone with a reachable spot two fields away (its ring is blocked), water with fish
    /// next to the shore, a river (waterworks), ripe grain, or a free spot to plant or sow on (randomized a little so
    /// forests and fields spread).
    /// </summary>
    private (GridPoint Target, GridPoint Spot, Direction Facing)? FindTarget(Workplace w, GatherAction action)
    {
        if (action == GatherAction.Farm)
            return FindTarget(w, Want.RipeGrain)
                   ?? (CropOf(w).TreeBonus > 0 ? FindTarget(w, Want.BarrenField) : null)
                   ?? (CropOf(w).MaxFields > 0 && FieldsOf(w) >= CropOf(w).MaxFields ? null : FindTarget(w, Want.FreeField));
        return FindTarget(w, action switch
        {
            GatherAction.CutTree => Want.Tree,
            GatherAction.CutStone => Want.Stone,
            GatherAction.PlantTree => Want.TreeSpot,
            GatherAction.Fish => Want.Fish,
            _ => Want.Water,
        });
    }

    private (GridPoint Target, GridPoint Spot, Direction Facing)? FindTarget(Workplace w, Want want)
    {
        var map = _world.Map!;
        var pathFinder = _world.PathFinder!;
        var definition = w.Building.Definition;
        var center = w.Building.WorkCenter;
        var door = w.Building.Door;
        int radius = definition.WorkRadius ?? DefaultRadius;
        var candidates = new List<(GridPoint Target, GridPoint Spot, Direction Facing, int Distance, int FromDoor)>();

        foreach (var p in Area(center, radius))
        {
            int index = map.Index(p);
            if (_reservedTargets.Contains(index) || _unreachableUntil.TryGetValue(index, out long until) && until > _world.Tick)
                continue;
            // Workers stay on their own land.
            if (_world.TerritoryRules && map.OwnerAt(p) != w.Building.Player)
                continue;

            bool fits = want switch
            {
                Want.Tree => MapObjectCodes.IsAdultTree(map.ObjectCodeAt(p)),
                Want.Stone => MapObjectCodes.StoneLeft(map.ObjectCodeAt(p)) > 0,
                Want.TreeSpot => CanPlant(p),
                Want.Fish => map.ResourceAmount(p, ResourceType.Fish) > 0 && IsWater(p),
                Want.Water => IsRiver(p),
                Want.RipeGrain => map.ObjectCodeAt(p) == CropOf(w).Ripe,
                Want.FreeField => CanSow(p, CropOf(w), ignoreWorker: null) && (CropOf(w).TreeBonus == 0 || TreeShare(CropOf(w), p) > 0),
                Want.BarrenField => IsBarren(w, p),
                _ => false,
            };
            if (!fits)
                continue;

            int reach = want switch
            {
                Want.Stone => 2,
                Want.RipeGrain or Want.FreeField or Want.BarrenField => 0, // the farmer stands in the field
                _ => 1,
            };
            var spot = SpotNear(p, reach, door);
            if (spot is not { } s || !_world.Reachable(w.Building.Player, door, s)
                || _world.TerritoryRules && map.OwnerAt(s) != w.Building.Player)
                continue;
            candidates.Add((p, s, s == p ? Direction.SouthWest : FacingTowards(s, p), PathFinder.Distance(center, p), PathFinder.Distance(door, s)));
        }

        if (candidates.Count == 0)
            return null;
        if (want == Want.FreeField && CropOf(w) is { TreeBonus: > 0 } crop)
        {
            // Beehives go where the most trees are (up to the ideal number), nearer the center first; a little at random among those.
            var nearTrees = candidates.Select(c => (c, Trees: TreeShare(crop, c.Target)))
                .OrderByDescending(c => c.Trees).ThenBy(c => c.c.Distance).ThenBy(c => c.c.FromDoor)
                .Take(Math.Max(1, candidates.Count / 8)).ToList();
            var (pick, _) = nearTrees[_random.Next(nearTrees.Count)];
            return (pick.Target, pick.Spot, pick.Facing);
        }
        if (want is Want.TreeSpot or Want.FreeField)
        {
            // Pick among the half nearer the center at random, so foresters and farmers do not plant a single line.
            var near = candidates.OrderBy(c => c.Distance).ThenBy(c => c.FromDoor).Take(Math.Max(1, candidates.Count / 2)).ToList();
            var pick = near[_random.Next(near.Count)];
            return (pick.Target, pick.Spot, pick.Facing);
        }
        var best = candidates.MinBy(c => (c.Distance, c.FromDoor));
        return (best.Target, best.Spot, best.Facing);
    }

    /// <summary>
    /// The farm sowed on <paramref name="sown"/>: it becomes farmland, and so does the free grass up to
    /// <paramref name="radius"/> steps around in the farm's work area and on its land where farmland fits.
    /// </summary>
    private void MakeFarmland(Building farm, GridPoint sown, int radius)
    {
        var map = _world.Map!;
        int workRadius = farm.Definition.WorkRadius ?? DefaultRadius;
        Make(sown);
        foreach (var p in PathFinder.Circle(sown, radius).Where(p => p != sown && PathFinder.Distance(sown, p) <= radius)
                     .OrderBy(p => PathFinder.Distance(sown, p)))
            if (map.Contains(p) && PathFinder.InCircle(farm.WorkCenter, p, workRadius) && map.CoverAt(p) == GroundCover.None && map.IsWalkable(p) && _world.IsFreeGround(p)
                && (map.ObjectCodeAt(p) == MapObjectCodes.None || _growing.ContainsKey(map.Index(p)))
                && map.OwnerAt(p) == map.OwnerAt(sown) && map.CoverFits(p, GroundCover.Farmland))
                Make(p);

        void Make(GridPoint p)
        {
            _world.Trails.CutAround(p);
            map.SetCover(p, GroundCover.Farmland);
            _farmland[map.Index(p)] = farm;
        }
    }

    /// <summary>The farm is gone (torn down, lost): its farmland turns back into grass, the fields it kept (beehives) are gone.</summary>
    internal void RemoveFarmland(Building farm)
    {
        var map = _world.Map!;
        foreach (int index in _fields.Where(f => f.Value == farm).Select(f => f.Key).ToList())
            RemoveField(map.Point(index)); // beehives go with their beekeeper
        foreach (int index in _farmland.Where(f => f.Value == farm).Select(f => f.Key).ToList())
        {
            _farmland.Remove(index);
            if (map.CoverAt(map.Point(index)) == GroundCover.Farmland)
                map.SetCover(map.Point(index), GroundCover.None);
        }
    }

    private bool IsWater(GridPoint p) => _world.Map!.TerrainAt(p) is TerrainType.Water or TerrainType.River;

    /// <summary>Waterworks draw fresh water from rivers only, not from the sea.</summary>
    private bool IsRiver(GridPoint p) => _world.Map!.TerrainAt(p) == TerrainType.River;

    /// <summary>Free ground of the crop's kind for a new field (fields may touch each other; farmland keeps its distance, <see cref="GameMap.CoverFits"/>).</summary>
    private bool CanSow(GridPoint p, CropRecipe crop, Settler? ignoreWorker)
    {
        var map = _world.Map!;
        return map.Contains(p) && map.IsWalkable(p) && map.ObjectCodeAt(p) == MapObjectCodes.None
               && crop.Ground.Contains(map.TerrainAt(p)) && (!crop.Farmland || map.CoverFits(p, GroundCover.Farmland)) && _world.IsFreeGround(p)
               && (!_world.IsOccupied(p) || ignoreWorker is not null && ignoreWorker.Position == p)
               && (crop.FieldSpacing == 0 || !Area(p, crop.FieldSpacing - 1).Any(n => crop.Contains(map.ObjectCodeAt(n))));
    }

    /// <summary>A walkable point exactly <paramref name="distance"/> steps from the target, nearest to <paramref name="from"/>.</summary>
    private GridPoint? SpotNear(GridPoint target, int distance, GridPoint from)
    {
        var map = _world.Map!;
        GridPoint? best = null;
        int bestDistance = int.MaxValue;
        for (int dy = -distance; dy <= distance; dy++)
        for (int dx = -distance; dx <= distance; dx++)
        {
            var p = new GridPoint(target.X + dx, target.Y + dy);
            if (PathFinder.Distance(target, p) != distance || !map.IsWalkable(p) || !_world.IsFreeGround(p))
                continue;
            int d = PathFinder.Distance(from, p);
            if (d < bestDistance)
            {
                best = p;
                bestDistance = d;
            }
        }
        return best;
    }

    /// <summary>Free grass, not near buildings or piles, and no tree right next to it (keeps paths open).</summary>
    private bool CanPlant(GridPoint p)
    {
        var map = _world.Map!;
        if (!map.Contains(p) || !map.IsWalkable(p) || map.ObjectCodeAt(p) != MapObjectCodes.None
            || map.TerrainAt(p) != TerrainType.Grass || !_world.IsFreeGround(p) || _world.IsOccupied(p))
            return false;
        for (int d = 0; d < DirectionExtensions.Count; d++)
        {
            var n = p.Neighbor((Direction)d);
            if (map.Contains(n) && map.ObjectAt(n) == MapObjectKind.Tree)
                return false;
        }
        return true;
    }

    private static Direction FacingTowards(GridPoint from, GridPoint to)
    {
        var best = Direction.SouthWest;
        int bestDistance = int.MaxValue;
        for (int d = 0; d < DirectionExtensions.Count; d++)
        {
            int distance = PathFinder.Distance(from.Neighbor((Direction)d), to);
            if (distance < bestDistance)
            {
                best = (Direction)d;
                bestDistance = distance;
            }
        }
        return best;
    }

    private Activity ActivityAt(Workplace w) => w.Gatherer!.Action switch
    {
        GatherAction.CutTree => Activity.Chopping,
        GatherAction.CutStone => Activity.Mining,
        GatherAction.Fish => Activity.Fishing,
        GatherAction.DrawWater => Activity.DrawingWater,
        GatherAction.Farm => CropOf(w).Contains(_world.Map!.ObjectCodeAt(w.Target)) ? Activity.Harvesting : Activity.Sowing,
        _ => Activity.Planting,
    };

    /// <summary>What a gatherer looks for.</summary>
    private enum Want
    {
        Tree,
        Stone,
        TreeSpot,
        Fish,
        Water,
        RipeGrain,
        FreeField,

        /// <summary>A field of the building's own that needs trees and has none left around (beehives): it is taken home.</summary>
        BarrenField,
    }
}
