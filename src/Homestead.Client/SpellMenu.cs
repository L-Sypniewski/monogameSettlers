using System.Text.Json;
using Homestead.Assets.Gfx;
using Homestead.Simulation.Buildings;
using Homestead.Simulation.Magic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Homestead.Client;

/// <summary>
/// Shown in the blue panel instead of the build menu while priests are selected, like the original: the spell menu
/// of their people (<see cref="Magic.MenuOf"/>, placeholders included), each spell its icon and a charge bar (mana / cost).
/// Above the spells "Priester", or the name and cost of the spell under the cursor, or the aiming hint; below the mana.
/// A click on a spell starts aiming (gold frame); the next click on the map casts.
/// </summary>
public sealed class SpellMenu
{
    private const int HeaderHeight = 20;
    private const int RowHeight = 40;

    // The charge bar (see DrawBar).
    private static readonly Color Light = new(0, 218, 213);
    private static readonly Color Dark = new(0, 172, 167);
    private static readonly Color GradientStart = new(0, 38, 44);
    private static readonly Color GradientEnd = new(0, 150, 148);

    /// <summary>Each charge's stripe is this many times as wide as the one before.</summary>
    private const double StripeGrowth = 1.35;

    /// <summary>Large icon of each people's top spell (GUI image of its file; each further spell three lower).</summary>
    private readonly Dictionary<Civilization, (int File, int Index)> _first = [];
    private readonly Dictionary<Civilization, Texture2D[]> _icons = [];

    public SpellMenu()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(BuildingCatalog.DefaultDataDirectory, "ui.json")));
        foreach (var people in document.RootElement.GetProperty("spellMenu").EnumerateObject())
            _first[Enum.Parse<Civilization>(people.Name)] = (people.Value[0].GetInt32(), people.Value[1].GetInt32());
    }

    public void LoadIcons(GraphicsDevice device, GfxDirectory gfx, int spells = 8)
    {
        if (_icons.Count > 0)
            return;
        foreach (var (people, (fileIndex, first)) in _first)
        {
            var file = gfx.Open(fileIndex);
            _icons[people] = Enumerable.Range(0, spells).Select(i =>
            {
                var bitmap = file.ReadGui(first - 3 * i);
                var texture = new Texture2D(device, bitmap.Width, bitmap.Height);
                texture.SetData(bitmap.Pixels);
                return texture;
            }).ToArray();
        }
    }

    private static Rectangle Row(int i) => new(BuildMenu.Content.X, BuildMenu.Content.Y + HeaderHeight + i * RowHeight, BuildMenu.Content.Width, RowHeight);

    /// <summary>The spell under the cursor (panel space).</summary>
    public int? SpellAt(Vector2 screen, int spells)
    {
        for (int i = 0; i < spells; i++)
            if (Row(i).Contains(screen))
                return i;
        return null;
    }

    public void Draw(SpriteBatch batch, Texture2D pixel, UiFont font, Civilization people, IReadOnlyList<MenuSpell> spells, int mana,
        Spell? aiming, Vector2 cursor)
    {
        var content = BuildMenu.Content;
        var icons = _icons.GetValueOrDefault(people);
        MenuSpell? hovered = null;
        for (int i = 0; i < spells.Count; i++)
        {
            var spell = spells[i];
            var row = Row(i);
            if (row.Contains(cursor))
                hovered = spell;
            var icon = new Rectangle(row.X, row.Y, 60, RowHeight);
            if (icons is not null && i < icons.Length)
                batch.Draw(icons[i], icon, Color.White);
            var bar = new Rectangle(icon.Right + 6, row.Y + 9, row.Right - icon.Right - 8, RowHeight - 18);
            DrawBar(batch, pixel, bar, spell.Cost > 0 ? mana / (double)spell.Cost : 0);
            if (aiming is not null && aiming == spell.Spell)
                Frame(batch, pixel, icon, Color.Gold);
        }

        string title = aiming is not null ? Lang.T(aiming.Kind switch
            {
                SpellKind.Haste or SpellKind.Strengthen or SpellKind.Shield or SpellKind.CursedArrows or SpellKind.Reinforce => "spellMenu.clickOwn",
                SpellKind.Convert or SpellKind.Scatter or SpellKind.Freeze or SpellKind.Fear or SpellKind.BowsToPioneers => "spellMenu.clickEnemies",
                _ => "spellMenu.clickPlace",
            })
            : hovered is null ? Lang.T("spellMenu.title")
            : hovered.Spell is null ? Lang.T("spellMenu.cost", Lang.T("spells." + hovered.Id), hovered.Cost) + " · " + Lang.T("spellMenu.placeholder")
            : Lang.T("spellMenu.cost", Lang.T("spells." + hovered.Id), hovered.Cost);
        int size = 15;
        while (size > 9 && font.Measure(title, size).X > content.Width)
            size--;
        var measured = font.Measure(title, size);
        SelectionPanel.Shadowed(batch, font, title, new Vector2(content.Center.X - measured.X / 2, content.Y + (HeaderHeight - measured.Y) / 2), size);
        SelectionPanel.Shadowed(batch, font, Lang.T("spellMenu.mana", mana), new Vector2(content.X + 2, Row(spells.Count).Y + 2), 13);
    }

    /// <summary>
    /// A charge bar: below one charge a gradient up to it. From one charge on the bar holds all the mana, one stripe per charge,
    /// light and dark in turn, each <see cref="StripeGrowth"/> times as wide as the one before – the newest grows at the right
    /// and pushes the older ones together to the left (measured in the original: borders at (g^k − 1) / (g^charges − 1)).
    /// </summary>
    private static void DrawBar(SpriteBatch batch, Texture2D pixel, Rectangle bar, double charges)
    {
        batch.Draw(pixel, bar, Color.Black * 0.45f);
        var inner = new Rectangle(bar.X + 1, bar.Y + 1, bar.Width - 2, bar.Height - 2);
        if (charges < 1)
        {
            int width = (int)Math.Round(inner.Width * charges);
            for (int x = 0; x < width; x++)
                batch.Draw(pixel, new Rectangle(inner.X + x, inner.Y, 1, inner.Height), Color.Lerp(GradientStart, GradientEnd, (x + 1) / (float)inner.Width));
        }
        else
        {
            double all = Math.Pow(StripeGrowth, charges) - 1;
            int Border(double k) => inner.X + (int)Math.Round(inner.Width * (Math.Pow(StripeGrowth, Math.Min(k, charges)) - 1) / all);
            for (int k = 0; k < charges; k++)
            {
                int left = Border(k), right = Border(k + 1);
                if (right > left)
                    batch.Draw(pixel, new Rectangle(left, inner.Y, right - left, inner.Height), k % 2 == 0 ? Light : Dark);
            }
        }
        Frame(batch, pixel, bar, Color.White, 1);
    }

    private static void Frame(SpriteBatch batch, Texture2D pixel, Rectangle rect, Color color, int thickness = 2)
    {
        batch.Draw(pixel, new Rectangle(rect.X, rect.Y, rect.Width, thickness), color);
        batch.Draw(pixel, new Rectangle(rect.X, rect.Bottom - thickness, rect.Width, thickness), color);
        batch.Draw(pixel, new Rectangle(rect.X, rect.Y, thickness, rect.Height), color);
        batch.Draw(pixel, new Rectangle(rect.Right - thickness, rect.Y, thickness, rect.Height), color);
    }
}
