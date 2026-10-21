using Homestead.Simulation.Buildings;
using Microsoft.Xna.Framework.Audio;
using NVorbis;

namespace Homestead.Client;

/// <summary>
/// Background music: the installation's <c>MUSIC/TrackNN.ogg</c>, shuffled from the list of the <see cref="People"/>
/// (<c>music</c> in <c>sound.json</c>) with a pause between two pieces, decoded while playing. Another people fades
/// the piece out and goes on with theirs; none is silence. Fed from <see cref="Update"/>, once a frame.
/// </summary>
public sealed class MusicPlayer : IDisposable
{
    private const int FramesPerBuffer = 8192;
    private const int BuffersAhead = 3;

    private readonly string? _directory;
    private readonly SoundConfig _config;
    private readonly Random _random = new();
    private VorbisReader? _reader;
    private DynamicSoundEffectInstance? _output;
    private (int Rate, int Channels) _format;
    private float[] _floats = [];
    private byte[] _bytes = [];
    private int _lastTrack = -1;
    private bool _ending;
    private float _fade = 1;
    private bool _fadingOut;
    private float _pause;
    private bool _broken;
    private Civilization? _people;

    public MusicPlayer(string? directory, SoundConfig config)
    {
        _directory = directory;
        _config = config;
        if (directory is null)
            Console.WriteLine("Music off: no MUSIC folder in the installation");
    }

    /// <summary>Whose music plays; null: none.</summary>
    public Civilization? People
    {
        get => _people;
        set
        {
            if (_people == value)
                return;
            _people = value;
            if (_reader is not null)
                _fadingOut = true;
            else
                _pause = 0.5f;
        }
    }

    /// <summary>Loudness, 0–1 (the player's setting); 0 stops the music.</summary>
    public float Volume { get; set; } = 1;

    /// <summary>The track playing, for display and tests; −1 while silent.</summary>
    public int Track => _reader is null ? -1 : _lastTrack;

    public void Update(float seconds)
    {
        if (_broken || _directory is null)
            return;
        if (Volume <= 0 && _reader is not null)
            Stop();
        if (_reader is null)
        {
            if (Volume <= 0 || People is not { } people || !_config.Music.TryGetValue(people, out var tracks) || tracks.Length == 0)
                return;
            _pause -= seconds;
            if (_pause > 0)
                return;
            Start(tracks);
            return;
        }
        if (_fadingOut)
        {
            _fade -= seconds / Math.Max(0.01f, _config.FadeSeconds);
            if (_fade <= 0)
            {
                Stop();
                _pause = 0.5f;
                return;
            }
        }
        try
        {
            _output!.Volume = Math.Clamp(Volume * _config.MusicVolume * _fade, 0, 1);
            while (!_ending && _output.PendingBufferCount < BuffersAhead)
                Feed();
            if (_ending && _output.PendingBufferCount == 0)
            {
                Stop();
                _pause = _config.MinPause + (float)_random.NextDouble() * Math.Max(0, _config.MaxPause - _config.MinPause);
            }
        }
        catch (Exception e) when (e is NoAudioHardwareException or InvalidOperationException or IOException or InvalidDataException)
        {
            Console.WriteLine($"Music off: {e.Message}");
            Stop();
            _broken = true;
        }
    }

    private void Start(int[] tracks)
    {
        // Another piece than the last, if there is one.
        var choices = tracks.Length > 1 ? tracks.Where(t => t != _lastTrack).ToArray() : tracks;
        int track = choices[_random.Next(choices.Length)];
        string? path = Directory.EnumerateFiles(_directory!)
            .FirstOrDefault(f => Path.GetFileName(f).Equals($"Track{track:00}.ogg", StringComparison.OrdinalIgnoreCase));
        _lastTrack = track;
        if (path is null)
        {
            Console.WriteLine($"Music: Track{track:00}.ogg missing");
            _pause = _config.MaxPause;
            return;
        }
        try
        {
            _reader = new VorbisReader(path);
            if (_output is null || _format != (_reader.SampleRate, _reader.Channels))
            {
                _output?.Dispose();
                _format = (_reader.SampleRate, _reader.Channels);
                _output = new DynamicSoundEffectInstance(_reader.SampleRate, _reader.Channels == 2 ? AudioChannels.Stereo : AudioChannels.Mono);
            }
            _floats = new float[FramesPerBuffer * _reader.Channels];
            _bytes = new byte[_floats.Length * 2];
            _ending = _fadingOut = false;
            _fade = 1;
            _output.Volume = Math.Clamp(Volume * _config.MusicVolume, 0, 1);
            while (_output.PendingBufferCount < BuffersAhead && !_ending)
                Feed();
            _output.Play();
            Console.WriteLine($"Music: Track{track:00} ({People})");
        }
        catch (Exception e) when (e is NoAudioHardwareException or InvalidOperationException or IOException or InvalidDataException or ArgumentException)
        {
            Console.WriteLine($"Music off: {e.Message}");
            Stop();
            _broken = true;
        }
    }

    private void Feed()
    {
        int read = _reader!.ReadSamples(_floats, 0, _floats.Length);
        if (read <= 0)
        {
            _ending = true;
            return;
        }
        // Whole frames only.
        read -= read % _reader.Channels;
        for (int i = 0; i < read; i++)
        {
            short sample = (short)Math.Clamp((int)(_floats[i] * short.MaxValue), short.MinValue, short.MaxValue);
            _bytes[i * 2] = (byte)sample;
            _bytes[i * 2 + 1] = (byte)(sample >> 8);
        }
        if (read > 0)
            _output!.SubmitBuffer(_bytes, 0, read * 2);
        if (read < _floats.Length)
            _ending = true;
    }

    private void Stop()
    {
        _output?.Stop();
        _reader?.Dispose();
        _reader = null;
        _ending = _fadingOut = false;
        _fade = 1;
    }

    public void Dispose()
    {
        Stop();
        _output?.Dispose();
        _output = null;
    }
}
