using System.Buffers.Binary;
using System.Text;

namespace Homestead.Assets.Maps;

/// <summary>
/// Reader for original <c>.map</c> files (also used for savegames of the same format).
/// </summary>
/// <remarks>
/// Layout (little endian): <c>u32 checksum, u32 version (0x0A, Amazons: 0x0B)</c>, then chunks of
/// <c>u32 type (low 16 bits), u32 length incl. this 8 byte header</c>, until type 0.
/// Chunk contents are XOR-obfuscated with a rolling key that starts at the chunk type.
/// Format knowledge cross-checked against JSettlers (MIT), see THIRD_PARTY_NOTICES.md.
/// </remarks>
public sealed class OriginalMap
{
    private OriginalMap(string path, int version, IReadOnlyDictionary<MapChunkType, byte[]> chunks)
    {
        Path = path;
        Version = version;
        Chunks = chunks;
        (IsSinglePlayer, PlayerCount, StartResources) = ReadMapInfo();
        Players = ReadPlayers();
        Area = ReadArea();
        Settlers = ReadRecords(MapChunkType.Settlers, 6, (c, p) => new MapSettler(c[p], c[p + 1], U16(c, p + 2), U16(c, p + 4)));
        Buildings = ReadRecords(MapChunkType.Buildings, 12, (c, p) => new MapBuilding(c[p], c[p + 1], U16(c, p + 2), U16(c, p + 4)));
        Stacks = ReadRecords(MapChunkType.Stacks, 8, (c, p) => new MapStack(U16(c, p), U16(c, p + 2), c[p + 4], c[p + 5]));
    }

    private OriginalMap(string name, MapArea area, IReadOnlyList<MapPlayer> players, IReadOnlyList<MapStack> stacks, int startResources)
    {
        Path = name;
        Version = 0x0A;
        Chunks = new Dictionary<MapChunkType, byte[]>();
        (IsSinglePlayer, PlayerCount, StartResources) = (false, players.Count, startResources);
        Players = players;
        Area = area;
        Settlers = [];
        Buildings = [];
        Stacks = stacks;
    }

    /// <summary>A multiplayer map made in memory (random maps): no buildings or settlers, every player gets the start equipment.</summary>
    public static OriginalMap Create(string name, MapArea area, IReadOnlyList<MapPlayer> players, IReadOnlyList<MapStack> stacks, int startResources) =>
        new(name, area, players, stacks, startResources);

    public string Path { get; }

    public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);

    public int Version { get; }

    public bool IsSinglePlayer { get; }

    public int PlayerCount { get; }

    public int StartResources { get; }

    public IReadOnlyList<MapPlayer> Players { get; }

    public MapArea Area { get; }

    /// <summary>Settlers placed in the editor (chunk 7).</summary>
    public IReadOnlyList<MapSettler> Settlers { get; }

    /// <summary>Buildings placed in the editor (chunk 8). Soldier counts are not read yet.</summary>
    public IReadOnlyList<MapBuilding> Buildings { get; }

    /// <summary>Piles of goods placed in the editor (chunk 9).</summary>
    public IReadOnlyList<MapStack> Stacks { get; }

    /// <summary>Decoded chunk contents, including those not interpreted yet.</summary>
    public IReadOnlyDictionary<MapChunkType, byte[]> Chunks { get; }

    public static OriginalMap Open(string path)
    {
        var (version, chunks) = ReadChunks(path, _ => true);
        return new OriginalMap(path, version, chunks);
    }

    /// <summary>Number of players, read without decoding the rest of the map (for map lists).</summary>
    public static int ReadPlayerCount(string path)
    {
        var (_, chunks) = ReadChunks(path, type => type == MapChunkType.MapInfo);
        return chunks.TryGetValue(MapChunkType.MapInfo, out var info) && info.Length >= 8
            ? (int)U32(info, 4)
            : throw new InvalidDataException($"{path}: no {MapChunkType.MapInfo} chunk.");
    }

    private static (int Version, Dictionary<MapChunkType, byte[]> Chunks) ReadChunks(string path, Func<MapChunkType, bool> wanted)
    {
        byte[] data = File.ReadAllBytes(path);
        if (data.Length < 16)
            throw new InvalidDataException($"{path}: file too short.");

        int version = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4));
        if (version is not (0x0A or 0x0B))
            throw new InvalidDataException($"{path}: unknown map version 0x{version:x}.");

        var chunks = new Dictionary<MapChunkType, byte[]>();
        int pos = 8;
        while (pos + 8 <= data.Length)
        {
            uint rawType = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos));
            int length = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos + 4));
            if (rawType == 0)
                break;
            if (length < 8 || pos + length > data.Length)
                throw new InvalidDataException($"{path}: broken chunk at 0x{pos:x}.");

            var type = (MapChunkType)(rawType & 0xffff);
            if (wanted(type))
                chunks[type] = Decode(data.AsSpan(pos + 8, length - 8), (byte)type);
            pos += length;
        }
        return (version, chunks);
    }

    private static byte[] Decode(ReadOnlySpan<byte> encoded, byte key)
    {
        var decoded = new byte[encoded.Length];
        for (int i = 0; i < encoded.Length; i++)
        {
            byte value = (byte)(encoded[i] ^ key);
            decoded[i] = value;
            key = (byte)((key << 1) ^ value);
        }
        return decoded;
    }

    private byte[] Chunk(MapChunkType type) =>
        Chunks.TryGetValue(type, out var chunk) ? chunk : throw new InvalidDataException($"{Path}: no {type} chunk.");

    private (bool SinglePlayer, int Players, int StartResources) ReadMapInfo()
    {
        var info = Chunk(MapChunkType.MapInfo);
        return (U32(info, 0) == 1, (int)U32(info, 4), (int)U32(info, 8));
    }

    private IReadOnlyList<MapPlayer> ReadPlayers()
    {
        // Per player: u32 tribe, u32 startX, u32 startY, char[33] name.
        const int recordSize = 45;
        var chunk = Chunk(MapChunkType.PlayerInfo);
        var players = new List<MapPlayer>();
        for (int i = 0; i < PlayerCount && (i + 1) * recordSize <= chunk.Length; i++)
        {
            int p = i * recordSize;
            var nameBytes = chunk.AsSpan(p + 12, 33);
            int end = nameBytes.IndexOf((byte)0);
            string name = Encoding.Latin1.GetString(end >= 0 ? nameBytes[..end] : nameBytes);
            players.Add(new MapPlayer((int)U32(chunk, p), (int)U32(chunk, p + 4), (int)U32(chunk, p + 8), name));
        }
        return players;
    }

    private MapArea ReadArea()
    {
        // u32 size (maps are square), then 6 bytes per point:
        // height, landscape, object, owner, accessibility, resources (high nibble type, low nibble amount).
        var chunk = Chunk(MapChunkType.Area);
        int size = (int)U32(chunk, 0);
        if (size <= 0 || 4 + size * size * 6 > chunk.Length)
            throw new InvalidDataException($"{Path}: area chunk does not match size {size}.");

        var area = new MapArea(size);
        int pos = 4;
        for (int i = 0; i < size * size; i++, pos += 6)
        {
            area.Height[i] = chunk[pos];
            area.Landscape[i] = chunk[pos + 1];
            area.Object[i] = chunk[pos + 2];
            area.Owner[i] = chunk[pos + 3];
            area.Accessibility[i] = chunk[pos + 4];
            area.Resources[i] = chunk[pos + 5];
        }
        return area;
    }

    /// <summary>Optional chunk with <c>u32 count</c> followed by fixed-size records.</summary>
    private IReadOnlyList<T> ReadRecords<T>(MapChunkType type, int recordSize, Func<byte[], int, T> read)
    {
        if (!Chunks.TryGetValue(type, out var chunk) || chunk.Length < 4)
            return [];
        int count = (int)U32(chunk, 0);
        if (count < 0 || 4 + (long)count * recordSize > chunk.Length)
            throw new InvalidDataException($"{Path}: {type} chunk holds {count} records but is only {chunk.Length} bytes.");
        var records = new T[count];
        for (int i = 0; i < count; i++)
            records[i] = read(chunk, 4 + i * recordSize);
        return records;
    }

    private static uint U32(byte[] data, int pos) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos));

    private static int U16(byte[] data, int pos) => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(pos));
}

public enum MapChunkType
{
    MapInfo = 1,
    PlayerInfo = 2,
    TeamInfo = 3,
    Preview = 4,
    Area = 6,
    Settlers = 7,
    Buildings = 8,
    Stacks = 9,
    WinCondition = 10,
    QuestText = 11,
    QuestTip = 12,
}

public sealed record MapPlayer(int Tribe, int StartX, int StartY, string Name);

/// <summary>A settler from the map. Type codes see docs/formate/map.md.</summary>
public sealed record MapSettler(int Player, int Type, int X, int Y);

/// <summary>A building from the map, positioned at its anchor point.</summary>
public sealed record MapBuilding(int Player, int Type, int X, int Y);

/// <summary>A pile of goods from the map.</summary>
public sealed record MapStack(int X, int Y, int Type, int Count);

/// <summary>Per-point raw map data, row-major (<c>index = y * Size + x</c>).</summary>
public sealed class MapArea
{
    public MapArea(int size)
    {
        Size = size;
        int count = size * size;
        Height = new byte[count];
        Landscape = new byte[count];
        Object = new byte[count];
        Owner = new byte[count];
        Accessibility = new byte[count];
        Resources = new byte[count];
    }

    public int Size { get; }
    public byte[] Height { get; }
    public byte[] Landscape { get; }
    public byte[] Object { get; }
    public byte[] Owner { get; }
    public byte[] Accessibility { get; }
    public byte[] Resources { get; }

    public int Index(int x, int y) => y * Size + x;
}
