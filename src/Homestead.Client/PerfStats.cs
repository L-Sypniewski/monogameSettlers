using System.Diagnostics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Homestead.Client;

/// <summary>
/// Frame timings and graphics counters, averaged over about a second: frames per second, milliseconds
/// for the simulation, Update (simulation included) and Draw (CPU side), draw calls, sprites and texture switches.
/// </summary>
public sealed class PerfStats
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Stopwatch _section = new();
    private double _windowStart;
    private int _frames;
    private double _simulation, _update, _draw;
    private long _drawCalls, _sprites, _textures;

    public string Summary { get; private set; } = "";

    public void BeginSection() => _section.Restart();

    public void EndSimulation() => _simulation += _section.Elapsed.TotalMilliseconds;

    /// <summary>The whole Update, simulation included.</summary>
    public void AddUpdate(double milliseconds) => _update += milliseconds;

    /// <summary>End of Draw (before Present): adds the draw time and the device's counters of this frame.</summary>
    public void EndDraw(GraphicsDevice device)
    {
        _draw += _section.Elapsed.TotalMilliseconds;
        var metrics = device.Metrics;
        _drawCalls += metrics.DrawCount;
        _sprites += metrics.SpriteCount;
        _textures += metrics.TextureCount;
        _frames++;

        double now = _clock.Elapsed.TotalSeconds;
        if (now - _windowStart < 1)
            return;
        double seconds = now - _windowStart;
        Summary = $"{_frames / seconds:F0} fps · sim {_simulation / _frames:F2} ms · update {_update / _frames:F2} ms (with sim) · " +
                  $"draw {_draw / _frames:F2} ms · {_drawCalls / _frames} draw calls · {_sprites / _frames} sprites · " +
                  $"{_textures / _frames} texture switches";
        _windowStart = now;
        _frames = 0;
        _simulation = _update = _draw = 0;
        _drawCalls = _sprites = _textures = 0;
    }
}
