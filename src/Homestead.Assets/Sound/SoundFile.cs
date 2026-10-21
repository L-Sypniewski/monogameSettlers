using System.Buffers.Binary;

namespace Homestead.Assets.Sound;

/// <summary>One recording: signed 16-bit PCM samples, mono.</summary>
public sealed record SoundClip(short[] Samples, int SampleRate, int Channels)
{
    public TimeSpan Duration => TimeSpan.FromSeconds((double)Samples.Length / Channels / SampleRate);

    /// <summary>The samples as little-endian bytes, as audio APIs take them.</summary>
    public byte[] ToBytes()
    {
        var bytes = new byte[Samples.Length * 2];
        for (int i = 0; i < Samples.Length; i++)
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * 2), Samples[i]);
        return bytes;
    }
}

/// <summary>
/// The sound effects of the original (<c>SND/Siedler3_00.dat</c>): numbered sounds, each with one or more recorded
/// variants of which one is picked at random. Format: <c>docs/formate/snd.md</c>.
/// </summary>
public sealed class SoundFile
{
    public const string FileName = "siedler3_00.dat";

    private static ReadOnlySpan<byte> Signature => [0x44, 0x15, 0x01, 0x00, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x1C, 0x00, 0x00, 0x00];

    private const int TableStart = 0x24;
    private const int ClipHeader = 20;

    private readonly byte[] _data;
    private readonly int[][] _variants;

    private SoundFile(byte[] data)
    {
        _data = data;
        if (data.Length < TableStart + 4 || !data.AsSpan(0, Signature.Length).SequenceEqual(Signature))
            throw new InvalidDataException("Not a sound file (signature)");
        // The table of sounds ends where the first sound's list of variants begins.
        int first = Int(TableStart);
        if (first <= TableStart || first > data.Length || (first - TableStart) % 4 != 0)
            throw new InvalidDataException("Sound table broken");
        _variants = new int[(first - TableStart) / 4][];
        for (int i = 0; i < _variants.Length; i++)
        {
            int list = Int(TableStart + i * 4);
            int count = list + 4 <= data.Length ? Int(list) : 0;
            if (count < 0 || list + 4 + count * 4 > data.Length)
                throw new InvalidDataException($"Sound {i}: variant list broken");
            _variants[i] = Enumerable.Range(0, count).Select(j => Int(list + 4 + j * 4)).ToArray();
        }
    }

    public static SoundFile Open(string path) => new(File.ReadAllBytes(path));

    /// <summary>The effects file in an installation's <c>SND</c> folder (names differ in case).</summary>
    public static SoundFile Open(GameInstallation installation) =>
        Open(Directory.EnumerateFiles(installation.SndDirectory)
                 .FirstOrDefault(f => Path.GetFileName(f).Equals(FileName, StringComparison.OrdinalIgnoreCase))
             ?? throw new FileNotFoundException($"No {FileName} in {installation.SndDirectory}"));

    /// <summary>How many sounds there are (numbers 0 to Count − 1).</summary>
    public int Count => _variants.Length;

    /// <summary>How many recorded variants the sound has (0 for an unknown number).</summary>
    public int Variants(int sound) => sound >= 0 && sound < _variants.Length ? _variants[sound].Length : 0;

    public SoundClip Read(int sound, int variant)
    {
        int start = _variants[sound][variant];
        if (start < 0 || start + ClipHeader > _data.Length)
            throw new InvalidDataException($"Sound {sound}/{variant}: outside the file");
        // Header: length of the whole record, then a WAVEFORMAT (format tag, channels, rate, bytes per second,
        // block align, bits per sample).
        int length = Int(start);
        int channels = BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(start + 6));
        int rate = Int(start + 8);
        int bits = BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(start + 18));
        if (bits != 16 || channels is < 1 or > 2 || rate <= 0 || length < ClipHeader || start + length > _data.Length)
            throw new InvalidDataException($"Sound {sound}/{variant}: unexpected format ({channels} channels, {bits} bit, {rate} Hz)");
        var samples = new short[(length - ClipHeader) / 2 / channels * channels];
        for (int i = 0; i < samples.Length; i++)
            samples[i] = BinaryPrimitives.ReadInt16LittleEndian(_data.AsSpan(start + ClipHeader + i * 2));
        return new SoundClip(samples, rate, channels);
    }

    private int Int(int offset) => BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(offset));
}
