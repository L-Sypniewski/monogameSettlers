using Homestead.Simulation.Buildings;
using Homestead.Simulation.Pathfinding;

namespace Homestead.Simulation.Military;

/// <summary>
/// Combat: attacks on enemy military buildings, fights between soldiers, defenders, bowmen on towers
/// and the capture of buildings.
/// </summary>
/// <remarks>
/// A fighter looks for the nearest enemy soldier outside the buildings within sight. Melee soldiers
/// walk up to it and strike from the neighboring field, bowmen shoot from up to their range; one hit
/// every attack interval. Without an enemy an attacker walks to its target's door. When attackers
/// stand near the door, the building sends out one soldier at a time to fight them; bowmen in the
/// building shoot at enemies in range. An attacker at the door of a building with nobody left
/// inside or out defending takes it: the building, its soldier places and its land change sides,
/// and buildings standing on land their player lost are destroyed. Idle soldiers fight enemies
/// that come within sight, defenders go back into their place afterwards.
/// </remarks>
public sealed partial class Military
{
    private const int EngageInterval = 10;
    private const int ReplanTicks = 20;
    private const int DoorDefenseDistance = 3;

    private readonly Dictionary<int, Fighter> _fighters = new();
    private readonly Dictionary<int, long> _nextShot = new();
    private const int UnreachableTicks = 200;

    public int Kills { get; private set; }

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
                _world.LeaveBuilding(soldier);
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

    private void TickCombat()
    {
        if (_world.Tick % EngageInterval == 0)
        {
            EngageIdleSoldiers();
            SendOutDefenders();
        }
        ShootFromBuildings();
        foreach (var fighter in _fighters.Values.ToList())
            Advance(fighter);
    }

    private IEnumerable<Settler> EnemiesOf(int player) =>
        _world.Settlers.Where(s => s.Profession == Profession.Soldier && s.Player != player && !s.IsInside && !s.IsDead);

    private Settler? NearestEnemy(GridPoint from, int player, int range) =>
        EnemiesOf(player).Where(e => PathFinder.Distance(from, e.Position) <= range)
            .MinBy(e => PathFinder.Distance(from, e.Position));

    /// <summary>Soldiers standing around fight enemies that come within sight.</summary>
    private void EngageIdleSoldiers()
    {
        foreach (var soldier in _world.Settlers.Where(s => s.Profession == Profession.Soldier && !s.HasTask && !s.IsInside).ToList())
            if (NearestEnemy(soldier.Position, soldier.Player, _config.SightRange) is not null)
            {
                soldier.HasTask = true;
                _fighters[soldier.Id] = new Fighter(soldier);
            }
    }

    /// <summary>With enemies at the door, an occupied building sends out one soldier at a time.</summary>
    private void SendOutDefenders()
    {
        foreach (var g in _garrisons.Values)
        {
            if (!g.Building.Exists || g.Defending.Count > 0)
                continue;
            if (NearestEnemy(g.Building.Door, g.Building.Player, DoorDefenseDistance) is null)
                continue;
            // Infantry first; bowmen keep shooting from the building.
            int place = Enumerable.Range(0, g.Places.Length)
                .Where(i => g.Places[i] is not null)
                .OrderBy(i => g.Places[i]!.SoldierType == SoldierType.Bowman ? 1 : 0)
                .DefaultIfEmpty(-1).First();
            if (place < 0)
                continue;
            var defender = g.Places[place]!;
            g.Places[place] = null;
            g.Defending[place] = defender;
            _world.LeaveBuilding(defender);
            _fighters[defender.Id] = new Fighter(defender) { Home = g.Building, HomePlace = place };
        }
    }

    /// <summary>Bowmen in military buildings shoot at enemies within range of the building.</summary>
    private void ShootFromBuildings()
    {
        foreach (var g in _garrisons.Values)
        {
            if (!g.Building.Exists)
                continue;
            foreach (var bowman in g.Places)
            {
                if (bowman is not { SoldierType: SoldierType.Bowman } || _nextShot.GetValueOrDefault(bowman.Id) > _world.Tick)
                    continue;
                var stats = _config.Soldiers[SoldierType.Bowman];
                if (NearestEnemy(g.Building.Position, bowman.Player, stats.Range) is not { } enemy)
                    continue;
                Hit(bowman, enemy);
                _nextShot[bowman.Id] = _world.Tick + stats.AttackTicks;
            }
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

        // Keep the current enemy while it is in reach, otherwise the nearest reachable one in sight.
        if (f.Enemy is { } current && (current.IsDead || current.IsInside || PathFinder.Distance(soldier.Position, current.Position) > _config.SightRange + 4))
            f.Enemy = null;
        f.Enemy ??= EnemiesOf(soldier.Player)
            .Where(e => PathFinder.Distance(soldier.Position, e.Position) <= _config.SightRange
                        && !(f.Unreachable.TryGetValue(e.Id, out long until) && until > _world.Tick))
            .MinBy(e => PathFinder.Distance(soldier.Position, e.Position));

        if (f.Enemy is { } enemy)
        {
            int distance = PathFinder.Distance(soldier.Position, enemy.Position);
            if (distance <= stats.Range)
            {
                if (soldier.State == SettlerState.Stepping)
                    return;
                if (soldier.Destination is not null)
                    _world.WalkTo(soldier, soldier.Position); // stop here
                soldier.Facing = FacingTowards(soldier.Position, enemy.Position);
                SetActivity(soldier, stats.Range > 1 ? Activity.Shooting : Activity.Fighting);
                if (_world.Tick >= f.NextHit)
                {
                    Hit(soldier, enemy);
                    f.NextHit = _world.Tick + stats.AttackTicks;
                }
                return;
            }
            SetActivity(soldier, Activity.None);
            if (soldier.Destination is null || _world.Tick >= f.NextReplan)
            {
                if (!_world.WalkTo(soldier, enemy.Position))
                {
                    // No way there (walled in, other side of the water): leave it for a while.
                    f.Unreachable[enemy.Id] = _world.Tick + UnreachableTicks;
                    f.Enemy = null;
                }
                f.NextReplan = _world.Tick + ReplanTicks;
            }
            return;
        }

        SetActivity(soldier, Activity.None);
        if (f.Target is { } target)
        {
            if (!target.Exists || target.Player == soldier.Player)
            {
                Dismiss(f);
                return;
            }
            if (soldier.Position == target.Door && soldier.State != SettlerState.Stepping)
            {
                if (_garrisons.TryGetValue(target.Id, out var g) && g.Places.All(p => p is null) && g.Defending.Count == 0)
                    Capture(target, soldier);
                return; // otherwise a defender comes out
            }
            if (soldier.Destination is null || _world.Tick >= f.NextReplan + 10 * ReplanTicks)
            {
                if (!_world.WalkTo(soldier, target.Door))
                    Dismiss(f);
                f.NextReplan = _world.Tick;
            }
            return;
        }
        if (f.Home is { } home && _garrisons.TryGetValue(home.Id, out var homeGarrison) && homeGarrison.Defending.Remove(f.HomePlace))
        {
            // Back into the place it came from.
            _fighters.Remove(soldier.Id);
            homeGarrison.Coming[f.HomePlace] = (soldier, new WalkWatch(_world.Tick));
            _world.WalkTo(soldier, home.Door);
            return;
        }
        Dismiss(f);
    }

    private void Hit(Settler attacker, Settler enemy)
    {
        enemy.Health -= _config.Damage(attacker.SoldierType!.Value, attacker.Level);
        if (enemy.Health <= 0)
            Kill(enemy);
    }

    private void Kill(Settler soldier)
    {
        _fighters.Remove(soldier.Id);
        _nextShot.Remove(soldier.Id);
        foreach (var g in _garrisons.Values)
        {
            foreach (var (place, defender) in g.Defending.ToList())
                if (defender == soldier)
                    g.Defending.Remove(place);
            foreach (var (place, coming) in g.Coming.ToList())
                if (coming.Soldier == soldier)
                    g.Coming.Remove(place);
        }
        _world.Remove(soldier);
        Kills++;
    }

    /// <summary>The attacker takes the building: it changes sides with its land.</summary>
    private void Capture(Building building, Settler attacker)
    {
        var g = _garrisons[building.Id];
        foreach (var place in g.Coming.Keys.ToList())
            Release(g, place);
        Unclaim(building);
        building.Player = attacker.Player;
        _fighters.Remove(attacker.Id);
        SetActivity(attacker, Activity.None);
        int free = Enumerable.Range(0, g.Places.Length)
            .Where(i => attacker.SoldierType is { } type && building.Definition.Occupiers[i].Accepts(type))
            .DefaultIfEmpty(0).First();
        _world.EnterBuilding(attacker);
        g.Places[free] = attacker;
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
        public Building? Target { get; init; }

        /// <summary>Defender: the building and place it came out of.</summary>
        public Building? Home { get; init; }
        public int HomePlace { get; init; }

        public Settler? Enemy { get; set; }
        public Dictionary<int, long> Unreachable { get; } = new();
        public long NextHit { get; set; }
        public long NextReplan { get; set; }
    }
}
