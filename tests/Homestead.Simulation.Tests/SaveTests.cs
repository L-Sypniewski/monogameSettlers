using System.Text;
using Homestead.Simulation.Ai;
using Homestead.Simulation.Buildings;
using Homestead.Simulation.Map.Generation;
using Homestead.Simulation.Persistence;

namespace Homestead.Simulation.Tests;

public class SaveTests
{
    [Fact]
    public void A_loaded_game_goes_on_exactly_like_the_saved_one()
    {
        var world = NewGame();
        for (int t = 0; t < 4 * 60 * World.TicksPerSecond; t++)
            world.Step();

        var stream = new MemoryStream();
        Snapshot.Write(stream, world);
        stream.Position = 0;
        var loaded = Snapshot.Read<World>(stream);

        Assert.Equal(Fingerprint(world), Fingerprint(loaded));
        for (int t = 0; t < 2 * 60 * World.TicksPerSecond; t++)
        {
            world.Step();
            loaded.Step();
            if (t % 600 == 0)
                Assert.Equal(Fingerprint(world), Fingerprint(loaded));
        }
        Assert.Equal(Fingerprint(world), Fingerprint(loaded));
        Assert.NotSame(world.Map, loaded.Map);
        // The fingerprint notices a difference: one tick more.
        loaded.Step();
        Assert.NotEqual(Fingerprint(world), Fingerprint(loaded));
    }

    [Fact]
    public void Saving_and_loading_take_little_time_and_space()
    {
        var world = NewGame();
        for (int t = 0; t < 10 * 60 * World.TicksPerSecond; t++)
            world.Step();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var stream = new MemoryStream();
        Snapshot.Write(stream, world);
        long written = watch.ElapsedMilliseconds;
        stream.Position = 0;
        watch.Restart();
        Snapshot.Read<World>(stream);
        long read = watch.ElapsedMilliseconds;
        Console.WriteLine($"snapshot {stream.Length / 1024} KB, write {written} ms, read {read} ms");
        Assert.True(written < 5000 && read < 5000, $"write {written} ms, read {read} ms");
    }

    [Fact]
    public void A_slot_keeps_the_game_with_its_catalogs_and_map()
    {
        var (world, catalogs, map) = NewGameWithMap();
        for (int t = 0; t < 60 * World.TicksPerSecond; t++)
            world.Step();
        SaveSlots.Directory = Path.Combine(Path.GetTempPath(), "homestead-save-test-" + Guid.NewGuid());
        try
        {
            Assert.Null(SaveSlots.Info(2));
            var info = new SaveInfo(map.Name, "Roman", new DateTime(2026, 10, 16, 22, 30, 0), world.Tick);
            SaveSlots.Save(2, info, new SaveGame { World = world, Catalogs = catalogs, Map = map, Client = { ["camera"] = "1,2" } });

            Assert.Equal(info, SaveSlots.Info(2));
            Assert.Null(SaveSlots.Info(3));
            var loaded = SaveSlots.Load(2);
            Assert.Equal(world.Tick, loaded.World.Tick);
            Assert.Equal("1,2", loaded.Client["camera"]);
            Assert.Equal(map.Area.Size, loaded.Map.Area.Size);
            // The buildings use the definitions of the loaded catalogs: new ones are placed from those.
            foreach (var building in loaded.World.Buildings)
                Assert.Same(loaded.Catalogs[building.Player].Find(building.Definition.Name), building.Definition);
        }
        finally
        {
            Directory.Delete(SaveSlots.Directory, recursive: true);
        }
    }

    private static World NewGame() => NewGameWithMap().World;

    private static (World World, IReadOnlyList<Buildings.BuildingCatalog> Catalogs, Assets.Maps.OriginalMap Map) NewGameWithMap()
    {
        var map = RandomMapGenerator.Generate(RandomMapSettings.ParseName("random:2:mirror:7")!.Value.Settings, 7);
        var random = new Random(3);
        var (world, report) = OriginalScenario.Load(map, random: random, seed: 3);
        for (int player = 0; player < report.Catalogs.Count; player++)
            world.AiPlayers.Add(new AiPlayer(world, report.Catalogs[player], player, random: random));
        return (world, report.Catalogs, map);
    }

    /// <summary>Everything that shows: tick, settlers, buildings, piles, map changes, mana.</summary>
    private static string Fingerprint(World world)
    {
        var text = new StringBuilder();
        text.Append(world.Tick).Append('\n');
        foreach (var s in world.Settlers.OrderBy(s => s.Id))
            text.Append($"{s.Id}:{s.Player}:{s.Profession}:{s.Trade}:{s.Position.X},{s.Position.Y}:{s.Carrying}:{s.Health}:{s.IsInside};");
        text.Append('\n');
        foreach (var b in world.Buildings.OrderBy(b => b.Id))
            text.Append($"{b.Id}:{b.Definition.Name}:{b.Player}:{b.State}:{b.Exists}:{string.Join(',', b.Piles.Select(p => p.Count))};");
        text.Append('\n');
        foreach (var p in world.Piles.OrderBy(p => p.Id))
            text.Append($"{p.Id}:{p.Material}:{p.Count}:{p.Position.X},{p.Position.Y};");
        text.Append('\n');
        var map = world.Map!;
        long sum = 0;
        for (int y = 0; y < map.Size; y++)
        for (int x = 0; x < map.Size; x++)
        {
            var point = new GridPoint(x, y);
            sum = sum * 31 + map.ObjectCodeAt(point) + map.HeightAt(point) * 7 + (map.OwnerAt(point) ?? -1) * 13;
        }
        text.Append(sum).Append('\n');
        for (int player = 0; player < 2; player++)
            text.Append(world.Magic.Mana(player)).Append(',');
        return text.ToString();
    }

    [Fact]
    public void The_campaign_progress_and_the_mission_of_a_saved_game_are_kept()
    {
        SaveSlots.Directory = Path.Combine(Path.GetTempPath(), "homestead-save-test-" + Guid.NewGuid());
        try
        {
            Assert.Equal(0, CampaignProgress.Load().Accomplished("settlers", Civilization.Roman));
            var progress = CampaignProgress.Load();
            progress.Accomplish(new MissionRef("settlers", Civilization.Roman, 1));
            progress.Accomplish(new MissionRef("settlers", Civilization.Roman, 0));
            progress.Accomplish(new MissionRef("settlers", Civilization.Asian, 0));
            progress.Save();
            var loaded = CampaignProgress.Load();
            Assert.Equal(2, loaded.Accomplished("settlers", Civilization.Roman));
            Assert.Equal(1, loaded.Accomplished("settlers", Civilization.Asian));
            Assert.Equal(0, loaded.Accomplished("settlers", Civilization.Egyptian));
            Assert.Equal(0, loaded.Accomplished("amazons", Civilization.Roman));

            var mission = new MissionRef("settlers", Civilization.Egyptian, 3);
            Assert.Equal("settlers/Egyptian/3", mission.ToString());
            Assert.Equal(mission, MissionRef.Parse(mission.ToString()));
            Assert.Null(MissionRef.Parse(null));
            Assert.Null(MissionRef.Parse("settlers/Martian/1"));

            // The header names the mission; one written before missions existed has none.
            var (world, catalogs, map) = NewGameWithMap();
            var info = new SaveInfo(map.Name, "Egyptian", new DateTime(2026, 10, 21, 18, 0, 0), world.Tick, mission.ToString());
            SaveSlots.Save(0, info, new SaveGame { World = world, Catalogs = catalogs, Map = map, Client = { ["mission"] = mission.ToString() } });
            Assert.Equal(info, SaveSlots.Info(0));
            Assert.Equal(mission, MissionRef.Parse(SaveSlots.Load(0).Client["mission"]));
            Assert.Null(System.Text.Json.JsonSerializer.Deserialize<SaveInfo>(
                """{"Map":"x","People":"Roman","SavedAt":"2026-10-16T22:30:00","Tick":5}""")!.Mission);
        }
        finally
        {
            Directory.Delete(SaveSlots.Directory, recursive: true);
        }
    }
}
