using Homestead.Simulation.Buildings;
using Homestead.Simulation.Economy;
using Homestead.Simulation.Pathfinding;

namespace Homestead.Simulation.Ai;

/// <summary>
/// The AI in the fight: in small fights badly wounded soldiers are pulled out and brought back later
/// (<c>military.wounded</c> in ai.json); with a big army (late game) the weaponsmiths make mostly spears and bows
/// (<c>army</c>). How a strike is led (bowmen ahead, melee behind) is up to the character's traits
/// (decision point "leading fights", <see cref="AiTrait.LeadStrike"/>).
/// </summary>
public sealed partial class AiPlayer
{
    /// <summary>A soldier brought back into the fight stays in it at least this long (no going back and forth).</summary>
    private const long BackInFightTicks = 20 * World.TicksPerSecond;

    /// <summary>Soldiers brought back into the fight, with the tick they may withdraw again.</summary>
    private readonly Dictionary<Settler, long> _backInFight = new();

    /// <summary>An enemy soldier this near a withdrawn one that has arrived is after it.</summary>
    private const int Chased = 4;

    /// <summary>The weaponsmiths' shares before the late game (null: not late).</summary>
    private Dictionary<Material, double>? _earlyShares;

    /// <summary>
    /// Pulls badly wounded soldiers out of a small fight and brings them back: a fighting soldier below
    /// <c>withdrawHealth</c> walks (without fighting) to a spot <c>restDistance</c> away from
    /// <paramref name="enemy"/>, out of the enemies' sight, and holds its position (an enemy following it there sends it on
    /// to the own military building) – or, with an own hospital within call range, is freed and walks to its door to be
    /// healed (holding soldiers are not called), while the others still fighting hold without it
    /// (<c>holdRatio</c> times <paramref name="opposition"/>). It comes back (<paramref name="rejoin"/>) once
    /// healed (a hospital nearby calls it in while it waits), when those fighting fall below <c>needRatio</c>
    /// times the opposition (every blade counts), or to finish off an enemy that is as good as beaten.
    /// </summary>
    /// <param name="soldiers">All soldiers of the strike or campaign, the withdrawn among them.</param>
    /// <param name="resting">The withdrawn ones (kept up to date here).</param>
    private void TendWounded(List<Settler> soldiers, HashSet<Settler> resting, GridPoint enemy, double opposition, Action<List<Settler>> rejoin)
    {
        var rules = _config.Wounded ?? AiConfig.WoundedRules.Default;
        foreach (var (soldier, until) in _backInFight.ToList())
            if (_world.Tick >= until || soldier.IsDead)
                _backInFight.Remove(soldier);
        double fighting = soldiers.Where(s => !resting.Contains(s)).Sum(s => Strength(s, enemy));
        bool beaten = opposition <= fighting * rules.MopUpRatio;

        // Back in: healed, needed, or to finish the enemy off.
        bool needed = fighting < opposition * rules.NeedRatio;
        var back = resting.Where(s => needed || beaten || HealthShare(s) >= rules.RejoinHealth).ToList();
        if (back.Count > 0)
        {
            resting.ExceptWith(back);
            foreach (var soldier in back)
            {
                _backInFight[soldier] = _world.Tick + BackInFightTicks;
                fighting += Strength(soldier, enemy);
            }
            rejoin(back);
            Note($"brings {back.Count} withdrawn soldiers back into the fight at {enemy.X},{enemy.Y}"
                 + (needed ? " (needed)" : beaten ? " (to finish it)" : " (healed)"));
        }

        // Waiting withdrawn: freed for a hospital within its call range (holding soldiers are not called), otherwise on
        // to the own military building when an enemy followed it (its bowmen, the full combat strength).
        var toHospital = new List<Settler>();
        foreach (var soldier in resting.Where(_world.Military.Holds))
            if (HospitalDoorFor(soldier.Position, enemy) is { } hospital)
            {
                _world.Military.Move([soldier], hospital, forced: true);
                toHospital.Add(soldier);
            }
        if (toHospital.Count > 0)
            Note($"sends {toHospital.Count} wounded soldiers to the hospital");
        foreach (var soldier in resting)
            if (_world.Military.Holds(soldier) && _world.Military.HoldTarget(soldier) is { } spot
                && PathFinder.Distance(spot, RetreatDoor(soldier.Position, enemy)) > 3
                && _world.Settlers.Any(e => e.Player != Player && e.Profession == Profession.Soldier && !e.IsInside && !e.IsDead
                                            && PathFinder.Distance(e.Position, soldier.Position) <= Chased))
                _world.Military.Move([soldier], RetreatDoor(soldier.Position, enemy), forced: true, hold: true);

        // Out: the worst wounded of those fighting first, while the others hold without them; never the last one.
        if (rules.WithdrawHealth <= 0 || soldiers.Count > rules.MaxSoldiers || beaten)
            return;
        var withdrawn = new List<Settler>();
        foreach (var soldier in soldiers.Where(s => !resting.Contains(s) && !_backInFight.ContainsKey(s)
                                                    && s.Activity is Activity.Fighting or Activity.Shooting
                                                    && HealthShare(s) < rules.WithdrawHealth).OrderBy(HealthShare))
        {
            double without = fighting - Strength(soldier, enemy);
            if (without < opposition * rules.HoldRatio || soldiers.Count - resting.Count <= 1)
                break;
            resting.Add(soldier);
            withdrawn.Add(soldier);
            fighting = without;
            if (HospitalDoorFor(soldier.Position, enemy) is { } hospital)
                _world.Military.Move([soldier], hospital, forced: true); // idle at the door: the healer takes it
            else
                _world.Military.Move([soldier], RestSpot(soldier.Position, enemy, rules.RestDistance), forced: true, hold: true);
        }
        if (withdrawn.Count > 0)
            Note($"pulls {withdrawn.Count} wounded soldiers out of the fight at {enemy.X},{enemy.Y}");
    }

    /// <summary>
    /// Door of the nearest own finished hospital whose healer could call a soldier at <paramref name="from"/> (within
    /// <c>hospital.callRadius</c>), reachable and not nearer to the enemies at <paramref name="enemy"/> than it; null if none.
    /// </summary>
    private GridPoint? HospitalDoorFor(GridPoint from, GridPoint enemy)
    {
        int radius = _world.Military.Config.HospitalCallRadius, danger = PathFinder.Distance(from, enemy);
        return _world.Buildings.Where(b => b.Exists && b.Player == Player && b.Definition.Name == HospitalName && b.State == BuildingState.Finished
                                           && PathFinder.Distance(b.Door, from) <= radius && PathFinder.Distance(b.Door, enemy) >= danger
                                           && _world.PathFinder!.AreConnected(from, b.Door))
            .MinBy(b => PathFinder.Distance(b.Door, from))?.Door;
    }

    private const string HospitalName = "hospital";

    /// <summary>Where the wanted hospital goes (see <see cref="HospitalSpot"/>), while one is wanted.</summary>
    [NonSerialized] private GridPoint? _hospitalAnchor;

    /// <summary>Wounded in the sense of <c>hospital.woundedHealth</c>.</summary>
    private bool Wounded(Settler soldier) => HealthShare(soldier) < (_config.Hospital ?? AiConfig.HospitalRules.Default).WoundedHealth;

    /// <summary>
    /// Wanted hospital: more than <c>hospital.wounded</c> own wounded soldiers further than <c>hospital.sendDistance</c>
    /// from every own hospital (sites too; nearer ones are sent there), not counting those still fighting a strike or campaign. It goes to the own military building nearest them; null while none is wanted.
    /// </summary>
    private GridPoint? HospitalSpot(List<Building> mine)
    {
        var rules = _config.Hospital ?? AiConfig.HospitalRules.Default;
        if (rules.Wounded <= 0 || _catalog.Find(HospitalName) is null)
            return null;
        var hospitals = mine.Where(b => b.Definition.Name == HospitalName).Select(b => b.Door).ToList();
        // Not those still fighting a strike or campaign: they are wherever the fight is.
        var fighting = _strikes.SelectMany(k => k.Fighting).Concat(_campaign?.Soldiers.Except(_campaign.Resting) ?? []).ToHashSet();
        var wounded = _world.Settlers.Where(s => s.Player == Player && s.Profession == Profession.Soldier && !s.IsInside && !s.IsDead
                                                 && !fighting.Contains(s) && Wounded(s)
                                                 && hospitals.All(h => PathFinder.Distance(h, s.Position) > rules.SendDistance)).ToList();
        if (wounded.Count <= rules.Wounded)
            return null;
        var center = CenterOf(wounded);
        return mine.Where(b => IsMilitary(b) && b.State == BuildingState.Finished).MinBy(b => PathFinder.Distance(b.Position, center))?.Position;
    }

    /// <summary>
    /// Idle wounded soldiers go to the nearest own finished hospital within <c>hospital.sendDistance</c>: whoever stands
    /// still on its heal spot is healed, called or not. One at a time, the weakest first: the others wait
    /// <see cref="HospitalQueueDistance"/> fields out in front of it (idle soldiers keep a field apart, a crowd at the door
    /// would push the one on the spot away and the healer would never come out). Returns those sent (or already
    /// there), which nothing else places.
    /// </summary>
    private List<Settler> SendToHospital(List<Settler> idle)
    {
        var rules = _config.Hospital ?? AiConfig.HospitalRules.Default;
        var hospitals = _world.Buildings.Where(b => b.Exists && b.Player == Player && b.Definition.Name == HospitalName
                                                    && b.State == BuildingState.Finished && _world.Production.HasWorker(b)).ToList();
        var patients = new List<Settler>();
        if (hospitals.Count == 0)
            return patients;
        var pathFinder = _world.PathFinder!;
        var queues = new Dictionary<Building, List<Settler>>();
        foreach (var soldier in idle.Where(Wounded))
        {
            var hospital = hospitals.Where(h => PathFinder.Distance(h.Door, soldier.Position) <= rules.SendDistance && pathFinder.AreConnected(soldier.Position, h.Door))
                .MinBy(h => PathFinder.Distance(h.Door, soldier.Position));
            if (hospital is null)
                continue;
            patients.Add(soldier);
            (queues.TryGetValue(hospital, out var queue) ? queue : queues[hospital] = new List<Settler>()).Add(soldier);
        }
        var map = _world.Map!;
        foreach (var (hospital, queue) in queues)
        {
            if (_world.Production.HealSpotOf(hospital) is not { } spot)
                continue;
            var door = hospital.Door;
            var waiting = new GridPoint(Math.Clamp(door.X + (door.X - hospital.Position.X) * HospitalQueueDistance, 0, map.Size - 1),
                Math.Clamp(door.Y + (door.Y - hospital.Position.Y) * HospitalQueueDistance, 0, map.Size - 1));
            // The weakest to the spot, unless somebody is already there or on the way.
            var next = queue.FirstOrDefault(s => PathFinder.Distance(Heading(s), spot) <= 1) ?? queue.MinBy(s => s.Health)!;
            if (PathFinder.Distance(Heading(next), spot) > 1)
                _world.Military.Move([next], spot);
            var rest = queue.Where(s => s != next && PathFinder.Distance(Heading(s), waiting) > HospitalQueueDistance).ToList();
            if (rest.Count > 0)
                _world.Military.Move(rest, waiting);
        }
        return patients;
    }

    /// <summary>How far out in front of a hospital's door the wounded wait for their turn.</summary>
    private const int HospitalQueueDistance = 3;

    /// <summary>Share of its full health a soldier has left.</summary>
    private double HealthShare(Settler soldier) =>
        soldier.SoldierType is { } type ? soldier.Health / _world.MaxHealth(soldier.Player, type, soldier.Level) : 1;

    /// <summary>
    /// Where a soldier at <paramref name="from"/> waits away from the enemies at <paramref name="enemy"/>: on the
    /// way to the nearest own military building away from them (<see cref="RetreatDoor"/>), <paramref name="distance"/>
    /// fields from the enemies; the door itself when it is not much further or that spot cannot be reached.
    /// </summary>
    private GridPoint RestSpot(GridPoint from, GridPoint enemy, int distance)
    {
        var door = RetreatDoor(from, enemy);
        int away = PathFinder.Distance(door, enemy);
        if (away <= distance + 5)
            return door;
        var map = _world.Map!;
        var spot = new GridPoint(enemy.X + (door.X - enemy.X) * distance / away, enemy.Y + (door.Y - enemy.Y) * distance / away);
        return map.Contains(spot) && map.IsWalkable(spot) && _world.PathFinder!.AreConnected(from, spot) ? spot : door;
    }

    /// <summary>
    /// Late game: a big army (<c>army.lateSoldiers</c> soldiers outside the buildings or more), in whose fights single
    /// wounded can no longer be pulled out. Its weaponsmiths get the shares of <c>army.lateShares</c>: mostly spears
    /// and bows – pikemen wear armour, bowmen shoot from behind them. Once the army has shrunk below
    /// <see cref="AiConfig.ArmyRules.EarlySoldiers"/>, the shares it had before come back.
    /// </summary>
    private void ArmForLateGame()
    {
        if (_config.Army is not { LateSoldiers: > 0 } army)
            return;
        int soldiers = _world.Settlers.Count(s => s.Player == Player && s.Profession == Profession.Soldier && !s.IsInside && !s.IsDead);
        var production = _world.Production;
        if (_earlyShares is null && soldiers >= army.LateSoldiers)
        {
            _earlyShares = army.LateShares.Keys.ToDictionary(w => w, w => production.ShareOf(Player, w));
            foreach (var (weapon, share) in army.LateShares)
                production.SetShare(Player, weapon, share);
            Note($"late game, {soldiers} soldiers in the field: {string.Join(", ", army.LateShares.Select(w => $"{w.Key} {w.Value:P0}"))}");
        }
        else if (_earlyShares is { } early && soldiers < army.EarlySoldiers)
        {
            foreach (var (weapon, share) in early)
                production.SetShare(Player, weapon, share);
            _earlyShares = null;
            Note($"only {soldiers} soldiers left in the field: weapons as before");
        }
    }
}
