using Homestead.Simulation;
using Homestead.Client.Terrain;
using Homestead.Simulation.Map;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Homestead.Client;

/// <summary>
/// Bubbles rising in the swamp, only where the player looks (decoration, not part of the game): a bubble grows
/// (object file sequence 106), then sinks back (107) or grows on and bursts (108) – with a blub
/// (<c>ambience.bubbles</c> in <c>sound.json</c>).
/// </summary>
public sealed class SwampBubbles
{
    private const float FramesPerSecond = 10;
    private const int RiseFrames = 6, SinkFrames = 6, BurstFrames = 8;
    /// <summary>The frame of the burst sequence where the bubble breaks.</summary>
    private const int BreakFrame = 3;
    private const int MaxBubbles = 60;

    private sealed class Bubble(GridPoint at, Vector2 offset, double start, bool bursts)
    {
        public GridPoint At { get; } = at;
        public Vector2 Offset { get; } = offset;
        public double Start { get; } = start;
        public bool Bursts { get; } = bursts;
        public bool Sounded { get; set; }

        public int Frames => RiseFrames + (Bursts ? BurstFrames : SinkFrames);
    }

    private readonly List<Bubble> _bubbles = new();
    private readonly Random _random = new();
    private GameMap? _map;
    private double _due;

    /// <param name="view">The view in world pixels.</param>
    /// <param name="seen">Whether the player sees the field now.</param>
    /// <param name="now">Game time in seconds (stands still with the game).</param>
    /// <param name="seconds">Game time since the last frame.</param>
    public void Update(GameMap map, RectangleF view, Func<GridPoint, bool> seen, BubbleConfig config, GameAudio? audio,
        double now, float seconds)
    {
        if (_map != map)
        {
            _bubbles.Clear();
            _map = map;
        }
        _bubbles.RemoveAll(b => (now - b.Start) * FramesPerSecond >= b.Frames);
        foreach (var bubble in _bubbles)
            if (bubble.Bursts && !bubble.Sounded && (now - bubble.Start) * FramesPerSecond >= RiseFrames + BreakFrame)
            {
                bubble.Sounded = true;
                audio?.Play(config.Sound, bubble.At, config.Volume);
            }

        // Every field in view brings one bubble every PerFieldSeconds on average: fields picked at random, swamp ones bubble.
        float fields = view.Width / ScreenProjection.TileWidth * (view.Height / ScreenProjection.RowHeight);
        _due += fields * seconds / Math.Max(1, config.PerFieldSeconds);
        for (; _due >= 1; _due--)
        {
            if (_bubbles.Count >= MaxBubbles)
                continue;
            var world = new Vector2(view.X + (float)_random.NextDouble() * view.Width, view.Y + (float)_random.NextDouble() * view.Height);
            int y = (int)MathF.Round(world.Y / ScreenProjection.RowHeight);
            var p = new GridPoint((int)MathF.Round((world.X + y * ScreenProjection.TileWidth / 2f) / ScreenProjection.TileWidth), y);
            if (!map.Contains(p) || Array.IndexOf(config.Ground, map.TerrainAt(p)) < 0 || !seen(p))
                continue;
            var offset = new Vector2((float)(_random.NextDouble() - 0.5) * ScreenProjection.TileWidth,
                (float)(_random.NextDouble() - 0.5) * ScreenProjection.RowHeight);
            _bubbles.Add(new Bubble(p, offset, now, _random.Next(2) == 0));
        }
    }

    public void Draw(SpriteBatch batch, EconomyRenderer renderer, TerrainMesh terrain, double now)
    {
        foreach (var bubble in _bubbles)
        {
            int frame = (int)((now - bubble.Start) * FramesPerSecond);
            if (frame >= bubble.Frames)
                continue;
            var anchor = ScreenProjection.ToWorld(bubble.At.X, bubble.At.Y, terrain.HeightAt(bubble.At.X, bubble.At.Y)) + bubble.Offset;
            if (frame < RiseFrames)
                renderer.DrawBubble(batch, anchor, EconomyRenderer.BubbleRises, frame);
            else
                renderer.DrawBubble(batch, anchor, bubble.Bursts ? EconomyRenderer.BubbleBursts : EconomyRenderer.BubbleSinks, frame - RiseFrames);
        }
    }
}
