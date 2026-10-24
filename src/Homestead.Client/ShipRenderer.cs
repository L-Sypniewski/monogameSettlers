using System.Text.Json;
using Homestead.Client.Terrain;
using Homestead.Simulation;
using Homestead.Simulation.Buildings;
using Homestead.Simulation.Economy;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Homestead.Client;

/// <summary>
/// Draws ships (GFX file 36, <c>ships</c> in <c>ui.json</c>): per people and kind the whole ship and its sail (in the
/// player's color), one frame per direction; a ship on the stocks as its hull growing from the keel up.
/// </summary>
public sealed class ShipRenderer
{
    private readonly SpriteLibrary _library;
    private readonly int _file;
    private readonly Dictionary<(Civilization, ShipKind), (int Ship, int Sail)> _sequences = new();

    /// <summary>How much of the hull shows when the first unit is not in yet.</summary>
    private const float FirstHull = 0.12f;

    public ShipRenderer(SpriteLibrary library)
    {
        _library = library;
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(BuildingCatalog.DefaultDataDirectory, "ui.json")));
        var ships = document.RootElement.GetProperty("ships");
        _file = ships.GetProperty("file").GetInt32();
        foreach (var people in ships.GetProperty("peoples").EnumerateObject())
        foreach (var kind in people.Value.EnumerateObject())
            _sequences[(Enum.Parse<Civilization>(people.Name), Enum.Parse<ShipKind>(kind.Name, ignoreCase: true))] =
                (kind.Value.GetProperty("ship").GetInt32(), kind.Value.GetProperty("sail").GetInt32());
    }

    /// <summary>Where a ship is drawn: between its field and the next while it moves.</summary>
    public static Vector2 Anchor(Ship ship, TerrainMesh terrain, int stepTicks, float alpha)
    {
        var from = ScreenProjection.ToWorld(ship.Position.X, ship.Position.Y, terrain.HeightAt(ship.Position.X, ship.Position.Y));
        if (ship.StepTicks == 0)
            return from;
        var next = ship.Position.Neighbor(ship.Facing);
        var to = ScreenProjection.ToWorld(next.X, next.Y, terrain.HeightAt(next.X, next.Y));
        return Vector2.Lerp(from, to, Math.Clamp((ship.StepTicks + alpha) / stepTicks, 0, 1));
    }

    /// <summary>The row a ship is drawn in (with what lies in front of it drawn later).</summary>
    public static int DepthRow(Ship ship) =>
        ship.StepTicks > 0 ? Math.Max(ship.Position.Y, ship.Position.Neighbor(ship.Facing).Y) : ship.Position.Y;

    public void Draw(SpriteBatch batch, Ship ship, Vector2 anchor, Color playerColor)
    {
        if (!Sequences(ship.People, ship.Kind, out var hull, out var sail))
            return;
        int frame = SettlerSprites.Block(ship.Facing);
        hull.Draw(batch, frame, anchor, 1, playerColor);
        sail.Draw(batch, frame, anchor, 1, playerColor);
    }

    /// <summary>A ship on the stocks: the hull, the more of it the further it is built.</summary>
    public void DrawOnStocks(SpriteBatch batch, Civilization people, ShipKind kind, Direction facing, Vector2 anchor, double progress, Color playerColor)
    {
        if (Sequences(people, kind, out var ship, out _))
            ship.Draw(batch, SettlerSprites.Block(facing), anchor, 1, playerColor, FirstHull + (1 - FirstHull) * (float)Math.Clamp(progress, 0, 1));
    }

    private bool Sequences(Civilization people, ShipKind kind, out SettlerSprites ship, out SettlerSprites sail)
    {
        ship = sail = null!;
        if (!_sequences.TryGetValue((people, kind), out var s) && !_sequences.TryGetValue((Civilization.Roman, kind), out s))
            return false;
        ship = _library.Sequence(_file, s.Ship);
        sail = _library.Sequence(_file, s.Sail);
        return true;
    }
}
