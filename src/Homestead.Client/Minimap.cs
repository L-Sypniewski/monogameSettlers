using Homestead.Assets.Maps;
using Homestead.Simulation.Map;
using Microsoft.Xna.Framework.Graphics;

namespace Homestead.Client;

/// <summary>
/// The minimap's picture: one texel per grid point, landscape colors (<see cref="MapOverview"/>) where
/// player 1 has explored, black elsewhere. No settlers or buildings yet.
/// </summary>
public sealed class Minimap : IDisposable
{
    private readonly uint[] _landscape;
    private readonly uint[] _pixels;

    public Minimap(GraphicsDevice device, MapArea area)
    {
        _landscape = MapOverview.Pixels(area);
        _pixels = new uint[_landscape.Length];
        Texture = new Texture2D(device, area.Size, area.Size);
    }

    public Texture2D Texture { get; }

    /// <summary>Shows what <paramref name="vision"/> has explored (everything with <paramref name="all"/>).</summary>
    public void Update(Vision vision, bool all)
    {
        for (int i = 0; i < _pixels.Length; i++)
            _pixels[i] = all || vision.IsExplored(i) ? _landscape[i] : 0xff000000;
        Texture.SetData(_pixels);
    }

    public void Dispose() => Texture.Dispose();
}
