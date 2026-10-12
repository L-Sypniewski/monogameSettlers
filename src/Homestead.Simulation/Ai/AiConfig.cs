using System.Text.Json;
using Homestead.Simulation.Map;

namespace Homestead.Simulation.Ai;

/// <summary>AI balancing, loaded from <c>data/ai.json</c>.</summary>
public sealed record AiConfig
{
    public required int ThinkTicks { get; init; }
    public required int MaxSites { get; init; }
    public required int MaxTowerSites { get; init; }
    public required int FreeCarriers { get; init; }
    public required string House { get; init; }
    public required int StockWhenWaiting { get; init; }
    public required IReadOnlyList<(string Building, int Count)> BuildOrder { get; init; }
    public required string ExpansionBuilding { get; init; }
    public required int MinStoneUnits { get; init; }
    public required int WantedStoneUnits { get; init; }
    public required int MinTrees { get; init; }
    public required IReadOnlyList<ResourceType> Ores { get; init; }
    public required int SpareSoldiersForLand { get; init; }
    public required int LandStock { get; init; }

    public static AiConfig Load(string? dataDirectory = null)
    {
        string path = Path.Combine(dataDirectory ?? Buildings.BuildingCatalog.DefaultDataDirectory, "ai.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        var expansion = root.GetProperty("expansion");
        return new AiConfig
        {
            ThinkTicks = Math.Max(1, (int)Math.Round(root.GetProperty("thinkSeconds").GetDouble() * World.TicksPerSecond)),
            MaxSites = root.GetProperty("maxSites").GetInt32(),
            MaxTowerSites = root.GetProperty("maxTowerSites").GetInt32(),
            FreeCarriers = root.GetProperty("freeCarriers").GetInt32(),
            House = root.GetProperty("house").GetString()!,
            StockWhenWaiting = root.GetProperty("stockWhenWaiting").GetInt32(),
            BuildOrder = root.GetProperty("buildOrder").EnumerateArray()
                .Select(e => (e[0].GetString()!, e[1].GetInt32())).ToArray(),
            ExpansionBuilding = expansion.GetProperty("building").GetString()!,
            MinStoneUnits = expansion.GetProperty("minStoneUnits").GetInt32(),
            WantedStoneUnits = expansion.GetProperty("wantedStoneUnits").GetInt32(),
            MinTrees = expansion.GetProperty("minTrees").GetInt32(),
            SpareSoldiersForLand = expansion.GetProperty("spareSoldiersForLand").GetInt32(),
            LandStock = expansion.GetProperty("landStock").GetInt32(),
            Ores = expansion.GetProperty("ores").EnumerateArray().Select(o => Enum.Parse<ResourceType>(o.GetString()!)).ToArray(),
        };
    }
}
