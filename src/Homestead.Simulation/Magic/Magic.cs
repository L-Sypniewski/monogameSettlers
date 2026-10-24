using Homestead.Simulation.Buildings;
using Homestead.Simulation.Economy;
using Homestead.Simulation.Map;
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

    /// <summary>Nobody the spell works on is at the target.</summary>
    NoTarget,
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
/// where there is someone they work on (<see cref="HasTargets"/>); priests can be attacked like soldiers.
/// Watchdog: a priest that does not get within range for <see cref="StallTicks"/> drops the order.
/// </remarks>
public sealed partial class Magic
{
    public const int StallTicks = 400;
    private const int DiscoverInterval = 10;

    private readonly World _world;
    private MagicConfig _config;
    private readonly Dictionary<int, int> _mana = new();
    private readonly Dictionary<int, int> _earned = new();
    private readonly Dictionary<(int Player, SoldierType Type), int> _levels = new();
    private readonly Dictionary<int, long> _nextPriest = new();
    private readonly Dictionary<int, long> _nextMana = new();
    private readonly Dictionary<(int Player, SpellKind Kind), int> _casts = new();
    private readonly Dictionary<int, CastOrder> _orders = new();
    private readonly List<SpellEffect> _effects = new();
    private readonly List<SpellSight> _sights = new();
    private readonly Random _random;

    internal Magic(World world, MagicConfig config)
    {
        _world = world;
        _random = new Random(4711 + world.Seed);
        _config = config;
    }

    public MagicConfig Config => _config;

    /// <summary>
    /// A game saved before spells had their own areas, menus and range takes the current rules (keeping its cost increase).
    /// </summary>
    internal void FollowRules()
    {
        if (_config.Range <= 0 || _config.Own.Count == 0)
            _config = MagicConfig.Load() with { CostIncrease = _config.CostIncrease };
    }

    public int PriestsSent { get; private set; }
    public int SpellsCast { get; private set; }

    /// <summary>What players see by a spell (magic eye, the whole map) until its time is over.</summary>
    public IReadOnlyList<SpellSight> Sights => _sights;

    /// <summary>Whether the player sees the point by a spell.</summary>
    public bool SeesBySpell(int player, GridPoint p) =>
        _sights.Any(v => v.Player == player && v.Until > _world.Tick && (v.Radius == 0 || PathFinder.InCircle(v.Center, p, v.Radius)));

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

    /// <summary>The spells a priest knows: those of its people, in menu order.</summary>
    public IReadOnlyList<Spell> SpellsOf(Settler priest) =>
        _config.Spells.GetValueOrDefault(priest.People ?? _world.PeopleOf(priest.Player)) ?? [];

    /// <summary>The spells the priests of a player's people know.</summary>
    public IReadOnlyList<Spell> SpellsOf(int player) => _config.Spells.GetValueOrDefault(_world.PeopleOf(player)) ?? [];

    /// <summary>The spell menu of a player's priests, each spell with what it costs the player now (placeholders without a spell).</summary>
    public IReadOnlyList<MenuSpell> MenuOf(int player) =>
        SpellsOf(player).Select(s => new MenuSpell(s.Kind.ToString(), CostOf(player, s), s.Placeholder ? null : s)).ToArray();

    /// <summary>How often the player has cast spells of the kind.</summary>
    public int Casts(int player, SpellKind kind) => _casts.GetValueOrDefault((player, kind));

    /// <summary>
    /// What the player's next spell of the kind costs, or with <paramref name="ahead"/> the one that many casts after it:
    /// each cast of a kind makes the next <see cref="MagicConfig.CostIncrease"/> percent dearer, rounded up (like the original).
    /// </summary>
    public int CostOf(int player, Spell spell, int ahead = 0)
    {
        int cost = spell.Cost;
        for (int i = Casts(player, spell.Kind) + ahead; i > 0 && _config.CostIncrease > 0; i--)
            cost = (cost * (100 + _config.CostIncrease) + 99) / 100;
        return cost;
    }

    /// <summary>The own spell of a priest's people (the one the AI casts in fights).</summary>
    public Spell? SpellOf(Settler priest) => OwnOf(priest.People ?? _world.PeopleOf(priest.Player), SpellsOf(priest));

    /// <summary>The own spell of a player's people.</summary>
    public Spell? SpellOf(int player) => OwnOf(_world.PeopleOf(player), SpellsOf(player));

    private Spell? OwnOf(Civilization people, IReadOnlyList<Spell> spells) =>
        _config.Own.TryGetValue(people, out var kind) ? spells.FirstOrDefault(s => s.Kind == kind) : spells.FirstOrDefault();

    /// <summary>How far from a priest a spell can be cast (<see cref="MagicConfig.Range"/>).</summary>
    public int Range => _config.Range;

    /// <summary>Whether a priest is casting or on the way to.</summary>
    public bool IsBusy(Settler priest) => _orders.ContainsKey(priest.Id);

    /// <summary>Where a busy priest is going to cast its spell; null for a free one.</summary>
    public GridPoint? TargetOf(Settler priest) => _orders.TryGetValue(priest.Id, out var order) ? order.Target : null;

    /// <summary>
    /// A fight is going on at the point: soldiers of at least two players within <paramref name="radius"/> (fighters
    /// switch between striking and stepping up all the time, so their activity alone would flicker).
    /// </summary>
    public bool IsFight(GridPoint target, int radius) => _world.Settlers
        .Where(s => s.Profession == Profession.Soldier && !s.IsInside && !s.IsDead && PathFinder.InCircle(target, s.Position, radius))
        .Select(s => s.Player).Distinct().Skip(1).Any();

    /// <summary>The units the spell of the player works on, within its area around the point, nearest first.</summary>
    public IEnumerable<Settler> Affected(Spell spell, int player, GridPoint target)
    {
        return _world.Settlers
            .Where(s => !s.IsDead && (spell.Radius == 0 || PathFinder.InCircle(target, s.Position, spell.Radius)) && Affects(spell, player, s))
            .OrderBy(s => PathFinder.Distance(target, s.Position)).ThenBy(s => s.Id);
    }

    /// <summary>
    /// Whether the spell of the player works on the unit. Spells moving or changing units (convert, banish, bows to pioneers,
    /// reinforcements) take priests, soldiers, scouts, geologists and pioneers outside, but no soldier belonging to a
    /// military building (garrison, guard at the door, on the way in); spells on soldiers for a while also take those.
    /// </summary>
    public bool Affects(Spell spell, int player, Settler s)
    {
        var map = _world.Map!;
        bool unit = s.Profession is Profession.Soldier or Profession.Priest or Profession.Scout or Profession.Geologist or Profession.Pioneer;
        bool free = !s.IsInside && !_world.Military.IsTowerBound(s);
        bool soldier = s.Profession == Profession.Soldier && (!s.IsInside || _world.Military.IsGarrisoned(s));
        return spell.Kind switch
        {
            SpellKind.Convert or SpellKind.Scatter => s.Player != player && unit && free,
            SpellKind.Reinforce => s.Player == player && unit && free,
            SpellKind.BowsToPioneers => s.Player != player && s.SoldierType == SoldierType.Bowman && free,
            SpellKind.Freeze => s.Player != player && soldier,
            SpellKind.Fear => s.Player != player && soldier && map.OwnerAt(s.Position) == player,
            SpellKind.Haste => s.Player == player && soldier && s.SoldierType == SoldierType.Swordsman,
            SpellKind.Strengthen or SpellKind.Shield or SpellKind.CursedArrows => s.Player == player && soldier,
            _ => false,
        };
    }

    /// <summary>
    /// Whether the spell of the player would work at the point: on units, someone it works on in its area (see
    /// <see cref="Affects"/>); spells on goods or the land, something to work on there; the others always.
    /// </summary>
    public bool HasTargets(Spell spell, int player, GridPoint target) => spell.Kind switch
    {
        SpellKind.Convert or SpellKind.Scatter or SpellKind.Reinforce or SpellKind.BowsToPioneers or SpellKind.Freeze or SpellKind.Fear
            or SpellKind.Haste or SpellKind.Strengthen or SpellKind.Shield or SpellKind.CursedArrows => Affected(spell, player, target).Any(),
        SpellKind.IronToGold or SpellKind.FishToMeat or SpellKind.StoneToIron or SpellKind.GoldToStone or SpellKind.SendGoods
            or SpellKind.CallGoods => GoodsIn(spell, player, target).Any(),
        _ => HasLandTargets(spell, target),
    };

    /// <summary>
    /// The piles with goods the spell takes, nearest to the target first: goods lying around, in output piles and in
    /// storehouses (nobody coming for them), of the material it changes – CallGoods only on the player's own land.
    /// </summary>
    private IEnumerable<Pile> GoodsIn(Spell spell, int player, GridPoint target)
    {
        var map = _world.Map!;
        return _world.Piles
            .Where(p => p.Available > 0 && p.Material is { } m && (spell.From is null || m == spell.From)
                        && PathFinder.InCircle(target, p.Position, spell.Radius)
                        && (spell.Kind != SpellKind.CallGoods || map.OwnerAt(p.Position) == player))
            .OrderBy(p => PathFinder.Distance(target, p.Position)).ThenBy(p => p.Id)
            .ToList();
    }

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
    /// Orders one of the priests (the nearest to the target that is free and knows a spell of that kind) to
    /// cast a spell at the target: its own version of <paramref name="spell"/>, or its people's own spell.
    /// Spells working anywhere (or on the whole map) are cast where the priest stands.
    /// </summary>
    public CastResult Cast(IEnumerable<Settler> priests, GridPoint target, Spell? spell = null)
    {
        var free = priests.Where(p => p.Profession == Profession.Priest && !p.IsDead && !p.IsInside && !IsBusy(p)
                                      && (spell is null || SpellsOf(p).Any(s => s.Kind == spell.Kind))).ToList();
        if (free.Count == 0)
            return CastResult.NoPriest;
        var priest = free.MinBy(p => PathFinder.Distance(p.Position, target))!;
        spell = spell is null ? SpellOf(priest) : SpellsOf(priest).First(s => s.Kind == spell.Kind);
        if (spell is null || spell.Placeholder)
            return CastResult.NoSpell;
        if (Mana(priest.Player) < CostOf(priest.Player, spell))
            return CastResult.NoMana;
        if (!HasTargets(spell, priest.Player, target))
            return CastResult.NoTarget;
        var order = new CastOrder(priest, target, spell) { LastProgress = _world.Tick, Best = PathFinder.Distance(priest.Position, target) };
        if (!InRange(priest, order) && !WalkTowards(priest, target))
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
        _sights.RemoveAll(v => v.Until <= _world.Tick);
        if (_fires.Count > 0)
            SpreadFire();
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
                if (SpawnPriest(building.Door, building.Player, building.Definition.Civilization) is not null)
                    PriestsSent++;
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

    /// <summary>A new priest of the people (default: the player's) near the point (temples, debugging).</summary>
    public Settler? SpawnPriest(GridPoint near, int player, Civilization? people = null)
    {
        if (_world.Spawn(near, Profession.Priest, player) is not { } priest)
            return null;
        priest.People = people ?? _world.PeopleOf(player);
        priest.Health = _config.PriestHealth;
        return priest;
    }

    private bool InRange(Settler priest, CastOrder order) =>
        order.Spell.Anywhere || !order.Spell.Targeted || PathFinder.Distance(priest.Position, order.Target) <= Range;

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
            // The spell took effect when the casting began; the priest only finishes the gesture.
            if (_world.Tick >= until)
                Finish(order);
            return;
        }
        if (priest.FrozenUntil > _world.Tick)
            return;

        if (!InRange(priest, order))
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
        if (Mana(priest.Player) < CostOf(priest.Player, order.Spell) || !HasTargets(order.Spell, priest.Player, order.Target))
        {
            Finish(order);
            return;
        }
        AddMana(priest.Player, -CostOf(priest.Player, order.Spell));
        _casts[(priest.Player, order.Spell.Kind)] = Casts(priest.Player, order.Spell.Kind) + 1;
        // At once: nobody walks out of the area while the priest raises its hands.
        Apply(order.Spell, priest.Player, order.Target);
        order.CastingUntil = _world.Tick + _config.CastTicks;
        priest.Facing = FacingTowards(priest.Position, order.Target);
        priest.Activity = Activity.Casting;
        priest.ActivityTicks = 0;
        _world.Cue(new SoundCue(SoundCueKind.Spell, priest.Position, priest.Player));
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
        var effect = new SpellEffect(spell.Kind, target, spell.Radius, _world.Tick);
        long until = _world.Tick + spell.Ticks;
        var affected = spell.Count > 0 ? Affected(spell, player, target).Take(spell.Count).ToList() : [];
        switch (spell.Kind)
        {
            case SpellKind.SummonGoods:
                effect = effect with { Points = SummonGoods(spell, target) };
                break;
            case SpellKind.Convert:
                foreach (var unit in affected)
                {
                    Release(unit);
                    unit.People ??= _world.PeopleOf(unit.Player);
                    unit.Player = player;
                }
                break;
            case SpellKind.Scatter:
                foreach (var unit in affected)
                {
                    Release(unit);
                    for (int attempt = 0; attempt < 1000; attempt++)
                        if (_world.Teleport(unit, RandomPoint()))
                            break;
                }
                break;
            case SpellKind.Reinforce:
                var spots = FreePointsAround(CasterAt(spell, player, target)?.Position ?? target).GetEnumerator();
                foreach (var unit in affected)
                {
                    if (!spots.MoveNext())
                        break;
                    Release(unit);
                    _world.Teleport(unit, spots.Current);
                }
                break;
            case SpellKind.BowsToPioneers:
                foreach (var bowman in affected)
                {
                    Release(bowman);
                    _world.Military.Disarm(bowman, Profession.Pioneer);
                }
                break;
            case SpellKind.Freeze:
                foreach (var soldier in affected)
                    soldier.FrozenUntil = until;
                break;
            case SpellKind.Haste:
                foreach (var soldier in affected)
                    soldier.Charm(SpellKind.Haste, until, spell.Factor, spell.Damage, player);
                break;
            case SpellKind.Fear or SpellKind.Strengthen or SpellKind.Shield or SpellKind.CursedArrows:
                foreach (var soldier in affected)
                    soldier.Charm(spell.Kind, until, 1, spell.Factor, player);
                break;
            case SpellKind.IronToGold or SpellKind.FishToMeat or SpellKind.StoneToIron or SpellKind.GoldToStone:
                effect = effect with { Points = MoveGoods(spell, player, target, _ => spell.To!.Value, p => p) };
                break;
            case SpellKind.SendGoods:
                var far = RandomFreePoint();
                effect = effect with { Points = MoveGoods(spell, player, target, m => m, _ => far) };
                break;
            case SpellKind.CallGoods:
                var priest = CasterAt(spell, player, target)?.Position ?? target;
                effect = effect with { Points = MoveGoods(spell, player, target, m => m, _ => priest) };
                break;
            case SpellKind.MagicEye or SpellKind.RevealMap:
                _sights.Add(new SpellSight(player, target, spell.Radius, until));
                break;
            case SpellKind.ShrinkResources or SpellKind.Growth or SpellKind.Grassland or SpellKind.ForestFire or SpellKind.DrainSwamp
                or SpellKind.Desertify or SpellKind.FishFlood or SpellKind.StoneCurse or SpellKind.MeltSnow or SpellKind.GrowForest:
                effect = effect with { Points = ApplyToLand(spell, target) };
                break;
        }
        _effects.Add(effect);
    }

    /// <summary>The priest casting the spell at the target (it takes effect while the order is there).</summary>
    private Settler? CasterAt(Spell spell, int player, GridPoint target) =>
        _orders.Values.FirstOrDefault(o => o.Target == target && o.Spell == spell && o.Priest.Player == player)?.Priest;

    private GridPoint RandomPoint() => new(_random.Next(_world.Map!.Size), _random.Next(_world.Map!.Size));

    /// <summary>A random walkable point of the map without a building or pile on it (or the middle, if none is found).</summary>
    private GridPoint RandomFreePoint()
    {
        var map = _world.Map!;
        for (int attempt = 0; attempt < 1000; attempt++)
            if (RandomPoint() is var p && map.IsWalkable(p) && _world.PileAt(p) is null && _world.BuildingAt(p) is null)
                return p;
        return new GridPoint(map.Size / 2, map.Size / 2);
    }

    /// <summary>
    /// Takes up to <see cref="Spell.Count"/> units out of the piles the spell works on and drops them, as <paramref name="into"/>
    /// makes of their material, near the point <paramref name="to"/> gives for each pile; returns where they went.
    /// </summary>
    private IReadOnlyList<GridPoint> MoveGoods(Spell spell, int player, GridPoint target, Func<Material, Material> into, Func<GridPoint, GridPoint> to)
    {
        int left = spell.Count;
        var moved = new List<(GridPoint At, Material Material, int Count)>();
        foreach (var pile in GoodsIn(spell, player, target))
        {
            if (left <= 0)
                break;
            var material = pile.Material!.Value;
            int taken = _world.TakeUnits(pile, Math.Min(left, pile.Available));
            left -= taken;
            moved.Add((pile.Position, material, taken));
        }
        // Taken out first, dropped then: a pile turned into another material may lie where it was.
        var points = new List<GridPoint>();
        foreach (var (at, material, count) in moved)
        {
            var place = to(at);
            for (int i = 0; i < count; i++)
                _world.DropGoods(place, into(material));
            points.Add(place);
        }
        return points.Distinct().ToList();
    }

    /// <summary>The unit leaves whatever it was doing (a spell moves or changes it).</summary>
    private void Release(Settler unit)
    {
        if (unit.Profession == Profession.Soldier)
            _world.Military.Release(unit);
        else if (unit.Profession == Profession.Priest)
        {
            if (_orders.TryGetValue(unit.Id, out var order))
                Finish(order);
        }
        else
        {
            _world.Geology.Stop(unit);
            _world.Theft.Stop(unit);
            _world.Pioneering.Stop(unit);
        }
        unit.HasTask = false;
        unit.IsWorking = false;
        unit.Activity = Activity.None;
    }

    /// <summary>Free walkable points around the point, nearest first (the point itself excluded).</summary>
    private IEnumerable<GridPoint> FreePointsAround(GridPoint center)
    {
        var map = _world.Map!;
        for (int r = 1; r <= 32; r++)
            foreach (var p in PathFinder.Circle(center, r).Where(p => PathFinder.Distance(center, p) == r).OrderBy(p => p.Y).ThenBy(p => p.X))
                if (map.Contains(p) && map.IsWalkable(p) && !_world.IsOccupied(p))
                    yield return p;
    }

    /// <summary>
    /// Piles of random goods appear on free points around the target, a field apart (nearer ones first,
    /// a little shuffled); returns where.
    /// </summary>
    private IReadOnlyList<GridPoint> SummonGoods(Spell spell, GridPoint target)
    {
        var map = _world.Map!;
        var piles = new List<Material>();
        int count = _random.Next(spell.Piles.Min, spell.Piles.Max + 1);
        for (int i = 0; i < count && spell.Goods.Count > 0; i++)
        {
            var group = spell.Goods[_random.Next(spell.Goods.Count)];
            piles.Add(group[_random.Next(group.Count)]);
        }
        foreach (var (material, chance) in spell.Extra)
            if (_random.NextDouble() < chance)
                piles.Add(material);

        int radius = Math.Max(spell.Radius, SummonSpread);
        var spots = PathFinder.Circle(target, radius)
            .Where(p => map.Contains(p) && PathFinder.InCircle(target, p, radius))
            .OrderBy(p => PathFinder.Distance(target, p) + _random.NextDouble() * 2)
            .ToList();
        var placed = new List<GridPoint>();
        foreach (var material in piles)
        {
            int amount = Math.Min(_random.Next(spell.Amount.Min, spell.Amount.Max + 1), Materials.MaxPileSize);
            foreach (var spot in spots)
            {
                if (placed.Any(p => PathFinder.Distance(p, spot) < 2) || _world.AddLooseGoods(spot, material, amount) is null)
                    continue;
                placed.Add(spot);
                break;
            }
        }
        return placed;
    }

    /// <summary>How far around the target summoned piles may lie (the spell itself has no area).</summary>
    public const int SummonSpread = 6;

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

    /// <summary>SummonGoods: where the piles appeared.</summary>
    public IReadOnlyList<GridPoint> Points { get; init; } = [];
}

/// <summary>The player sees the circle (<paramref name="Radius"/> 0: the whole map) by a spell until the tick.</summary>
public sealed record SpellSight(int Player, GridPoint Center, int Radius, long Until);
