using System.Text.Json;
using Homestead.Assets;
using Homestead.Assets.Sound;
using Homestead.Simulation;
using Homestead.Simulation.Buildings;
using Microsoft.Xna.Framework.Audio;

namespace Homestead.Client;

/// <summary>The values of <c>data/sound.json</c>.</summary>
public sealed record SoundConfig(
    IReadOnlyDictionary<SoldierType, int> Strike,
    int Death,
    IReadOnlyDictionary<Civilization, int> DeathByPeople,
    float StrikeVolume,
    float DeathVolume,
    int MaxVoices,
    int PerFrame,
    float Hearing,
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
        var volume = root.GetProperty("volume");
        var music = root.GetProperty("music");
        var pause = music.GetProperty("pauseSeconds");
        var deaths = effects.GetProperty("death").EnumerateObject().ToArray();
        return new SoundConfig(
            effects.GetProperty("strike").EnumerateObject().ToDictionary(p => Enum.Parse<SoldierType>(p.Name), p => p.Value.GetInt32()),
            deaths.Single(p => p.Name == "default").Value.GetInt32(),
            deaths.Where(p => p.Name != "default").ToDictionary(p => Enum.Parse<Civilization>(p.Name), p => p.Value.GetInt32()),
            volume.GetProperty("strike").GetSingle(),
            volume.GetProperty("death").GetSingle(),
            root.GetProperty("maxVoices").GetInt32(),
            root.GetProperty("perFrame").GetInt32(),
            root.GetProperty("hearing").GetSingle(),
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
        _ => -1,
    };

    public float VolumeOf(SoundCueKind kind) => kind == SoundCueKind.Death ? DeathVolume : StrikeVolume;
}

/// <summary>
/// Sound effects from the original's sound file, placed by where they happen: the world's <see cref="SoundCue"/>s are
/// heard within the view (panned left/right), fading out a little beyond it, and only where the player sees.
/// Recordings are converted once, on first use. Without the sound file or an audio device everything stays silent.
/// </summary>
public sealed class GameAudio : IDisposable
{
    private readonly SoundConfig _config;
    private readonly SoundFile? _file;
    private readonly Dictionary<(int Sound, int Variant), SoundEffect?> _effects = new();
    private readonly List<SoundEffectInstance> _playing = new();
    private readonly Dictionary<int, int> _startedThisFrame = new();
    private readonly Random _random = new();

    public GameAudio(GameInstallation installation, SoundConfig config)
    {
        _config = config;
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

    public MusicPlayer Music { get; }

    /// <summary>Loudness of the effects, 0–1 (the player's setting).</summary>
    public float EffectsVolume { get; set; } = 1;

    /// <summary>
    /// Plays the cues. <paramref name="place"/> says for a point how it is heard: horizontal and vertical distance from
    /// the view's center in half view sizes (−1…1 is on screen), or null if it cannot be heard (not in sight).
    /// </summary>
    public void Play(IEnumerable<SoundCue> cues, Func<GridPoint, (float X, float Y)?> place)
    {
        _playing.RemoveAll(i =>
        {
            if (i.State != SoundState.Stopped)
                return false;
            i.Dispose();
            return true;
        });
        _startedThisFrame.Clear();
        if (_file is null || EffectsVolume <= 0)
            return;
        foreach (var cue in cues)
        {
            int sound = _config.SoundOf(cue);
            if (sound < 0 || _file.Variants(sound) == 0 || _playing.Count >= _config.MaxVoices)
                continue;
            if (_startedThisFrame.GetValueOrDefault(sound) >= _config.PerFrame || place(cue.Position) is not { } at)
                continue;
            float distance = Math.Max(Math.Abs(at.X), Math.Abs(at.Y));
            float fade = distance <= 1 ? 1 : 1 - (distance - 1) / Math.Max(0.01f, _config.Hearing - 1);
            if (fade <= 0.02f)
                continue;
            var effect = Effect(sound, _random.Next(_file.Variants(sound)));
            if (effect is null)
                continue;
            try
            {
                var instance = effect.CreateInstance();
                instance.Volume = Math.Clamp(EffectsVolume * _config.VolumeOf(cue.Kind) * fade, 0, 1);
                instance.Pan = Math.Clamp(at.X * 0.8f, -1, 1);
                // A little variety in pitch, so that many of the same do not sound like one.
                instance.Pitch = (float)(_random.NextDouble() * 0.1 - 0.05);
                instance.Play();
                _playing.Add(instance);
                _startedThisFrame[sound] = _startedThisFrame.GetValueOrDefault(sound) + 1;
            }
            catch (Exception e) when (e is InstancePlayLimitException or NoAudioHardwareException)
            {
                return;
            }
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
