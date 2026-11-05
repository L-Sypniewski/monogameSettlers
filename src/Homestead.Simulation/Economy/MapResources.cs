using Homestead.Simulation.Map;

namespace Homestead.Simulation.Economy;

/// <summary>
/// The map's own lifecycle: saplings become trees, crops grow stage by stage and stumps of felled trees
/// rot away; depleted resource fields regain their amount step by step. The per-kind depletion counter
/// (<see cref="_depletionDue"/>) and the per-point bookkeeping counters (<see cref="_stoneCuts"/>,
/// <see cref="_resourceTaken"/>) decide when a gathered unit uses up the ground; spells reach in through
/// <see cref="Hasten"/>, <see cref="GrowsSlowerThan"/> and <see cref="BurnTree"/>.
/// </summary>
public sealed partial class Production
{
    /// <summary>Saplings and crops (by point index) and when they change next.</summary>
    private readonly Dictionary<int, long> _growing = new();

    /// <summary>Stones already taken at the current size of a quarry stone (see StonesPerCut); advanced by the stone step in Production.FinishGathering.</summary>
    private readonly Dictionary<int, int> _stoneCuts = new();

    /// <summary>Units already taken at the current amount step of a resource field (see OrePerAmount).</summary>
    private readonly Dictionary<int, int> _resourceTaken = new();

    /// <summary>
    /// Per kind of resource (stone, mountain, fish): percent points collected towards the next unit that
    /// uses up the ground. Exactly the configured share counts, without chance.
    /// </summary>
    private readonly int[] _depletionDue = new int[3];

    /// <summary>Depleted resource fields: their start amount and when they regain the next step.</summary>
    private readonly Dictionary<int, (int Start, long Next)> _regenerating = new();

    /// <summary>Saplings become trees; crops grow stage by stage; stumps of felled trees disappear.</summary>
    private void Grow()
    {
        var map = _world.Map!;
        foreach (var (index, due) in _growing.ToList())
        {
            if (_world.Tick < due)
                continue;
            _growing.Remove(index);
            var p = map.Point(index);
            byte code = map.ObjectCodeAt(p);
            if (code == MapObjectCodes.TreeStump)
                map.SetObjectCode(p, MapObjectCodes.None);
            else if (code == MapObjectCodes.Sapling)
            {
                map.SetObjectCode(p, (byte)(MapObjectCodes.FirstTree + _random.Next(MapObjectCodes.LastTree - MapObjectCodes.FirstTree + 1)));
                TreesGrown++;
            }
            else if (_config.CropOf(code) is { } crop && code < crop.Ripe)
            {
                map.SetObjectCode(p, (byte)(code + 1));
                if (code + 1 < crop.Ripe)
                    _growing[index] = _world.Tick + StageTicks(crop, p);
            }
        }
    }

    /// <summary>A tree burns down (spell): a stump is left that rots away.</summary>
    internal void BurnTree(GridPoint p)
    {
        var map = _world.Map!;
        map.SetObjectCode(p, MapObjectCodes.TreeStump);
        _growing[map.Index(p)] = _world.Tick + _config.StumpTicks;
    }

    /// <summary>
    /// The growing crop on the point (grain, wine, rice) ripens within <paramref name="ticks"/> at the latest (spell): stages
    /// it would not reach in time are skipped. False if there is none that would take longer.
    /// </summary>
    internal bool Hasten(GridPoint p, int ticks)
    {
        var map = _world.Map!;
        byte code = map.ObjectCodeAt(p);
        int index = map.Index(p);
        if (_config.CropOf(code) is not { } crop || code >= crop.Ripe || !_growing.TryGetValue(index, out long due))
            return false;
        int stage = StageTicks(crop, p);
        long left = due - _world.Tick + (long)(crop.Ripe - code - 1) * stage;
        if (left <= ticks)
            return false;
        while (code + 1 < crop.Ripe && (long)(crop.Ripe - code - 1) * stage >= ticks)
            code++;
        map.SetObjectCode(p, code);
        _growing[index] = _world.Tick + Math.Max(1, ticks - (long)(crop.Ripe - code - 1) * stage);
        return true;
    }

    /// <summary>Whether a crop grows on the point that would take longer than <paramref name="ticks"/> to ripen.</summary>
    internal bool GrowsSlowerThan(GridPoint p, int ticks)
    {
        var map = _world.Map!;
        byte code = map.ObjectCodeAt(p);
        return _config.CropOf(code) is { } crop && code < crop.Ripe && _growing.TryGetValue(map.Index(p), out long due)
               && due - _world.Tick + (long)(crop.Ripe - code - 1) * StageTicks(crop, p) > ticks;
    }

    /// <summary>Time for one growing stage of a crop at a point (faster on slopes for crops with a slope bonus).</summary>
    private int StageTicks(CropRecipe crop, GridPoint p)
    {
        double ticks = (double)crop.GrowTicks / Math.Max(1, crop.Ripe - crop.First);
        if (crop.SlopeBonus > 0)
        {
            var map = _world.Map!;
            int slope = 0;
            for (int d = 0; d < DirectionExtensions.Count; d++)
            {
                var n = p.Neighbor((Direction)d);
                if (map.Contains(n))
                    slope = Math.Max(slope, Math.Abs(map.HeightAt(n) - map.HeightAt(p)));
            }
            ticks /= 1 + crop.SlopeBonus * Math.Min(1.0, (double)slope / Math.Max(1, crop.SlopeHeight));
        }
        if (crop.TreeBonus > 0)
            ticks /= 1 + crop.TreeBonus * TreeShare(crop, p);
        return Math.Max(1, (int)Math.Round(ticks));
    }

    /// <summary>Adult trees within the crop's tree radius of the point against its ideal number (0–1).</summary>
    private double TreeShare(CropRecipe crop, GridPoint p)
    {
        var map = _world.Map!;
        int trees = Area(p, crop.TreeRadius).Count(t => MapObjectCodes.IsAdultTree(map.ObjectCodeAt(t)));
        return Math.Min(1.0, (double)trees / Math.Max(1, crop.IdealTrees));
    }

    /// <summary>Depleted fields regain one amount step every <see cref="ProductionConfig.RegenerateTicks"/>.</summary>
    private void Regenerate()
    {
        var map = _world.Map!;
        foreach (var (index, (start, next)) in _regenerating.ToList())
        {
            if (_world.Tick < next)
                continue;
            var p = map.Point(index);
            var (type, amount) = map.ResourceAt(p);
            map.SetResource(p, type, amount + 1);
            if (amount + 1 >= start)
                _regenerating.Remove(index);
            else
                _regenerating[index] = (start, next + _config.RegenerateTicks);
        }
    }

    /// <summary>Whether the unit just taken uses up the ground: <paramref name="percent"/> of all units of the kind do.</summary>
    private bool UsesUp(int kind, int percent)
    {
        _depletionDue[kind] += Math.Clamp(percent, 0, 100);
        if (_depletionDue[kind] < 100)
            return false;
        _depletionDue[kind] -= 100;
        return true;
    }

    /// <summary>Takes one unit from a resource field; every <paramref name="perAmount"/> units lower its amount by one.</summary>
    private void TakeResource(GridPoint p, int perAmount)
    {
        var map = _world.Map!;
        int index = map.Index(p);
        int taken = _resourceTaken.GetValueOrDefault(index) + 1;
        if (taken < perAmount)
        {
            _resourceTaken[index] = taken;
            return;
        }
        _resourceTaken.Remove(index);
        var (type, amount) = map.ResourceAt(p);
        if (_config.RegenerateTicks > 0 && !_regenerating.ContainsKey(index))
            _regenerating[index] = (amount, _world.Tick + _config.RegenerateTicks);
        map.SetResource(p, type, amount - 1);
    }
}
