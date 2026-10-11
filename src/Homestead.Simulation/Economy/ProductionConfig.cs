using System.Text.Json;

namespace Homestead.Simulation.Economy;

public enum GatherAction
{
    CutTree,
    CutStone,
    PlantTree,
}

public sealed record WorkshopRecipe(IReadOnlyList<Material> Inputs, IReadOnlyList<Material> Outputs, int Ticks);

public sealed record GathererRecipe(GatherAction Action, Material? Output, int WorkTicks, int RestTicks);

/// <summary>Production balancing, loaded from <c>data/production.json</c>. Keyed by building name.</summary>
public sealed class ProductionConfig
{
    public required IReadOnlyDictionary<string, WorkshopRecipe> Workshops { get; init; }
    public required IReadOnlyDictionary<string, GathererRecipe> Gatherers { get; init; }
    public required int TreeGrowTicks { get; init; }
    public required int StonesPerCut { get; init; }

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
        static IReadOnlyList<Material> MaterialList(JsonElement e) =>
            e.EnumerateArray().Select(m => Materials.Parse(m.GetString()!)!.Value).ToArray();

        var workshops = root.GetProperty("workshops").EnumerateObject().ToDictionary(
            w => w.Name,
            w => new WorkshopRecipe(MaterialList(w.Value.GetProperty("inputs")), MaterialList(w.Value.GetProperty("outputs")),
                Ticks(w.Value, "seconds")));
        var gatherers = root.GetProperty("gatherers").EnumerateObject().ToDictionary(
            g => g.Name,
            g => new GathererRecipe(
                Enum.Parse<GatherAction>(g.Value.GetProperty("action").GetString()!),
                g.Value.TryGetProperty("output", out var output) ? Materials.Parse(output.GetString()!) : null,
                Ticks(g.Value, "workSeconds"), Ticks(g.Value, "restSeconds")));

        return new ProductionConfig
        {
            Workshops = workshops,
            Gatherers = gatherers,
            TreeGrowTicks = Ticks(root.GetProperty("trees"), "growSeconds"),
            StonesPerCut = root.GetProperty("stones").GetProperty("stonesPerCut").GetInt32(),
        };
    }
}
