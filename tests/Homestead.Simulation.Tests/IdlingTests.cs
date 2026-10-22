using Homestead.Simulation.Buildings;
using Homestead.Simulation.Economy;
using Homestead.Simulation.Map;

namespace Homestead.Simulation.Tests;

public class IdlingTests
{
    private static readonly BuildingCatalog Romans = BuildingCatalog.Load(Civilization.Roman);

    [Fact]
    public void Idle_carriers_leave_the_storehouse_yard()
    {
        var world = new World(new GameMap(70), ProductionConfig.None);
        var stock = world.PlaceBuilding(Romans["stock"], 0, new GridPoint(35, 35))!;
        foreach (var pile in stock.Piles)
            for (int i = 0; i < 4; i++)
                world.Spawn(pile.Position, Profession.Carrier);
        Assert.Contains(world.Settlers, s => !IsOutOfTheWay(world, s));

        Run(world, 1_000);

        Assert.True(world.Idling.Strolls > 0);
        Assert.All(world.Settlers, s => Assert.True(IsOutOfTheWay(world, s), $"carrier at {s.Position} still in the way"));
    }

    [Fact]
    public void Idle_soldiers_step_apart_at_once_and_keep_a_field_between_them()
    {
        var world = new World(new GameMap(70), ProductionConfig.None);
        var soldiers = Enumerable.Range(0, 12).Select(_ => world.SpawnSoldier(new GridPoint(35, 35), SoldierType.Swordsman)!).ToList();
        bool Apart() => soldiers.All(a => soldiers.All(b => a == b || Pathfinding.PathFinder.Distance(a.Position, b.Position) >= 2));
        Assert.False(Apart());

        // Well under the idle time others wait before they stroll, plus the few steps.
        Run(world, Idling.IdleTicks + 4 * World.StepTicks);

        Assert.True(Apart(), string.Join(" ", soldiers.Select(s => $"{s.Position.X},{s.Position.Y}")));
        Assert.All(soldiers, s => Assert.InRange(Pathfinding.PathFinder.Distance(s.Position, new GridPoint(35, 35)), 0, 5)); // they stay together
    }

    [Fact]
    public void A_stranded_carrier_flees_to_the_nearest_own_land()
    {
        var map = new GameMap(80);
        foreach (var p in Pathfinding.PathFinder.Circle(new GridPoint(60, 40), 6).Where(map.Contains))
            map.SetOwner(p, 0);
        var world = new World(map, ProductionConfig.None) { TerritoryRules = true };
        var carrier = world.Spawn(new GridPoint(20, 40), Profession.Carrier)!;

        Run(world, 60 * World.TicksPerSecond, () => map.OwnerAt(carrier.Position) == 0);

        Assert.False(carrier.IsDead);
        Assert.Equal(0, map.OwnerAt(carrier.Position));
    }

    [Fact]
    public void A_carrier_whose_land_is_lost_drops_its_load_and_flees_instead_of_crossing_foreign_land()
    {
        // Own land all over; the carrier walks the trunk east. Halfway the middle becomes player 1's land.
        var map = new GameMap(90);
        var all = Enumerable.Range(0, 90).SelectMany(y => Enumerable.Range(0, 90).Select(x => new GridPoint(x, y))).ToList();
        foreach (var p in all)
            map.SetOwner(p, 0);
        var world = new World(map, ProductionConfig.None) { TerritoryRules = true };
        var sawmill = world.PlaceBuilding(Romans["sawmill"], 0, new GridPoint(75, 45))!;
        world.AddLooseGoods(new GridPoint(10, 45), Material.Trunk, 1);
        var carrier = world.Spawn(new GridPoint(10, 40), Profession.Carrier)!;
        Run(world, 120 * World.TicksPerSecond, () => carrier.Carrying is not null && carrier.Position.X >= 38);
        Assert.Equal(Material.Trunk, carrier.Carrying);

        bool Middle(GridPoint p) => p.X - p.Y / 2 is >= 15 and < 65;
        foreach (var p in all.Where(Middle))
            map.SetOwner(p, 1);
        Run(world, 2);

        Assert.Null(carrier.Carrying);
        Assert.False(carrier.HasTask);
        Assert.Equal(1, world.Piles.Where(p => p.Owner is null && p.Material == Material.Trunk).Sum(p => p.Count));

        int deepest = 0;
        Run(world, 60 * World.TicksPerSecond, () =>
        {
            if (map.OwnerAt(carrier.Position) == 1)
                deepest = Math.Max(deepest, Math.Min(carrier.Position.X - carrier.Position.Y / 2 - 15, 65 - (carrier.Position.X - carrier.Position.Y / 2)));
            return map.OwnerAt(carrier.Position) == 0 && carrier.Destination is null;
        });
        Assert.Equal(0, map.OwnerAt(carrier.Position));
        Assert.False(carrier.IsDead);
        Assert.Equal(0, sawmill.Piles.Single(p => p.Role == PileRole.Request).Count);
        Assert.True(deepest < 15, $"walked {deepest} fields deep into the foreign land");
    }

    [Fact]
    public void A_carrier_with_no_own_land_wanders_and_dies_within_a_minute()
    {
        var map = new GameMap(80);
        foreach (var p in Pathfinding.PathFinder.Circle(new GridPoint(60, 40), 6).Where(map.Contains))
            map.SetOwner(p, 1);
        var world = new World(map, ProductionConfig.None) { TerritoryRules = true };
        var carrier = world.Spawn(new GridPoint(20, 40), Profession.Carrier)!;
        var start = carrier.Position;
        bool moved = false;

        Run(world, (Idling.MinLostSeconds - 1) * World.TicksPerSecond, () => (moved |= carrier.Position != start) && false);
        Assert.False(carrier.IsDead);
        Assert.True(moved, "a lost carrier wanders about");

        Run(world, (Idling.MaxLostSeconds - Idling.MinLostSeconds + 5) * World.TicksPerSecond);
        Assert.True(carrier.IsDead);
    }

    [Fact]
    public void A_strolling_carrier_takes_a_job_at_once()
    {
        var world = new World(new GameMap(70), ProductionConfig.None);
        var stock = world.PlaceBuilding(Romans["stock"], 0, new GridPoint(35, 35))!;
        var carrier = world.Spawn(stock.Piles[0].Position, Profession.Carrier)!;
        Run(world, 1_000, stopWhen: () => carrier.IsStrolling);
        Assert.True(carrier.IsStrolling);

        world.AddLooseGoods(new GridPoint(10, 10), Material.Plank, 1);
        Run(world, 10);

        Assert.Contains(world.Logistics.Jobs, j => j.Carrier == carrier);
        Assert.False(carrier.IsStrolling);
    }

    /// <summary>Not on a building's ground or surroundings, not next to a pile.</summary>
    private static bool IsOutOfTheWay(World world, Settler s)
    {
        if (!world.IsFreeGround(s.Position))
            return false;
        for (int d = 0; d < DirectionExtensions.Count; d++)
            if (world.Map!.Contains(s.Position.Neighbor((Direction)d)) && world.PileAt(s.Position.Neighbor((Direction)d)) is not null)
                return false;
        return true;
    }

    private static void Run(World world, int ticks, Func<bool>? stopWhen = null)
    {
        for (int t = 0; t < ticks && stopWhen?.Invoke() != true; t++)
            world.Step();
    }
}
