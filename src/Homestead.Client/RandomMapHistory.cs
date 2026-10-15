using System.Text.Json;
using System.Text.Json.Serialization;
using Homestead.Simulation.Map.Generation;

namespace Homestead.Client;

/// <summary>
/// The last random maps played, newest first: settings and seed are enough to make them again.
/// Kept in <c>random-maps.json</c> next to the settings (<c>~/.config/homestead</c>, Windows: <c>%APPDATA%\homestead</c>).
/// </summary>
public sealed class RandomMapHistory
{
    public const int Size = 5;

    public sealed record Entry(RandomMapSettings Settings, int Seed, DateTime Created);

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly List<Entry> _entries = new();

    public RandomMapHistory(string? path = null)
    {
        _path = path ?? Path.Combine(OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "homestead")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "homestead"), "random-maps.json");
        try
        {
            if (File.Exists(_path) && JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(_path), Options) is { } entries)
                _entries.AddRange(entries.Where(e => e.Settings is not null).Take(Size));
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"Zufallskarten-Verlauf nicht lesbar: {e.Message}");
        }
    }

    public IReadOnlyList<Entry> Entries => _entries;

    /// <summary>Puts the map first (once) and saves the list.</summary>
    public void Add(RandomMapSettings settings, int seed)
    {
        _entries.RemoveAll(e => e.Settings == settings && e.Seed == seed);
        _entries.Insert(0, new Entry(settings, seed, DateTime.Now));
        if (_entries.Count > Size)
            _entries.RemoveRange(Size, _entries.Count - Size);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_entries, Options));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"Zufallskarten-Verlauf nicht gespeichert: {e.Message}");
        }
    }
}
