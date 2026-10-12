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
    private const int RetargetTicks = 10;

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

    /// <summary>
    /// Orders soldiers (of one player, outside buildings) to a point; they spread over free fields around
    /// it. Enemies in sight on the way are fought first, then they carry on.
    /// </summary>
    public void Move(IEnumerable<Settler> soldiers, GridPoint destination)
    {
        var list = Commandable(soldiers);
        var spots = FreeSpotsAround(destination, list.Count);
        for (int i = 0; i < list.Count; i++)
        {
            var soldier = list[i];
            _fighters[soldier.Id] = new Fighter(soldier) { MoveTo = spots.Count > i ? spots[i] : destination };
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
        var list = soldiers.Where(s => s.Profession == Profession.Soldier && !s.IsDead && !s.IsInside).Distinct().ToList();
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

        ChooseEnemy(f, stats);

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

    /// <summary>
    /// Picks the enemy to fight. An enemy already within reach (the neighboring field for melee) is
    /// attacked at once; every <see cref="RetargetTicks"/> a clearly nearer enemy replaces the current
    /// one, so a soldier does not wait behind someone who stepped into its way.
    /// </summary>
    private void ChooseEnemy(Fighter f, SoldierStats stats)
    {
        var soldier = f.Soldier;
        if (f.Enemy is { } current && (current.IsDead || current.IsInside || PathFinder.Distance(soldier.Position, current.Position) > _config.SightRange + 4))
            f.Enemy = null;

        int currentDistance = f.Enemy is { } e ? PathFinder.Distance(soldier.Position, e.Position) : int.MaxValue;
        if (currentDistance <= stats.Range)
            return;
        bool recheck = f.Enemy is null || _world.Tick >= f.NextRetarget;
        var candidates = EnemiesOf(soldier.Player)
            .Select(x => (Enemy: x, Distance: PathFinder.Distance(soldier.Position, x.Position)))
            .Where(x => x.Distance <= (recheck ? _config.SightRange : stats.Range)
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
        public long NextRetarget { get; set; }

        /// <summary>Move order: where to go when no enemy is around.</summary>
        public GridPoint? MoveTo { get; init; }
    }
}
