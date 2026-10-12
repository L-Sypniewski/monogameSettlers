using Homestead.Simulation.Buildings;
using Homestead.Simulation.Magic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Homestead.Client;

/// <summary>
/// Shown at the left edge instead of the build menu while priests are selected: their people's spell
/// with its cost and the player's mana. A click on it starts aiming; the next click on the map casts.
/// </summary>
public sealed class SpellMenu
{
    private static readonly Rectangle Button = new(4, 4, BuildMenu.Width - 8, 92);

    public bool Contains(Vector2 screen) => Button.Contains(screen);

    public void Draw(SpriteBatch batch, Texture2D pixel, int height, UiFont font, SettlerRenderer settlers, Civilization people,
        Spell? spell, int mana, bool aiming, Vector2 cursor)
    {
        batch.Draw(pixel, new Rectangle(0, 0, BuildMenu.Width, height), new Color(36, 28, 18, 230));
        bool affordable = spell is not null && mana >= spell.Cost;
        var color = aiming ? new Color(170, 140, 70) : Button.Contains(cursor) && affordable ? new Color(150, 115, 60) : new Color(80, 62, 38);
        batch.Draw(pixel, Button, color);
        settlers.DrawIcon(batch, people, "MAGE", new Rectangle(Button.X + 4, Button.Y + 4, 48, 56), 2f, PlayerColors.Of(0));
        if (spell is not null)
        {
            int y = Button.Y + 4;
            foreach (string line in font.Wrap(spell.Name, Button.Width - 60, 13))
            {
                font.Draw(batch, line, new Vector2(Button.X + 56, y), Color.White, 13);
                y += 16;
            }
            font.Draw(batch, $"{spell.Cost} Mana", new Vector2(Button.X + 56, y), affordable ? Color.White : new Color(230, 120, 100), 13);
        }
        font.Draw(batch, $"Mana: {mana}", new Vector2(Button.X + 4, Button.Bottom - 22), Color.White, 14);
        if (!aiming)
            return;
        int row = Button.Bottom + 6;
        foreach (string line in font.Wrap("Auf einen Kampf klicken, Rechtsklick bricht ab", Button.Width, 12))
        {
            font.Draw(batch, line, new Vector2(Button.X, row), Color.White, 12);
            row += 14;
        }
    }
}
