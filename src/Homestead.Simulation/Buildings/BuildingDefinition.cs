using System.Text.Json;
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

    public required IReadOnlyList<WorkSpot> BuilderSpots { get; init; }

    /// <summary>How far gatherers (woodcutter, forester, ...) walk from the building, if set in the data.</summary>
    public int? WorkRadius { get; init; }

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
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var definitions = new Dictionary<string, BuildingDefinition>();
        foreach (var entry in document.RootElement.GetProperty("buildings").EnumerateObject())
            definitions[entry.Name] = Parse(entry.Name, civilization, entry.Value);
        return new BuildingCatalog(civilization, definitions);
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
            Door = Point(b.GetProperty("door")),
            Ground = b.GetProperty("ground").EnumerateArray().Select(g => g.GetString()!).ToHashSet(),
            BuilderSpots = builderSpots,
            WorkRadius = b.TryGetProperty("workradius", out var radius) && int.TryParse(radius.GetString(), out int r) ? r : null,
            WorkCenter = workCenter,
            DropPosition = dropPosition,
            ConstructionPiles = Piles(b.GetProperty("constructionStacks")),
            RequestPiles = Piles(b.GetProperty("requestStacks")),
            OfferPiles = Piles(b.GetProperty("offerStacks")),
            FinalImages = Images(images, "final"),
            ConstructionImage = Images(images, "build").Cast<ImageRef?>().FirstOrDefault(),
        };
    }
}
