using Homestead.Simulation;
using Homestead.Simulation.Map;
using Microsoft.Xna.Framework.Audio;

namespace Homestead.Client;

/// <summary>
/// The sounds of the land in view (<c>ambience</c> in <c>sound.json</c>): the view is sampled now and then, each kind
/// (sea, river, desert, wind, birds) is as loud as its ground fills it, kinds of map objects (bees) as many of them are in it.
/// Lasting kinds string their variants together and pan towards their ground; birds call now and then from one of their fields.
/// </summary>
public sealed class Ambience : IDisposable
{
    private const float SampleSeconds = 0.1f;
    /// <summary>Below this a lasting kind falls silent.</summary>
    private const float Silent = 0.005f;

    private sealed class Channel
    {
        public float Level, Target, Pan, Share;
        public readonly List<(SoundEffectInstance Instance, double Ends)> Playing = new();
        public readonly List<GridPoint> Fields = new();
        public double Next;
    }

    private readonly Dictionary<string, Channel> _channels = new();
    private readonly Random _random = new();
    private double _clock, _nextSample;

    /// <param name="view">The view in world pixels.</param>
    /// <param name="heard">Whether a field sounds at all (not unexplored under the fog).</param>
    /// <param name="seconds">Real time since the last frame (the land sounds on in a pause).</param>
    public void Update(GameMap map, RectangleF view, Func<GridPoint, bool> heard, GameAudio audio, float seconds)
    {
        var config = audio.Config.Ambience;
        _clock += seconds;
        if (_clock >= _nextSample)
        {
            Sample(map, view, heard, config);
            _nextSample = _clock + SampleSeconds;
        }
        float glide = Math.Min(1, seconds / Math.Max(0.01f, config.FadeSeconds));
        foreach (var kind in config.Kinds)
        {
            var channel = ChannelOf(kind.Name);
            channel.Level += (channel.Target - channel.Level) * glide;
            if (kind.Overlap is { } overlap)
                Sound(kind, channel, overlap, audio);
            else
                Call(kind, channel, audio);
        }
    }

    /// <summary>Stops every sound of the land at once (the game is left).</summary>
    public void Silence()
    {
        foreach (var channel in _channels.Values)
        {
            Silence(channel);
            channel.Level = channel.Target = 0;
        }
    }

    private Channel ChannelOf(string name)
    {
        if (!_channels.TryGetValue(name, out var channel))
            _channels[name] = channel = new Channel();
        return channel;
    }

    /// <summary>
    /// The share of each kind's ground in view: every <see cref="AmbienceConfig.SampleStep"/> field, out to
    /// <see cref="AmbienceConfig.Margin"/> half view sizes beyond the edges, there counting less and less. Objects are
    /// counted on every field (a few beehives would slip through the samples).
    /// </summary>
    private void Sample(GameMap map, RectangleF view, Func<GridPoint, bool> heard, AmbienceConfig config)
    {
        float halfWidth = view.Width / 2, halfHeight = view.Height / 2;
        float centerX = view.X + halfWidth, centerY = view.Y + halfHeight;
        float margin = Math.Max(0.01f, config.Margin), reach = 1 + margin;
        int step = Math.Max(1, config.SampleStep);
        var weights = new float[config.Kinds.Length];
        var pans = new float[config.Kinds.Length];
        foreach (var kind in config.Kinds)
            ChannelOf(kind.Name).Fields.Clear();
        float total = 0;
        bool objects = config.Kinds.Any(k => k.Objects is not null);
        int top = Math.Max(0, (int)((centerY - halfHeight * reach) / ScreenProjection.RowHeight));
        int bottom = Math.Min(map.Size - 1, (int)((centerY + halfHeight * reach) / ScreenProjection.RowHeight) + 1);
        for (int y = objects ? top : top - top % step; y <= bottom; y += objects ? 1 : step)
        {
            float rowOffset = y * ScreenProjection.TileWidth / 2f;
            int left = Math.Max(0, (int)((centerX - halfWidth * reach + rowOffset) / ScreenProjection.TileWidth));
            int right = Math.Min(map.Size - 1, (int)((centerX + halfWidth * reach + rowOffset) / ScreenProjection.TileWidth) + 1);
            for (int x = objects ? left : left - left % step; x <= right; x += objects ? 1 : step)
            {
                bool sampled = x % step == 0 && y % step == 0;
                if (!objects && !sampled)
                    continue;
                var p = new GridPoint(x, y);
                var at = ScreenProjection.ToWorld(p);
                float dx = (at.X - centerX) / halfWidth, dy = (at.Y - centerY) / halfHeight;
                float distance = Math.Max(Math.Abs(dx), Math.Abs(dy));
                if (distance >= reach)
                    continue;
                float weight = distance <= 1 ? 1 : 1 - (distance - 1) / margin;
                if (sampled)
                    total += weight;
                if (!heard(p))
                    continue;
                var terrain = map.TerrainAt(p);
                byte code = map.ObjectCodeAt(p);
                for (int k = 0; k < config.Kinds.Length; k++)
                {
                    if (config.Kinds[k].Objects is var (first, last) ? code < first || code > last
                        : !sampled || Array.IndexOf(config.Kinds[k].Ground, terrain) < 0)
                        continue;
                    weights[k] += weight;
                    pans[k] += weight * Math.Clamp(dx * 0.8f, -1, 1);
                    if (distance <= 1)
                        ChannelOf(config.Kinds[k].Name).Fields.Add(p);
                }
            }
        }
        for (int k = 0; k < config.Kinds.Length; k++)
        {
            var kind = config.Kinds[k];
            var channel = ChannelOf(kind.Name);
            channel.Share = kind.Objects is not null ? weights[k] : total > 0 ? weights[k] / total : 0;
            channel.Target = kind.Objects is not null ? Math.Min(1, weights[k] / Math.Max(0.01f, kind.FullCount))
                : channel.Share < kind.MinShare ? 0 : Math.Min(1, channel.Share / Math.Max(0.01f, kind.FullShare));
            if (weights[k] > 0)
                channel.Pan = pans[k] / weights[k];
        }
    }

    /// <summary>A lasting kind: the next variant starts <paramref name="overlap"/> seconds before the last one ends.</summary>
    private void Sound(AmbientKind kind, Channel channel, float overlap, GameAudio audio)
    {
        channel.Playing.RemoveAll(p =>
        {
            if (p.Instance.State != SoundState.Stopped)
                return false;
            p.Instance.Dispose();
            return true;
        });
        if (channel.Level < Silent && channel.Target < Silent)
        {
            if (channel.Playing.Count > 0)
                Silence(channel);
            return;
        }
        if (channel.Playing.Count == 0 || channel.Playing[^1].Ends - overlap <= _clock)
        {
            if (audio.CreateInstance(kind.Sounds[_random.Next(kind.Sounds.Length)]) is var (instance, duration))
            {
                channel.Playing.Add((instance, _clock + duration.TotalSeconds));
                Steer(instance, kind, channel, audio);
                instance.Play();
            }
        }
        foreach (var (instance, _) in channel.Playing)
            Steer(instance, kind, channel, audio);
    }

    private static void Steer(SoundEffectInstance instance, AmbientKind kind, Channel channel, GameAudio audio)
    {
        instance.Volume = Math.Clamp(channel.Level * kind.Volume * audio.EffectsVolume, 0, 1);
        instance.Pan = Math.Clamp(channel.Pan, -1, 1);
    }

    private static void Silence(Channel channel)
    {
        foreach (var (instance, _) in channel.Playing)
            instance.Dispose();
        channel.Playing.Clear();
    }

    /// <summary>Single calls now and then, from one of the kind's fields in view.</summary>
    private void Call(AmbientKind kind, Channel channel, GameAudio audio)
    {
        if (_clock < channel.Next)
            return;
        channel.Next = _clock + kind.MinEvery + _random.NextDouble() * (kind.MaxEvery - kind.MinEvery);
        if (channel.Target > 0 && channel.Fields.Count > 0)
            audio.Play(kind.Sounds[_random.Next(kind.Sounds.Length)], channel.Fields[_random.Next(channel.Fields.Count)],
                kind.Volume * channel.Target);
    }

    public void Dispose() => Silence();
}
