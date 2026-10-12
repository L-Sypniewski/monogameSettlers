using Homestead.Simulation;
using Homestead.Simulation.Buildings;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Homestead.Client;

/// <summary>
/// Shown at the left edge instead of the build menu while carriers are selected: what they can become,
/// each as the standing figure of the own people. A click converts the selected carriers.
/// </summary>
public sealed class ConversionMenu
{
    private const int Cell = 66;
    private const int Top = 4;

    /// <summary>The professions carriers can be turned into, with their type in the animation tables.</summary>
    private static readonly (Profession Profession, string Type, string Name)[] Entries =
    [
        (Profession.Geologist, "GEOLOGIST", "geologist"),
    ];

    private static Rectangle CellRect(int i) => new(i % 2 * Cell, Top + i / 2 * Cell, Cell, Cell);

    /// <summary>The profession under the cursor, with its name (for the hover text and clicks).</summary>
    public (Profession Profession, string Name)? EntryAt(Vector2 screen)
    {
        for (int i = 0; i < Entries.Length; i++)
            if (CellRect(i).Contains(screen))
                return (Entries[i].Profession, Entries[i].Name);
        return null;
    }

    public void Draw(SpriteBatch batch, Texture2D pixel, int height, SettlerRenderer settlers, Civilization people, Vector2 cursor)
    {
        batch.Draw(pixel, new Rectangle(0, 0, BuildMenu.Width, height), new Color(36, 28, 18, 230));
        for (int i = 0; i < Entries.Length; i++)
        {
            var rect = CellRect(i);
            batch.Draw(pixel, rect, rect.Contains(cursor) ? new Color(150, 115, 60) : new Color(80, 62, 38));
            // The figure's offsets put it left of and above its anchor: shifted so it stands centered in the cell.
            settlers.DrawIcon(batch, people, Entries[i].Type, new Vector2(rect.Center.X + 12, rect.Bottom - 17), 2f, PlayerColors.Of(0));
        }
    }
}
