using Homestead.Simulation;
using Homestead.Simulation.Buildings;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Homestead.Client;

/// <summary>
/// Groups 1-9 of selected settlers (Ctrl+number stores the selection, number or a click on its icon
/// selects it again). Each group that has members shows as an icon at the top right: the portrait of its
/// first member (as in the selection panel), the number and how many are left.
/// </summary>
public sealed class ControlGroups
{
    public const int Count = 9;
    private const int Gap = 4;
    private const int Margin = 8;

    private readonly List<Settler>[] _groups = Enumerable.Range(0, Count).Select(_ => new List<Settler>()).ToArray();

    /// <summary>
    /// Stores the selection as group <paramref name="index"/> (an empty selection clears it). A settler is in one
    /// group at most: the members leave their other groups.
    /// </summary>
    public void Store(int index, IEnumerable<Settler> selection)
    {
        var members = selection.ToList();
        var moved = members.ToHashSet();
        foreach (var group in _groups)
            group.RemoveAll(moved.Contains);
        _groups[index].Clear();
        _groups[index].AddRange(members);
    }

    /// <summary>The living members of a group.</summary>
    public IReadOnlyList<Settler> Members(int index) => _groups[index];

    /// <summary>The dead, soldiers who enter a building and those won over by a spell leave their groups.</summary>
    public void Update()
    {
        foreach (var group in _groups)
            group.RemoveAll(s => s.IsDead || s.IsInside || s.Player != 0);
    }

    /// <summary>The group whose icon is under the screen point.</summary>
    public int? GroupAt(Vector2 screen, int width, float scale)
    {
        foreach (var (index, rect) in Icons(width, scale))
            if (rect.Contains(screen))
                return index;
        return null;
    }

    /// <param name="scale">The side panel's scale: the icons are as large as the portraits there.</param>
    public void Draw(SpriteBatch batch, Texture2D pixel, int width, float scale, UiFont font, SettlerRenderer settlers,
        Func<Civilization, Portraits> portraits, IReadOnlyCollection<Settler> selection, Vector2 cursor)
    {
        int textSize = (int)MathF.Round(13 * scale);
        foreach (var (index, rect) in Icons(width, scale))
        {
            var group = _groups[index];
            // The selected group: a yellow frame around its portrait.
            if (selection.Count > 0 && group.All(selection.Contains))
            {
                var frame = rect;
                frame.Inflate(2, 2);
                batch.Draw(pixel, frame, Color.Yellow);
                batch.Draw(pixel, rect, Color.Black);
            }
            if (SettlerRenderer.TypeOf(group[0]) is { } type)
            {
                var people = settlers.PeopleOf(group[0]);
                portraits(people).Draw(batch, type, rect, settlers, people, PlayerColors.Of(0), scale);
            }
            if (rect.Contains(cursor))
                batch.Draw(pixel, rect, Color.White * 0.15f);
            SelectionPanel.Shadowed(batch, font, (index + 1).ToString(), new Vector2(rect.X + 4 * scale, rect.Y + 2 * scale), textSize);
            string count = group.Count.ToString();
            var size = font.Measure(count, textSize);
            SelectionPanel.Shadowed(batch, font, count, new Vector2(rect.Right - size.X - 4 * scale, rect.Bottom - size.Y - 3 * scale), textSize);
        }
    }

    /// <summary>Where the icons end (the cheat menu goes below them).</summary>
    public static int Bottom(float scale) => Margin + (int)MathF.Round(Portraits.Size.Y * scale);

    /// <summary>Icons of the groups with members, in number order, ending at the right edge.</summary>
    private IEnumerable<(int Index, Rectangle Rect)> Icons(int width, float scale)
    {
        var used = Enumerable.Range(0, Count).Where(i => _groups[i].Count > 0).ToList();
        var size = new Point((int)MathF.Round(Portraits.Size.X * scale), (int)MathF.Round(Portraits.Size.Y * scale));
        int x = width - Margin - used.Count * (size.X + Gap) + Gap;
        foreach (int index in used)
        {
            yield return (index, new Rectangle(x, Margin, size.X, size.Y));
            x += size.X + Gap;
        }
    }
}
