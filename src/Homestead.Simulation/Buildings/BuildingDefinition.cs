using System.Text.Json;
using System.Text.Json.Nodes;
using Homestead.Simulation.Economy;

namespace Homestead.Simulation.Buildings;

public enum Civilization
{
    Roman,
    Egyptian,
    Asian,
    Amazon,
}

/// <summary>A graphic: image <see cref="Image"/> of sequence <see cref="Sequence"/> in GFX file <see cref="File"/>.</summary>
public readonly record struct ImageRef(int File, int Sequence, int Image);

public sealed record PileDefinition(GridPoint Offset, Material? Material, int Required = 0);

/// <summary>
/// A soldier place of a military building: bowmen stand on top, infantry (swordsmen, pikemen) at the
/// front. <see cref="Pixel"/> is where the soldier is drawn, relative to the building anchor, y up.
/// </summary>
public sealed record OccupierPlace(bool ForBowman, GridPoint Soldier, (int X, int Y) Pixel, bool LooksRight)
{
    public bool Accepts(SoldierType type) => ForBowman == (type == SoldierType.Bowman);
}

/// <summary>Where a builder stands while building, and which way it faces.</summary>
public sealed record WorkSpot(GridPoint Offset, Direction Facing);

/// <summary>
/// Static description of a building type. All positions are offsets from the building's anchor
/// point. Loaded from <c>data/buildings/&lt;civilization&gt;.json</c>.
/// </summary>
public sealed class BuildingDefinition
{
    public required string Name { get; init; }
    public required Civilization Civilization { get; init; }
    public string? Worker { get; init; }
    public required IReadOnlyList<GridPoint> Blocked { get; init; }

    /// <summary>Fields around the building that must stay free of other buildings (walkable).</summary>
    public required IReadOnlyList<GridPoint> Protected { get; init; }

    public required GridPoint Door { get; init; }

    /// <summary>Ground types the building may be placed on (data names, e.g. GRASS, MOUNTAIN).</summary>
    public required IReadOnlySet<string> Ground { get; init; }

    /// <summary>Whether a construction site is levelled first (mines are not: they go into the mountain).</summary>
    public bool NeedsLevelling { get; init; } = true;

    public required IReadOnlyList<WorkSpot> BuilderSpots { get; init; }

    /// <summary>Where the posts of a construction site stand (around its ground).</summary>
    public IReadOnlyList<GridPoint> BuildMarks { get; init; } = [];

    /// <summary>How far gatherers (woodcutter, forester, ...) walk from the building, if set in the data.</summary>
    public int? WorkRadius { get; init; }

    /// <summary>Soldier places (military buildings only).</summary>
    public IReadOnlyList<OccupierPlace> Occupiers { get; init; } = [];

    /// <summary>Where the building's flag stands (on the roof, or at the door of buildings without a worker).</summary>
    public GridPoint? Flag { get; init; }

    /// <summary>Where a smith puts down what it made (it has no output pile).</summary>
    public GridPoint? DropPosition { get; init; }

    /// <summary>Center of the work area if it is not the building itself (the fields in front of a farm).</summary>
    public GridPoint? WorkCenter { get; init; }

    public required IReadOnlyList<PileDefinition> ConstructionPiles { get; init; }
    public required IReadOnlyList<PileDefinition> RequestPiles { get; init; }
    public required IReadOnlyList<PileDefinition> OfferPiles { get; init; }

    /// <summary>Layers of the finished building, drawn in order.</summary>
    public required IReadOnlyList<ImageRef> FinalImages { get; init; }

    public ImageRef? ConstructionImage { get; init; }

    /// <summary>Icon of the build menu (a GUI image: <see cref="ImageRef.Sequence"/> is its index in the file).</summary>
    public ImageRef? GuiImage { get; init; }

    /// <summary>Lowest row offset covered by the building, used for drawing order.</summary>
    public int BottomRow => Blocked.Count == 0 ? 0 : Blocked.Max(p => p.Y);
}

/// <summary>All building definitions of one civilization.</summary>
public sealed class BuildingCatalog
{
    private readonly Dictionary<string, BuildingDefinition> _definitions;

    private BuildingCatalog(Civilization civilization, Dictionary<string, BuildingDefinition> definitions)
    {
        Civilization = civilization;
        _definitions = definitions;
    }

    public Civilization Civilization { get; }

    public IReadOnlyCollection<BuildingDefinition> All => _definitions.Values;

    public BuildingDefinition? Find(string name) => _definitions.GetValueOrDefault(name);

    public BuildingDefinition this[string name] =>
        Find(name) ?? throw new KeyNotFoundException($"No building '{name}' for {Civilization}.");

    /// <summary>Default data directory: <c>data</c> next to the executable (copied there by the build).</summary>
    public static string DefaultDataDirectory => Path.Combine(AppContext.BaseDirectory, "data");

    public static BuildingCatalog Load(Civilization civilization, string? dataDirectory = null)
    {
        string path = Path.Combine(dataDirectory ?? DefaultDataDirectory, "buildings",
            civilization.ToString().ToLowerInvariant() + ".json");
        var buildings = JsonNode.Parse(File.ReadAllText(path))!["buildings"]!.AsObject();
        ApplyOverrides(buildings, civilization, Path.Combine(Path.GetDirectoryName(path)!, "overrides.json"));
        var definitions = new Dictionary<string, BuildingDefinition>();
        foreach (var (name, building) in buildings)
            definitions[name] = Parse(name, civilization, JsonSerializer.SerializeToElement(building));
        return new BuildingCatalog(civilization, definitions);
    }

    /// <summary>
    /// Hand-made changes (<c>buildings/overrides.json</c>): each property given replaces the generated one,
    /// those under <c>all</c> for every people, then those under the people's name.
    /// </summary>
    private static void ApplyOverrides(JsonObject buildings, Civilization civilization, string path)
    {
        if (!File.Exists(path))
            return;
        var root = JsonNode.Parse(File.ReadAllText(path))!;
        foreach (var section in new[] { "all", civilization.ToString() })
        {
            if (root[section] is not JsonObject overrides)
                continue;
            foreach (var (name, changes) in overrides)
                if (buildings[name] is JsonObject building && changes is JsonObject properties)
                    foreach (var (key, value) in properties)
                        if (!key.StartsWith('_'))
                            building[key] = value?.DeepClone();
        }
    }

    private static Direction ParseDirection(string name) => name switch
    {
        "EAST" => Direction.East,
        "SOUTH_EAST" => Direction.SouthEast,
        "SOUTH_WEST" => Direction.SouthWest,
        "WEST" => Direction.West,
        "NORTH_WEST" => Direction.NorthWest,
        "NORTH_EAST" => Direction.NorthEast,
        _ => throw new FormatException($"Unknown direction '{name}'."),
    };

    /// <summary>
    /// The door, moved off the building's ground where the data put it inside (Roman and Amazon temples):
    /// to the nearest point outside, searching downwards first (doors face the viewer).
    /// </summary>
    private static GridPoint OutsideDoor(GridPoint door, IReadOnlyList<GridPoint> blocked)
    {
        var footprint = blocked.ToHashSet();
        for (int step = 0; step < 8; step++)
        {
            foreach (var candidate in new[] { door + new GridPoint(step, step), door + new GridPoint(0, step), door + new GridPoint(step, 0) })
                if (!footprint.Contains(candidate))
                    return candidate;
        }
        return door;
    }

    private static BuildingDefinition Parse(string name, Civilization civilization, JsonElement b)
    {
        static GridPoint Point(JsonElement e) => new(e[0].GetInt32(), e[1].GetInt32());
        static IReadOnlyList<GridPoint> Points(JsonElement e) => e.EnumerateArray().Select(Point).ToArray();
        static IReadOnlyList<PileDefinition> Piles(JsonElement e) => e.EnumerateArray()
            .Select(p => new PileDefinition(Point(p.GetProperty("at")), Materials.Parse(p.GetProperty("material").GetString()!),
                p.TryGetProperty("required", out var r) ? r.GetInt32() : 0))
            .ToArray();
        static IReadOnlyList<ImageRef> Images(JsonElement images, string key) =>
            images.TryGetProperty(key, out var list)
                ? list.EnumerateArray().Select(i => new ImageRef(
                    i.GetProperty("file").GetInt32(), i.GetProperty("sequence").GetInt32(), i.GetProperty("image").GetInt32())).ToArray()
                : [];

        var images = b.GetProperty("images");
        var builderSpots = b.GetProperty("positions").TryGetProperty("bricklayer", out var spots)
            ? spots.EnumerateArray().Select(s => new WorkSpot(
                new GridPoint(s.GetProperty("dx").GetInt32(), s.GetProperty("dy").GetInt32()),
                ParseDirection(s.GetProperty("direction").GetString()!))).ToArray()
            : [];
        GridPoint? workCenter = b.GetProperty("positions").TryGetProperty("workcenter", out var centers) && centers.GetArrayLength() > 0
            ? new GridPoint(centers[0].GetProperty("dx").GetInt32(), centers[0].GetProperty("dy").GetInt32())
            : null;
        GridPoint? dropPosition = b.GetProperty("positions").TryGetProperty("smithDropPosition", out var drops) && drops.GetArrayLength() > 0
            ? new GridPoint(drops[0].GetProperty("dx").GetInt32(), drops[0].GetProperty("dy").GetInt32())
            : null;
        return new BuildingDefinition
        {
            Name = name,
            Civilization = civilization,
            Worker = b.GetProperty("worker").ValueKind == JsonValueKind.String ? b.GetProperty("worker").GetString() : null,
            Blocked = Points(b.GetProperty("blocked")),
            Protected = Points(b.GetProperty("protected")),
            Door = OutsideDoor(Point(b.GetProperty("door")), Points(b.GetProperty("blocked"))),
            Ground = b.GetProperty("ground").EnumerateArray().Select(g => g.GetString()!).ToHashSet(),
            NeedsLevelling = !b.TryGetProperty("levelling", out var levelling) || levelling.GetBoolean(),
            BuilderSpots = builderSpots,
            BuildMarks = b.GetProperty("positions").TryGetProperty("buildmark", out var marks)
                ? marks.EnumerateArray().Select(m => new GridPoint(m.GetProperty("dx").GetInt32(), m.GetProperty("dy").GetInt32())).ToArray()
                : [],
            WorkRadius = b.TryGetProperty("workradius", out var radius) && int.TryParse(radius.GetString(), out int r) ? r : null,
            WorkCenter = workCenter,
            DropPosition = dropPosition,
            Flag = b.TryGetProperty("flag", out var flag) ? Point(flag) : null,
            Occupiers = b.TryGetProperty("occupiers", out var occupiers)
                ? occupiers.EnumerateArray().Select(o => new OccupierPlace(
                    o.GetProperty("type").GetString() == "BOWMAN", Point(o.GetProperty("soldier")),
                    (o.GetProperty("pixel")[0].GetInt32(), o.GetProperty("pixel")[1].GetInt32()),
                    o.GetProperty("looksRight").GetBoolean())).ToArray()
                : [],
            ConstructionPiles = Piles(b.GetProperty("constructionStacks")),
            RequestPiles = Piles(b.GetProperty("requestStacks")),
            OfferPiles = Piles(b.GetProperty("offerStacks")),
            FinalImages = Images(images, "final"),
            ConstructionImage = Images(images, "build").Cast<ImageRef?>().FirstOrDefault(),
            GuiImage = Images(images, "gui").Cast<ImageRef?>().FirstOrDefault(),
        };
    }
}
