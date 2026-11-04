using Homestead.Client.Terrain;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Homestead.Client;

/// <summary>
/// The water lab's popup (F5): one toggle row per proposal, a preset row (Off, All) and a hint line,
/// like the cheat menu an overlay the running game stays visible and alive under. Clicking a row
/// replaces <see cref="WaterLab.Current"/> live; the corner text announces the new set. Experiment
/// branch: the strings are hardcoded English (like the F2 announcement), not Lang keys.
/// </summary>
public sealed class WaterLabMenu
{
    private const int Width = 190;
    private const int RowHeight = 22;
    private const int Gap = 3;
    private const int HeaderHeight = 22;
    private const int HintHeight = 18;
    private const int PresetRow = 5, RowCount = 6;

    private static readonly (string Name, Func<WaterLabState, bool> Get, Action<WaterLabState, bool> Set)[] Flags =
    [
        ("flatwater", s => s.FlatWater, (s, v) => s.FlatWater = v),
        ("pixelsurf", s => s.Pixelsurf, (s, v) => s.Pixelsurf = v),
        ("normals", s => s.Normals, (s, v) => s.Normals = v),
        ("foam", s => s.Foam, (s, v) => s.Foam = v),
        ("rings", s => s.Rings, (s, v) => s.Rings = v),
    ];

    /// <summary>Where the panel begins; kept clear of the cheat menu by MapGame.</summary>
    public int Top { get; set; } = 76;

    public bool IsOpen { get; set; }

    /// <summary>Whether the point is on the open panel (clicks there never reach the game).</summary>
    public bool Contains(Vector2 screen, int width) => IsOpen && Frame(width).Contains(screen);

    /// <summary>Flips the flag or preset under the point; false if the point is not on the panel.</summary>
    public bool Click(Vector2 screen, int width)
    {
        if (!IsOpen)
            return false;
        for (int i = 0; i < Flags.Length; i++)
            if (Row(width, i).Contains(screen))
            {
                var next = WaterLab.Current with { };
                Flags[i].Set(next, !Flags[i].Get(next));
                WaterLab.Set(next);
                return true;
            }
        if (Preset(width, false).Contains(screen))
        {
            WaterLab.Set(new WaterLabState());
            return true;
        }
        if (Preset(width, true).Contains(screen))
        {
            var next = new WaterLabState();
            foreach (var flag in Flags)
                flag.Set(next, true);
            WaterLab.Set(next);
            return true;
        }
        return Contains(screen, width);
    }

    public void Draw(SpriteBatch batch, Texture2D pixel, int width, UiFont font, Vector2 cursor)
    {
        if (!IsOpen)
            return;
        var frame = Frame(width);
        batch.Draw(pixel, frame, new Color(20, 15, 10, 230));
        font.Draw(batch, "Water lab (F5)", new Vector2(frame.X + 6, frame.Y + 4), new Color(230, 200, 120), 13);
        for (int i = 0; i < Flags.Length; i++)
        {
            var row = Row(width, i);
            bool on = Flags[i].Get(WaterLab.Current);
            batch.Draw(pixel, row, row.Contains(cursor) ? new Color(150, 115, 60) : new Color(80, 62, 38));
            font.Draw(batch, $"{(on ? "[x]" : "[ ]")} {Flags[i].Name}", new Vector2(row.X + 6, row.Y + 4), Color.White, 13);
        }
        var off = Preset(width, all: false);
        var all = Preset(width, all: true);
        batch.Draw(pixel, off, off.Contains(cursor) ? new Color(150, 115, 60) : new Color(80, 62, 38));
        batch.Draw(pixel, all, all.Contains(cursor) ? new Color(150, 115, 60) : new Color(80, 62, 38));
        font.Draw(batch, "Off", new Vector2(off.X + 6, off.Y + 4), Color.White, 13);
        font.Draw(batch, "All", new Vector2(all.X + 6, all.Y + 4), Color.White, 13);
        font.Draw(batch, "click to toggle, esc closes", new Vector2(frame.X + 6, frame.Bottom - HintHeight + 2), new Color(190, 170, 140), 12);
    }

    private Rectangle Frame(int width) =>
        new(width - 8 - Width, Top, Width, HeaderHeight + RowCount * (RowHeight + Gap) + HintHeight + Gap);

    private Rectangle Row(int width, int i) =>
        new(width - 8 - Width + 4, Top + HeaderHeight + i * (RowHeight + Gap), Width - 8, RowHeight);

    private Rectangle Preset(int width, bool all)
    {
        var row = Row(width, PresetRow);
        return all
            ? new Rectangle(row.X + row.Width / 2, row.Y, row.Width / 2, row.Height)
            : new Rectangle(row.X, row.Y, row.Width / 2, row.Height);
    }
}
