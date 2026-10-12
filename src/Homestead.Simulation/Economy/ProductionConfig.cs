using System.Text.Json;
using Homestead.Simulation.Map;

namespace Homestead.Simulation.Economy;

public enum GatherAction
{
    CutTree,
    CutStone,
    PlantTree,
    Fish,
    DrawWater,

    /// <summary>Harvest a ripe field of the gatherer's crop (grain, vines, rice, beehives), otherwise sow a new one.</summary>
    Farm,
}

/// <summary>
/// Something a farm-like gatherer sows and harvests: object codes <see cref="First"/> (just sown) up to
/// <see cref="Ripe"/>, growing in <see cref="GrowTicks"/> on the given <see cref="Ground"/>. A crop that
/// <see cref="Regrows"/> starts over after the harvest (vines, beehives) instead of leaving free ground.
/// With <see cref="SlopeBonus"/> it grows faster on slopes: up to (1 + bonus) times as fast where the
/// height to a neighbor differs by <see cref="SlopeHeight"/> or more.
/// </summary>
public sealed record CropRecipe(byte First, byte Ripe, int GrowTicks, IReadOnlySet<TerrainType> Ground, bool Regrows = false,
    double SlopeBonus = 0, int SlopeHeight = 4)
{
    public bool Contains(byte code) => code >= First && code <= Ripe;

    public static readonly CropRecipe Grain = new(MapObjectCodes.FirstGrain, MapObjectCodes.RipeGrain, 100 * World.TicksPerSecond,
        new HashSet<TerrainType> { TerrainType.Grass });
}

public sealed record WorkshopRecipe(IReadOnlyList<Material> Inputs, IReadOnlyList<Material> Outputs, int Ticks);

/// <param name="PickupTicks">Pause between finishing the work and picking up the result (the woodcutter after felling).</param>
/// <param name="Crop">Farm: what is sown and harvested (see <see cref="ProductionConfig.Crops"/>).</param>
public sealed record GathererRecipe(GatherAction Action, Material? Output, int WorkTicks, int RestTicks, int PickupTicks = 0,
    string Crop = "grain");

/// <summary>
/// A mine: takes ore from the ground within <see cref="Radius"/> of the building. A unit of food (any
/// of the building's input piles, they differ per civilization) lasts <see cref="PreferredRuns"/>
/// trips into the mine if it is the <see cref="PreferredFood"/>, otherwise <see cref="OtherRuns"/>.
/// </summary>
public sealed record MineRecipe(ResourceType Resource, Material Output, int Ticks, int Radius,
    Material? PreferredFood = null, int PreferredRuns = 1, int OtherRuns = 1)
{
    public int RunsFor(Material food) => food == PreferredFood ? PreferredRuns : OtherRuns;
}

/// <summary>
/// A smith: like a workshop, but it chooses one of several <see cref="Products"/> per cycle (tools that
/// are missing first, otherwise whatever has fewer than <see cref="KeepInStock"/> units around).
/// </summary>
public sealed record SmithRecipe(IReadOnlyList<Material> Inputs, IReadOnlyList<Material> Products, int Ticks, int KeepInStock);

/// <summary>Production balancing, loaded from <c>data/production.json</c>. Keyed by building name.</summary>
public sealed record ProductionConfig
{
    public required IReadOnlyDictionary<string, WorkshopRecipe> Workshops { get; init; }
    public required IReadOnlyDictionary<string, GathererRecipe> Gatherers { get; init; }
    public IReadOnlyDictionary<string, MineRecipe> Mines { get; init; } = new Dictionary<string, MineRecipe>();
    public IReadOnlyDictionary<string, SmithRecipe> Smiths { get; init; } = new Dictionary<string, SmithRecipe>();

    /// <summary>
    /// The tool a carrier must pick up to become this trade (keys: worker names of the building data,
    /// plus DIGGER and BRICKLAYER). Trades not listed need none.
    /// </summary>
    public IReadOnlyDictionary<string, Material> Tools { get; init; } = new Dictionary<string, Material>();
    public required int TreeGrowTicks { get; init; }

    /// <summary>A felled tree's stump stays this long, then the field is free (for the forester) again.</summary>
    public int StumpTicks { get; init; } = 60 * World.TicksPerSecond;
    public required int StonesPerCut { get; init; }

    /// <summary>Crops of the farm-like gatherers by name (grain, wine, rice, honey).</summary>
    public IReadOnlyDictionary<string, CropRecipe> Crops { get; init; } = new Dictionary<string, CropRecipe> { ["grain"] = CropRecipe.Grain };

    /// <summary>The crop a field code belongs to, if any.</summary>
    public CropRecipe? CropOf(byte code) => Crops.Values.FirstOrDefault(c => c.Contains(code));

    /// <summary>A copy with another grow time for one crop (tests).</summary>
    public ProductionConfig WithCropGrowTicks(string crop, int ticks) =>
        this with { Crops = Crops.ToDictionary(c => c.Key, c => c.Key == crop ? c.Value with { GrowTicks = ticks } : c.Value) };

    /// <summary>Ore units a mine gets per amount step of a field (original maps: 0-15 per field).</summary>
    public int OrePerAmount { get; init; } = 1;

    /// <summary>Fish a fisher gets per amount step of a water field.</summary>
    public int FishPerAmount { get; init; } = 1;

    /// <summary>Every this many ticks a depleted field regains one amount step (up to its start value); 0 = never.</summary>
    public int RegenerateTicks { get; init; }

    /// <summary>
    /// Percent of the units taken that use up the ground: quarry stone, everything in mountains (ores,
    /// coal, gems, sulfur) and fish. 100 as in the original, 0 = never runs out.
    /// </summary>
    public Depletion Depletion { get; init; } = new(100, 100, 100);

    /// <summary>Share of its construction material a building leaves when it comes down (sites: of what was built in).</summary>
    public double DemolitionPayback { get; init; } = 0.5;

    /// <summary>How geologists search (see <see cref="Geology"/>).</summary>
    public GeologistRules Geologists { get; init; } = new(10, 3 * World.TicksPerSecond, 2 * World.TicksPerSecond,
        4 * 60 * World.TicksPerSecond, 9 * 60 * World.TicksPerSecond);

    /// <summary>Units of food one food pile of a mine holds.</summary>
    public int MineFoodPileCapacity { get; init; } = Materials.MaxPileSize;

    /// <summary>Units one storehouse pile holds (a storehouse has six piles).</summary>
    public int StoragePileCapacity { get; init; } = Materials.MaxPileSize;

    /// <summary>Share of a player's carriers and specialists that always stays carriers (0 = none).</summary>
    public double KeptCarriers { get; init; }

    /// <summary>Fields kept free between a new building and the surroundings of the others.</summary>
    public int BuildingMargin { get; init; }

    /// <summary>
    /// Materials carriers bring to storehouses (null = all). Everything else stays at its producer's
    /// output pile until somebody needs it.
    /// </summary>
    public IReadOnlySet<Material>? StoredMaterials { get; init; }

    public bool IsStored(Material material) => StoredMaterials?.Contains(material) != false;

    /// <summary>
    /// Distribution of a material among building types that use it (weights by building name, like
    /// the original's 90:10 for food to mines). Materials without an entry go to the nearest user.
    /// </summary>
    public IReadOnlyDictionary<Material, IReadOnlyDictionary<string, int>> Distribution { get; init; } =
        new Dictionary<Material, IReadOnlyDictionary<string, int>>();

    /// <summary>Carriers a living house releases once it is finished (by building name).</summary>
    public IReadOnlyDictionary<string, int> Residents { get; init; } = new Dictionary<string, int>();

    /// <summary>Time between two residents leaving the same house.</summary>
    public int ResidentIntervalTicks { get; init; } = World.TicksPerSecond;

    /// <summary>No production at all (worlds without workers, logistics tests).</summary>
    public static ProductionConfig None { get; } = new()
    {
        Workshops = new Dictionary<string, WorkshopRecipe>(),
        Gatherers = new Dictionary<string, GathererRecipe>(),
        TreeGrowTicks = 1,
        StonesPerCut = 1,
    };

    public static ProductionConfig Load(string? dataDirectory = null)
    {
        string path = Path.Combine(dataDirectory ?? Buildings.BuildingCatalog.DefaultDataDirectory, "production.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;

        static int Ticks(JsonElement e, string name) => (int)Math.Round(e.GetProperty(name).GetDouble() * World.TicksPerSecond);
        static Material Material(JsonElement e) => Materials.Parse(e.GetString()!)!.Value;
        static IReadOnlyList<Material> MaterialList(JsonElement e) => e.EnumerateArray().Select(Material).ToArray();

        var workshops = root.GetProperty("workshops").EnumerateObject().ToDictionary(
            w => w.Name,
            w => new WorkshopRecipe(MaterialList(w.Value.GetProperty("inputs")), MaterialList(w.Value.GetProperty("outputs")),
                Ticks(w.Value, "seconds")));
        var gatherers = root.GetProperty("gatherers").EnumerateObject().ToDictionary(
            g => g.Name,
            g => new GathererRecipe(
                Enum.Parse<GatherAction>(g.Value.GetProperty("action").GetString()!),
                g.Value.TryGetProperty("output", out var output) ? Materials.Parse(output.GetString()!) : null,
                Ticks(g.Value, "workSeconds"), Ticks(g.Value, "restSeconds"),
                g.Value.TryGetProperty("pickupSeconds", out _) ? Ticks(g.Value, "pickupSeconds") : 0,
                g.Value.TryGetProperty("crop", out var crop) ? crop.GetString()! : "grain"));
        var codes = new Dictionary<string, (byte First, byte Ripe)>
        {
            ["grain"] = (MapObjectCodes.FirstGrain, MapObjectCodes.RipeGrain),
            ["wine"] = (MapObjectCodes.FirstWine, MapObjectCodes.RipeWine),
            ["rice"] = (MapObjectCodes.FirstRice, MapObjectCodes.RipeRice),
            ["honey"] = (MapObjectCodes.FirstHive, MapObjectCodes.FullHive),
        };
        var crops = root.GetProperty("crops").EnumerateObject().Where(c => !c.Name.StartsWith('_')).ToDictionary(
            c => c.Name,
            c => new CropRecipe(codes[c.Name].First, codes[c.Name].Ripe, Ticks(c.Value, "growSeconds"),
                c.Value.GetProperty("ground").EnumerateArray().Select(t => Enum.Parse<TerrainType>(t.GetString()!)).ToHashSet(),
                c.Value.TryGetProperty("regrows", out var regrows) && regrows.GetBoolean(),
                c.Value.TryGetProperty("slopeBonus", out var slope) ? slope.GetDouble() : 0,
                c.Value.TryGetProperty("slopeHeight", out var slopeHeight) ? slopeHeight.GetInt32() : 4));
        var mineFood = root.GetProperty("mineFood");
        var mines = root.GetProperty("mines").EnumerateObject().Where(m => !m.Name.StartsWith('_')).ToDictionary(
            m => m.Name,
            m => new MineRecipe(
                Enum.Parse<ResourceType>(m.Value.GetProperty("resource").GetString()!),
                Material(m.Value.GetProperty("output")),
                Ticks(m.Value, "seconds"),
                m.Value.GetProperty("radius").GetInt32(),
                m.Value.TryGetProperty("prefersFood", out var preferred) ? Material(preferred) : null,
                mineFood.GetProperty("preferredRuns").GetInt32(),
                mineFood.GetProperty("otherRuns").GetInt32()));
        var smiths = root.GetProperty("smiths").EnumerateObject().Where(m => !m.Name.StartsWith('_')).ToDictionary(
            m => m.Name,
            m => new SmithRecipe(MaterialList(m.Value.GetProperty("inputs")), MaterialList(m.Value.GetProperty("products")),
                Ticks(m.Value, "seconds"), m.Value.GetProperty("keepInStock").GetInt32()));
        var tools = root.GetProperty("tools").EnumerateObject()
            .Where(t => !t.Name.StartsWith('_'))
            .ToDictionary(t => t.Name, t => Material(t.Value));
        var resources = root.GetProperty("resources");
        var housing = root.GetProperty("housing");

        return new ProductionConfig
        {
            Workshops = workshops,
            Gatherers = gatherers,
            Mines = mines,
            Distribution = root.GetProperty("distribution").EnumerateObject().Where(d => !d.Name.StartsWith('_')).ToDictionary(
                d => Materials.Parse(d.Name)!.Value,
                d => (IReadOnlyDictionary<string, int>)d.Value.EnumerateObject().ToDictionary(w => w.Name, w => w.Value.GetInt32())),
            Smiths = smiths,
            Tools = tools,
            StoragePileCapacity = root.GetProperty("storage").GetProperty("pileCapacity").GetInt32(),
            MineFoodPileCapacity = mineFood.TryGetProperty("pileCapacity", out var foodPile) ? foodPile.GetInt32() : Materials.MaxPileSize,
            KeptCarriers = root.GetProperty("recruitment").GetProperty("keptCarriers").GetDouble(),
            BuildingMargin = root.GetProperty("placement").GetProperty("margin").GetInt32(),
            StoredMaterials = root.GetProperty("storage").GetProperty("materials").EnumerateArray()
                .Select(m => Materials.Parse(m.GetString()!)!.Value).ToHashSet(),
            Residents = housing.GetProperty("residents").EnumerateObject().ToDictionary(r => r.Name, r => r.Value.GetInt32()),
            ResidentIntervalTicks = Ticks(housing, "intervalSeconds"),
            TreeGrowTicks = Ticks(root.GetProperty("trees"), "growSeconds"),
            StumpTicks = root.GetProperty("trees").TryGetProperty("stumpSeconds", out _) ? Ticks(root.GetProperty("trees"), "stumpSeconds") : 60 * World.TicksPerSecond,
            Crops = crops,
            StonesPerCut = root.GetProperty("stones").GetProperty("stonesPerCut").GetInt32(),
            OrePerAmount = resources.GetProperty("orePerAmount").GetInt32(),
            FishPerAmount = resources.GetProperty("fishPerAmount").GetInt32(),
            RegenerateTicks = Ticks(resources, "regenerateSeconds"),
            Geologists = root.TryGetProperty("geologists", out var geologists)
                ? new GeologistRules(geologists.GetProperty("searchRadius").GetInt32(), Ticks(geologists, "surveySeconds"),
                    Ticks(geologists, "signSeconds"), (int)(geologists.GetProperty("signMinutes")[0].GetDouble() * 60 * World.TicksPerSecond),
                    (int)(geologists.GetProperty("signMinutes")[1].GetDouble() * 60 * World.TicksPerSecond))
                : new GeologistRules(10, 3 * World.TicksPerSecond, 2 * World.TicksPerSecond, 4 * 60 * World.TicksPerSecond, 9 * 60 * World.TicksPerSecond),
            DemolitionPayback = root.TryGetProperty("demolition", out var demolition) ? demolition.GetProperty("payback").GetDouble() : 0.5,
            Depletion = root.TryGetProperty("depletion", out var depletion)
                ? new Depletion(depletion.GetProperty("stone").GetInt32(), depletion.GetProperty("mountain").GetInt32(), depletion.GetProperty("fish").GetInt32())
                : new Depletion(100, 100, 100),
        };
    }
}

/// <summary>Percent of the units taken that use up the ground, per kind of resource (see <see cref="ProductionConfig.Depletion"/>).</summary>
public sealed record Depletion(int Stone, int Mountain, int Fish);

/// <summary>
/// Geologists search fields within <paramref name="SearchRadius"/> of the point they were sent to, each
/// for <paramref name="SurveyTicks"/> plus <paramref name="SignTicks"/>; a sign stays between
/// <paramref name="SignMinTicks"/> and <paramref name="SignMaxTicks"/>.
/// </summary>
public sealed record GeologistRules(int SearchRadius, int SurveyTicks, int SignTicks, int SignMinTicks, int SignMaxTicks);
