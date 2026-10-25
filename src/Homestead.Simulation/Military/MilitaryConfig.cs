using System.Text.Json;
using Homestead.Simulation.Buildings;
using Homestead.Simulation.Economy;

namespace Homestead.Simulation.Military;

public sealed record SoldierStats(float Health, float Damage, int AttackTicks, int Range, double Speed = 1, int MinRange = 0, double Armor = 0)
{
    /// <summary>
    /// What a hit of <paramref name="damage"/> takes from the soldier: with armour that share less, rounded down, but at least 1
    /// (weak hits, e.g. with little combat strength, would otherwise leave armoured soldiers unhurt for ever).
    /// </summary>
    public float Taken(float damage) => Armor > 0 && damage > 0 ? Math.Max(1, MathF.Floor(damage * (float)(1 - Armor))) : damage;

    /// <summary>How many times its health the soldier stands up to, thanks to its armour (for weighing strengths).</summary>
    public double Toughness => Armor is > 0 and < 1 ? 1 / (1 - Armor) : 1;

    /// <summary>Walking speed as a factor (0 in games saved before there were speeds: as everybody).</summary>
    public double SpeedFactor => Speed > 0 ? Speed : 1;

    /// <summary>Whether an enemy this many fields away can be struck or shot: not farther than the range, not nearer than the minimum.</summary>
    public bool InReach(int distance) => distance <= Range && distance >= MinRange;

    /// <summary>How many fields the soldier is off reach at this distance (0 within it).</summary>
    public int OffReach(int distance) => distance > Range ? distance - Range : Math.Max(0, MinRange - distance);
}

/// <summary>
/// Hand-set fields of a door fight, as offsets from the door: where the guard stands and where the melee
/// attackers fight it (the first <see cref="MilitaryConfig.DoorFighters"/> of them).
/// </summary>
public sealed record DoorPoints(GridPoint Guard, IReadOnlyList<GridPoint> Fighters);

/// <summary>Military balancing, loaded from <c>data/military.json</c>.</summary>
public sealed record MilitaryConfig
{
    /// <summary>Radius of the land an occupied military building claims (by building name).</summary>
    public required IReadOnlyDictionary<string, double> Territory { get; init; }

    /// <summary>Which soldier a weapon makes in the barracks.</summary>
    public required IReadOnlyDictionary<Material, SoldierType> Weapons { get; init; }

    public required IReadOnlyDictionary<SoldierType, SoldierStats> Soldiers { get; init; }

    /// <summary>Health and damage factor per level (index 0 = level 1).</summary>
    public required IReadOnlyList<float> LevelBonus { get; init; }

    /// <summary>Health of a military building's door, which melee attackers must break first.</summary>
    public float DoorHealth { get; init; } = 130;

    /// <summary>A door repairs itself from broken to whole in this many ticks (evenly), once it is calm (<see cref="DoorCalmTicks"/>).</summary>
    public int DoorRegenTicks { get; init; } = 30 * World.TicksPerSecond;

    /// <summary>
    /// With no enemy within <see cref="DoorCalmRadius"/> of the door fight for this many ticks, the guard
    /// goes back in and the door starts its repair.
    /// </summary>
    public int DoorCalmTicks { get; init; } = 5 * World.TicksPerSecond;

    public int DoorCalmRadius { get; init; } = 6;

    /// <summary>How many melee attackers fit at the door of a building at once (by building name; 2 elsewhere).</summary>
    public IReadOnlyDictionary<string, int> DoorFighters { get; init; } = new Dictionary<string, int>();

    /// <summary>Hand-set door fight fields per people and building name; missing ones are found automatically.</summary>
    public IReadOnlyDictionary<(Civilization People, string Building), DoorPoints> DoorPoints { get; init; } =
        new Dictionary<(Civilization, string), DoorPoints>();

    /// <summary>Health of a geologist: soldiers fight geologists of other players like soldiers (they do not strike back).</summary>
    public float GeologistHealth { get; init; } = 40;

    /// <summary>Scouts (see <see cref="ScoutRules"/>); null in games saved before there were scouts.</summary>
    public ScoutRules? Scouts { get; init; } = ScoutRules.Default;

    /// <summary>Pioneers (see <see cref="PioneerRules"/>); null in games saved before there were pioneers.</summary>
    public PioneerRules? Pioneers { get; init; } = PioneerRules.Default;

    /// <summary>Hospital: wounded idle soldiers within this distance of its door are called in.</summary>
    public int HospitalCallRadius { get; init; } = 20;

    /// <summary>Hospital: how long the healer takes to heal a soldier fully.</summary>
    public int HealTicks { get; init; } = 2 * World.TicksPerSecond;

    /// <summary>Hospital: after a heal, the healer waits this long before calling the next soldier.</summary>
    public int HospitalCallPauseTicks { get; init; } = 10 * World.TicksPerSecond;

    /// <summary>Soldiers react to enemies within this distance.</summary>
    public required int SightRange { get; init; }

    /// <summary>Fog of war: how far finished buildings of these names see (by building name).</summary>
    public IReadOnlyDictionary<string, int> BuildingSight { get; init; } = new Dictionary<string, int>();

    /// <summary>Fog of war: occupied military buildings see this many fields beyond their land.</summary>
    public int MilitarySightMargin { get; init; } = 3;

    /// <summary>Fog of war: sight of every other finished building, not military (null in older games: 5).</summary>
    public int? BuildingSightRadius { get; init; }

    /// <summary>Near the enemy, ways are planned locally within this radius around everyone standing.</summary>
    public required int CombatPathRadius { get; init; }

    /// <summary>An attack takes soldiers from own buildings within this distance of the target.</summary>
    public required int AttackRange { get; init; }

    /// <summary>Combat strength away from own land: how much the goods on own land add.</summary>
    public CombatStrengthConfig CombatStrength { get; init; } = CombatStrengthConfig.None;

    /// <summary>War machines and their works (see <see cref="Military.WarMachines"/>).</summary>
    public WarMachineRules WarMachines { get; init; } = WarMachineRules.Default;

    /// <summary>Bowmen in military buildings shoot this much further than on the ground (0 in games saved before: as on the ground).</summary>
    public double TowerRangeFactor { get; init; } = 1;

    /// <summary>How far bowmen in military buildings shoot.</summary>
    public int TowerRange => (int)Math.Round(Soldiers[SoldierType.Bowman].Range * (TowerRangeFactor > 0 ? TowerRangeFactor : 1));

    /// <summary>How far a soldier of the type notices enemies: the sight range, or its shooting range if further.</summary>
    public int Sight(SoldierType type) => Math.Max(SightRange, Soldiers[type].Range);

    public float MaxHealth(SoldierType type, int level) => Soldiers[type].Health * Bonus(level);

    public float Damage(SoldierType type, int level) => Soldiers[type].Damage * Bonus(level);

    private float Bonus(int level) => LevelBonus[Math.Clamp(level, 1, LevelBonus.Count) - 1];

    public static MilitaryConfig Load(string? dataDirectory = null)
    {
        string path = Path.Combine(dataDirectory ?? Buildings.BuildingCatalog.DefaultDataDirectory, "military.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        var soldiers = root.GetProperty("soldiers").EnumerateObject().ToDictionary(
            s => Enum.Parse<SoldierType>(s.Name),
            s => new SoldierStats(s.Value.GetProperty("health").GetSingle(), s.Value.GetProperty("damage").GetSingle(),
                (int)Math.Round(s.Value.GetProperty("attackSeconds").GetDouble() * World.TicksPerSecond), s.Value.GetProperty("range").GetInt32(),
                s.Value.TryGetProperty("speed", out var speed) ? speed.GetDouble() : 1,
                s.Value.TryGetProperty("minRange", out var minRange) ? minRange.GetInt32() : 0,
                s.Value.TryGetProperty("armor", out var armor) ? armor.GetDouble() : 0));
        var door = root.TryGetProperty("door", out var d) ? d : default;
        return new MilitaryConfig
        {
            Territory = root.GetProperty("territory").EnumerateObject().ToDictionary(t => t.Name, t => t.Value.GetDouble()),
            Weapons = root.GetProperty("weapons").EnumerateObject()
                .ToDictionary(w => Materials.Parse(w.Name)!.Value, w => Enum.Parse<SoldierType>(w.Value.GetString()!)),
            Soldiers = soldiers,
            DoorHealth = door.ValueKind == JsonValueKind.Object
                ? soldiers[Enum.Parse<SoldierType>(door.GetProperty("healthOf").GetString()!)].Health
                : soldiers[SoldierType.Pikeman].Health,
            DoorFighters = door.ValueKind == JsonValueKind.Object && door.TryGetProperty("fighters", out var fighters)
                ? fighters.EnumerateObject().ToDictionary(f => f.Name, f => f.Value.GetInt32())
                : new Dictionary<string, int>(),
            DoorPoints = door.ValueKind == JsonValueKind.Object && door.TryGetProperty("points", out var points)
                ? ParseDoorPoints(points)
                : new Dictionary<(Civilization, string), DoorPoints>(),
            DoorCalmTicks = (int)Math.Round((door.ValueKind == JsonValueKind.Object && door.TryGetProperty("calmSeconds", out var calm) ? calm.GetDouble() : 5)
                                            * World.TicksPerSecond),
            DoorCalmRadius = door.ValueKind == JsonValueKind.Object && door.TryGetProperty("calmRadius", out var calmRadius) ? calmRadius.GetInt32() : 6,
            DoorRegenTicks = (int)Math.Round((door.ValueKind == JsonValueKind.Object ? door.GetProperty("regenSeconds").GetDouble() : 30) * World.TicksPerSecond),
            LevelBonus = root.GetProperty("levelBonus").EnumerateArray().Select(v => v.GetSingle()).ToArray(),
            TowerRangeFactor = root.TryGetProperty("towerRangeFactor", out var towerRange) ? towerRange.GetDouble() : 1,
            SightRange = root.GetProperty("sightRange").GetInt32(),
            GeologistHealth = root.TryGetProperty("geologistHealth", out var geologistHealth) ? geologistHealth.GetSingle() : 40,
            Scouts = root.TryGetProperty("scouts", out var scouts)
                ? new ScoutRules(scouts.GetProperty("health").GetSingle(), scouts.GetProperty("revealRadius").GetInt32(), scouts.GetProperty("sight").GetInt32(),
                    scouts.GetProperty("stealRadius").GetInt32(), scouts.GetProperty("loadedSpeed").GetDouble())
                : ScoutRules.Default,
            Pioneers = root.TryGetProperty("pioneers", out var pioneers)
                ? new PioneerRules(pioneers.GetProperty("health").GetSingle(), pioneers.GetProperty("searchRadius").GetInt32(),
                    (int)Math.Round(pioneers.GetProperty("claimSeconds").GetDouble() * World.TicksPerSecond),
                    pioneers.TryGetProperty("waterReach", out var waterReach) ? waterReach.GetInt32() : PioneerRules.Default.WaterReach)
                : PioneerRules.Default,
            HospitalCallRadius = root.TryGetProperty("hospital", out var hospital) ? hospital.GetProperty("callRadius").GetInt32() : 20,
            HealTicks = (int)Math.Round((hospital.ValueKind == JsonValueKind.Object ? hospital.GetProperty("healSeconds").GetDouble() : 2) * World.TicksPerSecond),
            HospitalCallPauseTicks = (int)Math.Round((hospital.ValueKind == JsonValueKind.Object ? hospital.GetProperty("callPauseSeconds").GetDouble() : 10)
                                                     * World.TicksPerSecond),
            BuildingSight = root.TryGetProperty("buildingSight", out var buildingSight)
                ? buildingSight.EnumerateObject().ToDictionary(b => b.Name, b => b.Value.GetInt32())
                : new Dictionary<string, int>(),
            MilitarySightMargin = root.TryGetProperty("militarySightMargin", out var sightMargin) ? sightMargin.GetInt32() : 3,
            BuildingSightRadius = root.TryGetProperty("otherBuildingSight", out var otherSight) ? otherSight.GetInt32() : 5,
            CombatPathRadius = root.GetProperty("combatPathRadius").GetInt32(),
            AttackRange = root.GetProperty("attackRange").GetInt32(),
            CombatStrength = root.TryGetProperty("combatStrength", out var strength) ? CombatStrengthConfig.Parse(strength) : CombatStrengthConfig.None,
            WarMachines = root.TryGetProperty("warMachines", out var machines) ? WarMachineRules.Parse(machines) : WarMachineRules.Default,
        };
    }

    private static Dictionary<(Civilization, string), DoorPoints> ParseDoorPoints(JsonElement points)
    {
        static GridPoint Offset(JsonElement e) => new(e[0].GetInt32(), e[1].GetInt32());
        var result = new Dictionary<(Civilization, string), DoorPoints>();
        foreach (var people in points.EnumerateObject().Where(p => !p.Name.StartsWith('_')))
            foreach (var building in people.Value.EnumerateObject())
                result[(Enum.Parse<Civilization>(people.Name), building.Name)] = new DoorPoints(
                    Offset(building.Value.GetProperty("guard")),
                    building.Value.GetProperty("fighters").EnumerateArray().Select(Offset).ToArray());
        return result;
    }

    /// <summary>The weapon a soldier type is trained with.</summary>
    public Material WeaponOf(SoldierType type) => Weapons.First(w => w.Value == type).Key;
}

/// <summary>
/// Combat strength (Kampfkraft): away from own land it starts at <see cref="StartPercent"/> for the number of players;
/// goods on own land (weighted per people) raise it without limit (2 = soldiers hit twice as hard). Each point of strength within the
/// i-th band of <see cref="StepPercent"/> points (counted from 0 %, not from the start) costs <see cref="UnitsPerPercent"/>[i]
/// units, the last value going on.
/// </summary>
public sealed record CombatStrengthConfig(float StepPercent, IReadOnlyList<float> UnitsPerPercent, IReadOnlyDictionary<Material, float> Goods,
    IReadOnlyDictionary<Civilization, IReadOnlyDictionary<Material, float>> Civilizations)
{
    public static CombatStrengthConfig None { get; } = new(10, [], new Dictionary<Material, float>(), new Dictionary<Civilization, IReadOnlyDictionary<Material, float>>());

    /// <summary>What each unit of the goods counts for a player of the people.</summary>
    public IReadOnlyDictionary<Material, float> GoodsOf(Civilization people) => Civilizations.GetValueOrDefault(people) ?? Goods;

    /// <summary>Start in percent for 1, 2, … players; more players than listed start at the last value.</summary>
    public IReadOnlyList<float> StartPercent { get; init; } = [];

    /// <summary>The strength (1 = 100 %) away from own land without goods (1 / number of players if nothing is listed).</summary>
    public float Start(int players) => StartPercent.Count == 0
        ? 1f / Math.Max(1, players)
        : StartPercent[Math.Clamp(players, 1, StartPercent.Count) - 1] / 100;

    /// <summary>
    /// The strength (1 = 100 %) the units raise <paramref name="start"/> to: each further percent costs what the band of
    /// the strength reached so far asks, so the same goods buy more from a lower start.
    /// </summary>
    public float Raise(float start, float units)
    {
        float percent = start * 100;
        if (UnitsPerPercent.Count == 0 || units <= 0)
            return start;
        while (true)
        {
            int band = (int)(percent / StepPercent);
            float cost = UnitsPerPercent[Math.Min(band, UnitsPerPercent.Count - 1)];
            float next = (band + 1) * StepPercent;
            if (band >= UnitsPerPercent.Count - 1 || units <= (next - percent) * cost)
                return (percent + units / cost) / 100;
            units -= (next - percent) * cost;
            percent = next;
        }
    }

    internal static CombatStrengthConfig Parse(JsonElement element)
    {
        static IReadOnlyDictionary<Material, float> Weights(JsonElement goods) =>
            goods.EnumerateObject().ToDictionary(g => Materials.Parse(g.Name)!.Value, g => g.Value.GetSingle());
        return new CombatStrengthConfig(element.GetProperty("stepPercent").GetSingle(),
            element.GetProperty("unitsPerPercent").EnumerateArray().Select(v => v.GetSingle()).ToArray(),
            Weights(element.GetProperty("goods")),
            element.TryGetProperty("civilizations", out var civilizations)
                ? civilizations.EnumerateObject().ToDictionary(c => Enum.Parse<Civilization>(c.Name), c => Weights(c.Value))
                : new Dictionary<Civilization, IReadOnlyDictionary<Material, float>>())
        {
            StartPercent = element.TryGetProperty("startPercent", out var start) ? start.EnumerateArray().Select(v => v.GetSingle()).ToArray() : [],
        };
    }
}

/// <summary>
/// Scouts: <paramref name="Health"/> when soldiers fight them (they do not strike back); an enemy soldier
/// within <paramref name="RevealRadius"/> sees through the disguise; <paramref name="Sight"/> for the fog of war.
/// Stealing, he takes the nearest good within <paramref name="StealRadius"/> and walks at
/// <paramref name="LoadedSpeed"/> times his speed while he carries it.
/// </summary>
public sealed record ScoutRules(float Health, int RevealRadius, int Sight, int StealRadius, double LoadedSpeed)
{
    public static readonly ScoutRules Default = new(40, 10, 26, 5, 0.5);
}

/// <summary>
/// Pioneers: <paramref name="Health"/> when soldiers fight them (they do not strike back); taking land, the
/// next field is the nearest one within <paramref name="SearchRadius"/>; each takes <paramref name="ClaimTicks"/>.
/// A shore field takes the free water off it up to <paramref name="WaterReach"/> fields out (0 in games saved before: the default).
/// </summary>
public sealed record PioneerRules(float Health, int SearchRadius, int ClaimTicks, int WaterReach)
{
    public static readonly PioneerRules Default = new(40, 10, 2 * World.TicksPerSecond, 6);
}

/// <summary>
/// One people's war machine: built in <paramref name="Works"/> from <paramref name="Cost"/>, one load of ammunition from
/// <paramref name="Ammo"/> fills it up to <paramref name="Shots"/> (0: needs none, the Amazon gong); fires at enemy military
/// buildings up to <paramref name="Range"/> fields away, hitting with <paramref name="HitChance"/>; its shot flies
/// <paramref name="FlightTicks"/>. Melee soldiers destroy it (<paramref name="Health"/>); it drives at <paramref name="Speed"/>.
/// </summary>
public sealed record WarMachineKind(string Works, IReadOnlyDictionary<Material, int> Cost, IReadOnlyDictionary<Material, int> Ammo, int Shots,
    double HitChance, int Range, float Health, double Speed, int FlightTicks);

/// <summary>
/// War machines (<c>warMachines</c> in <c>military.json</c>): every <paramref name="CooldownTicks"/> a shot, the works' worker
/// hammers <paramref name="BuildTicks"/> per unit, a machine reloads within <paramref name="ReloadDistance"/> of its works;
/// a military building falls after <paramref name="BuildingHits"/> hits and mends one every <paramref name="RepairTicks"/> after the last.
/// </summary>
public sealed record WarMachineRules(int CooldownTicks, int BuildTicks, int ReloadDistance, IReadOnlyDictionary<string, int> BuildingHits,
    IReadOnlyDictionary<Civilization, WarMachineKind> Kinds, int RepairTicks = 60 * World.TicksPerSecond)
{
    public static WarMachineRules Default { get; } = new(5 * World.TicksPerSecond, 3 * World.TicksPerSecond, 3, new Dictionary<string, int>(),
        new Dictionary<Civilization, WarMachineKind>());

    /// <summary>Hits the military building takes before it falls (one if not listed).</summary>
    public int HitsOf(string building) => Math.Max(1, BuildingHits.GetValueOrDefault(building, 1));

    internal static WarMachineRules Parse(JsonElement e)
    {
        static int Ticks(double seconds) => (int)Math.Round(seconds * World.TicksPerSecond);
        static IReadOnlyDictionary<Material, int> Goods(JsonElement goods) =>
            goods.EnumerateObject().ToDictionary(g => Materials.Parse(g.Name)!.Value, g => g.Value.GetInt32());
        return new WarMachineRules(Ticks(e.GetProperty("cooldownSeconds").GetDouble()), Ticks(e.GetProperty("buildSeconds").GetDouble()),
            e.GetProperty("reloadDistance").GetInt32(),
            e.GetProperty("buildingHits").EnumerateObject().ToDictionary(b => b.Name, b => b.Value.GetInt32()),
            e.GetProperty("kinds").EnumerateObject().ToDictionary(k => Enum.Parse<Civilization>(k.Name), k => new WarMachineKind(
                k.Value.GetProperty("works").GetString()!, Goods(k.Value.GetProperty("cost")), Goods(k.Value.GetProperty("ammo")),
                k.Value.GetProperty("shots").GetInt32(), k.Value.GetProperty("hitChance").GetDouble(), k.Value.GetProperty("range").GetInt32(),
                k.Value.GetProperty("health").GetSingle(), k.Value.GetProperty("speed").GetDouble(), Ticks(k.Value.GetProperty("flightSeconds").GetDouble()))),
            e.TryGetProperty("repairSeconds", out var repair) ? Ticks(repair.GetDouble()) : 60 * World.TicksPerSecond);
    }
}
