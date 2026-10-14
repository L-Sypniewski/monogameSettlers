using Homestead.Simulation.Buildings;
using Homestead.Simulation.Economy;
using Homestead.Simulation.Pathfinding;

namespace Homestead.Simulation.Magic;

public enum UpgradeResult
{
    Done,

    /// <summary>The type has the highest level already.</summary>
    Highest,

    /// <summary>Level 3 only once every type has level 2.</summary>
    OthersFirst,

    /// <summary>Upgrades are made at a finished small temple of the player.</summary>
    NoTemple,

    /// <summary>The player has not earned enough mana in all yet.</summary>
    NoMana,
}

public enum CastResult
{
    /// <summary>The priest casts now or is on the way to get within range.</summary>
    Started,
    NoPriest,
    NoSpell,
    NoMana,

    /// <summary>Nobody is fighting at the target.</summary>
    NoFight,
    Unreachable,
}

/// <summary>
/// Mana, temples, priests and spells (see <c>data/magic.json</c>).
/// </summary>
/// <remarks>
/// A finished big temple sends out a priest of its people at once and then every
/// <see cref="MagicConfig.PriestTicks"/>. Small temples turn the drink delivered to them into mana for
/// their player. A priest ordered to cast walks until the target is within the bowmen's range, then
/// casts (the mana is paid when the casting starts, the spell works when it ends). Spells only work
/// where there is a fight; priests can be attacked like soldiers.
/// Watchdog: a priest that does not get within range for <see cref="StallTicks"/> drops the order.
/// </remarks>
public sealed class Magic
{
    public const int StallTicks = 400;
    private const int DiscoverInterval = 10;

    private readonly World _world;
    private readonly MagicConfig _config;
    private readonly Dictionary<int, int> _mana = new();
    private readonly Dictionary<int, int> _earned = new();
    private readonly Dictionary<(int Player, SoldierType Type), int> _levels = new();
    private readonly Dictionary<int, long> _nextPriest = new();
    private readonly Dictionary<int, long> _nextMana = new();
    private readonly Dictionary<int, CastOrder> _orders = new();
    private readonly List<SpellEffect> _effects = new();
    private readonly Random _random;

    internal Magic(World world, MagicConfig config)
    {
        _world = world;
        _random = new Random(4711 + world.Seed);
        _config = config;
    }

    public MagicConfig Config => _config;

    public int PriestsSent { get; private set; }
    public int SpellsCast { get; private set; }

    /// <summary>Spells that took effect recently (for drawing), newest last.</summary>
    public IReadOnlyList<SpellEffect> Effects => _effects;

    public int Mana(int player) => _mana.GetValueOrDefault(player);

    /// <summary>Adds (or with a negative amount takes) mana (temples, tests).</summary>
    /// <remarks>Mana goes to two accounts: spells spend <see cref="Mana"/>; what is added also counts in <see cref="ManaEarned"/>, which upgrades ask for.</remarks>
    public void AddMana(int player, int amount)
    {
        _mana[player] = Math.Max(0, Mana(player) + amount);
        if (amount > 0)
            _earned[player] = ManaEarned(player) + amount;
    }

    /// <summary>All the mana the player has earned (temples), whatever was spent on spells since.</summary>
    public int ManaEarned(int player) => _earned.GetValueOrDefault(player);

    /// <summary>The level the player's newly trained soldiers of the type get (1 until upgraded).</summary>
    public int SoldierLevel(int player, SoldierType type) => _levels.GetValueOrDefault((player, type), 1);

    /// <summary>How many upgrades the player has made.</summary>
    public int Upgrades(int player) => Enum.GetValues<SoldierType>().Sum(t => SoldierLevel(player, t) - 1);

    /// <summary>Mana earned in all that the player's next upgrade takes; null when there is none left.</summary>
    public int? UpgradeThreshold(int player)
    {
        int done = Upgrades(player);
        var thresholds = _config.UpgradeThresholds;
        if (thresholds.Count == 0 || done >= Enum.GetValues<SoldierType>().Length * (MaxLevel - 1))
            return null;
        // Past the list, each further one takes the last step again.
        int last = thresholds.Count - 1, step = last > 0 ? thresholds[last] - thresholds[last - 1] : thresholds[last];
        return done <= last ? thresholds[done] : thresholds[last] + (done - last) * step;
    }

    /// <summary>Whether the player has earned the mana for its next upgrade.</summary>
    public bool CanAffordUpgrade(int player) => UpgradeThreshold(player) is { } needed && ManaEarned(player) >= needed;

    public const int MaxLevel = 3;

    /// <summary>Whether the type may be upgraded next (level and order only, not temple or mana).</summary>
    public UpgradeResult UpgradeOrder(int player, SoldierType type)
    {
        int level = SoldierLevel(player, type);
        if (level >= MaxLevel || UpgradeThreshold(player) is null)
            return UpgradeResult.Highest;
        // All types to level 2 first, then to level 3.
        if (Enum.GetValues<SoldierType>().Any(t => SoldierLevel(player, t) < level))
            return UpgradeResult.OthersFirst;
        return UpgradeResult.Done;
    }

    /// <summary>
    /// Upgrades the player's soldier type by one level, at a finished small temple of the player, once it
    /// has earned enough mana in all (nothing is spent). Soldiers trained from now on get the level; those
    /// already trained keep theirs.
    /// </summary>
    public UpgradeResult Upgrade(int player, SoldierType type)
    {
        if (UpgradeOrder(player, type) is var order && order != UpgradeResult.Done)
            return order;
        if (!_world.Buildings.Any(b => b.Exists && b.Player == player && b.Definition.Name == _config.Temple && b.State == BuildingState.Finished))
            return UpgradeResult.NoTemple;
        if (!CanAffordUpgrade(player))
            return UpgradeResult.NoMana;
        _levels[(player, type)] = SoldierLevel(player, type) + 1;
        return UpgradeResult.Done;
    }

    /// <summary>The spell of a priest's people.</summary>
    public Spell? SpellOf(Settler priest) =>
        _config.Spells.GetValueOrDefault(priest.People ?? _world.PeopleOf(priest.Player));

    /// <summary>The spell the priests of a player's people know.</summary>
    public Spell? SpellOf(int player) => _config.Spells.GetValueOrDefault(_world.PeopleOf(player));

    /// <summary>How far from a priest a spell can be cast: the bowmen's range.</summary>
    public int Range => _world.Military.Config.Soldiers[SoldierType.Bowman].Range;

    /// <summary>Whether a priest is casting or on the way to.</summary>
    public bool IsBusy(Settler priest) => _orders.ContainsKey(priest.Id);

    /// <summary>
    /// A fight is going on at the point: soldiers of at least two players within the spell radius (fighters
    /// switch between striking and stepping up all the time, so their activity alone would flicker).
    /// </summary>
    public bool IsFight(GridPoint target) => _world.Settlers
        .Where(s => s.Profession == Profession.Soldier && !s.IsInside && !s.IsDead && PathFinder.InCircle(target, s.Position, _config.Radius))
        .Select(s => s.Player).Distinct().Skip(1).Any();

    /// <summary>A short description for the player (null for buildings other than temples).</summary>
    public string? Status(Building building)
    {
        if (building.Definition.Name == _config.BigTemple)
        {
            long seconds = Math.Max(0, _nextPriest.GetValueOrDefault(building.Id) - _world.Tick) / World.TicksPerSecond;
            return $"next priest in {seconds / 60}:{seconds % 60:00}";
        }
        if (building.Definition.Name != _config.Temple)
            return null;
        var drink = building.Piles.FirstOrDefault(p => p.Role == PileRole.Request && p.Material is { } m && _config.Mana.ContainsKey(m));
        return drink is { Count: > 0 } ? $"making mana ({Mana(building.Player)} mana)" : $"waiting for {drink?.Material} ({Mana(building.Player)} mana)";
    }

    /// <summary>
    /// Orders one of the priests (the nearest to the target that is free) to cast its spell at the target.
    /// </summary>
    public CastResult Cast(IEnumerable<Settler> priests, GridPoint target)
    {
        var free = priests.Where(p => p.Profession == Profession.Priest && !p.IsDead && !p.IsInside && !IsBusy(p)).ToList();
        if (free.Count == 0)
            return CastResult.NoPriest;
        var priest = free.MinBy(p => PathFinder.Distance(p.Position, target))!;
        if (SpellOf(priest) is not { } spell)
            return CastResult.NoSpell;
        if (Mana(priest.Player) < spell.Cost)
            return CastResult.NoMana;
        if (!IsFight(target))
            return CastResult.NoFight;
        var order = new CastOrder(priest, target, spell) { LastProgress = _world.Tick, Best = PathFinder.Distance(priest.Position, target) };
        if (!InRange(priest, target) && !WalkTowards(priest, target))
            return CastResult.Unreachable;
        priest.HasTask = true;
        priest.IsStrolling = false;
        _orders[priest.Id] = order;
        return CastResult.Started;
    }

    internal void Tick()
    {
        if (_world.Tick % DiscoverInterval == 0)
            Temples();
        foreach (var order in _orders.Values.ToList())
            Advance(order);
        _effects.RemoveAll(e => _world.Tick - e.Tick > SpellEffect.ShowTicks);
    }

    /// <summary>Big temples send priests, small temples turn drink into mana.</summary>
    private void Temples()
    {
        foreach (var building in _world.Buildings)
        {
            if (!building.Exists || building.State != BuildingState.Finished)
                continue;
            if (building.Definition.Name == _config.BigTemple)
            {
                if (_world.Tick < _nextPriest.GetValueOrDefault(building.Id))
                    continue;
                if (_world.Spawn(building.Door, Profession.Priest, building.Player) is { } priest)
                {
                    priest.People = building.Definition.Civilization;
                    priest.Health = _config.PriestHealth;
                    PriestsSent++;
                }
                _nextPriest[building.Id] = _world.Tick + _config.PriestTicks;
            }
            else if (building.Definition.Name == _config.Temple)
            {
                if (_world.Tick < _nextMana.GetValueOrDefault(building.Id))
                    continue;
                var pile = building.Piles.FirstOrDefault(p => p.Role == PileRole.Request && p.Count > 0 && p.Material is { } m && _config.Mana.ContainsKey(m));
                if (pile is null)
                    continue;
                pile.Count--;
                AddMana(building.Player, _config.Mana[pile.Material!.Value]);
                _nextMana[building.Id] = _world.Tick + _config.TempleTicks;
            }
        }
    }

    private bool InRange(Settler priest, GridPoint target) => PathFinder.Distance(priest.Position, target) <= Range;

    /// <summary>
    /// Sends the priest towards the target: to the target itself, or (water, crowd) to the reachable point
    /// nearest to it within range. The order ends as soon as the target is within range.
    /// </summary>
    private bool WalkTowards(Settler priest, GridPoint target)
    {
        if (_world.WalkTo(priest, target))
            return true;
        var map = _world.Map!;
        for (int r = 1; r <= Range; r++)
        {
            var spot = PathFinder.Circle(target, r)
                .Where(p => PathFinder.Distance(target, p) == r && map.Contains(p) && map.IsWalkable(p) && _world.Reachable(priest, p))
                .OrderBy(p => PathFinder.Distance(priest.Position, p))
                .Cast<GridPoint?>().FirstOrDefault();
            if (spot is { } s && _world.WalkTo(priest, s))
                return true;
        }
        return false;
    }

    private void Advance(CastOrder order)
    {
        var priest = order.Priest;
        if (priest.IsDead || priest.Profession != Profession.Priest)
        {
            _orders.Remove(priest.Id);
            return;
        }
        if (order.CastingUntil is { } until)
        {
            if (_world.Tick < until)
                return;
            Apply(order.Spell, priest.Player, order.Target);
            Finish(order);
            return;
        }
        if (priest.FrozenUntil > _world.Tick)
            return;

        if (!InRange(priest, order.Target))
        {
            int distance = PathFinder.Distance(priest.Position, order.Target);
            if (distance < order.Best)
            {
                order.Best = distance;
                order.LastProgress = _world.Tick;
            }
            if (_world.Tick - order.LastProgress > StallTicks || priest.Destination is null && priest.State != SettlerState.Stepping
                && !WalkTowards(priest, order.Target))
                Finish(order);
            return;
        }
        // Within range: stop after the current step and cast, if there still is a fight and the mana.
        if (priest.Path.Count > 0)
            _world.WalkTo(priest, priest.StepTarget);
        if (priest.State == SettlerState.Stepping)
            return;
        if (Mana(priest.Player) < order.Spell.Cost || !IsFight(order.Target))
        {
            Finish(order);
            return;
        }
        AddMana(priest.Player, -order.Spell.Cost);
        order.CastingUntil = _world.Tick + _config.CastTicks;
        priest.Facing = FacingTowards(priest.Position, order.Target);
        priest.Activity = Activity.Casting;
        priest.ActivityTicks = 0;
        priest.IsWorking = true;
    }

    private void Finish(CastOrder order)
    {
        _orders.Remove(order.Priest.Id);
        order.Priest.HasTask = false;
        order.Priest.IsWorking = false;
        order.Priest.Activity = Activity.None;
    }

    /// <summary>The spell takes effect at the target.</summary>
    internal void Apply(Spell spell, int player, GridPoint target)
    {
        SpellsCast++;
        _effects.Add(new SpellEffect(spell.Kind, target, _config.Radius, _world.Tick));
        var inArea = _world.Settlers
            .Where(s => !s.IsInside && !s.IsDead && PathFinder.InCircle(target, s.Position, _config.Radius))
            .OrderBy(s => PathFinder.Distance(target, s.Position))
            .ToList();
        var enemySoldiers = inArea.Where(s => s.Player != player && s.Profession == Profession.Soldier).ToList();
        switch (spell.Kind)
        {
            case SpellKind.Convert:
                foreach (var soldier in enemySoldiers.Take(spell.Count))
                {
                    _world.Military.Release(soldier);
                    soldier.People ??= _world.PeopleOf(soldier.Player);
                    soldier.Player = player;
                }
                break;
            case SpellKind.Haste:
                foreach (var soldier in inArea.Where(s => s.Player == player && s.Profession == Profession.Soldier))
                {
                    soldier.HastedUntil = _world.Tick + spell.Ticks;
                    soldier.HasteFactor = spell.Factor;
                }
                break;
            case SpellKind.Freeze:
                foreach (var enemy in inArea.Where(s => s.Player != player && s.Profession is Profession.Soldier or Profession.Priest))
                    enemy.FrozenUntil = _world.Tick + spell.Ticks;
                break;
            case SpellKind.Scatter:
                foreach (var soldier in enemySoldiers.Take(spell.Count))
                {
                    _world.Military.Release(soldier);
                    for (int attempt = 0; attempt < 1000; attempt++)
                    {
                        var map = _world.Map!;
                        var p = new GridPoint(_random.Next(map.Size), _random.Next(map.Size));
                        if (_world.Teleport(soldier, p))
                            break;
                    }
                }
                break;
        }
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

    private sealed class CastOrder(Settler priest, GridPoint target, Spell spell)
    {
        public Settler Priest { get; } = priest;
        public GridPoint Target { get; } = target;
        public Spell Spell { get; } = spell;
        public long? CastingUntil { get; set; }
        public long LastProgress { get; set; }
        public int Best { get; set; }
    }
}

/// <summary>A spell that took effect (for drawing).</summary>
public sealed record SpellEffect(SpellKind Kind, GridPoint Target, int Radius, long Tick)
{
    public const int ShowTicks = 2 * World.TicksPerSecond;
}
