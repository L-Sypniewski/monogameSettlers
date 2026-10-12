using Homestead.Simulation;
using Homestead.Simulation.Buildings;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Homestead.Client;

/// <summary>
/// Groups 1-9 of selected settlers (Ctrl+number stores the selection, number or a click on its icon
/// selects it again). Each group that has members shows as an icon at the top right: the figure of its
/// first member, the number and how many are left.
/// </summary>
public sealed class ControlGroups
{
    public const int Count = 9;
    private const int Size = 44;
    private const int Gap = 4;
    private const int Margin = 8;

    private readonly List<Settler>[] _groups = Enumerable.Range(0, Count).Select(_ => new List<Settler>()).ToArray();

    /// <summary>Stores the selection as group <paramref name="index"/> (an empty selection clears it).</summary>
    public void Store(int index, IEnumerable<Settler> selection)
    {
        _groups[index].Clear();
        _groups[index].AddRange(selection);
    }

    /// <summary>The living members of a group.</summary>
    public IReadOnlyList<Settler> Members(int index) => _groups[index];

    /// <summary>The dead leave their groups.</summary>
    public void Update()
    {
        foreach (var group in _groups)
            group.RemoveAll(s => s.IsDead);
    }

    /// <summary>The group whose icon is under the screen point.</summary>
    public int? GroupAt(Vector2 screen, int width)
    {
        foreach (var (index, rect) in Icons(width))
            if (rect.Contains(screen))
                return index;
        return null;
    }

    public void Draw(SpriteBatch batch, Texture2D pixel, int width, UiFont font, SettlerRenderer settlers, Civilization people,
        IReadOnlyCollection<Settler> selection, Vector2 cursor)
    {
        foreach (var (index, rect) in Icons(width))
        {
            var group = _groups[index];
            bool selected = selection.Count > 0 && group.All(selection.Contains);
            batch.Draw(pixel, rect, selected ? Color.Yellow : new Color(20, 15, 10, 230));
            var inner = rect;
            inner.Inflate(-1, -1);
            batch.Draw(pixel, inner, rect.Contains(cursor) ? new Color(150, 115, 60) : new Color(80, 62, 38));
            var figure = inner;
            figure.Inflate(-4, -4);
            if (SettlerRenderer.TypeOf(group[0]) is { } type)
                settlers.DrawIcon(batch, people, type, figure, 1.5f, PlayerColors.Of(0));
            font.Draw(batch, (index + 1).ToString(), new Vector2(rect.X + 3, rect.Y + 1), Color.White, 13);
            string count = group.Count.ToString();
            var size = font.Measure(count, 13);
            font.Draw(batch, count, new Vector2(rect.Right - size.X - 3, rect.Bottom - size.Y - 2), Color.White, 13);
        }
    }

    /// <summary>Icons of the groups with members, in number order, ending at the right edge.</summary>
    private IEnumerable<(int Index, Rectangle Rect)> Icons(int width)
    {
        var used = Enumerable.Range(0, Count).Where(i => _groups[i].Count > 0).ToList();
        int x = width - Margin - used.Count * (Size + Gap) + Gap;
        foreach (int index in used)
        {
            yield return (index, new Rectangle(x, Margin, Size, Size));
            x += Size + Gap;
        }
    }
}
