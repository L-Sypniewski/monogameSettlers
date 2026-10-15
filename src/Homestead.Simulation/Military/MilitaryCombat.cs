using Homestead.Simulation.Buildings;
using Homestead.Simulation.Pathfinding;

namespace Homestead.Simulation.Military;

/// <summary>
/// Combat: attacks on enemy military buildings, fights between soldiers, defenders, bowmen on towers
/// and the capture of buildings.
/// </summary>
/// <remarks>
/// A fighter looks for the nearest enemy soldier outside the buildings within sight (also while on a
/// move or attack order; it stops as soon as the enemy is within reach). Melee soldiers walk up to it
/// and strike from the neighboring field, bowmen shoot from up to their range; one hit every attack
/// interval. A shot hurts at once, as in the original (nobody shoots at an enemy that is as good as
/// dead); its arrow only shows on screen, flying <see cref="ArrowTicks"/>. Near the enemy the way is planned locally around everyone standing (see <see cref="Approach"/>). Idle soldiers fight enemies
/// that come within sight.
/// </remarks>
/// <remarks>
/// Enemy military buildings are taken the same way on their own: an idle soldier or one on a (not forced)
/// move order attacks the nearest defended enemy military building whose door is within sight
/// (<see cref="NearestEnemyBuilding"/>) unless an enemy soldier is nearer: whichever is nearer comes first
/// (<see cref="PreferNearer"/>). A moving one carries on afterwards if it did not move in.
/// </remarks>
/// <remarks>
/// Attacked buildings: the soldiers stay inside (bowmen keep shooting from the top). Without an enemy an
/// attacker walks to its target's door: melee soldiers (pikemen too, as if their range were 1) break it
/// from the few fields next to it (<see cref="MilitaryConfig.DoorFighters"/>; the others queue behind),
/// bowmen wait <see cref="BowmanDoorDistance"/> fields away (<see cref="MilitaryConfig.DoorHealth"/>). In the
/// broken door stands one infantry soldier at a time (<see cref="DefendDoors"/>): it cannot walk, strikes
/// the attackers on those fields and can be fought only by them, so bowmen cannot help to take a
/// building. When it falls the next one steps in, the bowmen last: they shoot from the door at enemies
/// within range, but are fought the same way. The attacker that killed the
/// last defender walks in and takes the building: it, its soldier places and its land change sides,
/// and buildings standing on land their player lost are destroyed. With no enemy near for
/// <see cref="MilitaryConfig.DoorCalmTicks"/> the guard goes back inside and the door repairs itself evenly,
/// from broken to whole in <see cref="MilitaryConfig.DoorRegenTicks"/>; already its first moment of repair
/// takes another hit to break it.
/// </remarks>
public sealed partial class Military
{
    private const int EngageInterval = 10;
    private const int ReplanTicks = 20;
    /// <summary>Attacking bowmen wait this far from a defended door: they can hurt neither it nor its guard.</summary>
    private const int BowmanDoorDistance = 4;

    /// <summary>Door guards (by soldier id) and the building they stand in.</summary>
    private readonly Dictionary<int, Garrison> _guards = new();

    private readonly Dictionary<int, Fighter> _fighters = new();
    private readonly Dictionary<int, long> _nextShot = new();
    private readonly Dictionary<int, (long Tick, float Strength)> _strengths = new();
    private const int UnreachableTicks = 200;
    private const int RetargetTicks = 10;

    public int Kills { get; private set; }

    private readonly List<Arrow> _arrows = new();

    /// <summary>Arrows in flight.</summary>
    public IReadOnlyList<Arrow> Arrows => _arrows;

    /// <summary>Flight time of an arrow over a distance in fields.</summary>
    public static int ArrowTicks(int distance) => 2 + distance / 2;

    public int Captures { get; private set; }

    /// <summary>
    /// Sends the player's soldiers against an enemy military building: idle soldiers and the soldiers
    /// of own military buildings within <see cref="MilitaryConfig.AttackRange"/> (one stays in each).
    /// Returns how many go.
    /// </summary>
    public int Attack(int player, Building target)
    {
        if (!target.Exists || target.Player == player || !_garrisons.ContainsKey(target.Id))
            return 0;
        int range = _config.AttackRange;
        var attackers = _world.Settlers
            .Where(s => s.Profession == Profession.Soldier && s.Player == player && !s.HasTask && !s.IsInside
                        && PathFinder.Distance(s.Position, target.Door) <= range)
            .ToList();
        foreach (var g in _garrisons.Values.Where(g => g.Building.Player == player && g.Building.Exists
                                                       && PathFinder.Distance(g.Building.Door, target.Door) <= range))
        {
            for (int i = 0; i < g.Places.Length; i++)
            {
                if (g.Places[i] is not { } soldier || g.Places.Count(p => p is not null) <= 1)
                    continue;
                g.Places[i] = null;
                LeaveBuilding(soldier);
                attackers.Add(soldier);
            }
        }
        foreach (var soldier in attackers)
        {
            soldier.HasTask = true;
            _fighters[soldier.Id] = new Fighter(soldier) { Target = target };
        }
        return attackers.Count;
    }

    /// <summary>
    /// Orders soldiers (of one player, outside buildings) to a point; they spread over free fields around
    /// it. Enemies in sight on the way are fought first, then they carry on.
    /// </summary>
    /// <param name="forced">Forced order (escape, retreat): walk there without fighting anybody on the way.</param>
    public void Move(IEnumerable<Settler> soldiers, GridPoint destination, bool forced = false)
    {
        var list = Commandable(soldiers);
        var spots = FreeSpotsAround(destination, list.Count);
        for (int i = 0; i < list.Count; i++)
        {
            var soldier = list[i];
            _fighters[soldier.Id] = new Fighter(soldier) { MoveTo = spots.Count > i ? spots[i] : destination, Forced = forced };
            soldier.HasTask = true;
        }
    }

    /// <summary>Orders the given soldiers to attack an enemy military building (enemies on the way first).</summary>
    public int Attack(IEnumerable<Settler> soldiers, Building target)
    {
        if (!target.Exists || !_garrisons.ContainsKey(target.Id))
            return 0;
        var list = Commandable(soldiers).Where(s => s.Player != target.Player).ToList();
        foreach (var soldier in list)
        {
            _fighters[soldier.Id] = new Fighter(soldier) { Target = target };
            soldier.HasTask = true;
        }
        return list.Count;
    }

    /// <summary>Soldiers outside that can take an order; ones walking into a building drop that.</summary>
    private List<Settler> Commandable(IEnumerable<Settler> soldiers)
    {
        // A door guard holds its post.
        var list = soldiers.Where(s => s.Profession == Profession.Soldier && !s.IsDead && !s.IsInside && !_guards.ContainsKey(s.Id))
            .Distinct().ToList();
        foreach (var g in _garrisons.Values)
        {
            foreach (var (place, coming) in g.Coming.ToList())
                if (list.Contains(coming.Soldier))
                    g.Coming.Remove(place);
            foreach (var (place, defender) in g.Defending.ToList())
                if (list.Contains(defender))
                    g.Defending.Remove(place);
        }
        foreach (var soldier in list)
            SetActivity(soldier, Activity.None);
        return list;
    }

    /// <summary>Walkable points nearest to <paramref name="center"/>, one per soldier.</summary>
    private List<GridPoint> FreeSpotsAround(GridPoint center, int count)
    {
        var map = _world.Map!;
        var spots = new List<GridPoint>();
        for (int r = 0; r <= 12 && spots.Count < count; r++)
            for (int dy = -r; dy <= r && spots.Count < count; dy++)
            for (int dx = -r; dx <= r && spots.Count < count; dx++)
            {
                var p = new GridPoint(center.X + dx, center.Y + dy);
                if (PathFinder.Distance(center, p) == r && map.IsWalkable(p))
                    spots.Add(p);
            }
        return spots;
    }

    private void TickCombat()
    {
        // Who can fight at all, once per tick: looking through every settler for each bowman and
        // fighter is what made long games slow.
        _combatants.Clear();
        foreach (var settler in _world.Settlers)
            if (settler.Profession is Profession.Soldier or Profession.Priest && !settler.IsInside && !settler.IsDead)
                _combatants.Add(settler);
        if (_world.Tick % EngageInterval == 0)
            EngageIdleSoldiers();
        DefendDoors();
        _arrows.RemoveAll(a => a.Tick + a.FlightTicks <= _world.Tick);
        ShootFromBuildings();
        foreach (var fighter in _fighters.Values.ToList())
            Advance(fighter);
    }

    /// <summary>Soldiers and priests outside the buildings at the start of this tick.</summary>
    private readonly List<Settler> _combatants = new();

    /// <summary>
    /// Enemy soldiers and priests outside the buildings: who can be fought. Not door guards: only the
    /// attackers at the door fight them (see <see cref="Advance"/>).
    /// </summary>
    private IEnumerable<Settler> EnemiesOf(int player) =>
        _combatants.Where(s => s.Player != player && !s.IsInside && !s.IsDead && !_guards.ContainsKey(s.Id));

    private Settler? NearestEnemy(GridPoint from, int player, int range) =>
        EnemiesOf(player).Where(e => PathFinder.Distance(from, e.Position) <= range)
            .MinBy(e => PathFinder.Distance(from, e.Position));

    /// <summary>Ticks to the next strike; hasted by a spell, faster.</summary>
    private int HitInterval(Settler soldier, SoldierStats stats) =>
        soldier.HastedUntil > _world.Tick ? Math.Max(1, (int)Math.Round(stats.AttackTicks / soldier.HasteFactor)) : stats.AttackTicks;

    /// <summary>Soldiers standing around fight enemies that come within sight, then take enemy military buildings in sight.</summary>
    private void EngageIdleSoldiers()
    {
        foreach (var soldier in _world.Settlers.Where(s => s.Profession == Profession.Soldier && !s.HasTask && !s.IsInside).ToList())
        {
            var enemy = NearestEnemy(soldier.Position, soldier.Player, _config.Sight(soldier.SoldierType!.Value));
            var building = NearestEnemyBuilding(soldier);
            if (building is not null && (enemy is null
                    || PathFinder.Distance(soldier.Position, building.Door) < PathFinder.Distance(soldier.Position, enemy.Position)))
                _fighters[soldier.Id] = new Fighter(soldier) { Target = building, AutoTarget = true };
            else if (enemy is not null)
                _fighters[soldier.Id] = new Fighter(soldier);
            else
                continue;
            soldier.HasTask = true;
        }
    }

    /// <summary>Buildings a soldier could not reach on its own: (soldier id, building id) → until when it does not try again.</summary>
    private readonly Dictionary<(int Soldier, int Building), long> _unreachableBuildings = new();

    /// <summary>
    /// The nearest defended enemy military building whose door is within the soldier's sight and that it
    /// did not just fail to reach.
    /// </summary>
    private Building? NearestEnemyBuilding(Settler soldier)
    {
        int sight = _config.Sight(soldier.SoldierType!.Value);
        Building? nearest = null;
        int best = int.MaxValue;
        foreach (var g in _garrisons.Values)
        {
            var building = g.Building;
            if (building.Player == soldier.Player || !building.Exists || g.IsEmpty)
                continue;
            int distance = PathFinder.Distance(soldier.Position, building.Door);
            if (distance > sight || distance >= best)
                continue;
            if (_unreachableBuildings.TryGetValue((soldier.Id, building.Id), out long until))
            {
                if (until > _world.Tick)
                    continue;
                _unreachableBuildings.Remove((soldier.Id, building.Id));
            }
            nearest = building;
            best = distance;
        }
        return nearest;
    }

    /// <summary>
    /// Enemy soldiers and enemy buildings count alike for a soldier free to choose (no attack order, not
    /// forced): it goes for whichever is nearer, an enemy within reach always first.
    /// </summary>
    private void PreferNearer(Fighter f, SoldierStats stats)
    {
        var soldier = f.Soldier;
        int enemyDistance = f.Enemy is { } enemy ? PathFinder.Distance(soldier.Position, enemy.Position) : int.MaxValue;
        if (enemyDistance <= stats.Range)
            return;
        if (f.Target is null && _world.Tick % EngageInterval == 0 && NearestEnemyBuilding(soldier) is { } building)
        {
            f.Target = building;
            f.AutoTarget = true;
            f.NextReplan = -10 * ReplanTicks; // head for its door right away
        }
        if (f.Target is { } target && f.Enemy is not null && PathFinder.Distance(soldier.Position, target.Door) < enemyDistance)
        {
            f.Enemy = null;
            if (f.Chasing)
                f.NextReplan = -10 * ReplanTicks; // back to the door right away
        }
    }

    /// <summary>
    /// An attack the soldier took up on its own is over: on a move order it carries on, otherwise it is done.
    /// </summary>
    /// <param name="unreachable">The building could not be reached: leave it for a while.</param>
    private void EndAttack(Fighter f, bool unreachable = false)
    {
        if (f.AutoTarget && unreachable && f.Target is { } target)
            _unreachableBuildings[(f.Soldier.Id, target.Id)] = _world.Tick + UnreachableTicks;
        if (f.AutoTarget && f.MoveTo is not null)
        {
            f.Target = null;
            f.AutoTarget = false;
            SetActivity(f.Soldier, Activity.None);
            f.NextReplan = -10 * ReplanTicks; // walk on right away
            return;
        }
        Dismiss(f);
    }

    /// <summary>
    /// Broken doors: one defender at a time stands in front of it, infantry first, then the bowmen. Once it
    /// is calm the guard goes in and the door repairs itself.
    /// </summary>
    private void DefendDoors()
    {
        foreach (var g in _garrisons.Values)
        {
            if (!g.Building.Exists)
                continue;
            if (g.DoorHealth < _config.DoorHealth || g.Guard is not null)
            {
                if (EnemyNearDoor(g))
                    g.LastThreat = _world.Tick;
                else if (_world.Tick - g.LastThreat >= _config.DoorCalmTicks)
                    g.DoorHealth = Math.Min(_config.DoorHealth, g.DoorHealth + _config.DoorHealth / _config.DoorRegenTicks);
            }
            if (g.DoorBroken && g.Guard is null)
            {
                // Infantry first, then the bowmen come down.
                int place = Array.FindIndex(g.Places, p => p is { SoldierType: not SoldierType.Bowman });
                if (place < 0)
                    place = Array.FindIndex(g.Places, p => p is not null);
                if (place >= 0)
                    PostGuard(g, place);
            }
            if (g.Guard is { } guard)
                GuardDoor(g, guard);
        }
    }

    /// <summary>Whether an enemy stands within <see cref="MilitaryConfig.DoorCalmRadius"/> of the door fight.</summary>
    private bool EnemyNearDoor(Garrison g)
    {
        var center = FightPointOf(g);
        int radius = _config.DoorCalmRadius;
        return EnemiesOf(g.Building.Player).Any(e => PathFinder.Distance(center, e.Position) <= radius);
    }

    /// <summary>The soldier of the place comes out of the broken door to stand in front of it.</summary>
    private void PostGuard(Garrison g, int place)
    {
        var guard = g.Places[place]!;
        g.Places[place] = null;
        g.Defending[place] = guard;
        g.Guard = guard;
        _guards[guard.Id] = g;
        LeaveBuilding(guard);
        guard.HasTask = true;
        guard.Facing = Direction.SouthEast;
    }

    /// <summary>The door is whole again and its guard back at it: it goes into its place.</summary>
    private void ReturnGuard(Garrison g, Settler guard)
    {
        int place = g.Defending.First(d => d.Value == guard).Key;
        g.Defending.Remove(place);
        _guards.Remove(guard.Id);
        g.Guard = null;
        SetActivity(guard, Activity.None);
        _world.EnterBuilding(guard);
        g.Places[place] = guard;
    }

    /// <summary>
    /// The door guard steps out to the <see cref="Garrison.FightPoint"/> in front of the broken door and
    /// stays there: infantry strikes an enemy on the fighting fields next to it, a bowman shoots at the
    /// nearest enemy within range. Once the door is whole again it goes back in.
    /// </summary>
    private void GuardDoor(Garrison g, Settler guard)
    {
        if (guard.FrozenUntil > _world.Tick)
        {
            SetActivity(guard, Activity.None);
            return;
        }
        var post = g.DoorBroken ? FightPointOf(g) : g.Building.Door;
        if (guard.Position != post || guard.State == SettlerState.Stepping || guard.Destination is not null)
        {
            SetActivity(guard, Activity.None);
            if (guard.Destination != post && guard.State != SettlerState.Stepping)
                _world.WalkTo(guard, post);
            return;
        }
        if (!g.DoorBroken)
        {
            ReturnGuard(g, guard);
            return;
        }
        var stats = _config.Soldiers[guard.SoldierType!.Value];
        bool bowman = guard.SoldierType == SoldierType.Bowman;
        var spots = DoorSpotsOf(g);
        var enemy = bowman
            ? NearestEnemy(guard.Position, guard.Player, stats.Range)
            : EnemiesOf(guard.Player).Where(e => spots.Contains(e.Position)).MinBy(e => e.Health);
        if (enemy is null)
        {
            SetActivity(guard, Activity.None);
            return;
        }
        guard.Facing = FacingTowards(guard.Position, enemy.Position);
        SetActivity(guard, bowman ? Activity.Shooting : Activity.Fighting);
        if (_world.Tick < g.GuardNextHit)
            return;
        int interval = HitInterval(guard, stats);
        Hit(guard, enemy);
        g.GuardNextHit = _world.Tick + interval;
        guard.ShotTicks = interval;
    }

    /// <summary>Bowmen in military buildings shoot at enemies within range of the building.</summary>
    private void ShootFromBuildings()
    {
        foreach (var g in _garrisons.Values)
        {
            if (!g.Building.Exists)
                continue;
            // All bowmen of a building shoot from the same spot: one search for the target will do,
            // another only once it has died.
            Settler? enemy = null;
            bool searched = false;
            foreach (var bowman in g.Places)
            {
                if (bowman is not { SoldierType: SoldierType.Bowman } || _nextShot.GetValueOrDefault(bowman.Id) > _world.Tick)
                    continue;
                var stats = _config.Soldiers[SoldierType.Bowman];
                if (!searched || enemy is { IsDead: true })
                {
                    enemy = NearestEnemy(g.Building.Position, bowman.Player, stats.Range);
                    searched = true;
                }
                if (enemy is null)
                    break;
                bowman.Facing = FacingTowards(g.Building.Position, enemy.Position);
                SetActivity(bowman, Activity.Shooting);
                Hit(bowman, enemy, g.Building);
                bowman.ShotTicks = stats.AttackTicks;
                _nextShot[bowman.Id] = _world.Tick + stats.AttackTicks;
            }
            // Nobody left to shoot at: the bowmen lower their bows once the last shot is through.
            foreach (var bowman in g.Places)
                if (bowman is { Activity: Activity.Shooting } && _nextShot.GetValueOrDefault(bowman.Id) <= _world.Tick)
                    SetActivity(bowman, Activity.None);
        }
    }

    private void Advance(Fighter f)
    {
        var soldier = f.Soldier;
        if (soldier.IsDead)
        {
            _fighters.Remove(soldier.Id);
            return;
        }
        var stats = _config.Soldiers[soldier.SoldierType!.Value];
        // Frozen by a spell: stands still, strikes nobody.
        if (soldier.FrozenUntil > _world.Tick)
        {
            SetActivity(soldier, Activity.None);
            return;
        }

        if (f.Forced)
            f.Enemy = null;
        else
        {
            ChooseEnemy(f, stats);
            if (f.Target is null || f.AutoTarget)
                PreferNearer(f, stats);
        }
        f.Chasing = f.Enemy is not null;

        if (f.Enemy is { } enemy)
        {
            int distance = PathFinder.Distance(soldier.Position, enemy.Position);
            if (distance <= stats.Range)
            {
                // Stop after the current step. Walkers are mid-step whenever this runs (movement comes
                // first in a tick), so waiting for a standstill would let them walk on.
                if (soldier.Path.Count > 0)
                    _world.WalkTo(soldier, soldier.StepTarget);
                if (soldier.State == SettlerState.Stepping)
                    return;
                soldier.Facing = FacingTowards(soldier.Position, enemy.Position);
                SetActivity(soldier, soldier.SoldierType == SoldierType.Bowman ? Activity.Shooting : Activity.Fighting);
                if (_world.Tick >= f.NextHit)
                {
                    int interval = HitInterval(soldier, stats);
                    Hit(soldier, enemy);
                    f.NextHit = _world.Tick + interval;
                    soldier.ShotTicks = interval;
                }
                return;
            }
            SetActivity(soldier, Activity.None);
            if (soldier.Destination is null || _world.Tick >= f.NextReplan)
            {
                if (!Approach(f, enemy, stats))
                {
                    // No way there (walled in, other side of the water): leave it for a while.
                    f.Unreachable[enemy.Id] = _world.Tick + UnreachableTicks;
                    f.Enemy = null;
                }
                f.NextReplan = _world.Tick + ReplanTicks;
            }
            return;
        }

        // An attacker at a door keeps its activity while it fights there (resetting it every tick would
        // restart the animation); every other way through sets it to none.
        if (f.Target is null)
            SetActivity(soldier, Activity.None);
        if (f.Target is { } target)
        {
            if (!target.Exists || target.Player == soldier.Player)
            {
                EndAttack(f);
                return;
            }
            bool standing = soldier.State != SettlerState.Stepping;
            if (!_garrisons.TryGetValue(target.Id, out var g))
            {
                EndAttack(f);
                return;
            }
            var door = target.Door;
            if (g.IsEmpty)
            {
                SetActivity(soldier, Activity.None);
                // Nobody left inside: the one that struck the last defender down walks in, the others are done.
                if (!MayCapture(g, soldier))
                {
                    EndAttack(f);
                    return;
                }
                if (soldier.Position == door && standing)
                {
                    Capture(target, soldier);
                    return;
                }
            }
            else
            {
                // Defended: melee soldiers break the door from the fighting fields in front of it, then fight
                // its guard from there; the others queue behind, bowmen keep their distance. Nobody else
                // stands in the door or on the guard's field.
                bool bowman = soldier.SoldierType == SoldierType.Bowman;
                var spots = DoorSpotsOf(g);
                var fightPoint = FightPointOf(g);
                // A walk-through (watchdog) can leave two on one field: only its owner fights from there.
                if (!bowman && standing && soldier.Destination is null && spots.Contains(soldier.Position)
                    && _world.OccupantAt(soldier.Position) == soldier)
                {
                    // The next defender is coming, or the guard is not yet at its post.
                    if (g.DoorBroken && (g.Guard is not { } posted || posted.Position != fightPoint || posted.State == SettlerState.Stepping))
                    {
                        SetActivity(soldier, Activity.None);
                        return;
                    }
                    soldier.Facing = FacingTowards(soldier.Position, fightPoint);
                    SetActivity(soldier, Activity.Fighting);
                    if (_world.Tick >= f.NextHit)
                    {
                        int interval = HitInterval(soldier, stats);
                        // A guard still on its way back in after the repair began: the door is in the way again.
                        if (g.DoorBroken && g.Guard is { } guard)
                            Hit(soldier, guard);
                        else
                        {
                            g.DoorHealth = Math.Max(0, g.DoorHealth - _config.Damage(soldier.SoldierType!.Value, soldier.Level) * CombatStrengthOf(soldier));
                            g.LastThreat = _world.Tick;
                        }
                        f.NextHit = _world.Tick + interval;
                        soldier.ShotTicks = interval;
                    }
                    return;
                }
                SetActivity(soldier, Activity.None);
                // Heading for a fighting field somebody else took meanwhile: wait elsewhere.
                bool taken = soldier.Destination is { } heading && spots.Contains(heading)
                             && _world.OccupantAt(heading) is { } holder && holder != soldier;
                if (soldier.Destination is null || taken || _world.Tick >= f.NextReplan + 10 * ReplanTicks)
                {
                    var free = bowman ? null : spots.Where(p => _world.OccupantAt(p) is null || _world.OccupantAt(p) == soldier)
                        .Select(p => (GridPoint?)p).MinBy(p => PathFinder.Distance(soldier.Position, p!.Value));
                    var wait = free ?? QueueSpot(soldier, fightPoint, bowman ? BowmanDoorDistance : 2, spots.Append(door).ToArray());
                    if (!_world.WalkTo(soldier, wait) && !_world.WalkTo(soldier, door))
                        EndAttack(f, unreachable: true);
                    f.NextReplan = _world.Tick;
                }
                return;
            }
            if (soldier.Destination is null || _world.Tick >= f.NextReplan + 10 * ReplanTicks)
            {
                if (!_world.WalkTo(soldier, door))
                    EndAttack(f, unreachable: true);
                f.NextReplan = _world.Tick;
            }
            return;
        }
        if (f.MoveTo is { } spot)
        {
            if (soldier.Position == spot || soldier.Destination is null && PathFinder.Distance(soldier.Position, spot) <= 1 && f.NextReplan > 0)
            {
                Dismiss(f);
                return;
            }
            if (soldier.Destination is null || _world.Tick >= f.NextReplan + 10 * ReplanTicks)
            {
                if (!_world.WalkTo(soldier, spot))
                    Dismiss(f);
                f.NextReplan = _world.Tick;
            }
            return;
        }
        Dismiss(f);
    }

    /// <summary>
    /// The field in front of the door where its guard stands (see <see cref="OutwardNeighbors"/>; the door
    /// itself if there is none), clear of the building so it can be seen.
    /// </summary>
    private GridPoint FightPointOf(Garrison g)
    {
        if (g.FightPoint is { } known)
            return known;
        var door = g.Building.Door;
        if (SetDoorPoints(g) is { } set)
            return (g.FightPoint = door + set.Guard).Value;
        return (g.FightPoint = OutwardNeighbors(g.Building, door).DefaultIfEmpty(door).First()).Value;
    }

    /// <summary>
    /// The building's hand-set door fight fields (<see cref="MilitaryConfig.DoorPoints"/>), if any and all walkable;
    /// otherwise a warning and the automatic ones.
    /// </summary>
    private DoorPoints? SetDoorPoints(Garrison g)
    {
        var definition = g.Building.Definition;
        if (!_config.DoorPoints.TryGetValue((definition.Civilization, definition.Name), out var set))
            return null;
        var map = _world.Map!;
        var door = g.Building.Door;
        if (set.Fighters.Prepend(set.Guard).All(o => map.Contains(door + o) && map.IsWalkable(door + o)))
            return set;
        if (_warnedDoorPoints.Add((definition.Civilization, definition.Name)))
            Console.Error.WriteLine($"military.json door.points: a field of {definition.Civilization} {definition.Name} at {g.Building.Position} is not walkable (wrong offset or something in the way); using the automatic ones.");
        return null;
    }

    private readonly HashSet<(Civilization, string)> _warnedDoorPoints = new();

    /// <summary>
    /// The fields next to the <see cref="FightPointOf">fight point</see> where melee attackers fight: its walkable
    /// neighbors in front (not the door), as many as <see cref="MilitaryConfig.DoorFighters"/> allows.
    /// </summary>
    private GridPoint[] DoorSpotsOf(Garrison g)
    {
        if (g.DoorSpots is { } known)
            return known;
        var building = g.Building;
        int count = _config.DoorFighters.GetValueOrDefault(building.Definition.Name, 2);
        if (SetDoorPoints(g) is { } set)
            return g.DoorSpots = set.Fighters.Select(o => building.Door + o).Take(count).ToArray();
        return g.DoorSpots = OutwardNeighbors(building, FightPointOf(g)).Where(p => p != building.Door).Take(count).ToArray();
    }

    /// <summary>
    /// The walkable neighbors of the point, those in front of the building first (lower on screen, so
    /// drawn over it), then farthest from it.
    /// </summary>
    private IEnumerable<GridPoint> OutwardNeighbors(Building building, GridPoint point)
    {
        var map = _world.Map!;
        return Enumerable.Range(0, DirectionExtensions.Count)
            .Select(d => point.Neighbor((Direction)d))
            .Where(p => map.Contains(p) && map.IsWalkable(p))
            .OrderByDescending(p => p.Y)
            .ThenByDescending(p => PathFinder.Distance(building.Position, p));
    }

    /// <summary>
    /// Where an attacker waits its turn at a defended door: the free walkable field nearest to it, at
    /// least <paramref name="ring"/> fields from the fight point and not on the <paramref name="kept"/> fields
    /// (fighting fields, door); the fight point if there is none.
    /// </summary>
    private GridPoint QueueSpot(Settler soldier, GridPoint door, int ring, GridPoint[] kept)
    {
        var map = _world.Map!;
        for (int r = ring; r <= ring + 3; r++)
        {
            GridPoint? best = null;
            int bestDistance = int.MaxValue;
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                var p = new GridPoint(door.X + dx, door.Y + dy);
                if (PathFinder.Distance(door, p) != r || kept.Contains(p) || !map.Contains(p) || !map.IsWalkable(p)
                    || _world.OccupantAt(p) is { } other && other != soldier)
                    continue;
                int distance = PathFinder.Distance(soldier.Position, p);
                if (distance < bestDistance)
                {
                    best = p;
                    bestDistance = distance;
                }
            }
            if (best is { } spot)
                return spot;
        }
        return door;
    }

    /// <summary>
    /// Picks the enemy to fight. An enemy already within reach (the neighboring field for melee) is
    /// attacked at once; every <see cref="RetargetTicks"/> a clearly nearer enemy replaces the current
    /// one, so a soldier does not wait behind someone who stepped into its way.
    /// </summary>
    private void ChooseEnemy(Fighter f, SoldierStats stats)
    {
        var soldier = f.Soldier;
        // Gone, inside, or won over by a spell: look for another one.
        if (f.Enemy is { } current && (current.IsDead || current.IsInside || current.Player == soldier.Player || PathFinder.Distance(soldier.Position, current.Position) > _config.Sight(soldier.SoldierType!.Value) + 4))
            f.Enemy = null;

        int currentDistance = f.Enemy is { } e ? PathFinder.Distance(soldier.Position, e.Position) : int.MaxValue;
        if (currentDistance <= stats.Range)
            return;
        bool recheck = f.Enemy is null || _world.Tick >= f.NextRetarget;
        var candidates = EnemiesOf(soldier.Player)
            .Select(x => (Enemy: x, Distance: PathFinder.Distance(soldier.Position, x.Position)))
            .Where(x => x.Distance <= (recheck ? _config.Sight(soldier.SoldierType!.Value) : stats.Range)
                        && !(f.Unreachable.TryGetValue(x.Enemy.Id, out long until) && until > _world.Tick))
            .ToList();
        if (candidates.Count == 0)
            return;
        var nearest = candidates.MinBy(x => x.Distance);
        if (nearest.Distance <= stats.Range || nearest.Distance < currentDistance - 1)
        {
            f.Enemy = nearest.Enemy;
            f.NextReplan = 0; // walk towards the new one right away
        }
        if (recheck)
            f.NextRetarget = _world.Tick + RetargetTicks;
    }

    /// <summary>
    /// Plans the way to a field within reach of the enemy. Near the enemy the search stays inside
    /// <see cref="MilitaryConfig.CombatPathRadius"/> and treats everyone standing (fighting, waiting, idle; own
    /// soldiers too) as an obstacle, so a soldier walks around a busy friend instead of queueing
    /// behind it. When the crowd blocks every way, it goes to the reachable field closest to any
    /// enemy (and takes that enemy). False if the enemy cannot be reached at all.
    /// </summary>
    private bool Approach(Fighter f, Settler enemy, SoldierStats stats)
    {
        var soldier = f.Soldier;
        var start = soldier.StepTarget;
        if (PathFinder.Distance(start, enemy.Position) > _config.CombatPathRadius)
            return _world.WalkTo(soldier, enemy.Position); // still far: the plain way, the crowd is handled on arrival

        var map = _world.Map!;
        var enemies = EnemiesOf(soldier.Player)
            .Where(e => PathFinder.Distance(start, e.Position) <= _config.CombatPathRadius + stats.Range)
            .ToList();
        // The combatants are those of the tick's start: a defender sent out since is not among them yet.
        if (!enemies.Contains(enemy))
            enemies.Add(enemy);
        int Reach(GridPoint p) => enemies.Min(e => PathFinder.Distance(p, e.Position));

        var cameFrom = new Dictionary<GridPoint, (GridPoint From, Direction Step)> { [start] = (start, default) };
        var queue = new Queue<GridPoint>();
        queue.Enqueue(start);
        GridPoint? goal = null;
        var closest = start;
        int closestReach = Reach(start);
        while (queue.Count > 0 && goal is null)
        {
            var current = queue.Dequeue();
            for (int d = 0; d < DirectionExtensions.Count; d++)
            {
                var next = current.Neighbor((Direction)d);
                if (cameFrom.ContainsKey(next) || !PathFinder.InCircle(start, next, _config.CombatPathRadius)
                    || !map.Contains(next) || !map.IsWalkable(next) || _world.OccupantAt(next) is { State: not SettlerState.Stepping } other && other != soldier)
                    continue;
                cameFrom[next] = (current, (Direction)d);
                if (PathFinder.Distance(next, enemy.Position) <= stats.Range)
                {
                    goal = next;
                    break;
                }
                // Breadth first: the first field at a distance is also the nearest to reach.
                int reach = Reach(next);
                if (reach < closestReach)
                {
                    closest = next;
                    closestReach = reach;
                }
                queue.Enqueue(next);
            }
        }

        if (goal is null)
        {
            if (closest == start)
            {
                // Hemmed in for now: wait here, the crowd moves. Give up only if there is no way at all.
                _world.WalkTo(soldier, start);
                return _world.PathFinder!.AreConnected(start, enemy.Position);
            }
            goal = closest;
            f.Enemy = enemies.MinBy(e => PathFinder.Distance(closest, e.Position));
        }

        var steps = new List<Direction>();
        for (var p = goal.Value; p != start; p = cameFrom[p].From)
            steps.Add(cameFrom[p].Step);
        steps.Reverse();
        _world.Follow(soldier, steps, goal.Value);
        return true;
    }

    /// <summary>
    /// Combat strength of the player away from own land (neutral or enemy land): 1 / number of players,
    /// raised by the goods (gold; Egyptians: gems count double) lying on the player's own land, each
    /// further percent dearer (<see cref="CombatStrengthConfig.Bonus"/>); at most 1. Recomputed at most once per tick.
    /// </summary>
    public float CombatStrength(int player)
    {
        if (_strengths.TryGetValue(player, out var cached) && cached.Tick == _world.Tick)
            return cached.Strength;
        float strength = 1;
        if (_world.PlayerCount > 1)
        {
            var goods = _config.CombatStrength.GoodsOf(_world.PeopleOf(player));
            var map = _world.Map!;
            float units = _world.Piles
                .Where(p => p.Count > 0 && p.Material is { } m && goods.ContainsKey(m) && map.OwnerAt(p.Position) == player)
                .Sum(p => p.Count * goods[p.Material!.Value]);
            float start = 1f / _world.PlayerCount;
            strength = start + _config.CombatStrength.Bonus(units, 1 - start);
        }
        _strengths[player] = (_world.Tick, strength);
        return strength;
    }

    /// <summary>The player's combat strength at the point: always 1 on own land.</summary>
    public float CombatStrengthAt(int player, GridPoint at) =>
        _world.Map?.OwnerAt(at) == player ? 1 : CombatStrength(player);

    /// <summary>The soldier's combat strength where it stands.</summary>
    public float CombatStrengthOf(Settler soldier) => CombatStrengthAt(soldier.Player, soldier.Position);

    /// <summary>A strike or shot hurts at once; a bowman's arrow (from <paramref name="tower"/>, if he is in one) is only for the eye.</summary>
    private void Hit(Settler attacker, Settler enemy, Building? tower = null)
    {
        if (attacker.SoldierType == SoldierType.Bowman)
        {
            var from = tower?.Position ?? attacker.Position;
            attacker.LastShotTick = _world.Tick;
            _arrows.Add(new Arrow(attacker, tower, enemy, from, enemy.Position, _world.Tick,
                ArrowTicks(PathFinder.Distance(from, enemy.Position))));
        }
        enemy.Health -= _config.Damage(attacker.SoldierType!.Value, attacker.Level) * CombatStrengthOf(attacker);
        var home = DefendedBy(enemy);
        if (home is not null)
            home.LastThreat = _world.Tick;
        if (enemy.Health <= 0)
        {
            Kill(enemy);
            if (home is { IsEmpty: true } && home.Building.Exists && !attacker.IsInside && !attacker.IsDead)
                GoCapture(home, attacker);
        }
    }

    /// <summary>The garrison whose door the soldier guards, if any.</summary>
    private Garrison? DefendedBy(Settler soldier) => _guards.GetValueOrDefault(soldier.Id);

    /// <summary>The attacker struck the last defender down: it walks into the building to take it.</summary>
    private void GoCapture(Garrison g, Settler attacker)
    {
        g.Conqueror = attacker;
        if (!_fighters.TryGetValue(attacker.Id, out var f))
            f = _fighters[attacker.Id] = new Fighter(attacker);
        f.Target = g.Building;
        f.MoveTo = null;
        f.Enemy = null;
        f.NextReplan = 0;
        attacker.HasTask = true;
        if (attacker.Destination is not null)
            _world.WalkTo(attacker, attacker.StepTarget);
    }

    /// <summary>Whether the soldier may take the empty building: its conqueror, or anyone once that one is gone.</summary>
    private bool MayCapture(Garrison g, Settler soldier) =>
        g.Conqueror is not { } conqueror || conqueror == soldier || conqueror.IsDead
        || !_fighters.TryGetValue(conqueror.Id, out var f) || f.Target != g.Building;

    private void Kill(Settler soldier)
    {
        Release(soldier);
        _world.Remove(soldier);
        Kills++;
    }

    /// <summary>
    /// The soldier drops every order and place it was on its way to (it dies, changes sides or is put
    /// elsewhere by a spell); it stands idle afterwards.
    /// </summary>
    internal void Release(Settler soldier)
    {
        _fighters.Remove(soldier.Id);
        _nextShot.Remove(soldier.Id);
        if (_guards.Remove(soldier.Id, out var guarded))
            guarded.Guard = null;
        foreach (var g in _garrisons.Values)
        {
            foreach (var (place, defender) in g.Defending.ToList())
                if (defender == soldier)
                    g.Defending.Remove(place);
            foreach (var (place, coming) in g.Coming.ToList())
                if (coming.Soldier == soldier)
                    g.Coming.Remove(place);
        }
        SetActivity(soldier, Activity.None);
        soldier.HasTask = false;
        if (!soldier.IsDead && !soldier.IsInside && soldier.Destination is not null)
            _world.WalkTo(soldier, soldier.StepTarget);
    }

    /// <summary>The attacker takes the building: it changes sides with its land.</summary>
    private void Capture(Building building, Settler attacker)
    {
        var g = _garrisons[building.Id];
        foreach (var place in g.Coming.Keys.ToList())
            Release(g, place);
        Unclaim(building);
        building.Player = attacker.Player;
        g.Conqueror = null;
        g.DoorHealth = _config.DoorHealth; // the new owner starts behind a whole door
        _fighters.Remove(attacker.Id);
        SetActivity(attacker, Activity.None);
        int free = Enumerable.Range(0, g.Places.Length)
            .Where(i => attacker.SoldierType is { } type && building.Definition.Occupiers[i].Accepts(type))
            .DefaultIfEmpty(0).First();
        _world.EnterBuilding(attacker);
        g.Places[free] = attacker;
        // The new owner starts with the attacker alone.
        Garrison.Initial(building, attacker.SoldierType).CopyTo(g.Wanted, 0);
        Claim(building);
        Captures++;

        // Buildings on land their player no longer owns go down.
        var map = _world.Map!;
        foreach (var other in _world.Buildings.Where(b => b.Exists && b != building).ToList())
            if (map.OwnerAt(other.Position) is { } owner && owner != other.Player)
                _world.RemoveBuilding(other);
    }

    private void Dismiss(Fighter f)
    {
        _fighters.Remove(f.Soldier.Id);
        SetActivity(f.Soldier, Activity.None);
        f.Soldier.HasTask = false;
    }

    /// <summary>A soldier steps out of its building; a bowman stops shooting from the top.</summary>
    private void LeaveBuilding(Settler soldier)
    {
        _world.LeaveBuilding(soldier);
        SetActivity(soldier, Activity.None);
    }

    private static void SetActivity(Settler soldier, Activity activity)
    {
        if (soldier.Activity == activity)
            return;
        soldier.Activity = activity;
        soldier.ActivityTicks = 0;
        soldier.IsWorking = activity != Activity.None;
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

    private sealed class Fighter(Settler soldier)
    {
        public Settler Soldier { get; } = soldier;

        /// <summary>Attack order: the enemy building to take.</summary>
        public Building? Target { get; set; }

        public Settler? Enemy { get; set; }
        public Dictionary<int, long> Unreachable { get; } = new();
        public long NextHit { get; set; }
        public long NextReplan { get; set; }
        public long NextRetarget { get; set; }

        /// <summary>Move order: where to go when no enemy is around.</summary>
        public GridPoint? MoveTo { get; set; }

        /// <summary>Forced move order: no fighting on the way.</summary>
        public bool Forced { get; init; }

        /// <summary>The <see cref="Target"/> was taken up on its own (seen, not ordered).</summary>
        public bool AutoTarget { get; set; }

        /// <summary>It went for an enemy soldier last tick.</summary>
        public bool Chasing { get; set; }
    }
}

/// <summary>
/// An arrow in flight (for display; its damage was done when it was shot) from a bowman (on <paramref name="Tower"/>, if he shoots from one) at
/// <paramref name="Target"/>, launched at <paramref name="Tick"/> from <paramref name="From"/> towards
/// where the target stood then (<paramref name="To"/>).
/// </summary>
public sealed record Arrow(Settler Shooter, Building? Tower, Settler Target, GridPoint From, GridPoint To, long Tick,
    int FlightTicks);

