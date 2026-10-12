using System.Text.Json;
using Homestead.Simulation.Buildings;
using Homestead.Simulation.Economy;
using Homestead.Simulation.Pathfinding;

namespace Homestead.Simulation;

/// <summary>Start equipment of one start level (see <c>data/start.json</c>).</summary>
public sealed record StartLevel(IReadOnlyList<(Material Material, int Count)> Goods, IReadOnlyDictionary<string, int> Settlers);

/// <summary>
/// Start conditions on multiplayer maps without start buildings: every player gets the same tower,
/// goods piles and settlers around its start point (equal conditions for the player and the AI).
/// The tower's soldiers occupy it at once, so every player starts with land to build on.
/// </summary>
public sealed class StartSetup
{
    private StartSetup(string building, IReadOnlyDictionary<int, StartLevel> levels)
    {
        Building = building;
        Levels = levels;
    }

    public string Building { get; }

    public IReadOnlyDictionary<int, StartLevel> Levels { get; }

    public static StartSetup Load(string? dataDirectory = null)
    {
        string path = Path.Combine(dataDirectory ?? BuildingCatalog.DefaultDataDirectory, "start.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        var levels = root.GetProperty("levels").EnumerateObject().ToDictionary(
            l => int.Parse(l.Name),
            l => new StartLevel(
                l.Value.GetProperty("goods").EnumerateArray()
                    .Select(g => (Materials.Parse(g[0].GetString()!)!.Value, g[1].GetInt32())).ToArray(),
                l.Value.GetProperty("settlers").EnumerateObject().ToDictionary(s => s.Name, s => s.Value.GetInt32())));
        return new StartSetup(root.GetProperty("building").GetString()!, levels);
    }

    /// <summary>Specialists in start.json: profession and trade they start with, carrying their tool (see production.json's tools).</summary>
    private static readonly (string Name, Profession Profession, string Trade)[] Specialists =
    [
        ("Miner", Profession.Worker, "MINER"), ("Smith", Profession.Worker, "SMITH"), ("Stonecutter", Profession.Worker, "STONECUTTER"),
        ("Builder", Profession.Builder, "BRICKLAYER"), ("Digger", Profession.Digger, "DIGGER"),
    ];

    /// <summary>Equips one player at <paramref name="start"/>. Returns the start building (null if it does not fit).</summary>
    public Building? Apply(World world, BuildingCatalog catalog, int player, GridPoint start, int level)
    {
        var equipment = Levels.TryGetValue(level, out var l) ? l : Levels[Levels.Keys.Max()];
        var definition = catalog[Building];
        var map = world.Map!;
        if (definition.Blocked.Any(o => !map.Contains(start + o)))
            return null;
        var tower = world.PlaceBuilding(definition, player, start, force: true);
        if (tower is null)
            return null;

        // Soldiers at the door: they occupy the tower right away.
        foreach (var type in Enum.GetValues<SoldierType>())
            for (int i = 0; i < equipment.Settlers.GetValueOrDefault(type.ToString()); i++)
                world.SpawnSoldier(tower.Door, type, player: player);
        PlaceGoods(world, tower, equipment.Goods.ToList());
        for (int i = 0; i < equipment.Settlers.GetValueOrDefault("Carrier"); i++)
            world.Spawn(start + new GridPoint(3, 5), Profession.Carrier, player);
        // Specialists come equipped: their tools are not in the piles, so no other trade can take them first.
        // Without a workplace they wait like workers whose building was torn down and take the first one.
        foreach (var (name, profession, trade) in Specialists)
            for (int i = 0; i < equipment.Settlers.GetValueOrDefault(name); i++)
                if (world.Spawn(start + new GridPoint(3, 5), profession, player) is { } specialist)
                {
                    specialist.Trade = trade;
                    specialist.Tool = world.Recruitment.ToolFor(trade);
                }
        return tower;
    }

    /// <summary>Goods piles on free points in rings around the tower, outside its ground and surroundings.</summary>
    private static void PlaceGoods(World world, Building tower, List<(Material Material, int Count)> goods)
    {
        var map = world.Map!;
        var keepFree = tower.Definition.Blocked.Concat(tower.Definition.Protected).Append(tower.Definition.Door)
            .Select(o => tower.Position + o).ToHashSet();
        int next = 0;
        for (int r = 2; r <= 20 && next < goods.Count; r++)
        for (int dy = -r; dy <= r && next < goods.Count; dy++)
        for (int dx = -r; dx <= r && next < goods.Count; dx++)
        {
            var p = new GridPoint(tower.Position.X + dx, tower.Position.Y + dy);
            if (PathFinder.Distance(tower.Position, p) != r || keepFree.Contains(p) || !map.Contains(p))
                continue;
            // Every other point, so carriers can walk between the piles.
            if ((p.X + p.Y) % 2 != 0)
                continue;
            if (world.AddLooseGoods(p, goods[next].Material, goods[next].Count) is not null)
                next++;
        }
    }
}
