using Homestead.Assets.Maps;
using Homestead.Simulation.Buildings;
using Homestead.Simulation.Economy;
using Homestead.Simulation.Magic;
using Homestead.Simulation.Map;

namespace Homestead.Simulation;

/// <summary>Builds a world from an original map: terrain, buildings, piles of goods and settlers.</summary>
public static class OriginalScenario
{
    /// <summary>
    /// Building type byte of original maps (chunk 8). Order from JSettlers' EOriginalMapBuildingType (MIT); every
    /// people's temples count as ours, null for buildings we do not have (catapult, powder, ballista, cannon works, gong).
    /// </summary>
    private static readonly string?[] BuildingNames =
    [
        null, "stock", "lumberjack", "stonecutter", "sawmill", "forester", "lookout_tower", "coalmine", "goldmine",
        "ironmine", "goldmelt", "ironmelt", "toolsmith", "weaponsmith", "winegrower", "tower", "big_tower", "mill",
        "castle", "barrack", "baker", "slaughterhouse", "distillery", "pig_farm", "farm", "fisher", "small_livinghouse",
        "medium_livinghouse", "big_livinghouse", "sulfurmine", "waterworks", null, "dockyard", "harbor", "market_place",
        "hospital", "rice_farm", "gemsmine", "brewery", "charcoal_burner", null, "big_temple", "temple", "big_temple",
        "temple", "big_temple", "temple", null, null, "donkey_farm", null, "beekeeping", "mead_brewery", "laboratory",
        "temple", "big_temple", "sulfurmine",
    ];

    /// <summary>
    /// Settler types of chunk 7 that we place as something other than a carrier (order from JSettlers'
    /// EOriginalMapSettlersType, MIT; 23, the thief, from the campaigns: scattered over the map where the story
    /// speaks of scouts, 57 of them in the Amazon campaign's stealing mission). Other specialists come as carriers:
    /// they become specialists again by taking up a tool.
    /// </summary>
    private static (Profession Profession, SoldierType? Soldier, int Level) SettlerOf(int type) => type switch
    {
        1 => (Profession.Digger, null, 1),
        2 => (Profession.Builder, null, 1),
        5 => (Profession.Soldier, SoldierType.Swordsman, 1),
        8 => (Profession.Soldier, SoldierType.Bowman, 1),
        15 => (Profession.Soldier, SoldierType.Pikeman, 1),
        22 => (Profession.Geologist, null, 1),
        23 => (Profession.Scout, null, 1),
        30 => (Profession.Soldier, SoldierType.Bowman, 2),
        31 => (Profession.Soldier, SoldierType.Pikeman, 2),
        32 => (Profession.Soldier, SoldierType.Swordsman, 2),
        33 => (Profession.Soldier, SoldierType.Bowman, 3),
        34 => (Profession.Soldier, SoldierType.Pikeman, 3),
        35 => (Profession.Soldier, SoldierType.Swordsman, 3),
        36 => (Profession.Priest, null, 1),
        40 => (Profession.Pioneer, null, 1),
        _ => (Profession.Carrier, null, 1),
    };

    /// <param name="Catalogs">Building catalog (civilization) of every player of the map.</param>
    public sealed record LoadReport(int Buildings, int Piles, int Settlers, IReadOnlyList<string> Skipped, IReadOnlyList<BuildingCatalog> Catalogs);

    /// <param name="freeChoice">
    /// Peoples for players whose people the map leaves open ("random" in most multiplayer maps): player i
    /// gets entry i modulo the count. Without it they get a random people from <paramref name="random"/>,
    /// without that too they are Romans.
    /// </param>
    /// <param name="seed">The world's seed (see <see cref="World.Seed"/>).</param>
    /// <param name="hardcore">Players with the <see cref="Hardcore"/> head start (also in their start equipment).</param>
    /// <param name="positions">
    /// The map player whose start position (with its people preset, buildings, settlers) each player takes:
    /// entry i for player i, a permutation of the map's players. Without it player i takes map player i.
    /// </param>
    /// <param name="production">The production rules (e.g. with the player's <see cref="GameSettings"/>); default <c>production.json</c>.</param>
    /// <param name="magic">The magic rules (e.g. with the player's <see cref="GameSettings"/>); default <c>magic.json</c>.</param>
    public static (World World, LoadReport Report) Load(OriginalMap original, string? dataDirectory = null,
        IReadOnlyList<Civilization>? freeChoice = null, Random? random = null, int seed = 0, IEnumerable<int>? hardcore = null,
        IReadOnlyList<int>? positions = null, ProductionConfig? production = null, MagicConfig? magic = null)
    {
        int count = original.Players.Count;
        if (positions is not null && (positions.Count != count || positions.Distinct().Count() != count || positions.Any(p => p < 0 || p >= count)))
            throw new ArgumentException("positions must be a permutation of the map's players", nameof(positions));
        // Map player → player, and back.
        int PositionOf(int player) => positions is not null && player < count ? positions[player] : player;
        var owners = Enumerable.Range(0, count).ToArray();
        for (int player = 0; player < count; player++)
            owners[PositionOf(player)] = player;
        int OwnerOf(int mapPlayer) => mapPlayer >= 0 && mapPlayer < count ? owners[mapPlayer] : mapPlayer;

        var peoples = Enum.GetValues<Civilization>();
        var drawn = new Dictionary<int, Civilization>();
        var map = GameMap.FromOriginal(original.Area);
        // The map's land belongs to map players, too.
        if (positions is not null)
        {
            for (int y = 0; y < map.Size; y++)
            {
                for (int x = 0; x < map.Size; x++)
                {
                    var p = new GridPoint(x, y);
                    if (map.OwnerAt(p) is int mapPlayer && mapPlayer < count)
                        map.SetOwner(p, OwnerOf(mapPlayer));
                }
            }
        }
        var world = new World(map, production, magic: magic, seed: seed) { TerritoryRules = true };
        world.HardcorePlayers.UnionWith(hardcore ?? []);
        var catalogs = new Dictionary<Civilization, BuildingCatalog>();
        var skipped = new List<string>();

        BuildingCatalog CatalogOf(int player)
        {
            var civilization = player < count && original.Players[PositionOf(player)].Tribe is >= 0 and <= 3
                ? (Civilization)original.Players[PositionOf(player)].Tribe
                : freeChoice is { Count: > 0 } ? freeChoice[player % freeChoice.Count]
                : random is null ? Civilization.Roman
                : drawn.TryGetValue(player, out var people) ? people
                : drawn[player] = peoples[random.Next(peoples.Length)];
            if (!catalogs.TryGetValue(civilization, out var catalog))
                catalogs[civilization] = catalog = BuildingCatalog.Load(civilization, dataDirectory);
            return catalog;
        }

        int buildings = 0, garrisoned = 0;
        foreach (var b in original.Buildings)
        {
            string? name = b.Type < BuildingNames.Length ? BuildingNames[b.Type] : null;
            int owner = OwnerOf(b.Player);
            var definition = name is null ? null : CatalogOf(owner).Find(name);
            if (definition is null)
            {
                skipped.Add($"building type {b.Type} ({name ?? "unknown"}) at {b.X},{b.Y}");
                continue;
            }
            if (world.PlaceBuilding(definition, owner, new GridPoint(b.X, b.Y), force: true) is { } building)
            {
                world.Housing.MarkPopulated(building); // the map lists the residents as settlers
                if (definition.Occupiers.Count > 0)
                    garrisoned += Garrison(world, building, b.Garrison);
                buildings++;
            }
        }

        int piles = 0;
        foreach (var s in original.Stacks)
        {
            if (Materials.FromOriginalStack(s.Type) is not { } material)
            {
                skipped.Add($"pile type {s.Type} at {s.X},{s.Y}");
                continue;
            }
            if (world.AddLooseGoods(new GridPoint(s.X, s.Y), material, s.Count) is not null)
                piles++;
        }

        int settlers = garrisoned;
        foreach (var s in original.Settlers)
        {
            var (profession, soldier, level) = SettlerOf(s.Type);
            var position = new GridPoint(s.X, s.Y);
            int owner = OwnerOf(s.Player);
            var settler = soldier is { } type ? world.SpawnSoldier(position, type, level, owner)
                : profession == Profession.Priest ? world.Magic.SpawnPriest(position, owner, CatalogOf(owner).Civilization)
                : world.Spawn(position, profession, owner);
            if (settler is not null)
                settlers++;
        }

        // Maps without start buildings (multiplayer, also some single player maps like the beginner missions):
        // equal start equipment for every player.
        if (original.Buildings.Count == 0)
        {
            var setup = StartSetup.Load(dataDirectory);
            for (int player = 0; player < count; player++)
            {
                var home = original.Players[PositionOf(player)];
                var start = new GridPoint(home.StartX, home.StartY);
                if (setup.Apply(world, CatalogOf(player), player, start, original.StartResources) is not null)
                    buildings++;
                else
                    skipped.Add($"start of player {player + 1} at {start.X},{start.Y}");
            }
            settlers = world.Settlers.Count;
            piles = world.Piles.Count();
        }

        world.Housing.SettleStartPopulation();
        var playerCatalogs = Enumerable.Range(0, original.Players.Count).Select(CatalogOf).ToArray();
        world.Peoples = playerCatalogs.Select(c => c.Civilization).ToArray();
        world.PlayerCount = original.Players.Count;
        return (world, new LoadReport(buildings, piles, settlers, skipped, playerCatalogs));
    }

    /// <summary>
    /// The map's soldiers of a military building, inside at once; those without a place wait at the door. Without
    /// any, one swordsman of the lowest level (as in the original). Returns how many came.
    /// </summary>
    private static int Garrison(World world, Building building, MapGarrison garrison)
    {
        if (garrison.Count == 0)
            garrison = new MapGarrison([1, 0, 0], [0, 0, 0], [0, 0, 0]);
        int count = 0;
        foreach (var (type, levels) in new[]
                 {
                     (SoldierType.Swordsman, garrison.Swordsmen), (SoldierType.Pikeman, garrison.Pikemen),
                     (SoldierType.Bowman, garrison.Bowmen),
                 })
        {
            for (int level = 1; level <= levels.Count; level++)
            {
                for (int i = 0; i < levels[level - 1]; i++)
                {
                    if (world.SpawnSoldier(building.Door, type, level, building.Player) is not { } soldier)
                        continue;
                    world.Military.Station(building, soldier);
                    count++;
                }
            }
        }
        return count;
    }
}
