using Homestead.Simulation;
using Homestead.Simulation.Buildings;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Homestead.Client;

/// <summary>
/// Shown in the blue panel instead of the build menu while carriers are selected: what they can become,
/// each as the portrait of the own people. A click converts the selected carriers.
/// </summary>
public sealed class ConversionMenu
{
    private const int Columns = 3;
    private const int CellHeight = 70;

    /// <summary>The professions carriers can be turned into, with their type in the animation tables.</summary>
    private static readonly (Profession Profession, string Type, string Name)[] Entries =
    [
        (Profession.Geologist, "GEOLOGIST", "geologist"),
        (Profession.Scout, "THIEF", "scout"),
        (Profession.Pioneer, "PIONEER", "pioneer"),
    ];

    /// <summary>Where the entries end (for what is drawn under them).</summary>
    public static int Bottom => BuildMenu.Content.Y + (Entries.Length + Columns - 1) / Columns * CellHeight + 6;

    private static Rectangle CellRect(int i)
    {
        var content = BuildMenu.Content;
        int width = content.Width / Columns;
        return new Rectangle(content.X + i % Columns * width, content.Y + i / Columns * CellHeight, width, CellHeight);
    }

    /// <summary>The profession under the cursor, with its name (for the hover text and clicks).</summary>
    public (Profession Profession, string Name)? EntryAt(Vector2 screen)
    {
        for (int i = 0; i < Entries.Length; i++)
            if (CellRect(i).Contains(screen))
                return (Entries[i].Profession, Entries[i].Name);
        return null;
    }

    public void Draw(SpriteBatch batch, Texture2D pixel, SettlerRenderer settlers, Portraits portraits, Civilization people, Vector2 cursor)
    {
        for (int i = 0; i < Entries.Length; i++)
        {
            var rect = CellRect(i);
            if (rect.Contains(cursor))
                batch.Draw(pixel, rect, Color.White * 0.08f);
            portraits.Draw(batch, Entries[i].Type, rect, settlers, people, PlayerColors.Of(0));
        }
    }
}
