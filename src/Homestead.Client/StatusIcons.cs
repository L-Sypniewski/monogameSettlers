using System.Text.Json;
using Homestead.Simulation;
using Homestead.Simulation.Buildings;
using Homestead.Simulation.Magic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Homestead.Client;

/// <summary>
/// The original's small status symbols over units a spell works on – samurai sword, strengthened, fear, shield,
/// cursed arrows, frozen (<c>statusIcons</c> in <c>data/ui.json</c>): above the head, side by side when several work.
/// </summary>
public sealed class StatusIcons
{
    /// <summary>How far above a settler's anchor the symbols' lower edge sits (above the selection brackets).</summary>
    private const int Above = 50;

    private const int Gap = 1;

    private readonly (SpellKind Kind, SettlerSprites Sprites)[] _icons;
    private readonly List<(SettlerSprites Sprites, Rectangle Bounds)> _shown = new();

    public StatusIcons(SpriteLibrary sprites)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(BuildingCatalog.DefaultDataDirectory, "ui.json")));
        var icons = document.RootElement.GetProperty("statusIcons");
        int file = icons.GetProperty("file").GetInt32();
        _icons = icons.EnumerateObject().Where(p => Enum.TryParse<SpellKind>(p.Name, out _))
            .Select(p => (Enum.Parse<SpellKind>(p.Name), sprites.Sequence(file, p.Value.GetInt32()))).ToArray();
    }

    /// <summary>The symbols of the spells working on the settler at the tick, centered above <paramref name="anchor"/>.</summary>
    public void Draw(SpriteBatch batch, Settler settler, long tick, Vector2 anchor)
    {
        _shown.Clear();
        foreach (var (kind, sprites) in _icons)
            if ((kind == SpellKind.Freeze ? settler.FrozenUntil > tick : settler.IsCharmed(kind, tick)) && sprites.Bounds(0) is { } bounds)
                _shown.Add((sprites, bounds));
        if (_shown.Count == 0)
            return;
        int width = _shown.Sum(s => s.Bounds.Width) + Gap * (_shown.Count - 1);
        int x = (int)anchor.X - width / 2, bottom = (int)anchor.Y - Above;
        foreach (var (sprites, bounds) in _shown)
        {
            // The frame's own offset away: its body's top left goes where it should.
            sprites.Draw(batch, 0, new Vector2(x - bounds.X, bottom - bounds.Height - bounds.Y), 1, Color.White, shadow: false);
            x += bounds.Width + Gap;
        }
    }
}
