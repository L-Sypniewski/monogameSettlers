using System.IO.Compression;
using System.Text.Json;
using Homestead.Assets.Maps;
using Homestead.Simulation.Buildings;

namespace Homestead.Simulation.Persistence;

/// <summary>
/// A saved game: the world with everything in it, the building catalogs of the players (the buildings
/// refer to their definitions) and the map it was made from (the client draws the ground from it), plus
/// what the client keeps of its own (camera, colors).
/// </summary>
public sealed class SaveGame
{
    public required World World { get; init; }
    public required IReadOnlyList<BuildingCatalog> Catalogs { get; init; }
    public required OriginalMap Map { get; init; }

    /// <summary>What the own player has seen (fog of war), if the client keeps it.</summary>
    public Map.Vision? Vision { get; init; }

    /// <summary>The client's own state, as it likes (e.g. camera position, player colors).</summary>
    public Dictionary<string, string> Client { get; init; } = new();
}

/// <summary>What the list of saved games shows of one, read without loading the game.</summary>
public sealed record SaveInfo(string Map, string People, DateTime SavedAt, long Tick);

/// <summary>
/// The save slots: files <c>slot N.hsav</c> in <c>saves</c> next to the settings (<c>~/.config/homestead</c>,
/// Windows: <c>%APPDATA%\homestead</c>). A file starts with a short header (<see cref="SaveInfo"/> as JSON)
/// and goes on with the game, compressed (<see cref="Snapshot"/>). Writing goes to a temporary file first,
/// so a failed save never spoils the slot.
/// </summary>
public static class SaveSlots
{
    public const int Count = 8;
    private const string Magic = "HSAV";
    private const int Version = 1;

    /// <summary>Where the slots are; <c>HOMESTEAD_SAVE_DIR</c> moves them (test runs).</summary>
    public static string Directory { get; set; } = Environment.GetEnvironmentVariable("HOMESTEAD_SAVE_DIR") ?? Path.Combine(OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "homestead")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "homestead"), "saves");

    public static string PathOf(int slot) => Path.Combine(Directory, $"slot {slot + 1}.hsav");

    public static void Save(int slot, SaveInfo info, SaveGame game)
    {
        System.IO.Directory.CreateDirectory(Directory);
        string path = PathOf(slot), temporary = path + ".tmp";
        using (var file = File.Create(temporary))
        {
            var header = new BinaryWriter(file);
            header.Write(Magic);
            header.Write(Version);
            header.Write(JsonSerializer.Serialize(info));
            header.Flush();
            using var compressed = new GZipStream(file, CompressionLevel.Fastest, leaveOpen: true);
            using var buffered = new BufferedStream(compressed, 1 << 16);
            Snapshot.Write(buffered, game);
        }
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>What a slot holds, or null when it is empty (or not readable).</summary>
    public static SaveInfo? Info(int slot)
    {
        try
        {
            using var file = File.OpenRead(PathOf(slot));
            return ReadHeader(new BinaryReader(file));
        }
        catch (Exception e) when (e is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static SaveGame Load(int slot)
    {
        using var file = File.OpenRead(PathOf(slot));
        ReadHeader(new BinaryReader(file));
        using var compressed = new GZipStream(file, CompressionMode.Decompress);
        using var buffered = new BufferedStream(compressed, 1 << 16);
        return Snapshot.Read<SaveGame>(buffered);
    }

    private static SaveInfo ReadHeader(BinaryReader reader)
    {
        if (reader.ReadString() != Magic)
            throw new InvalidDataException("Not a saved game.");
        if (reader.ReadInt32() != Version)
            throw new SaveIncompatibleException("Saved by another version.");
        return JsonSerializer.Deserialize<SaveInfo>(reader.ReadString()) ?? throw new InvalidDataException("No header.");
    }
}

/// <summary>The saved game was made by a build whose types or format this one no longer has.</summary>
public sealed class SaveIncompatibleException(string message) : IOException(message);

/// <summary>Why a saved game did not load.</summary>
public enum SaveProblem
{
    /// <summary>Made by another version of the game (<see cref="SaveIncompatibleException"/>).</summary>
    Incompatible = 1,
    /// <summary>The file is there but its contents make no sense (cut off, spoiled, not a saved game).</summary>
    Damaged = 2,
    /// <summary>The file could not be read at all (gone, no permission).</summary>
    Unreadable = 3,
}

/// <summary>
/// What the player is told about a saved game that did not load: the kind of problem and an error code
/// <c>HS-&lt;kind&gt;-&lt;4 hex digits&gt;</c>, the digits a stable hash of the exception, so the same fault gives the same code
/// (and a report names it).
/// </summary>
public sealed record SaveLoadFailure(SaveProblem Problem, string Code)
{
    public static SaveLoadFailure Of(Exception exception)
    {
        var problem = exception switch
        {
            SaveIncompatibleException => SaveProblem.Incompatible,
            EndOfStreamException or InvalidDataException or JsonException => SaveProblem.Damaged,
            IOException or UnauthorizedAccessException => SaveProblem.Unreadable,
            _ => SaveProblem.Damaged,
        };
        // FNV-1a: string.GetHashCode differs from run to run.
        uint hash = 2166136261;
        foreach (char c in exception.GetType().Name + exception.Message)
            hash = (hash ^ c) * 16777619;
        return new SaveLoadFailure(problem, $"HS-{(int)problem}-{(hash ^ hash >> 16) & 0xFFFF:X4}");
    }
}
