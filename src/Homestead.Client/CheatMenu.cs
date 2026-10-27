using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Homestead.Client;

/// <summary>
/// A small cheat menu at the top right (below the group icons), shown with /cheats: one button per entry; a click
/// runs its action (new units in the middle of the view, mana).
/// </summary>
public sealed class CheatMenu
{
    private const int Width = 190;
    private const int ButtonHeight = 24;
    private const int Gap = 3;
    /// <summary>Where the menu begins (below the group icons, which grow with the side panel).</summary>
    public int Top { get; set; } = 76;
    private const int Margin = 8;

    private readonly List<(string Label, Action Run)> _entries = new();

    public bool IsOpen { get; set; }

    public void Add(string label, Action run) => _entries.Add((label, run));

    /// <summary>Whether the point is on the open menu.</summary>
    public bool Contains(Vector2 screen, int width) => IsOpen && Frame(width).Contains(screen);

    /// <summary>Runs the entry under the point; false if there is none.</summary>
    public bool Click(Vector2 screen, int width)
    {
        if (!IsOpen)
            return false;
        for (int i = 0; i < _entries.Count; i++)
            if (Button(width, i).Contains(screen))
            {
                _entries[i].Run();
                return true;
            }
        return false;
    }

    public void Draw(SpriteBatch batch, Texture2D pixel, int width, UiFont font, Vector2 cursor)
    {
        if (!IsOpen)
            return;
        var frame = Frame(width);
        batch.Draw(pixel, frame, new Color(20, 15, 10, 230));
        font.Draw(batch, "Cheats (/cheats)", new Vector2(frame.X + 6, frame.Y + 4), new Color(230, 200, 120), 13);
        for (int i = 0; i < _entries.Count; i++)
        {
            var button = Button(width, i);
            batch.Draw(pixel, button, button.Contains(cursor) ? new Color(150, 115, 60) : new Color(80, 62, 38));
            font.Draw(batch, _entries[i].Label, new Vector2(button.X + 6, button.Y + 4), Color.White, 13);
        }
    }

    private Rectangle Frame(int width) =>
        new(width - Margin - Width, Top, Width, 24 + _entries.Count * (ButtonHeight + Gap) + Gap);

    private Rectangle Button(int width, int i) =>
        new(width - Margin - Width + 4, Top + 24 + i * (ButtonHeight + Gap), Width - 8, ButtonHeight);
}
