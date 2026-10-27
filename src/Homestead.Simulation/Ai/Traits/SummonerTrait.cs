using System.Text.Json;
using Homestead.Simulation.Buildings;
using Homestead.Simulation.Magic;
using Homestead.Simulation.Pathfinding;

namespace Homestead.Simulation.Ai.Traits;

/// <summary>
/// Summoner (<c>summoner</c> in docs/KI-TACTICS.md): mana beyond what the people's own spell needs for
/// <c>reserveSpells</c> casts goes into summoning goods. A free priest casts "Waren herbeirufen" on the
/// best spot within <c>priestRadius</c> of him: own land at least <c>edgeDistance</c> fields from where it
/// ends (thieves, enemies; the map border does not count), no enemy soldier outside within <c>soldierRadius</c>, at least
/// <c>minFreeFields</c> free fields (walkable, no pile, no building) within the spell radius; the most free
/// fields win, then the nearest to the priest. Only while no priest of the player is casting. Rechecked
/// every <c>checkSeconds</c>.
/// </summary>
internal sealed class SummonerTrait : AiTrait
{
    public const string TraitId = "summoner";

    private readonly int _reserveSpells;
    private readonly int _priestRadius;
    private readonly int _edgeDistance;
    private readonly int _soldierRadius;
    private readonly int _minFreeFields;
    private readonly double _checkSeconds;

    private long _nextCheck;

    public SummonerTrait(JsonElement settings)
    {
        _reserveSpells = (int)Setting(settings, "reserveSpells", 2);
        _priestRadius = (int)Setting(settings, "priestRadius", 12);
        _edgeDistance = (int)Setting(settings, "edgeDistance", 10);
        _soldierRadius = (int)Setting(settings, "soldierRadius", 20);
        _minFreeFields = (int)Setting(settings, "minFreeFields", 30);
        _checkSeconds = Setting(settings, "checkSeconds", 5);
    }

    public override string Id => TraitId;

    internal override void RunErrands(AiPlayer ai, List<Building> military)
    {
        var world = ai.World;
        if (world.Tick < _nextCheck)
            return;
        _nextCheck = world.Tick + (long)(_checkSeconds * World.TicksPerSecond);
        var magic = world.Magic;
        if (magic.SpellsOf(ai.Player).FirstOrDefault(s => s.Kind == SpellKind.SummonGoods) is not { } summon)
            return;
        int reserve = magic.SpellOf(ai.Player) is { Kind: not SpellKind.SummonGoods } own ? magic.CostOf(ai.Player, own) * _reserveSpells : 0;
        if (magic.Mana(ai.Player) - magic.CostOf(ai.Player, summon) < reserve)
            return;
        var priests = world.Settlers.Where(s => s.Player == ai.Player && s.Profession == Profession.Priest && !s.IsDead && !s.IsInside).ToList();
        // One spell at a time (a fight needs the priests more).
        if (priests.Count == 0 || priests.Any(magic.IsBusy))
            return;

        var enemySoldiers = world.Settlers
            .Where(s => s.Profession == Profession.Soldier && ai.IsEnemy(s.Player) && !s.IsInside && !s.IsDead)
            .Select(s => s.Position).ToList();
        var best = priests
            .SelectMany(p => Spots(ai, p.Position, enemySoldiers).Select(s => (Priest: p, s.Spot, s.Free)))
            .OrderByDescending(s => s.Free).ThenBy(s => PathFinder.Distance(s.Priest.Position, s.Spot))
            .ThenBy(s => s.Spot.Y).ThenBy(s => s.Spot.X)
            .FirstOrDefault();
        if (best.Priest is null)
            return;
        if (magic.Cast([best.Priest], best.Spot, summon) == CastResult.Started)
            ai.Note($"summoner: summons goods at {best.Spot.X},{best.Spot.Y} ({magic.Mana(ai.Player)} mana, {reserve} kept)");
    }

    /// <summary>Suitable spots within priestRadius of the point (every other field), with their free fields.</summary>
    private IEnumerable<(GridPoint Spot, int Free)> Spots(AiPlayer ai, GridPoint near, List<GridPoint> enemySoldiers)
    {
        var world = ai.World;
        var map = world.Map!;
        int radius = Magic.Magic.SummonSpread;
        foreach (var spot in PathFinder.Circle(near, _priestRadius))
        {
            if ((spot.X + spot.Y) % 2 != 0 || !map.Contains(spot) || map.OwnerAt(spot) != ai.Player)
                continue;
            if (enemySoldiers.Any(e => PathFinder.Distance(e, spot) <= _soldierRadius))
                continue;
            if (PathFinder.Circle(spot, _edgeDistance).Any(q => map.Contains(q) && map.OwnerAt(q) != ai.Player))
                continue;
            int free = PathFinder.Circle(spot, radius)
                .Count(q => map.Contains(q) && map.IsWalkable(q) && world.PileAt(q) is null && world.BuildingAt(q) is null);
            if (free >= _minFreeFields)
                yield return (spot, free);
        }
    }
}
