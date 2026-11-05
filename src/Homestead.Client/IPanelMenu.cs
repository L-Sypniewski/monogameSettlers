using Homestead.Simulation;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Homestead.Client;

/// <summary>
/// A sub-menu of the blue side panel (one of <c>panelTabs.menus</c> in <c>data/ui.json</c>): everything is drawn,
/// hit-tested and clicked in panel space. <see cref="Draw"/> runs every frame the menu is shown;
/// <see cref="Update"/> every frame it is the open sub-menu; <see cref="Hover"/> only while the cursor is on the
/// panel. Both <see cref="Update"/> and <see cref="Hover"/> return the message to show to the player, or null.
/// Orders go to the simulation through <paramref name="issue"/> of <see cref="Update"/> only
/// (<see cref="IssueCommand"/>) — input never changes the world any other way.
/// </summary>
public interface IPanelMenu
{
    /// <param name="pixel">A one-pixel texture, for frames and overlays.</param>
    /// <param name="cursor">The mouse in panel space.</param>
    /// <param name="seconds">Seconds since the last frame (animations; see <see cref="TransportMenu"/>'s glide).</param>
    void Draw(SpriteBatch batch, Texture2D pixel, UiFont font, World world, int player, Vector2 cursor, float seconds);

    /// <param name="panel">The mouse in panel space.</param>
    /// <param name="pressed">The left button is held (and no selection box is being dragged on the map).</param>
    /// <param name="justPressed">The left button went down this frame.</param>
    /// <param name="onMenu">The cursor is on the side panel.</param>
    string? Update(Vector2 panel, bool pressed, bool justPressed, bool onMenu, World world, int player, IssueCommand issue);

    /// <summary>What the element under the point does (hover text), or null.</summary>
    string? Hover(Vector2 panel, World world, int player);
}
