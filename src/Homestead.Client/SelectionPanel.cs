using Homestead.Simulation;
using Homestead.Simulation.Buildings;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Homestead.Client;

/// <summary>
/// The selected units at the left edge: one cell per kind (soldier type and level, geologist, priest,
/// carrier) with its figure and how many are selected; at the bottom the player's combat strength away
/// from own land. Below the spell or conversion menu when priests or carriers are selected.
/// </summary>
public sealed class SelectionPanel
{
    private const int Cell = 66;

    public void Draw(SpriteBatch batch, Texture2D pixel, int height, UiFont font, SettlerRenderer settlers, Civilization people,
        IReadOnlyCollection<Settler> selection, float combatStrength, int top, bool background)
    {
        if (background)
            batch.Draw(pixel, new Rectangle(0, 0, BuildMenu.Width, height), new Color(36, 28, 18, 230));
        var kinds = selection.Where(s => !s.IsDead).GroupBy(s => SettlerRenderer.TypeOf(s))
            .Where(g => g.Key is not null).OrderBy(g => g.Key).ToList();
        for (int i = 0; i < kinds.Count; i++)
        {
            var rect = new Rectangle(i % 2 * Cell, top + i / 2 * Cell, Cell, Cell);
            if (rect.Bottom > height - 30)
                break;
            var frame = rect;
            frame.Inflate(-2, -2);
            batch.Draw(pixel, frame, new Color(80, 62, 38));
            var figure = frame;
            figure.Inflate(-6, -6);
            settlers.DrawIcon(batch, people, kinds[i].Key!, figure, 2f, PlayerColors.Of(0));
            string count = kinds[i].Count().ToString();
            var size = font.Measure(count, 14);
            font.Draw(batch, count, new Vector2(frame.Right - size.X - 3, frame.Bottom - size.Y - 2), Color.White, 14);
        }
        font.Draw(batch, $"Kampfkraft: {combatStrength * 100:0}%", new Vector2(6, height - 24), Color.White, 14);
    }
}
