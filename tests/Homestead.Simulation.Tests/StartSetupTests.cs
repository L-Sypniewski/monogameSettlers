using Homestead.Assets;
using Homestead.Assets.Maps;
using Homestead.Simulation.Buildings;
using Homestead.Simulation.Economy;
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
}
