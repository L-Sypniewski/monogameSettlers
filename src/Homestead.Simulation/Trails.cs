using Homestead.Simulation.Economy;
using Homestead.Simulation.Map;

namespace Homestead.Simulation;

/// <summary>
/// Walking speed by ground and the trails carriers tread (see <c>walking</c> in production.json). A carrier
/// on a job that steps onto plain grass leaves a trace; after <see cref="WalkingRules.TrailSteps"/> such steps,
/// each less than <see cref="WalkingRules.TrailFadeTicks"/> after the one before, the point becomes a trail
/// (<see cref="GroundCover.Trail"/>), never on or next to farmland, rivers or other ground than grass
/// (<see cref="GameMap.CoverFits"/>); farms sow over trails and cut them back around their fields.
/// Everybody walks faster on trails and slower on mountains, and the path search knows it. A trail
/// nobody stepped on for <see cref="WalkingRules.TrailFadeTicks"/> fades back to grass.
/// </summary>
public sealed class Trails
{
    private const int SweepTicks = 5 * World.TicksPerSecond;

    private readonly World _world;
    private readonly WalkingRules _rules;
    private readonly int _trailSpeed, _mountainSpeed;

    /// <summary>Trodden points (by index): steps counted towards a trail, and the tick of the last one.</summary>
    private readonly Dictionary<int, (int Steps, long Last)> _trodden = new();

    internal Trails(World world, WalkingRules rules)
    {
        _world = world;
        _rules = rules;
        _trailSpeed = (int)Math.Round(World.BaseStepSpeed * rules.TrailSpeed);
        _mountainSpeed = (int)Math.Round(World.BaseStepSpeed * rules.MountainSpeed);
        // The path search weighs a step by its time: 10 on plain ground.
        world.PathFinder?.SetStepCosts(StepCost(World.BaseStepSpeed), StepCost(_trailSpeed), StepCost(_mountainSpeed));
    }

    private static int StepCost(int speed) => Math.Max(1, (int)Math.Round(10.0 * World.BaseStepSpeed / Math.Max(1, speed)));

    /// <summary>Points that are trails now.</summary>
    public int Count => _world.Map is { } map ? _trodden.Keys.Count(i => map.CoverAt(map.Point(i)) == GroundCover.Trail) : 0;

    /// <summary>Step progress per tick onto <paramref name="target"/> (see <see cref="Settler.StepSpeed"/>).</summary>
    internal int StepSpeed(GridPoint target)
    {
        var map = _world.Map!;
        if (map.CoverAt(target) == GroundCover.Trail)
            return _trailSpeed;
        return map.TerrainAt(target) is TerrainType.Mountain or TerrainType.MountainBorder ? _mountainSpeed : World.BaseStepSpeed;
    }

    /// <summary>The settler just arrived at its position.</summary>
    internal void Stepped(Settler settler)
    {
        if (_rules.TrailSteps <= 0)
            return;
        var map = _world.Map!;
        var p = settler.Position;
        int index = map.Index(p);
        var cover = map.CoverAt(p);
        if (cover == GroundCover.Trail)
        {
            _trodden[index] = (_rules.TrailSteps, _world.Tick);
            return;
        }
        if (!settler.IsCarrier || !settler.HasTask || cover != GroundCover.None || !map.CoverFits(p, GroundCover.Trail))
            return;
        var (steps, last) = _trodden.GetValueOrDefault(index);
        if (_world.Tick - last >= _rules.TrailFadeTicks)
            steps = 0;
        _trodden[index] = (++steps, _world.Tick);
        if (steps >= _rules.TrailSteps)
            map.SetCover(p, GroundCover.Trail);
    }

    /// <summary>Farmland was sown on <paramref name="p"/>: trails on it and next to it go.</summary>
    internal void CutAround(GridPoint p)
    {
        var map = _world.Map!;
        Cut(p);
        for (int d = 0; d < DirectionExtensions.Count; d++)
            Cut(p.Neighbor((Direction)d));

        void Cut(GridPoint q)
        {
            if (!map.Contains(q) || map.CoverAt(q) != GroundCover.Trail)
                return;
            _trodden.Remove(map.Index(q));
            map.SetCover(q, GroundCover.None);
        }
    }

    internal void Tick()
    {
        if (_rules.TrailSteps <= 0 || _world.Tick % SweepTicks != 0)
            return;
        var map = _world.Map!;
        foreach (int index in _trodden.Where(t => _world.Tick - t.Value.Last >= _rules.TrailFadeTicks).Select(t => t.Key).ToList())
        {
            _trodden.Remove(index);
            var p = map.Point(index);
            if (map.CoverAt(p) == GroundCover.Trail)
                map.SetCover(p, GroundCover.None);
        }
    }
}
