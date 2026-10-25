using System.Text.Json;
using Homestead.Assets;
using Homestead.Assets.Sound;
using Homestead.Simulation;
using Homestead.Simulation.Buildings;
using Homestead.Simulation.Map;
using Microsoft.Xna.Framework.Audio;

namespace Homestead.Client;

/// <summary>
/// A sound of the sound file, played in one of its recorded variants: any of them, or one of <see cref="Variants"/>.
/// In <c>sound.json</c> a number, or <c>{ "sound": n, "variants": [ … ] }</c>.
/// </summary>
public sealed record SoundRef(int Sound, int[]? Variants = null)
{
    public static SoundRef Parse(JsonElement e) => e.ValueKind == JsonValueKind.Number
        ? new SoundRef(e.GetInt32())
        : new SoundRef(e.GetProperty("sound").GetInt32(),
            e.TryGetProperty("variants", out var variants) ? variants.EnumerateArray().Select(v => v.GetInt32()).ToArray() : null);
}

/// <summary>A work sound: <see cref="Sound"/> when the work animation reaches <see cref="At"/> (0–1) of a round.</summary>
public sealed record WorkSound(SoundRef Sound, float At);

/// <summary>A workshop's sound (one of them) while its worker works inside, about every <see cref="EverySeconds"/>.</summary>
public sealed record WorkshopSound(SoundRef[] Sounds, float EverySeconds);

/// <summary>
/// A kind of sound of the land (<c>ambience.kinds</c>): as loud as its <see cref="Ground"/> fills the view. With
/// <see cref="Overlap"/> its variants follow each other without a pause, otherwise single calls every
/// <see cref="MinEvery"/>–<see cref="MaxEvery"/> seconds.
/// </summary>
public sealed record AmbientKind(string Name, SoundRef[] Sounds, TerrainType[] Ground, float Volume, float FullShare, float MinShare,
    float? Overlap, float MinEvery, float MaxEvery);

/// <summary>Swamp bubbles (<c>ambience.bubbles</c>): where they rise, how often, the sound of a bursting one.</summary>
public sealed record BubbleConfig(SoundRef Sound, TerrainType[] Ground, float Volume, float PerFieldSeconds);

/// <summary>The sounds of the land in view (<c>ambience</c> in <c>sound.json</c>).</summary>
public sealed record AmbienceConfig(int SampleStep, float Margin, float FadeSeconds, AmbientKind[] Kinds, BubbleConfig Bubbles)
{
    public static AmbienceConfig Parse(JsonElement e)
    {
        static TerrainType[] Ground(JsonElement e) => e.GetProperty("ground").EnumerateArray().Select(g => Enum.Parse<TerrainType>(g.GetString()!)).ToArray();
        static SoundRef[] Sounds(JsonElement e) =>
            e.TryGetProperty("sounds", out var sounds) ? sounds.EnumerateArray().Select(SoundRef.Parse).ToArray() : [SoundRef.Parse(e)];
        var bubbles = e.GetProperty("bubbles");
        return new AmbienceConfig(
            e.GetProperty("sampleStep").GetInt32(),
            e.GetProperty("margin").GetSingle(),
            e.GetProperty("fadeSeconds").GetSingle(),
            e.GetProperty("kinds").EnumerateObject().Select(p =>
            {
                var every = p.Value.TryGetProperty("everySeconds", out var seconds) ? seconds.EnumerateArray().Select(s => s.GetSingle()).ToArray() : [0f, 0f];
                return new AmbientKind(p.Name, Sounds(p.Value), Ground(p.Value), p.Value.GetProperty("volume").GetSingle(),
                    p.Value.GetProperty("fullShare").GetSingle(),
                    p.Value.TryGetProperty("minShare", out var min) ? min.GetSingle() : 0,
                    p.Value.TryGetProperty("overlap", out var overlap) ? overlap.GetSingle() : null,
                    every[0], every[1]);
            }).ToArray(),
            new BubbleConfig(SoundRef.Parse(bubbles), Ground(bubbles), bubbles.GetProperty("volume").GetSingle(),
                bubbles.GetProperty("perFieldSeconds").GetSingle()));
    }
}

/// <summary>The values of <c>data/sound.json</c>.</summary>
public sealed record SoundConfig(
    IReadOnlyDictionary<SoldierType, SoundRef> Strike,
    IReadOnlyDictionary<Civilization, IReadOnlyDictionary<SoldierType, SoundRef>> StrikeByPeople,
    SoundRef Death,
    IReadOnlyDictionary<Civilization, SoundRef> DeathByPeople,
    IReadOnlyDictionary<SoundCueKind, SoundRef> Simple,
    SoundRef GeologistFinds,
    int GeologistMinAmount,
    IReadOnlyDictionary<string, SoundRef> Sowing,
    SoundRef Command,
    IReadOnlyDictionary<string, SoundRef> Ui,
    SoundRef CombatEvent,
    SoundRef OtherEvent,
    IReadOnlyDictionary<string, float> Volumes,
    int MaxVoices,
    int PerFrame,
    float Hearing,
    IReadOnlyDictionary<Activity, WorkSound> Work,
    IReadOnlyDictionary<string, WorkshopSound> Workshops,
    AmbienceConfig Ambience,
    IReadOnlyDictionary<int, float> Gain,
    IReadOnlyDictionary<Civilization, int[]> Music,
    float MusicVolume,
    float MinPause,
    float MaxPause,
    float FadeSeconds)
{
    /// <summary>A war machine's shot (the gong's sound) by its people (<c>machineShot</c>).</summary>
    public IReadOnlyDictionary<Civilization, SoundRef> MachineShot { get; init; } = new Dictionary<Civilization, SoundRef>();

    /// <summary>Cues with one sound each, by their key in <c>effects</c> (and <c>volume</c>).</summary>
    private static readonly (SoundCueKind Kind, string Key)[] SimpleCues =
    [
        (SoundCueKind.TreeFalls, "treeFalls"), (SoundCueKind.BuildingFalls, "buildingFalls"), (SoundCueKind.Spell, "spell"),
        (SoundCueKind.Healing, "healing"), (SoundCueKind.Claiming, "claiming"), (SoundCueKind.MachineHit, "machineHit"),
    ];

    public static SoundConfig Load(string? path = null)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path ?? Path.Combine(BuildingCatalog.DefaultDataDirectory, "sound.json")));
        var root = document.RootElement;
        var effects = root.GetProperty("effects");
        var music = root.GetProperty("music");
        var pause = music.GetProperty("pauseSeconds");
        var deaths = effects.GetProperty("death").EnumerateObject().ToArray();
        // Per soldier type; an object named after a people holds that people's own.
        var strikes = effects.GetProperty("strike").EnumerateObject().ToArray();
        static Dictionary<SoldierType, SoundRef> Weapons(IEnumerable<JsonProperty> properties) =>
            properties.Where(p => Enum.TryParse<SoldierType>(p.Name, out _)).ToDictionary(p => Enum.Parse<SoldierType>(p.Name), p => SoundRef.Parse(p.Value));
        static Dictionary<string, SoundRef> Named(JsonElement e) => e.EnumerateObject().ToDictionary(p => p.Name, p => SoundRef.Parse(p.Value));
        var events = effects.GetProperty("events");
        var finds = effects.GetProperty("geologistFinds");
        return new SoundConfig(
            Weapons(strikes),
            strikes.Where(p => Enum.TryParse<Civilization>(p.Name, out _)).ToDictionary(
                p => Enum.Parse<Civilization>(p.Name), p => (IReadOnlyDictionary<SoldierType, SoundRef>)Weapons(p.Value.EnumerateObject())),
            SoundRef.Parse(deaths.Single(p => p.Name == "default").Value),
            deaths.Where(p => p.Name != "default").ToDictionary(p => Enum.Parse<Civilization>(p.Name), p => SoundRef.Parse(p.Value)),
            SimpleCues.ToDictionary(c => c.Kind, c => SoundRef.Parse(effects.GetProperty(c.Key))),
            SoundRef.Parse(finds),
            finds.GetProperty("minAmount").GetInt32(),
            Named(effects.GetProperty("sowing")),
            SoundRef.Parse(effects.GetProperty("command")),
            Named(effects.GetProperty("ui")),
            SoundRef.Parse(events.GetProperty("combat")),
            SoundRef.Parse(events.GetProperty("other")),
            root.GetProperty("volume").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetSingle()),
            root.GetProperty("maxVoices").GetInt32(),
            root.GetProperty("perFrame").GetInt32(),
            root.GetProperty("hearing").GetSingle(),
            root.GetProperty("work").EnumerateObject().ToDictionary(
                p => Enum.Parse<Activity>(p.Name), p => new WorkSound(SoundRef.Parse(p.Value), p.Value.GetProperty("at").GetSingle())),
            root.GetProperty("workshops").EnumerateObject().ToDictionary(
                p => p.Name, p => new WorkshopSound(
                    p.Value.TryGetProperty("sounds", out var sounds) ? sounds.EnumerateArray().Select(SoundRef.Parse).ToArray() : [SoundRef.Parse(p.Value)],
                    p.Value.GetProperty("everySeconds").GetSingle())),
            AmbienceConfig.Parse(root.GetProperty("ambience")),
            root.GetProperty("gain").EnumerateObject().ToDictionary(p => int.Parse(p.Name), p => p.Value.GetSingle()),
            music.GetProperty("tracks").EnumerateObject().ToDictionary(
                p => Enum.Parse<Civilization>(p.Name), p => p.Value.EnumerateArray().Select(t => t.GetInt32()).ToArray()),
            music.GetProperty("volume").GetSingle(),
            pause[0].GetSingle(),
            pause[1].GetSingle(),
            music.GetProperty("fadeSeconds").GetSingle())
        {
            MachineShot = effects.TryGetProperty("machineShot", out var machines)
                ? machines.EnumerateObject().Where(p => !p.Name.StartsWith('_'))
                    .ToDictionary(p => Enum.Parse<Civilization>(p.Name), p => SoundRef.Parse(p.Value))
                : new Dictionary<Civilization, SoundRef>(),
        };
    }

    /// <summary>The sound for a cue, if it has one.</summary>
    public SoundRef? SoundOf(SoundCue cue) => cue.Kind switch
    {
        SoundCueKind.Strike => cue.Soldier is not { } type ? null
            : cue.People is { } people && StrikeByPeople.TryGetValue(people, out var own) && own.TryGetValue(type, out var sound) ? sound
            : Strike.GetValueOrDefault(type),
        SoundCueKind.Death => cue.People is { } people ? DeathByPeople.GetValueOrDefault(people, Death) : Death,
        SoundCueKind.GeologistFinds => cue.Amount >= GeologistMinAmount ? GeologistFinds : null,
        SoundCueKind.Sowing => cue.Trade is { } trade ? Sowing.GetValueOrDefault(trade) : null,
        SoundCueKind.MachineShot => cue.People is { } people ? MachineShot.GetValueOrDefault(people) : null,
        var kind => Simple.GetValueOrDefault(kind),
    };

    /// <summary>
    /// Loudness of a kind of sound (<c>volume</c>: strike, death, treeFalls, buildingFalls, spell, healing, claiming,
    /// geologistFinds, sowing, command, ui, events, work), 1 if not given.
    /// </summary>
    public float VolumeOf(string kind) => Volumes.GetValueOrDefault(kind, 1);

    public float VolumeOf(SoundCueKind kind) => VolumeOf(kind switch
    {
        SoundCueKind.Strike => "strike",
        SoundCueKind.Death => "death",
        SoundCueKind.GeologistFinds => "geologistFinds",
        SoundCueKind.Sowing => "sowing",
        SoundCueKind.MachineShot => "machineShot",
        var other => SimpleCues.FirstOrDefault(c => c.Kind == other).Key ?? "",
    });

    /// <summary>
    /// The music for a people: its own pieces, or with <paramref name="ownPeople"/> off every piece (its own first, so the
    /// game still opens with its first); none without a people.
    /// </summary>
    public int[] TracksFor(Civilization? people, bool ownPeople) =>
        people is not { } p ? []
        : ownPeople ? Music.GetValueOrDefault(p, [])
        : Music.GetValueOrDefault(p, []).Concat(Music.Values.SelectMany(t => t)).Distinct().ToArray();
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
            if (Config.SoundOf(cue) is { } sound)
                Play(sound, cue.Position, Config.VolumeOf(cue.Kind));
    }

    /// <summary>Plays the sound (one of its variants) as from the point.</summary>
    public void Play(SoundRef sound, GridPoint at, float volume)
    {
        if (_place(at) is not { } p)
            return;
        float distance = Math.Max(Math.Abs(p.X), Math.Abs(p.Y));
        float fade = distance <= 1 ? 1 : 1 - (distance - 1) / Math.Max(0.01f, Config.Hearing - 1);
        if (fade > 0.02f)
            Start(sound, volume * fade, Math.Clamp(p.X * 0.8f, -1, 1));
    }

    /// <summary>Plays the sound as from the middle of the screen (answers to the player).</summary>
    public void Play(SoundRef sound, float volume) => Start(sound, volume, 0);

    /// <summary>
    /// The sound (one of its variants) for the caller to play, steer and dispose – sounds that last, like the sea;
    /// its volume is not yet times <see cref="EffectsVolume"/>. Null if it cannot sound.
    /// </summary>
    public (SoundEffectInstance Instance, TimeSpan Duration)? CreateInstance(SoundRef reference)
    {
        if (_file is null || EffectsVolume <= 0 || EffectOf(reference) is not { } effect)
            return null;
        try
        {
            return (effect.CreateInstance(), effect.Duration);
        }
        catch (Exception e) when (e is InstancePlayLimitException or NoAudioHardwareException)
        {
            return null;
        }
    }

    /// <summary>One of the sound's variants (any, or one of those it names).</summary>
    private SoundEffect? EffectOf(SoundRef reference)
    {
        int sound = reference.Sound;
        if (_file is null || _file.Variants(sound) == 0)
            return null;
        var variants = reference.Variants?.Where(v => v >= 0 && v < _file.Variants(sound)).ToArray();
        int variant = variants is null ? _random.Next(_file.Variants(sound))
            : variants.Length > 0 ? variants[_random.Next(variants.Length)]
            : -1;
        return variant < 0 ? null : Effect(sound, variant);
    }

    private void Start(SoundRef reference, float volume, float pan)
    {
        int sound = reference.Sound;
        if (_file is null || EffectsVolume <= 0 || _playing.Count >= Config.MaxVoices
            || _startedThisFrame.GetValueOrDefault(sound) >= Config.PerFrame || EffectOf(reference) is not { } effect)
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
            var clip = _file!.Read(sound, variant).Amplified(Config.Gain.GetValueOrDefault(sound, 1));
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
