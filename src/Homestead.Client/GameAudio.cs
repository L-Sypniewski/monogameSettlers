using System.Text.Json;
using Homestead.Assets;
using Homestead.Assets.Sound;
using Homestead.Simulation;
using Homestead.Simulation.Buildings;
using Microsoft.Xna.Framework.Audio;

namespace Homestead.Client;

/// <summary>A work sound: <see cref="Sound"/> when the work animation reaches <see cref="At"/> (0–1) of a round.</summary>
public sealed record WorkSound(int Sound, float At);

/// <summary>A workshop's sound while its worker works inside, about every <see cref="EverySeconds"/>.</summary>
public sealed record WorkshopSound(int Sound, float EverySeconds);

/// <summary>The values of <c>data/sound.json</c>.</summary>
public sealed record SoundConfig(
    IReadOnlyDictionary<SoldierType, int> Strike,
    int Death,
    IReadOnlyDictionary<Civilization, int> DeathByPeople,
    int TreeFalls,
    int Command,
    IReadOnlyDictionary<string, float> Volumes,
    int MaxVoices,
    int PerFrame,
    float Hearing,
    IReadOnlyDictionary<Activity, WorkSound> Work,
    IReadOnlyDictionary<string, WorkshopSound> Workshops,
    IReadOnlyDictionary<Civilization, int[]> Music,
    float MusicVolume,
    float MinPause,
    float MaxPause,
    float FadeSeconds)
{
    public static SoundConfig Load(string? path = null)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path ?? Path.Combine(BuildingCatalog.DefaultDataDirectory, "sound.json")));
        var root = document.RootElement;
        var effects = root.GetProperty("effects");
        var music = root.GetProperty("music");
        var pause = music.GetProperty("pauseSeconds");
        var deaths = effects.GetProperty("death").EnumerateObject().ToArray();
        return new SoundConfig(
            effects.GetProperty("strike").EnumerateObject().ToDictionary(p => Enum.Parse<SoldierType>(p.Name), p => p.Value.GetInt32()),
            deaths.Single(p => p.Name == "default").Value.GetInt32(),
            deaths.Where(p => p.Name != "default").ToDictionary(p => Enum.Parse<Civilization>(p.Name), p => p.Value.GetInt32()),
            effects.GetProperty("treeFalls").GetInt32(),
            effects.GetProperty("command").GetInt32(),
            root.GetProperty("volume").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetSingle()),
            root.GetProperty("maxVoices").GetInt32(),
            root.GetProperty("perFrame").GetInt32(),
            root.GetProperty("hearing").GetSingle(),
            root.GetProperty("work").EnumerateObject().ToDictionary(
                p => Enum.Parse<Activity>(p.Name), p => new WorkSound(p.Value.GetProperty("sound").GetInt32(), p.Value.GetProperty("at").GetSingle())),
            root.GetProperty("workshops").EnumerateObject().ToDictionary(
                p => p.Name, p => new WorkshopSound(p.Value.GetProperty("sound").GetInt32(), p.Value.GetProperty("everySeconds").GetSingle())),
            music.GetProperty("tracks").EnumerateObject().ToDictionary(
                p => Enum.Parse<Civilization>(p.Name), p => p.Value.EnumerateArray().Select(t => t.GetInt32()).ToArray()),
            music.GetProperty("volume").GetSingle(),
            pause[0].GetSingle(),
            pause[1].GetSingle(),
            music.GetProperty("fadeSeconds").GetSingle());
    }

    /// <summary>The sound for a cue, or −1.</summary>
    public int SoundOf(SoundCue cue) => cue.Kind switch
    {
        SoundCueKind.Strike => cue.Soldier is { } type ? Strike.GetValueOrDefault(type, -1) : -1,
        SoundCueKind.Death => cue.People is { } people ? DeathByPeople.GetValueOrDefault(people, Death) : Death,
        SoundCueKind.TreeFalls => TreeFalls,
        _ => -1,
    };

    /// <summary>Loudness of a kind of sound (<c>volume</c>: strike, death, treeFalls, command, work), 1 if not given.</summary>
    public float VolumeOf(string kind) => Volumes.GetValueOrDefault(kind, 1);

    public float VolumeOf(SoundCueKind kind) => VolumeOf(kind switch
    {
        SoundCueKind.Strike => "strike",
        SoundCueKind.Death => "death",
        SoundCueKind.TreeFalls => "treeFalls",
        _ => "",
    });

    /// <summary>The music for a people: its own pieces, or every piece if <paramref name="ownPeople"/> is off; none without a people.</summary>
    public int[] TracksFor(Civilization? people, bool ownPeople) =>
        people is not { } p ? []
        : ownPeople ? Music.GetValueOrDefault(p, [])
        : Music.Values.SelectMany(t => t).Distinct().Order().ToArray();
}

/// <summary>
/// Sound effects from the original's sound file, placed by where they happen: heard within the view (panned
/// left/right), fading out a little beyond it, and only where the player sees (<see cref="BeginFrame"/>).
/// Recordings are converted once, on first use. Without the sound file or an audio device everything stays silent.
/// </summary>
public sealed class GameAudio : IDisposable
{
    private readonly SoundFile? _file;
    private readonly Dictionary<(int Sound, int Variant), SoundEffect?> _effects = new();
    private readonly List<SoundEffectInstance> _playing = new();
    private readonly Dictionary<int, int> _startedThisFrame = new();
    private readonly Random _random = new();
    private Func<GridPoint, (float X, float Y)?> _place = _ => null;

    public GameAudio(GameInstallation installation, SoundConfig config)
    {
        Config = config;
        try
        {
            _file = SoundFile.Open(installation);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Console.WriteLine($"Sound effects off: {e.Message}");
        }
        Music = new MusicPlayer(installation.MusicDirectory, config);
    }

    public SoundConfig Config { get; }

    public MusicPlayer Music { get; }

    /// <summary>Loudness of the effects, 0–1 (the player's setting).</summary>
    public float EffectsVolume { get; set; } = 1;

    /// <summary>
    /// Starts a frame's sounds. <paramref name="place"/> says for a point how it is heard: horizontal and vertical distance
    /// from the view's center in half view sizes (−1…1 is on screen), or null if it cannot be heard (not in sight).
    /// </summary>
    public void BeginFrame(Func<GridPoint, (float X, float Y)?> place)
    {
        _place = place;
        _playing.RemoveAll(i =>
        {
            if (i.State != SoundState.Stopped)
                return false;
            i.Dispose();
            return true;
        });
        _startedThisFrame.Clear();
    }

    /// <summary>Whether a sound at the point would be heard at all (to skip work for the rest).</summary>
    public bool Hears(GridPoint at) =>
        _file is not null && EffectsVolume > 0 && _place(at) is { } p && Math.Max(Math.Abs(p.X), Math.Abs(p.Y)) < Config.Hearing;

    public void Play(IEnumerable<SoundCue> cues)
    {
        foreach (var cue in cues)
            Play(Config.SoundOf(cue), cue.Position, Config.VolumeOf(cue.Kind));
    }

    /// <summary>Plays the sound (one of its variants) as from the point.</summary>
    public void Play(int sound, GridPoint at, float volume)
    {
        if (_place(at) is not { } p)
            return;
        float distance = Math.Max(Math.Abs(p.X), Math.Abs(p.Y));
        float fade = distance <= 1 ? 1 : 1 - (distance - 1) / Math.Max(0.01f, Config.Hearing - 1);
        if (fade > 0.02f)
            Start(sound, volume * fade, Math.Clamp(p.X * 0.8f, -1, 1));
    }

    /// <summary>Plays the sound as from the middle of the screen (answers to the player).</summary>
    public void Play(int sound, float volume) => Start(sound, volume, 0);

    private void Start(int sound, float volume, float pan)
    {
        if (_file is null || EffectsVolume <= 0 || sound < 0 || _file.Variants(sound) == 0 || _playing.Count >= Config.MaxVoices
            || _startedThisFrame.GetValueOrDefault(sound) >= Config.PerFrame)
            return;
        if (Effect(sound, _random.Next(_file.Variants(sound))) is not { } effect)
            return;
        try
        {
            var instance = effect.CreateInstance();
            instance.Volume = Math.Clamp(EffectsVolume * volume, 0, 1);
            instance.Pan = pan;
            // A little variety in pitch, so that many of the same do not sound like one.
            instance.Pitch = (float)(_random.NextDouble() * 0.1 - 0.05);
            instance.Play();
            _playing.Add(instance);
            _startedThisFrame[sound] = _startedThisFrame.GetValueOrDefault(sound) + 1;
        }
        catch (Exception e) when (e is InstancePlayLimitException or NoAudioHardwareException)
        {
        }
    }

    private SoundEffect? Effect(int sound, int variant)
    {
        if (_effects.TryGetValue((sound, variant), out var effect))
            return effect;
        try
        {
            var clip = _file!.Read(sound, variant);
            effect = clip.Samples.Length == 0
                ? null
                : new SoundEffect(clip.ToBytes(), clip.SampleRate, clip.Channels == 2 ? AudioChannels.Stereo : AudioChannels.Mono);
        }
        catch (Exception e) when (e is InvalidDataException or NoAudioHardwareException or ArgumentException)
        {
            Console.WriteLine($"Sound {sound}/{variant}: {e.Message}");
            effect = null;
        }
        _effects[(sound, variant)] = effect;
        return effect;
    }

    public void Dispose()
    {
        foreach (var instance in _playing)
            instance.Dispose();
        _playing.Clear();
        foreach (var effect in _effects.Values)
            effect?.Dispose();
        _effects.Clear();
        Music.Dispose();
    }
}
