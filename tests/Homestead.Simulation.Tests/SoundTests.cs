using System.Buffers.Binary;
using Homestead.Assets;
using Homestead.Assets.Sound;
using Homestead.Simulation.Buildings;
using Homestead.Simulation.Map;
using Xunit.Abstractions;

namespace Homestead.Simulation.Tests;

public class SoundTests(ITestOutputHelper output)
{
    [Fact]
    public void Fights_cue_a_strike_per_hit_with_the_weapon_and_a_death_with_the_people()
    {
        var world = new World(new GameMap(60), TestConfig.NoTools) { PlayerCount = 2, Peoples = [Civilization.Roman, Civilization.Amazon] };
        world.SoundCues = new();
        world.SpawnSoldier(new GridPoint(28, 30), SoldierType.Swordsman, level: 3);
        world.SpawnSoldier(new GridPoint(20, 30), SoldierType.Bowman, level: 3);
        var amazon = world.SpawnSoldier(new GridPoint(30, 30), SoldierType.Pikeman, player: 1)!;

        for (int t = 0; t < 2_000 && !amazon.IsDead; t++)
            world.Step();

        Assert.True(amazon.IsDead);
        var cues = world.SoundCues;
        Assert.Contains(cues, c => c is { Kind: SoundCueKind.Strike, Soldier: SoldierType.Swordsman, Player: 0 });
        Assert.Contains(cues, c => c is { Kind: SoundCueKind.Strike, Soldier: SoldierType.Bowman, Player: 0, People: Civilization.Roman });
        var death = Assert.Single(cues, c => c.Kind == SoundCueKind.Death);
        Assert.Equal(Civilization.Amazon, death.People);
        Assert.Equal(1, death.Player);
    }

    [Fact]
    public void A_felled_tree_cues_its_fall_where_it_stood()
    {
        var map = new GameMap(60);
        var tree = new GridPoint(34, 36);
        map.SetObject(tree, MapObjectKind.Tree);
        var world = new World(map, TestConfig.NoTools) { SoundCues = new() };
        Assert.NotNull(world.PlaceBuilding(BuildingCatalog.Load(Civilization.Roman)["lumberjack"], 0, new GridPoint(30, 30)));
        Assert.NotNull(world.Spawn(new GridPoint(40, 35), Profession.Carrier));

        for (int t = 0; t < 20_000 && world.Production.TreesCut == 0; t++)
            world.Step();

        var fall = Assert.Single(world.SoundCues, c => c.Kind == SoundCueKind.TreeFalls);
        Assert.Equal(tree, fall.Position);
    }

    [Fact]
    public void A_building_coming_down_cues_its_fall()
    {
        var world = new World(new GameMap(60), TestConfig.NoTools) { SoundCues = new() };
        var lumberjack = world.PlaceBuilding(BuildingCatalog.Load(Civilization.Roman)["lumberjack"], 0, new GridPoint(30, 30))!;

        world.RemoveBuilding(lumberjack);

        var fall = Assert.Single(world.SoundCues, c => c.Kind == SoundCueKind.BuildingFalls);
        Assert.Equal(lumberjack.Position, fall.Position);
    }

    [Fact]
    public void Without_a_list_nothing_is_collected()
    {
        var world = new World(new GameMap(40), TestConfig.NoTools);
        world.SpawnSoldier(new GridPoint(18, 20), SoldierType.Swordsman);
        world.SpawnSoldier(new GridPoint(20, 20), SoldierType.Swordsman, player: 1);
        for (int t = 0; t < 100; t++)
            world.Step();
        Assert.Null(world.SoundCues);
    }

    [Fact]
    public void The_sound_file_lists_variants_and_reads_their_samples()
    {
        // Two sounds: the first with two recordings, the second with one.
        short[][] recordings = [[1, -2, 3], [100, 200], [-7]];
        var file = new List<byte>();
        void Int(int value) => file.AddRange(BitConverter.GetBytes(value));
        file.AddRange([0x44, 0x15, 0x01, 0x00, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x1C, 0x00, 0x00, 0x00]);
        while (file.Count < 0x24)
            Int(0);
        int lists = 0x24 + 2 * 4;
        Int(lists);
        Int(lists + 12);
        int clips = lists + 12 + 8;
        int[] starts = new int[recordings.Length];
        for (int i = 0, at = clips; i < recordings.Length; at += 20 + recordings[i].Length * 2, i++)
            starts[i] = at;
        Int(2); Int(starts[0]); Int(starts[1]);
        Int(1); Int(starts[2]);
        foreach (var samples in recordings)
        {
            Int(20 + samples.Length * 2);
            Int(0x00010001); // PCM, mono
            Int(22050);
            Int(44100);
            Int(0x00100002); // 2 bytes per frame, 16 bits
            foreach (short sample in samples)
                file.AddRange(BitConverter.GetBytes(sample));
        }
        string path = Path.Combine(Path.GetTempPath(), $"homestead-sound-{Guid.NewGuid()}.dat");
        File.WriteAllBytes(path, file.ToArray());
        try
        {
            var sounds = SoundFile.Open(path);
            Assert.Equal(2, sounds.Count);
            Assert.Equal(2, sounds.Variants(0));
            Assert.Equal(1, sounds.Variants(1));
            Assert.Equal(0, sounds.Variants(5));
            var clip = sounds.Read(0, 0);
            Assert.Equal([1, -2, 3], clip.Samples);
            Assert.Equal(22050, clip.SampleRate);
            Assert.Equal([100, 200], sounds.Read(0, 1).Samples);
            Assert.Equal([-7], sounds.Read(1, 0).Samples);
            Assert.Equal(-7, BinaryPrimitives.ReadInt16LittleEndian(sounds.Read(1, 0).ToBytes()));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Against the local installation; passes without one.</summary>
    [Fact]
    public void The_original_sound_file_has_the_sounds_the_game_plays()
    {
        GameInstallation installation;
        try
        {
            installation = GameInstallation.Locate();
        }
        catch (Exception)
        {
            output.WriteLine("No installation found, skipped.");
            return;
        }
        var sounds = SoundFile.Open(installation);
        // Strikes (sword, bow, spear), death, death of an Amazon.
        foreach (int sound in new[] { 30, 33, 34, 35, 113 })
        {
            Assert.True(sounds.Variants(sound) > 0, $"sound {sound}");
            for (int v = 0; v < sounds.Variants(sound); v++)
            {
                var clip = sounds.Read(sound, v);
                Assert.Equal(22050, clip.SampleRate);
                Assert.InRange(clip.Duration.TotalSeconds, 0.1, 3);
            }
        }
        output.WriteLine($"{sounds.Count} sounds");
    }
}
