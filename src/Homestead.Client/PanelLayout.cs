using Microsoft.Xna.Framework;

namespace Homestead.Client;

/// <summary>
/// The areas of the blue side panel (panel space): the dark bar for the sub-menus below the main tabs and the
/// content area below them, where the menus draw. Measured on the panel image; owned by <see cref="MapGame"/> and
/// passed to the menus instead of statics on any one of them.
/// </summary>
public sealed record PanelLayout(Rectangle Content, Rectangle TabBar);
