using System.Globalization;
using System.Text.Json;
using Homestead.Simulation.Buildings;
using Homestead.Simulation.Persistence;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Homestead.Client;

/// <summary>What a click in the <see cref="SystemMenu"/> asks the game to do.</summary>
public enum SystemAction
{
    None,
    Save,
    Quit,
}

/// <summary>
/// The menu behind the computer button at the bottom left of the panel, like the original's: „Spiel
/// speichern“ at the top and „Spiel beenden“ at the bottom. Saving lists the slots (map, date and time, people;
/// empty ones dashed) – a click saves into that slot, over what is there. Quitting asks first, „Ja“ at the
/// bottom left, „Nein“ at the bottom right. Panel space.
/// </summary>
public sealed class SystemMenu
{
    private enum Page
    {
        Overview,
        Save,
        Quit,
    }

    private static readonly Color Text = new(240, 225, 190);
    private static readonly Color Hover = new(255, 240, 160);
    private const int SlotHeight = 36;

    /// <summary>The computer button at the bottom left of the panel (measured on the panel image).</summary>
    public static readonly Rectangle Button = new(10, 494, 34, 38);

    private Page _page;
    private SaveInfo?[] _slots = new SaveInfo?[SaveSlots.Count];

    /// <summary>The slot chosen by the last <see cref="SystemAction.Save"/>.</summary>
    public int ChosenSlot { get; private set; }

    /// <summary>Back to the overview (the computer button was pressed).</summary>
    public void Open() => _page = Page.Overview;

    /// <summary>Shows a page by name: <c>save</c> (the slots) or <c>quit</c> (the question); for capture runs.</summary>
    public void Show(string page)
    {
        Refresh();
        _page = page == "save" ? Page.Save : page == "quit" ? Page.Quit : Page.Overview;
    }

    private static Rectangle Content => BuildMenu.Content;
    private static Rectangle SaveItem => new(Content.X, Content.Y, Content.Width, 24);
    private static Rectangle QuitItem => new(Content.X, Content.Bottom - 28, Content.Width, 24);
    private static Rectangle Slot(int i) => new(Content.X + 4, Content.Y + 30 + i * SlotHeight, Content.Width - 8, SlotHeight - 2);
    private static Rectangle Yes => new(Content.X + 4, Content.Bottom - 28, 70, 24);
    private static Rectangle No => new(Content.Right - 74, Content.Bottom - 28, 70, 24);

    /// <summary>Reads what the slots hold (after saving, or when the list opens).</summary>
    public void Refresh() => _slots = Enumerable.Range(0, SaveSlots.Count).Select(SaveSlots.Info).ToArray();

    public void Draw(SpriteBatch batch, UiFont font, Vector2 cursor)
    {
        switch (_page)
        {
            case Page.Overview:
                Centered(batch, font, Lang.T("system.save"), SaveItem, 18, SaveItem.Contains(cursor));
                Centered(batch, font, Lang.T("system.quit"), QuitItem, 18, QuitItem.Contains(cursor));
                break;
            case Page.Save:
                Centered(batch, font, Lang.T("system.save"), SaveItem, 18, false);
                for (int i = 0; i < _slots.Length; i++)
                {
                    var rect = Slot(i);
                    bool over = rect.Contains(cursor);
                    if (_slots[i] is not { } info)
                    {
                        Write(batch, font, "------------", new Vector2(rect.X, rect.Y + 8), over);
                        continue;
                    }
                    Write(batch, font, Fit(font, Lang.SaveName(info), rect.Width), new Vector2(rect.X, rect.Y), over);
                    string when = info.SavedAt.ToString(Lang.T("formats.savedAt"), Lang.Culture);
                    Write(batch, font, Fit(font, $"{when}  {Lang.T("peoples." + info.People)}", rect.Width), new Vector2(rect.X, rect.Y + 16), over);
                }
                break;
            case Page.Quit:
                var middle = new Rectangle(Content.X, Content.Center.Y - 12, Content.Width, 24);
                Centered(batch, font, Lang.T("system.quitQuestion"), middle, 17, false);
                Centered(batch, font, Lang.T("common.yes"), Yes, 18, Yes.Contains(cursor));
                Centered(batch, font, Lang.T("common.no"), No, 18, No.Contains(cursor));
                break;
        }
    }

    /// <summary>A click (panel space): turns the page, or asks the game to save (into <see cref="ChosenSlot"/>) or quit.</summary>
    public SystemAction Click(Vector2 panel)
    {
        switch (_page)
        {
            case Page.Overview when SaveItem.Contains(panel):
                Refresh();
                _page = Page.Save;
                break;
            case Page.Overview when QuitItem.Contains(panel):
                _page = Page.Quit;
                break;
            case Page.Save:
                for (int i = 0; i < SaveSlots.Count; i++)
                    if (Slot(i).Contains(panel))
                    {
                        ChosenSlot = i;
                        return SystemAction.Save;
                    }
                break;
            case Page.Quit when Yes.Contains(panel):
                return SystemAction.Quit;
            case Page.Quit when No.Contains(panel):
                _page = Page.Overview;
                break;
        }
        return SystemAction.None;
    }

    /// <summary>What the element under the point does (hover text), or null.</summary>
    public string? HoverText(Vector2 panel)
    {
        if (_page == Page.Save)
            for (int i = 0; i < SaveSlots.Count; i++)
                if (Slot(i).Contains(panel))
                    return _slots[i] is null ? $"save into slot {i + 1} (empty)" : $"save into slot {i + 1} (overwrites it)";
        return null;
    }

    /// <summary>The text, cut off with an ellipsis where it would not fit.</summary>
    private static string Fit(UiFont font, string text, int width)
    {
        if (font.Measure(text, 15).X <= width)
            return text;
        while (text.Length > 1 && font.Measure(text + "…", 15).X > width)
            text = text[..^1];
        return text + "…";
    }

    private static void Write(SpriteBatch batch, UiFont font, string text, Vector2 at, bool over) =>
        SelectionPanel.Shadowed(batch, font, text, at, 15, over ? Hover : Text);

    private static void Centered(SpriteBatch batch, UiFont font, string text, Rectangle rect, int size, bool over)
    {
        var measured = font.Measure(text, size);
        SelectionPanel.Shadowed(batch, font, text, new Vector2(rect.Center.X - measured.X / 2, rect.Center.Y - measured.Y / 2), size,
            over ? Hover : Text);
    }
}
