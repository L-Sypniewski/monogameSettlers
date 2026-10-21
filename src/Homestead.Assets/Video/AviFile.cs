using System.Buffers.Binary;
using System.Text;

namespace Homestead.Assets.Video;

/// <summary>Format of an uncompressed PCM sound stream (<c>WAVEFORMATEX</c>).</summary>
public sealed record AviAudioFormat(int Channels, int SampleRate, int BitsPerSample, int BlockAlign);

/// <summary>
/// Reader for the original's videos (RIFF AVI, e.g. <c>S3/ADD/001/02_03_01.DAT</c>): the main header and the PCM
/// sound stream. The picture stream (Indeo 5) is not decoded here. Format: <c>docs/formate/avi.md</c>.
/// </summary>
public sealed class AviFile
{
    private readonly string _path;
    /// <summary>Position and length of the <c>movi</c> list's contents.</summary>
    private readonly long _moviStart, _moviEnd;
    /// <summary>Index of the sound stream (chunk ids <c>NNwb</c>), −1 without one.</summary>
    private readonly int _audioStream = -1;

    private AviFile(string path)
    {
        _path = path;
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        if (Id(reader) != "RIFF")
            throw new InvalidDataException($"{path}: not a RIFF file.");
        reader.ReadUInt32();
        if (Id(reader) != "AVI ")
            throw new InvalidDataException($"{path}: not an AVI file.");

        int streams = 0;
        string? streamType = null;
        while (stream.Position + 8 <= stream.Length)
        {
            string id = Id(reader);
            long size = reader.ReadUInt32();
            long start = stream.Position;
            if (id == "LIST")
            {
                string kind = Id(reader);
                if (kind == "movi")
                {
                    (_moviStart, _moviEnd) = (stream.Position, Math.Min(start + size, stream.Length));
                    break;
                }
                // hdrl and strl hold the headers: read their chunks in place.
                if (kind is "hdrl" or "strl")
                    continue;
            }
            else if (id == "avih" && size >= 40)
            {
                var data = reader.ReadBytes((int)size);
                MicrosecondsPerFrame = (int)U32(data, 0);
                FrameCount = (int)U32(data, 16);
                Width = (int)U32(data, 32);
                Height = (int)U32(data, 36);
            }
            else if (id == "strh" && size >= 4)
            {
                streamType = Encoding.ASCII.GetString(reader.ReadBytes(4));
                streams++;
            }
            else if (id == "strf" && streamType == "auds" && size >= 16)
            {
                var data = reader.ReadBytes((int)size);
                int format = BinaryPrimitives.ReadUInt16LittleEndian(data);
                if (format == 1)
                {
                    Audio = new AviAudioFormat(BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(2)), (int)U32(data, 4),
                        BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(14)), BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(12)));
                    _audioStream = streams - 1;
                }
            }
            stream.Position = start + size + (size & 1);
        }
        if (_moviEnd == 0)
            throw new InvalidDataException($"{path}: no movi list.");
    }

    public static AviFile Open(string path) => new(path);

    public int Width { get; }

    public int Height { get; }

    public int FrameCount { get; }

    public int MicrosecondsPerFrame { get; }

    public double FramesPerSecond => MicrosecondsPerFrame > 0 ? 1_000_000.0 / MicrosecondsPerFrame : 25;

    public double Seconds => FrameCount / FramesPerSecond;

    /// <summary>The sound stream's format; null without uncompressed PCM sound.</summary>
    public AviAudioFormat? Audio { get; }

    /// <summary>All of the sound stream's samples in order, as stored (interleaved channels, little endian).</summary>
    public byte[] ReadAudio()
    {
        if (Audio is null)
            return [];
        string chunk = $"{_audioStream:00}wb";
        using var stream = File.OpenRead(_path);
        using var reader = new BinaryReader(stream);
        using var samples = new MemoryStream();
        var lists = new Stack<long>();
        stream.Position = _moviStart;
        long end = _moviEnd;
        while (true)
        {
            while (stream.Position + 8 > end && lists.Count > 0)
            {
                stream.Position = end;
                end = lists.Pop();
            }
            if (stream.Position + 8 > end)
                break;
            string id = Id(reader);
            long size = reader.ReadUInt32();
            long start = stream.Position;
            if (id == "LIST")
            {
                // Interleaved files group the chunks in "rec " lists.
                Id(reader);
                lists.Push(end);
                end = Math.Min(start + size, end);
                continue;
            }
            if (id == chunk)
                samples.Write(reader.ReadBytes((int)Math.Min(size, end - start)));
            stream.Position = start + size + (size & 1);
        }
        int whole = (int)(samples.Length / Math.Max(1, Audio.BlockAlign) * Math.Max(1, Audio.BlockAlign));
        return samples.GetBuffer()[..whole];
    }

    private static string Id(BinaryReader reader) => Encoding.ASCII.GetString(reader.ReadBytes(4));

    private static uint U32(byte[] data, int pos) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos));
}
