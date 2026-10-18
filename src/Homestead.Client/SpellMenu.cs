using Homestead.Simulation.Buildings;
using Homestead.Simulation.Magic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Homestead.Client;

/// <summary>
/// Shown in the blue panel instead of the build menu while priests are selected: the spells of their people
/// (its own first, then the common ones) with the priest's portrait and their cost, and the player's mana.
/// A click on a spell starts aiming (gold frame); the next click on the map casts.
/// </summary>
public sealed class SpellMenu
{
    private const int RowHeight = 66;

    /// <summary>Where the menu ends, with the aiming hint (for what is drawn under it).</summary>
    public static int Bottom(int spells) => BuildMenu.Content.Y + spells * RowHeight + 52;

    private static Rectangle Row(int i) => new(BuildMenu.Content.X, BuildMenu.Content.Y + i * RowHeight, BuildMenu.Content.Width, RowHeight - 2);

    /// <summary>The spell under the cursor (panel space).</summary>
    public int? SpellAt(Vector2 screen, int spells)
    {
        for (int i = 0; i < spells; i++)
            if (Row(i).Contains(screen))
                return i;
        return null;
    }

    public void Draw(SpriteBatch batch, Texture2D pixel, UiFont font, SettlerRenderer settlers, Portraits portraits, Civilization people,
        IReadOnlyList<Spell> spells, int mana, Spell? aiming, Vector2 cursor)
    {
        for (int i = 0; i < spells.Count; i++)
        {
            var spell = spells[i];
            var row = Row(i);
            bool affordable = mana >= spell.Cost;
            if (row.Contains(cursor) && affordable)
                batch.Draw(pixel, row, Color.White * 0.08f);
            portraits.Draw(batch, "MAGE", new Rectangle(row.X, row.Y, Portraits.Size.X + 4, row.Height), settlers, people, PlayerColors.Of(0));
            int y = row.Y + 8;
            foreach (string line in font.Wrap(Lang.T("spells." + spell.Kind), row.Width - 54, 13))
            {
                SelectionPanel.Shadowed(batch, font, line, new Vector2(row.X + 50, y), 13);
                y += 16;
            }
            SelectionPanel.Shadowed(batch, font, $"{spell.Cost} Mana", new Vector2(row.X + 50, y), 13,
                affordable ? new Color(255, 225, 140) : new Color(240, 130, 110));
            if (aiming == spell)
                Frame(batch, pixel, row, Color.Gold);
        }
        int bottom = BuildMenu.Content.Y + spells.Count * RowHeight;
        SelectionPanel.Shadowed(batch, font, $"Mana: {mana}", new Vector2(BuildMenu.Content.X + 2, bottom + 2), 14);
        if (aiming is null)
            return;
        int line2 = bottom + 22;
        string hint = Lang.T(aiming.Kind switch
        {
            SpellKind.SummonGoods => "spellMenu.clickPlace",
            SpellKind.Haste => "spellMenu.clickOwn",
            _ => "spellMenu.clickEnemies",
        });
        foreach (string text in font.Wrap(hint, BuildMenu.Content.Width, 12))
        {
            SelectionPanel.Shadowed(batch, font, text, new Vector2(BuildMenu.Content.X + 2, line2), 12);
            line2 += 14;
        }
    }

    private static void Frame(SpriteBatch batch, Texture2D pixel, Rectangle rect, Color color)
    {
        batch.Draw(pixel, new Rectangle(rect.X, rect.Y, rect.Width, 2), color);
        batch.Draw(pixel, new Rectangle(rect.X, rect.Bottom - 2, rect.Width, 2), color);
        batch.Draw(pixel, new Rectangle(rect.X, rect.Y, 2, rect.Height), color);
        batch.Draw(pixel, new Rectangle(rect.Right - 2, rect.Y, 2, rect.Height), color);
    }
}
