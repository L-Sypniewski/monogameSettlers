using Homestead.Simulation;
using Homestead.Simulation.Buildings;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Homestead.Client;

/// <summary>
/// The selected units in the blue panel: one portrait per kind (soldier type and level, geologist, priest,
/// carrier) with how many are selected; at the bottom the player's combat strength away from own land,
/// above it the orders of the selection (<see cref="ActionAt"/>): „Waren stehlen“ for scouts, „Land
/// einnehmen“ for pioneers. Below the spell or conversion menu when priests or carriers are selected.
/// </summary>
public sealed class SelectionPanel
{
    private const int Columns = 3;
    private const int CellHeight = 80;

    /// <summary>The portraits sit a little below the top of their cell (clear of the bar above).</summary>
    private const int PortraitDrop = 6;

    private const int ActionHeight = 22;

    /// <summary>The orders offered for the selection, bottom up.</summary>
    private static List<SelectionAction> Actions(IEnumerable<Settler> selection)
    {
        var actions = new List<SelectionAction>();
        if (selection.Any(s => s.Profession == Profession.Scout && !s.IsDead))
            actions.Add(SelectionAction.Steal);
        if (selection.Any(s => s.Profession == Profession.Pioneer && !s.IsDead))
            actions.Add(SelectionAction.Claim);
        return actions;
    }

    private static Rectangle ActionRect(int i) =>
        new(BuildMenu.Content.X + 2, BuildMenu.Content.Bottom - 44 - i * ActionHeight, BuildMenu.Content.Width - 4, 20);

    private static string TextOf(SelectionAction action) => action == SelectionAction.Steal ? "Waren stehlen" : "Land einnehmen";

    /// <summary>The order text under the point (panel space), or null.</summary>
    public static SelectionAction? ActionAt(IEnumerable<Settler> selection, Vector2 panel)
    {
        var actions = Actions(selection);
        for (int i = 0; i < actions.Count; i++)
            if (ActionRect(i).Contains(panel))
                return actions[i];
        return null;
    }

    /// <param name="top">Where the portraits start (panel space).</param>
    /// <param name="cursor">Mouse in panel space (highlights the order under it).</param>
    public void Draw(SpriteBatch batch, UiFont font, SettlerRenderer settlers, Portraits portraits, Civilization people,
        IReadOnlyCollection<Settler> selection, float combatStrength, int top, Vector2 cursor)
    {
        var actions = Actions(selection);
        var content = BuildMenu.Content;
        int cellWidth = content.Width / Columns;
        var kinds = selection.Where(s => !s.IsDead).GroupBy(s => SettlerRenderer.TypeOf(s))
            .Where(g => g.Key is not null).OrderBy(g => g.Key).ToList();
        for (int i = 0; i < kinds.Count; i++)
        {
            var cell = new Rectangle(content.X + i % Columns * cellWidth, top + i / Columns * CellHeight, cellWidth, CellHeight);
            if (cell.Bottom > content.Bottom - 22 - actions.Count * ActionHeight)
                break;
            var portrait = new Rectangle(cell.X, cell.Y + PortraitDrop, cell.Width, Portraits.Size.Y);
            portraits.Draw(batch, kinds[i].Key!, portrait, settlers, people, PlayerColors.Of(0));
            string count = kinds[i].Count().ToString();
            var size = font.Measure(count, 14);
            Shadowed(batch, font, count, new Vector2(cell.Center.X - size.X / 2, portrait.Bottom), 14);
        }
        for (int i = 0; i < actions.Count; i++)
        {
            var rect = ActionRect(i);
            Shadowed(batch, font, TextOf(actions[i]), rect.Location.ToVector2(), 14, rect.Contains(cursor) ? new Color(255, 240, 160) : Color.White);
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

/// <summary>Orders in <see cref="SelectionPanel"/>.</summary>
public enum SelectionAction
{
    /// <summary>„Waren stehlen“: scouts steal the nearest goods.</summary>
    Steal,

    /// <summary>„Land einnehmen“: pioneers take land around them.</summary>
    Claim,
}
