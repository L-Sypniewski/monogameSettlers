using Homestead.Simulation;
using Homestead.Simulation.Buildings;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Homestead.Client;

/// <summary>
/// The selected units in the blue panel: one portrait per kind (soldier type and level, geologist, priest,
/// carrier) with how many are selected; at the bottom the player's combat strength away from own land.
/// Below the spell or conversion menu when priests or carriers are selected.
/// </summary>
public sealed class SelectionPanel
{
    private const int Columns = 3;
    private const int CellHeight = 80;

    /// <summary>The portraits sit a little below the top of their cell (clear of the bar above).</summary>
    private const int PortraitDrop = 6;

    /// <param name="top">Where the portraits start (panel space).</param>
    public void Draw(SpriteBatch batch, UiFont font, SettlerRenderer settlers, Portraits portraits, Civilization people,
        IReadOnlyCollection<Settler> selection, float combatStrength, int top)
    {
        var content = BuildMenu.Content;
        int cellWidth = content.Width / Columns;
        var kinds = selection.Where(s => !s.IsDead).GroupBy(s => SettlerRenderer.TypeOf(s))
            .Where(g => g.Key is not null).OrderBy(g => g.Key).ToList();
        for (int i = 0; i < kinds.Count; i++)
        {
            var cell = new Rectangle(content.X + i % Columns * cellWidth, top + i / Columns * CellHeight, cellWidth, CellHeight);
            if (cell.Bottom > content.Bottom - 22)
                break;
            var portrait = new Rectangle(cell.X, cell.Y + PortraitDrop, cell.Width, Portraits.Size.Y);
            portraits.Draw(batch, kinds[i].Key!, portrait, settlers, people, PlayerColors.Of(0));
            string count = kinds[i].Count().ToString();
            var size = font.Measure(count, 14);
            Shadowed(batch, font, count, new Vector2(cell.Center.X - size.X / 2, portrait.Bottom), 14);
        }
        Shadowed(batch, font, $"Kampfkraft: {combatStrength * 100:0}%", new Vector2(content.X + 2, content.Bottom - 20), 14);
    }

    /// <summary>White text with a dark shadow, readable on the blue panel.</summary>
    internal static void Shadowed(SpriteBatch batch, UiFont font, string text, Vector2 position, int size, Color? color = null)
    {
        font.Draw(batch, text, position + new Vector2(1, 1), Color.Black * 0.8f, size);
        font.Draw(batch, text, position, color ?? Color.White, size);
    }
}
