using Homestead.Assets;
using Homestead.Assets.Maps;
using Homestead.Simulation.Buildings;
using Homestead.Simulation.Economy;
using Homestead.Simulation.Pathfinding;
using Xunit.Abstractions;

namespace Homestead.Simulation.Tests;

public class StartSetupTests(ITestOutputHelper output)
{
    [Fact]
    public void Every_player_of_a_multiplayer_map_starts_with_a_tower_land_settlers_and_goods()
    {
        GameInstallation installation;
        try { installation = GameInstallation.Locate(); }
        catch (Exception) { output.WriteLine("No installation found, skipped."); return; }

        var maps = installation.MapFiles.Select(OriginalMap.Open)
            .Where(m => !m.IsSinglePlayer && m.Buildings.Count == 0).Take(6).ToList();
        Assert.NotEmpty(maps);
        var romans = BuildingCatalog.Load(Civilization.Roman);
        foreach (var original in maps)
        {
            var (world, report) = OriginalScenario.Load(original);
            for (int t = 0; t < 300; t++)
                world.Step();

            output.WriteLine($"{original.Name}: level {original.StartResources}, {report.Buildings} towers, {report.Piles} piles, " +
                             $"{report.Settlers} settlers; skipped: {string.Join("; ", report.Skipped)}");
            var towers = world.Buildings.Where(b => b.Definition.Name == "tower").ToList();
            Assert.Equal(original.Players.Count, towers.Count);
            foreach (var tower in towers)
            {
                Assert.True(world.Military.IsOccupied(tower), $"{original.Name}: tower of player {tower.Player + 1} occupied");
                Assert.Equal(tower.Player, world.Map!.OwnerAt(tower.Position));
                // Bowmen wait outside: the tower takes them in only when the player asks.
                Assert.Equal(0, world.Military.Wanted(tower, SoldierType.Bowman));
            }
            // Equal conditions: the same number of settlers for everyone.
            var perPlayer = world.Settlers.GroupBy(s => s.Player).Select(g => g.Count()).Distinct().ToList();
            Assert.Single(perPlayer);
            Assert.True(perPlayer[0] > 10);

            // Specialists start with their trade and tool, not as carriers.
            var first = world.Settlers.Where(s => s.Player == 0).ToList();
            Assert.Equal(2, first.Count(s => s.Profession == Profession.Builder && s.Tool == Material.Hammer));
            Assert.Equal(2, first.Count(s => s.Profession == Profession.Digger && s.Tool == Material.Blade));
            Assert.Equal(1, first.Count(s => s.Trade == "STONECUTTER" && s.Tool == Material.Pick));
            Assert.True(first.Count(s => s.Trade == "MINER" && s.Tool == Material.Pick) >= 4);
            Assert.True(first.Count(s => s.Trade == "SMITH" && s.Tool == Material.Hammer) >= 3);

            // Player 1 can build next to its tower.
            var own = towers.Single(t => t.Player == 0);
            bool fits = Enumerable.Range(4, 20).SelectMany(r => new[] { new GridPoint(own.Position.X + r, own.Position.Y + r / 2),
                    new GridPoint(own.Position.X - r, own.Position.Y - r / 2), new GridPoint(own.Position.X, own.Position.Y + r) })
                .Any(p => world.CanPlace(romans["sawmill"], p, out _, player: 0));
            Assert.True(fits, $"{original.Name}: a sawmill fits on the own land");
        }
    }

    [Fact]
    public void Players_can_take_other_start_positions()
    {
        GameInstallation installation;
        try { installation = GameInstallation.Locate(); }
        catch (Exception) { output.WriteLine("No installation found, skipped."); return; }

        var original = installation.MapFiles.Select(OriginalMap.Open)
            .First(m => !m.IsSinglePlayer && m.Buildings.Count == 0 && m.Players.Count >= 2);
        int count = original.Players.Count;
        // Reversed: player i starts where map player count - 1 - i would.
        var positions = Enumerable.Range(0, count).Select(i => count - 1 - i).ToArray();
        var (world, _) = OriginalScenario.Load(original, positions: positions);

        for (int player = 0; player < count; player++)
        {
            var home = original.Players[positions[player]];
            var tower = world.Buildings.Single(b => b.Player == player && b.Definition.Name == "tower");
            Assert.True(PathFinder.Distance(tower.Position, new GridPoint(home.StartX, home.StartY)) <= 2,
                $"player {player + 1} starts at map player {positions[player] + 1}");
        }
        Assert.Throws<ArgumentException>(() => OriginalScenario.Load(original, positions: Enumerable.Repeat(0, count).ToArray()));
    }

    [Fact]
    public void Single_player_maps_without_buildings_get_the_start_equipment()
    {
        GameInstallation installation;
        try { installation = GameInstallation.Locate(); }
        catch (Exception) { output.WriteLine("No installation found, skipped."); return; }

        // E.g. Beginner I and III: no buildings or settlers in the map, the player would start with nothing.
        var maps = installation.MapFiles.Select(OriginalMap.Open).Where(m => m.IsSinglePlayer && m.Buildings.Count == 0).ToList();
        foreach (var original in maps)
        {
            var (world, _) = OriginalScenario.Load(original);
            for (int player = 0; player < original.Players.Count; player++)
            {
                var home = original.Players[player];
                var tower = world.Buildings.Single(b => b.Player == player && b.Definition.Name == "tower");
                Assert.True(PathFinder.Distance(tower.Position, new GridPoint(home.StartX, home.StartY)) <= 2,
                    $"{original.Name}: player {player + 1} starts at its start position");
                Assert.True(world.Settlers.Count(s => s.Player == player) > 10, $"{original.Name}: player {player + 1} has settlers");
            }
        }
    }
}
