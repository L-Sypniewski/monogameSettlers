using Homestead.Simulation;
using Homestead.Simulation.Commands;
using Homestead.Simulation.Buildings;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Homestead.Client;

/// <summary>
/// The work-area button of a finished own building with a work area (as in the original, centered between
/// the pause and the tear-down icon): after it, the next left click on the map moves the work area there
/// (see <see cref="World.SetWorkCenter"/>); a right click or Esc cancels. The marks of the moved area
/// show as always (see MapGame's work-area marks).
/// </summary>
public sealed partial class BuildingInfo
{
    /// <summary>The building whose work area the next left click on the map moves, if any.</summary>
    private Building? _workAiming;

    /// <summary>Whether the next left click on the map moves the work area of <paramref name="building"/>.</summary>
    public bool AimsWorkCenter(Building? building) => building is not null && _workAiming == building;

    /// <summary>No work area to move any more (right click, Esc).</summary>
    public void CancelWorkCenter() => _workAiming = null;

    private static bool OffersWorkArea(Building building) => building.HasWorkArea && building.State == BuildingState.Finished;

    /// <summary>
    /// The button's own icon: nested circles in spectral colors like the original's work-area rings, every ring
    /// edged dark so it stands out on the stone (the atlas holds no such glyph; generated once, like the
    /// pointer of <see cref="SoftwareCursor"/>).
    /// </summary>
    private static Texture2D CreateWorkAreaIcon(GraphicsDevice device)
    {
        const int size = DemolishSize; // drawn 1:1
        Color edge = new(40, 30, 15), red = new(205, 60, 25), amber = new(245, 190, 40),
            green = new(70, 165, 60), blue = new(50, 110, 200), core = new(205, 160, 245);
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - size / 2f, dy = y + 0.5f - size / 2f;
                float d = MathF.Sqrt(dx * dx + dy * dy);
                pixels[y * size + x] = d > 14.5f ? Color.Transparent
                    : d > 13.0f ? edge
                    : d > 11.0f ? red
                    : d > 10.0f ? edge
                    : d > 8.0f ? amber
                    : d > 7.0f ? edge
                    : d > 5.0f ? green
                    : d > 4.0f ? edge
                    : d > 2.2f ? blue
                    : core;
            }
        var texture = new Texture2D(device, size, size);
        texture.SetData(pixels);
        return texture;
    }

    /// <summary>A click on the panel (panel space): arms or disarms moving the work area. Returns a message, or null if it hit nothing.</summary>
    public string? WorkAreaClick(Vector2 panel, World world, Building building, int ownPlayer)
    {
        if (building.Player != ownPlayer || !OffersWorkArea(building) || _confirming == building || !WorkAreaButton(0).Contains(panel))
            return null;
        if (AimsWorkCenter(building))
        {
            _workAiming = null;
            return "work area choice undone";
        }
        _workAiming = building;
        UiSounds.Toggle();
        return "click where the work area goes (right click: cancel)";
    }

    /// <summary>
    /// A left click on the map while aiming (<see cref="AimsWorkCenter"/>), and the right click on the selected
    /// building: the work area moves there (at most <see cref="World.MaxWorkCenterDistance"/> fields away).
    /// </summary>
    public void WorkCenterCommand(World world, Building building, GridPoint clicked) =>
        Issue!(new SetWorkCenter(building.Player, building.Id, clicked), result =>
        {
            if (result is not GridPoint center)
                return null;
            if (_workAiming == building)
                _workAiming = null;
            UiSounds.WorkArea();
            return building.HasGatheringPoint ? $"gathering point moved to {center.X},{center.Y}"
                : center == clicked ? $"work area moved to {center.X},{center.Y}"
                : $"work area moved to {center.X},{center.Y} (at most {World.MaxWorkCenterDistance} fields away)";
        });
}
