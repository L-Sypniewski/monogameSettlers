using Homestead.Simulation.Buildings;
using Homestead.Simulation.Magic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Homestead.Client;

/// <summary>
/// Shown at the left edge instead of the build menu while priests are selected: the spells of their people
/// (its own first, then the common ones) with their cost, and the player's mana. A click on a spell starts
/// aiming; the next click on the map casts.
/// </summary>
public sealed class SpellMenu
{
    private const int ButtonHeight = 64;
    private const int Gap = 4;

    /// <summary>Where the spells below end (for what is drawn under them).</summary>
    public static int Bottom(int spells) => Gap + spells * (ButtonHeight + Gap) + 24;

    private static Rectangle Button(int i) => new(4, Gap + i * (ButtonHeight + Gap), BuildMenu.Width - 8, ButtonHeight);

    /// <summary>The spell under the cursor (panel space).</summary>
    public int? SpellAt(Vector2 screen, int spells)
    {
        for (int i = 0; i < spells; i++)
            if (Button(i).Contains(screen))
                return i;
        return null;
    }

    public void Draw(SpriteBatch batch, Texture2D pixel, int height, UiFont font, SettlerRenderer settlers, Civilization people,
        IReadOnlyList<Spell> spells, int mana, Spell? aiming, Vector2 cursor)
    {
        batch.Draw(pixel, new Rectangle(0, 0, BuildMenu.Width, height), new Color(36, 28, 18, 230));
        for (int i = 0; i < spells.Count; i++)
        {
            var spell = spells[i];
            var button = Button(i);
            bool affordable = mana >= spell.Cost;
            var color = aiming == spell ? new Color(170, 140, 70) : button.Contains(cursor) && affordable ? new Color(150, 115, 60) : new Color(80, 62, 38);
            batch.Draw(pixel, button, color);
            settlers.DrawIcon(batch, people, "MAGE", new Rectangle(button.X + 4, button.Y + 4, 40, 56), 1.6f, PlayerColors.Of(0));
            int y = button.Y + 6;
            foreach (string line in font.Wrap(spell.Name, button.Width - 56, 13))
            {
                font.Draw(batch, line, new Vector2(button.X + 50, y), Color.White, 13);
                y += 16;
            }
            font.Draw(batch, $"{spell.Cost} Mana", new Vector2(button.X + 50, y), affordable ? Color.White : new Color(230, 120, 100), 13);
        }
        int row = Bottom(spells.Count) - 22;
        font.Draw(batch, $"Mana: {mana}", new Vector2(8, row), Color.White, 14);
        if (aiming is null)
            return;
        row += 22;
        string hint = aiming.Kind switch
        {
            SpellKind.SummonGoods => "Auf den Zielort klicken",
            SpellKind.Haste => "Auf eigene Soldaten klicken",
            _ => "Auf feindliche Soldaten klicken",
        } + ", Rechtsklick bricht ab";
        foreach (string line in font.Wrap(hint, BuildMenu.Width - 8, 12))
        {
            font.Draw(batch, line, new Vector2(4, row), Color.White, 12);
            row += 14;
        }
    }
}
