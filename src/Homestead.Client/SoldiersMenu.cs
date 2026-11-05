using Homestead.Assets.Gfx;
using Homestead.Simulation;
using Homestead.Simulation.Commands;
using Homestead.Simulation.Buildings;
using Homestead.Simulation.Magic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Homestead.Client;

/// <summary>
/// Siedler › Krieger in the blue panel, like the original's: the combat strength ("Offensiv"), the divine
/// upgrade (progress of the mana earned towards the next one, one button per soldier type – its weapon plain
/// or upgraded, dark while it cannot be upgraded), the switch between automatic and manual recruitment with
/// the carriers free to become soldiers, and only for manual recruitment per soldier type the weapons in
/// stock and the soldiers planned (arrows: 5 or 1 more or fewer). Made for one people (its weapons and
/// portraits): when the people changes, the game makes it anew. Graphics in <c>data/ui.json</c>
/// (<c>soldiersMenu</c>). Panel space.
/// </summary>
public sealed class SoldiersMenu : IPanelMenu, IDisposable
{
    private static readonly SoldierType[] Types = [SoldierType.Swordsman, SoldierType.Bowman, SoldierType.Pikeman];
    private static readonly int[] ArrowSteps = [5, 1, -1, -5];
    private static readonly Color Text = new(240, 225, 190);
    private static readonly Color Label = new(190, 165, 120);

    private readonly PanelLayout _layout;
    private readonly Portraits _portraits;
    private readonly SettlerRenderer _settlers;
    private readonly Civilization _people;
    private readonly BuildingInfo _names;
    private readonly Dictionary<SoldierType, Texture2D[]> _upgrades = new();
    private readonly Texture2D[] _arrows;
    private readonly Dictionary<SoldierType, Texture2D> _weapons = new();
    private bool _wasPressed;

    public SoldiersMenu(GraphicsDevice device, GfxDirectory gfx, Civilization people, Portraits portraits, SettlerRenderer settlers,
        BuildingInfo names, PanelLayout layout, UiLayout ui)
    {
        _layout = layout;
        _portraits = portraits;
        _settlers = settlers;
        _people = people;
        _names = names;
        Texture2D Load(GfxFile file, int index)
        {
            var bitmap = file.ReadGui(index);
            var texture = new Texture2D(device, bitmap.Width, bitmap.Height);
            texture.SetData(bitmap.Pixels);
            return texture;
        }

        var menu = ui.SoldiersMenu;
        var gui = gfx.Open(3);
        foreach (var type in Types)
            _upgrades[type] = menu.Upgrades[Key(type)].Select(i => Load(gui, i)).ToArray();
        _arrows = menu.Arrows.Select(i => Load(gui, i)).ToArray();
        var ownWeapons = menu.Weapons[people.ToString()];
        var own = gfx.Open(ownWeapons.File);
        foreach (var type in Types)
            _weapons[type] = Load(own, type switch
            {
                SoldierType.Swordsman => ownWeapons.Swordsman,
                SoldierType.Bowman => ownWeapons.Bowman,
                _ => ownWeapons.Pikeman,
            });
    }

    private static string Key(SoldierType type) => type.ToString().ToUpperInvariant();

    // Layout (panel space), top down in the content area.
    private int Left => _layout.Content.X + 4;
    private int Width => _layout.Content.Width - 8;
    private int Top => _layout.Content.Y + 22; // below the title
    private int OffensiveY => Top;
    private int DivineY => Top + 20;
    private Rectangle UpgradeButton(int i) => new(Left + i * (Width / 3) + (Width / 3 - 60) / 2, Top + 42, 60, 40);
    private int SwitchY => Top + 92;
    private Rectangle SwitchButton => new(Left + 70, SwitchY + 16, Width - 70, 28);
    private int RecruitY => SwitchY + 70;
    private const int RowHeight = 34;
    private Rectangle WeaponIcon(int row) => new(Left, RecruitY + 20 + row * RowHeight, 30, 30);

    /// <summary>The arrows of a row: double up, up, down, double down; the numbers between them.</summary>
    private Rectangle Arrow(int row, int i)
    {
        int x = Left + 64 + i * 22 + (i >= 2 ? 32 : 0);
        return new Rectangle(x, RecruitY + 20 + row * RowHeight, 20, 30);
    }

    public void Draw(SpriteBatch batch, Texture2D pixel, UiFont font, World world, int player, Vector2 cursor, float seconds)
    {
        var military = world.Military;
        var magic = world.Magic;
        Write(batch, font, Lang.T("soldiersMenu.offensive", $"{military.CombatStrength(player) * 100:0}"), new Vector2(Left, OffensiveY), Text, 15);

        // Divine upgrade: how far the mana earned is on the way to the next upgrade.
        int earned = magic.ManaEarned(player);
        int progress = magic.UpgradeThreshold(player) is { } next && next > 0 ? Math.Min(100, earned * 100 / next) : 100;
        Write(batch, font, Lang.T("soldiersMenu.divine", progress), new Vector2(Left, DivineY), Text, 13);
        for (int i = 0; i < Types.Length; i++)
        {
            var type = Types[i];
            bool upgraded = magic.SoldierLevel(player, type) >= 2;
            bool possible = CanUpgrade(world, player, type);
            var images = _upgrades[type];
            var rect = UpgradeButton(i);
            batch.Draw(images[(upgraded ? 2 : 0) + (possible ? 0 : 1)], rect, Color.White);
            // The upgrade buttons keep their own, lighter veil.
            if (possible && rect.Contains(cursor))
                batch.Draw(pixel, rect, Color.White * 0.12f);
        }

        // The switch: the carriers free to become soldiers, automatic or manual.
        var switchButton = SwitchButton;
        _portraits.Draw(batch, "BEARER", new Rectangle(Left, SwitchY, 40, 60), _settlers, _people, PlayerColors.Of(player));
        Write(batch, font, world.Recruitment.Convertible(player).ToString(), new Vector2(Left + 44, SwitchY + 20), Text, 15);
        TextButton.Draw(batch, pixel, font, switchButton, Lang.T(military.IsManualRecruitment(player) ? "soldiersMenu.manual" : "soldiersMenu.automatic"), cursor);
        if (!military.IsManualRecruitment(player))
            return;

        // Manual recruitment: per type the weapons in stock, arrows, the soldiers planned.
        string recruitment = Lang.T("soldiersMenu.recruitment");
        var heading = font.Measure(recruitment, 13);
        Write(batch, font, recruitment, new Vector2(Left + (Width - heading.X) / 2, RecruitY), Label, 13);
        for (int row = 0; row < Types.Length; row++)
        {
            var type = Types[row];
            var icon = WeaponIcon(row);
            batch.Draw(_weapons[type], icon, Color.White);
            Write(batch, font, military.WeaponsInStock(player, type).ToString(), new Vector2(icon.Right + 6, icon.Y + 6), Text);
            for (int i = 0; i < _arrows.Length; i++)
            {
                var arrow = Arrow(row, i);
                batch.Draw(_arrows[i], arrow, Color.White);
                if (arrow.Contains(cursor))
                    PanelUi.HoverOverlay(batch, pixel, arrow);
            }
            string planned = military.Planned(player, type).ToString();
            var plannedSize = font.Measure(planned);
            float between = (Arrow(row, 1).Right + Arrow(row, 2).X) / 2f;
            Write(batch, font, planned, new Vector2(between - plannedSize.X / 2, icon.Y + 6), Text);
        }
    }

    private static bool CanUpgrade(World world, int player, SoldierType type) =>
        world.Magic.UpgradeOrder(player, type) == UpgradeResult.Done && world.Magic.CanAffordUpgrade(player)
        && world.Buildings.Any(b => b.Exists && b.Player == player && b.Definition.Name == world.Magic.Config.Temple && b.State == BuildingState.Finished);

    /// <summary>Mouse handling (panel space): a click – the button released on the menu – upgrades, switches or plans.</summary>
    public string? Update(Vector2 panel, bool pressed, bool justPressed, bool onMenu, World world, int player, IssueCommand issue)
    {
        string? done = !pressed && _wasPressed && onMenu ? Click(panel, world, player, issue) : null;
        _wasPressed = pressed;
        return done;
    }

    public void ResetInput() => _wasPressed = false;

    /// <summary>A click (panel space): upgrades, switches or plans; returns a message, or null if it hit nothing.</summary>
    private string? Click(Vector2 panel, World world, int player, IssueCommand issue)
    {
        var military = world.Military;
        for (int i = 0; i < Types.Length; i++)
            if (UpgradeButton(i).Contains(panel))
            {
                var type = Types[i];
                string name = _names.SoldierName(type);
                issue(new UpgradeSoldiers(player, type), result => (UpgradeResult)result! switch
                {
                    UpgradeResult.Done => $"{name}: new ones become {_names.LevelName(world.Magic.SoldierLevel(player, type))}",
                    UpgradeResult.OthersFirst => "all soldier types to level 2 first",
                    UpgradeResult.NoMana => "not enough mana earned yet",
                    UpgradeResult.Highest => $"{name}: highest level already",
                    _ => "a finished small temple is needed",
                });
                return null;
            }
        if (SwitchButton.Contains(panel))
        {
            bool manual = !military.IsManualRecruitment(player);
            issue(new SetManualRecruitment(player, manual));
            UiSounds.Toggle();
            return manual ? "manual recruitment: barracks train only the soldiers planned" : "automatic recruitment";
        }
        if (!military.IsManualRecruitment(player))
            return null;
        for (int row = 0; row < Types.Length; row++)
            for (int i = 0; i < ArrowSteps.Length; i++)
                if (Arrow(row, i).Contains(panel))
                {
                    var type = Types[row];
                    issue(new ChangePlanned(player, type, ArrowSteps[i]), _ => $"{_names.SoldierName(type)}: {military.Planned(player, type)} planned");
                    UiSounds.Step(ArrowSteps[i] > 0);
                    return null;
                }
        return null;
    }

    /// <summary>The tooltip of the upgrade button or arrow under the panel point, or null.</summary>
    public string? TooltipAt(Vector2 panel, World world, int player, BuildingInfo names)
    {
        for (int i = 0; i < Types.Length; i++)
            if (UpgradeButton(i).Contains(panel))
                return Lang.T("soldiersMenu.upgrade", names.SoldierName(Types[i]));
        if (world.Military.IsManualRecruitment(player))
            for (int row = 0; row < Types.Length; row++)
                for (int i = 0; i < ArrowSteps.Length; i++)
                    if (Arrow(row, i).Contains(panel))
                        return Lang.T(ArrowSteps[i] > 0 ? "soldiersMenu.planMore" : "soldiersMenu.planFewer", Math.Abs(ArrowSteps[i]));
        return null;
    }

    /// <summary>What the element under the point does (hover text), or null.</summary>
    public string? Hover(Vector2 panel, World world, int player)
    {
        var magic = world.Magic;
        for (int i = 0; i < Types.Length; i++)
            if (UpgradeButton(i).Contains(panel))
            {
                var type = Types[i];
                string rank = _names.LevelName(magic.SoldierLevel(player, type));
                return magic.UpgradeOrder(player, type) switch
                {
                    UpgradeResult.Highest => $"{_names.SoldierName(type)} ({rank}): highest level",
                    UpgradeResult.OthersFirst => $"{_names.SoldierName(type)} ({rank}): the others first",
                    _ => $"{_names.SoldierName(type)} ({rank}): upgrade at {magic.UpgradeThreshold(player)} mana earned ({magic.ManaEarned(player)} so far)",
                };
            }
        if (SwitchButton.Contains(panel))
            return "switch between automatic and manual recruitment";
        if (world.Military.IsManualRecruitment(player))
            for (int row = 0; row < Types.Length; row++)
                for (int i = 0; i < ArrowSteps.Length; i++)
                    if (Arrow(row, i).Contains(panel))
                        return $"{_names.SoldierName(Types[row])}: {ArrowSteps[i]:+0;-0} planned";
        return null;
    }

    private static void Write(SpriteBatch batch, UiFont font, string text, Vector2 position, Color color, int size = 14) =>
        SelectionPanel.Shadowed(batch, font, text, position, size, color);

    public void Dispose()
    {
        foreach (var texture in _upgrades.Values.SelectMany(t => t).Concat(_arrows).Concat(_weapons.Values))
            texture.Dispose();
    }
}
