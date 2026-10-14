using System.Text.Json;
using System.Text.Json.Nodes;
using Homestead.Simulation.Buildings;
using Homestead.Simulation.Map;

namespace Homestead.Simulation.Ai;

/// <summary>
/// AI balancing, loaded from <c>data/ai.json</c>. Its <c>civilizations</c> section may override any value
/// per people (objects are merged, other values replaced).
/// </summary>
public sealed record AiConfig
{
    public required int ThinkTicks { get; init; }
    public required MilitaryRules Military { get; init; }

    /// <summary>The AI's soldiers outside buildings (see <c>military</c> in ai.json).</summary>
    /// <param name="RetreatRatio">A running strike is called off when the strikers fall below this times the enemy's strength.</param>
    public sealed record MilitaryRules(int ThinkTicks, int GroupRadius, int StrikeRange, double StrikeRatio, int ThreatRange, int GuardDistance,
        double RetreatRatio = 1.0);

    public required AttackRules Attack { get; init; }

    /// <summary>Priests (see <c>magic</c> in ai.json).</summary>
    public MagicRules Magic { get; init; } = new(6, 3, DefaultUpgradeOrder);

    private static readonly SoldierType[] DefaultUpgradeOrder = [SoldierType.Swordsman, SoldierType.Bowman, SoldierType.Pikeman];

    /// <param name="PriestDistance">How far behind the soldiers (towards home) priests stay.</param>
    /// <param name="MinTargets">A spell is cast where it hits at least this many soldiers.</param>
    /// <param name="UpgradeOrder">Soldier types in the order they are upgraded (to level 2, then the same to level 3).</param>
    public sealed record MagicRules(int PriestDistance, int MinTargets, IReadOnlyList<SoldierType> UpgradeOrder);

    /// <summary>Campaigns against enemy military buildings (see <c>attack</c> in ai.json).</summary>
    /// <param name="ArmyRatio">A campaign needs this times the target owner's whole army (0: any army).</param>
    /// <param name="Commit">Share of the spare soldiers left after the force is chosen that come along as well (1: all of them).</param>
    /// <param name="AbortRatio">After gathering the campaign is given up below this times the defence (0: never).</param>
    public sealed record AttackRules(double Ratio, int DefenseRadius, int PathClearance, int GatherTicks, int MinSoldiers,
        double ArmyRatio, double Commit = 0, double AbortRatio = 0);
    public required int MaxSites { get; init; }
    public required int MaxTowerSites { get; init; }
    public required int FreeCarriers { get; init; }
    public required string House { get; init; }

    /// <summary>A living house is wanted while there are no more carriers than this many per production building.</summary>
    public double CarriersPerProductionBuilding { get; init; } = 2;

    /// <summary>
    /// Living houses from big to small, each with the sawmills and stonecutters (finished) it takes:
    /// the first the player has enough of is built, otherwise <see cref="House"/>.
    /// </summary>
    public IReadOnlyList<HouseSize> HouseSizes { get; init; } = [];

    public sealed record HouseSize(string Building, int Sawmills, int Stonecutters);

    /// <summary>A living house is wanted once fewer carriers than this may still become specialists (carrier reserve).</summary>
    public int HouseWhenConvertible { get; init; } = 4;

    /// <summary>A living house is torn down when the living space left over without it is still at least this much.</summary>
    public int SpareRoom { get; init; } = 15;
    public required int StockWhenWaiting { get; init; }
    public required IReadOnlyList<(string Building, int Count)> BuildOrder { get; init; }
    public required string ExpansionBuilding { get; init; }
    public required int MinStoneUnits { get; init; }
    public required int WantedStoneUnits { get; init; }
    public required int MinFreeLand { get; init; }
    public required int MinTrees { get; init; }
    public required IReadOnlyList<ResourceType> Ores { get; init; }

    /// <summary>
    /// Expanded towards only late: once every build order entry before the ore's mine is reached, after
    /// all other needs but plain land (gold for the combat strength).
    /// </summary>
    public IReadOnlyList<ResourceType> LateOres { get; init; } = [];
    public required int SpareSoldiersForLand { get; init; }
    public required int LandStock { get; init; }

    /// <summary>
    /// Largest share of water among the unclaimed points a new tower would take. Spots above it are passed
    /// over; for a needed resource the spot with the least water is taken if none is below.
    /// </summary>
    public double MaxWaterShare { get; init; } = 0.4;

    /// <summary>
    /// Characters by id (see <c>characters</c> in ai.json and docs/KI-TACTICS.md): the traits each plays
    /// with, in order of priority, with their settings (<c>traits</c> defaults merged with the character's own).
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<TraitSettings>> Characters { get; init; } =
        new Dictionary<string, IReadOnlyList<TraitSettings>> { [StandardCharacter] = [] };

    /// <summary>Which character a player of this people gets, drawn by weight once per game.</summary>
    public IReadOnlyList<(string Character, double Weight)> CharacterWeights { get; init; } = [(StandardCharacter, 1)];

    public const string StandardCharacter = "standard";

    /// <param name="Trait">The trait's id.</param>
    public sealed record TraitSettings(string Trait, JsonElement Settings);

    /// <param name="character">
    /// A character whose own values (all but <c>traits</c> and the trait settings) override the general
    /// ones, after the people's.
    /// </param>
    public static AiConfig Load(string? dataDirectory = null, Civilization? civilization = null, string? character = null)
    {
        string path = Path.Combine(dataDirectory ?? BuildingCatalog.DefaultDataDirectory, "ai.json");
        var node = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        if (civilization is { } people && node["civilizations"]?[people.ToString()] is JsonObject overrides)
            Merge(node, overrides);
        if (character is not null && node["characters"] is JsonObject known && known[character] is null && Variant(known, character) is { } variant)
            known[character] = variant;
        if (character is not null && node["characters"]?[character] is JsonObject own)
        {
            var traits = (own["traits"]?.AsArray() ?? []).Select(t => t!.GetValue<string>()).ToHashSet();
            var values = new JsonObject();
            foreach (var (key, value) in own)
                if (key != "traits" && !traits.Contains(key))
                    values[key] = value?.DeepClone();
            Merge(node, values);
        }
        MoveInBuildOrder(node);
        using var document = JsonDocument.Parse(node.ToJsonString());
        var root = document.RootElement;
        var characters = LoadCharacters(node);
        var expansion = root.GetProperty("expansion");
        var military = root.GetProperty("military");
        var attack = root.GetProperty("attack");
        return new AiConfig
        {
            ThinkTicks = Math.Max(1, (int)Math.Round(root.GetProperty("thinkSeconds").GetDouble() * World.TicksPerSecond)),
            MaxSites = root.GetProperty("maxSites").GetInt32(),
            Attack = new AttackRules(
                attack.GetProperty("ratio").GetDouble(),
                attack.GetProperty("defenseRadius").GetInt32(),
                attack.GetProperty("pathClearance").GetInt32(),
                (int)Math.Round(attack.GetProperty("gatherSeconds").GetDouble() * World.TicksPerSecond),
                attack.GetProperty("minSoldiers").GetInt32(),
                attack.TryGetProperty("armyRatio", out var armyRatio) ? armyRatio.GetDouble() : attack.GetProperty("ratio").GetDouble(),
                attack.TryGetProperty("commit", out var commit) ? commit.GetDouble() : 0,
                attack.TryGetProperty("abortRatio", out var abortRatio) ? abortRatio.GetDouble() : attack.GetProperty("ratio").GetDouble()),
            Military = new MilitaryRules(
                (int)Math.Round(military.GetProperty("thinkSeconds").GetDouble() * World.TicksPerSecond),
                military.GetProperty("groupRadius").GetInt32(),
                military.GetProperty("strikeRange").GetInt32(),
                military.GetProperty("strikeRatio").GetDouble(),
                military.GetProperty("threatRange").GetInt32(),
                military.GetProperty("guardDistance").GetInt32(),
                military.TryGetProperty("retreatRatio", out var retreat) ? retreat.GetDouble() : 1.0),
            Magic = root.TryGetProperty("magic", out var magic)
                ? new MagicRules(magic.GetProperty("priestDistance").GetInt32(), magic.GetProperty("minTargets").GetInt32(),
                    magic.TryGetProperty("upgradeOrder", out var order)
                        ? order.EnumerateArray().Select(t => Enum.Parse<SoldierType>(t.GetString()!)).ToArray()
                        : DefaultUpgradeOrder)
                : new MagicRules(6, 3, DefaultUpgradeOrder),
            MaxTowerSites = root.GetProperty("maxTowerSites").GetInt32(),
            FreeCarriers = root.GetProperty("freeCarriers").GetInt32(),
            House = root.GetProperty("house").GetString()!,
            HouseWhenConvertible = root.TryGetProperty("houseWhenConvertible", out var convertible) ? convertible.GetInt32() : 4,
            SpareRoom = root.TryGetProperty("spareRoom", out var spare) ? spare.GetInt32() : 15,
            CarriersPerProductionBuilding = root.TryGetProperty("carriersPerProductionBuilding", out var perBuilding) ? perBuilding.GetDouble() : 2,
            HouseSizes = root.TryGetProperty("houseSizes", out var sizes)
                ? sizes.EnumerateArray().Select(s => new HouseSize(s.GetProperty("building").GetString()!,
                    s.GetProperty("sawmills").GetInt32(), s.GetProperty("stonecutters").GetInt32())).ToArray()
                : [],
            StockWhenWaiting = root.GetProperty("stockWhenWaiting").GetInt32(),
            BuildOrder = root.GetProperty("buildOrder").EnumerateArray()
                .Select(e => (e[0].GetString()!, e[1].GetInt32())).ToArray(),
            ExpansionBuilding = expansion.GetProperty("building").GetString()!,
            MinStoneUnits = expansion.GetProperty("minStoneUnits").GetInt32(),
            WantedStoneUnits = expansion.GetProperty("wantedStoneUnits").GetInt32(),
            MinFreeLand = expansion.GetProperty("minFreeLand").GetInt32(),
            MinTrees = expansion.GetProperty("minTrees").GetInt32(),
            SpareSoldiersForLand = expansion.GetProperty("spareSoldiersForLand").GetInt32(),
            LandStock = expansion.GetProperty("landStock").GetInt32(),
            MaxWaterShare = expansion.TryGetProperty("maxWaterShare", out var waterShare) ? waterShare.GetDouble() : 0.4,
            Ores = expansion.GetProperty("ores").EnumerateArray().Select(o => Enum.Parse<ResourceType>(o.GetString()!)).ToArray(),
            LateOres = expansion.TryGetProperty("lateOres", out var late)
                ? late.EnumerateArray().Select(o => Enum.Parse<ResourceType>(o.GetString()!)).ToArray()
                : [],
            Characters = characters,
            CharacterWeights = root.TryGetProperty("characterWeights", out var weights)
                ? weights.EnumerateArray().Select(w => (w[0].GetString()!, w[1].GetDouble())).ToArray()
                : [(StandardCharacter, 1)],
        };
    }

    /// <summary>The characters with each trait's settings: the trait defaults overridden by the character's values.</summary>
    private static Dictionary<string, IReadOnlyList<TraitSettings>> LoadCharacters(JsonObject root)
    {
        var result = new Dictionary<string, IReadOnlyList<TraitSettings>> { [StandardCharacter] = [] };
        if (root["characters"] is not JsonObject characters)
            return result;
        var defaults = root["traits"] as JsonObject;
        foreach (var (name, value) in characters)
        {
            if (value is not JsonObject character)
                continue;
            var traits = new List<TraitSettings>();
            foreach (var trait in character["traits"]?.AsArray() ?? [])
            {
                string traitId = trait!.GetValue<string>();
                var settings = defaults?[traitId] is JsonObject d ? (JsonObject)d.DeepClone() : new JsonObject();
                if (character[traitId] is JsonObject own)
                    Merge(settings, own);
                using var document = JsonDocument.Parse(settings.ToJsonString());
                traits.Add(new TraitSettings(traitId, document.RootElement.Clone()));
            }
            result[name] = traits;
        }
        return result;
    }

    /// <summary>
    /// A character made up from its id (for trying out combinations, e.g. in the arena):
    /// <c>pincer+guardByThreat</c> is a character of just those traits with their default settings;
    /// <c>@path=value</c> parts set values on top of a character or such traits, e.g.
    /// <c>general@attack.ratio=1.3@pincer.feintShare=0.5@buildOrderMoves.barrack=-5</c>.
    /// Null if the id names neither a character nor traits.
    /// </summary>
    private static JsonObject? Variant(JsonObject characters, string id)
    {
        var parts = id.Split('@');
        JsonObject result;
        if (characters[parts[0]] is JsonObject known)
            result = (JsonObject)known.DeepClone();
        else if (parts[0].Split('+') is var traits && traits.All(t => AiTrait.Known.Contains(t)))
            result = new JsonObject { ["traits"] = new JsonArray(traits.Select(t => (JsonNode)JsonValue.Create(t)!).ToArray()) };
        else if (parts[0].Length == 0 && parts.Length > 1)
            result = (JsonObject)(characters[StandardCharacter]?.DeepClone() ?? new JsonObject { ["traits"] = new JsonArray() });
        else
            return null;
        foreach (string setting in parts.Skip(1))
        {
            int equals = setting.IndexOf('=');
            if (equals <= 0)
                throw new ArgumentException($"AI character '{id}': '{setting}' is no path=value");
            var path = setting[..equals].Split('.');
            string text = setting[(equals + 1)..];
            JsonNode? value;
            try
            {
                value = JsonNode.Parse(text);
            }
            catch (JsonException)
            {
                value = JsonValue.Create(text);
            }
            var at = result;
            foreach (string key in path[..^1])
                at = at[key] as JsonObject ?? (JsonObject)(at[key] = new JsonObject());
            at[path[^1]] = value;
        }
        return result;
    }

    /// <summary>
    /// <c>buildOrderMoves</c> (building: places, negative = earlier): every build order entry of that
    /// building moves that many places, so a character can shift the people's order without its own copy.
    /// </summary>
    private static void MoveInBuildOrder(JsonObject root)
    {
        if (root["buildOrderMoves"] is not JsonObject moves || moves.Count == 0 || root["buildOrder"] is not JsonArray order)
            return;
        var entries = order.Select((entry, index) =>
        {
            int shift = moves[entry![0]!.GetValue<string>()]?.GetValue<int>() ?? 0;
            return (Entry: entry, Key: index + shift + Math.Sign(shift) * 0.5);
        }).OrderBy(e => e.Key).Select(e => e.Entry!.DeepClone()).ToArray();
        root["buildOrder"] = new JsonArray(entries);
    }

    /// <summary>Copies the values of <paramref name="overrides"/> into <paramref name="target"/>, merging objects.</summary>
    private static void Merge(JsonObject target, JsonObject overrides)
    {
        foreach (var (key, value) in overrides)
        {
            if (value is JsonObject inner && target[key] is JsonObject existing)
                Merge(existing, inner);
            else
                target[key] = value?.DeepClone();
        }
    }
}
