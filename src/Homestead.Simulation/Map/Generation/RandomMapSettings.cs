using System.Text.Json;
using Homestead.Simulation.Buildings;
using Homestead.Simulation.Economy;

namespace Homestead.Simulation.Map.Generation;

/// <summary>How the random map repeats itself for fair starts.</summary>
public enum MapSymmetry
{
    /// <summary>Free terrain; every player still gets the same kinds of surroundings (mountain, pond, forest, stones).</summary>
    None,

    /// <summary>Mirror symmetry around the center: each player's sector mirrors its neighbors' (exact for 2, 3, 4 and 6 players, close for 5).</summary>
    Mirror,

    /// <summary>The surroundings of the start point are the same tile for every player; the land between them is free.</summary>
    Repeat,
}

public enum Abundance
{
    Few,
    Medium,
    Many,
}

public enum TreasureLevel
{
    Off,
    Normal,
    Many,
}

/// <summary>What the random map generator is asked for; together with a seed it makes the same map every time.</summary>
public sealed record RandomMapSettings(
    int Players = 2,
    MapSymmetry Symmetry = MapSymmetry.None,
    Abundance Rivers = Abundance.Medium,
    Abundance Resources = Abundance.Medium,
    Abundance Stones = Abundance.Medium,
    Abundance Forests = Abundance.Medium,
    Abundance Decorations = Abundance.Medium,
    TreasureLevel Treasures = TreasureLevel.Normal)
{
    public const int MinPlayers = 2;
    public const int MaxPlayers = 6;

    /// <summary>
    /// Reads a random map given as map name: <c>random[:players[:symmetry[:seed]]]</c>, e.g. <c>random:4:mirror:17</c>
    /// (symmetry none, mirror or repeat; the rest as default). Null if the name is no random map.
    /// </summary>
    public static (RandomMapSettings Settings, int Seed)? ParseName(string name, int defaultSeed = 1)
    {
        var parts = name.Split(':');
        if (!parts[0].Equals("random", StringComparison.OrdinalIgnoreCase))
            return null;
        var settings = new RandomMapSettings();
        if (parts.Length > 1 && int.TryParse(parts[1], out int players))
            settings = settings with { Players = Math.Clamp(players, MinPlayers, MaxPlayers) };
        if (parts.Length > 2 && Enum.TryParse<MapSymmetry>(parts[2], ignoreCase: true, out var symmetry))
            settings = settings with { Symmetry = symmetry };
        int seed = parts.Length > 3 && int.TryParse(parts[3], out int s) ? s : defaultSeed;
        return (settings, seed);
    }
}

/// <summary>Tuning of the generator (<c>data/randommap.json</c>); per-level values are indexed by <see cref="Abundance"/> or <see cref="TreasureLevel"/>.</summary>
public sealed record RandomMapConfig
{
    public required IReadOnlyDictionary<int, int> Sizes { get; init; }
    public int StartResources { get; init; } = 2;
    public int SeaMargin { get; init; } = 10;
    public double StartDistance { get; init; } = 0.58;
    public int HomeRadius { get; init; } = 36;
    public double MountainShare { get; init; } = 0.07;
    public int SnowLine { get; init; } = 105;
    public required double[] LakesPerPlayer { get; init; }
    public required double[] RiversPerPlayer { get; init; }
    public double SwampsPerPlayer { get; init; } = 0.75;
    public required ResourceLevel[] Resources { get; init; }
    public required IReadOnlyList<(ResourceType Type, int Weight)> VeinWeights { get; init; }
    public required double[] StoneClustersPerPlayer { get; init; }
    public required double[] ForestShare { get; init; }
    public double TreeDensity { get; init; } = 0.4;
    public required double[] DecorationShare { get; init; }
    public required double[] TreasureSitesPerPlayer { get; init; }
    public required IReadOnlyList<(Material Material, int Weight)> TreasureGoods { get; init; }

    /// <param name="Cover">Share of mountain fields with ore.</param>
    /// <param name="Fish">Fish amount in shallow water.</param>
    public sealed record ResourceLevel(double Cover, int Min, int Max, int Fish);

    public int SizeFor(int players) =>
        Sizes.TryGetValue(players, out int size) ? size : Sizes.OrderBy(s => Math.Abs(s.Key - players)).First().Value;

    public static RandomMapConfig Load(string? dataDirectory = null)
    {
        string path = Path.Combine(dataDirectory ?? BuildingCatalog.DefaultDataDirectory, "randommap.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        static double[] Levels(JsonElement e) => [e.GetProperty("few").GetDouble(), e.GetProperty("medium").GetDouble(), e.GetProperty("many").GetDouble()];
        static double[] Treasures(JsonElement e) => [e.GetProperty("off").GetDouble(), e.GetProperty("normal").GetDouble(), e.GetProperty("many").GetDouble()];
        static ResourceLevel Resource(JsonElement e) => new(e.GetProperty("cover").GetDouble(), e.GetProperty("min").GetInt32(),
            e.GetProperty("max").GetInt32(), e.GetProperty("fish").GetInt32());
        var resources = root.GetProperty("resources");
        return new RandomMapConfig
        {
            Sizes = root.GetProperty("sizes").EnumerateObject().ToDictionary(s => int.Parse(s.Name), s => s.Value.GetInt32()),
            StartResources = root.GetProperty("startResources").GetInt32(),
            SeaMargin = root.GetProperty("seaMargin").GetInt32(),
            StartDistance = root.GetProperty("startDistance").GetDouble(),
            HomeRadius = root.GetProperty("homeRadius").GetInt32(),
            MountainShare = root.GetProperty("mountainShare").GetDouble(),
            SnowLine = root.GetProperty("snowLine").GetInt32(),
            LakesPerPlayer = Levels(root.GetProperty("lakesPerPlayer")),
            RiversPerPlayer = Levels(root.GetProperty("riversPerPlayer")),
            SwampsPerPlayer = root.GetProperty("swampsPerPlayer").GetDouble(),
            Resources = [Resource(resources.GetProperty("few")), Resource(resources.GetProperty("medium")), Resource(resources.GetProperty("many"))],
            VeinWeights = root.GetProperty("veinWeights").EnumerateObject()
                .Select(v => (Enum.Parse<ResourceType>(v.Name), v.Value.GetInt32())).ToArray(),
            StoneClustersPerPlayer = Levels(root.GetProperty("stoneClustersPerPlayer")),
            ForestShare = Levels(root.GetProperty("forestShare")),
            TreeDensity = root.GetProperty("treeDensity").GetDouble(),
            DecorationShare = Levels(root.GetProperty("decorationShare")),
            TreasureSitesPerPlayer = Treasures(root.GetProperty("treasureSitesPerPlayer")),
            TreasureGoods = root.GetProperty("treasureGoods").EnumerateObject()
                .Select(g => (Materials.Parse(g.Name) ?? throw new InvalidDataException($"randommap.json: unknown material {g.Name}"), g.Value.GetInt32()))
                .ToArray(),
        };
    }
}
