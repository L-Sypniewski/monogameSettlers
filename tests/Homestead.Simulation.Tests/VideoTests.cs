using System.Text;
using Homestead.Assets;
using Homestead.Assets.Video;
using Xunit.Abstractions;

namespace Homestead.Simulation.Tests;

/// <summary>The original's videos (AVI): header and PCM sound; the parts with a real installation pass unchecked without it.</summary>
public class VideoTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("homestead-video-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static byte[] Chunk(string id, byte[] data)
    {
        var bytes = new List<byte>(Encoding.ASCII.GetBytes(id));
        bytes.AddRange(BitConverter.GetBytes(data.Length));
        bytes.AddRange(data);
        if (data.Length % 2 == 1)
            bytes.Add(0);
        return bytes.ToArray();
    }

    private static byte[] List(string kind, params byte[][] chunks) =>
        Chunk("LIST", Encoding.ASCII.GetBytes(kind).Concat(chunks.SelectMany(c => c)).ToArray());

    private static byte[] Ints(params int[] values) => values.SelectMany(BitConverter.GetBytes).ToArray();

    [Fact]
    public void The_sound_is_read_from_interleaved_records()
    {
        // 40 000 µs per frame (25 fps), 3 frames, 320 × 240; a video and a 16 bit stereo PCM stream.
        var avih = Chunk("avih", Ints(40_000, 0, 0, 0, 3, 0, 2, 0, 320, 240, 0, 0, 0, 0));
        var video = List("strl", Chunk("strh", Encoding.ASCII.GetBytes("vidsiv50").Concat(new byte[48]).ToArray()));
        byte[] format = [1, 0, 2, 0, .. BitConverter.GetBytes(22050), .. BitConverter.GetBytes(88200), 4, 0, 16, 0];
        var audio = List("strl", Chunk("strh", Encoding.ASCII.GetBytes("auds").Concat(new byte[52]).ToArray()), Chunk("strf", format));
        byte[] first = [1, 2, 3, 4], second = [5, 6, 7, 8, 9, 10, 11, 12];
        var movi = List("movi",
            List("rec ", Chunk("00dc", [0xAA, 0xBB, 0xCC]), Chunk("01wb", first)),
            List("rec ", Chunk("00dc", [0xDD]), Chunk("01wb", second)));
        var riff = Chunk("RIFF", Encoding.ASCII.GetBytes("AVI ").Concat(List("hdrl", avih, video, audio)).Concat(Chunk("JUNK", new byte[10])).Concat(movi).ToArray());
        string path = Path.Combine(_directory, "test.dat");
        File.WriteAllBytes(path, riff);

        var avi = AviFile.Open(path);
        Assert.Equal((320, 240, 3), (avi.Width, avi.Height, avi.FrameCount));
        Assert.Equal(25, avi.FramesPerSecond, 3);
        Assert.Equal(new AviAudioFormat(2, 22050, 16, 4), avi.Audio);
        Assert.Equal(first.Concat(second).ToArray(), avi.ReadAudio());
    }

    [Fact]
    public void The_campaign_intros_have_pictures_and_sound()
    {
        GameInstallation installation;
        try { installation = GameInstallation.Locate(); }
        catch (Exception) { output.WriteLine("No installation found, skipped."); return; }

        foreach (string intro in new[] { "S3/ADD/001/02_01_01.DAT", "s3/add/001/02_02_01.dat", "S3/ADD/001/02_03_01.DAT" })
        {
            string? path = installation.FindPath(intro);
            if (path is null)
            {
                output.WriteLine($"{intro} missing, skipped.");
                continue;
            }
            var avi = AviFile.Open(path);
            output.WriteLine($"{intro}: {avi.Width}×{avi.Height}, {avi.FrameCount} frames, {avi.Seconds:0.0} s, {avi.Audio}");
            Assert.Equal((320, 240), (avi.Width, avi.Height));
            Assert.Equal(new AviAudioFormat(2, 22050, 16, 4), avi.Audio);
            // The sound lasts as long as the pictures, within a second.
            double seconds = avi.ReadAudio().Length / (22050.0 * 4);
            Assert.InRange(seconds, avi.Seconds - 1, avi.Seconds + 1);
        }
        Assert.Null(installation.FindPath("S3/ADD/001/none.DAT"));
    }
}
