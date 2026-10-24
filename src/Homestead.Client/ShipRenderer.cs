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
/// player's color), one frame per direction; a ship on the stocks as its hull growing from the keel up. A ferry's passengers
/// stand on its deck, a trade ship's piles lie there, between the whole ship and its front part (the sequence before it:
/// cabin and rails), like the soldiers on a castle.
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

    /// <summary>Where a ship is in grid fields (fractional while it moves), as <see cref="Anchor"/>.</summary>
    public static Vector2 GridAt(Ship ship, int stepTicks, float alpha)
    {
        var from = new Vector2(ship.Position.X, ship.Position.Y);
        if (ship.StepTicks == 0)
            return from;
        var next = ship.Position.Neighbor(ship.Facing);
        return Vector2.Lerp(from, new Vector2(next.X, next.Y), Math.Clamp((ship.StepTicks + alpha) / stepTicks, 0, 1));
    }

    /// <summary>The row a ship is drawn in (with what lies in front of it drawn later).</summary>
    public static int DepthRow(Ship ship) =>
        ship.StepTicks > 0 ? Math.Max(ship.Position.Y, ship.Position.Neighbor(ship.Facing).Y) : ship.Position.Y;

    /// <summary>
    /// Where the passengers stand on the ferry (the small ship, mast amidships), in fields from the anchor along the ship
    /// (towards the bow) and across it; the first ones next to the mast.
    /// </summary>
    private static readonly (float Along, float Across)[] PassengerPlaces =
        [(0.8f, -0.5f), (0.8f, 0.5f), (-0.8f, -0.5f), (-0.8f, 0.5f), (2.0f, 0f), (-2.0f, -0.4f), (-2.0f, 0.4f)];

    /// <summary>Where the piles of goods lie on the trade ship (the big one: cabin aft, the mast a little before the middle).</summary>
    private static readonly (float Along, float Across)[] CargoPlaces = [(-0.4f, 0f), (2.6f, -0.3f), (4.0f, 0.3f)];

    /// <summary>How far above the waterline (the anchor) the deck is.</summary>
    private const float DeckHeight = 9;

    /// <summary>
    /// Where the mast stands, in fields from the anchor towards the bow: what is on deck in front of it (on the screen) hides
    /// the sail.
    /// </summary>
    private static float MastAlong(ShipKind kind) => kind == ShipKind.Ferry ? 0f : 1f;

    /// <summary>
    /// The ship and on its deck the ferry's passengers (<paramref name="settlers"/>) or the trade ship's
    /// <paramref name="cargo"/> piles (<paramref name="economy"/>), between the whole ship and its front part (the sequence
    /// before it), the sail over what is behind the mast.
    /// </summary>
    public void Draw(SpriteBatch batch, Ship ship, Vector2 anchor, Color playerColor, SettlerRenderer? settlers = null,
        EconomyRenderer? economy = null, IReadOnlyList<(Material Material, int Count)>? cargo = null)
    {
        if (!Sequences(ship.People, ship.Kind, out var hull, out var sail))
            return;
        int frame = SettlerSprites.Block(ship.Facing);
        hull.Draw(batch, frame, anchor, 1, playerColor);
        var items = new List<(Vector2 At, Action<Vector2> Draw)>();
        if (settlers is not null)
            items.AddRange(ship.Passengers.Take(PassengerPlaces.Length).Select((passenger, i) =>
                (Place(ship, anchor, PassengerPlaces[i]), (Action<Vector2>)(at => settlers.DrawStanding(batch, passenger, at, ship.Facing, playerColor)))));
        if (economy is not null && cargo is not null)
            items.AddRange(cargo.Take(CargoPlaces.Length).Select((pile, i) =>
                (Place(ship, anchor, CargoPlaces[i]), (Action<Vector2>)(at => economy.DrawStack(batch, pile.Material, pile.Count, at)))));
        if (items.Count == 0)
        {
            sail.Draw(batch, frame, anchor, 1, playerColor);
            return;
        }
        float mast = Place(ship, anchor, (MastAlong(ship.Kind), 0)).Y;
        items.Sort((a, b) => a.At.Y.CompareTo(b.At.Y));
        foreach (var (at, draw) in items.Where(i => i.At.Y <= mast))
            draw(at);
        _library.Sequence(_file, _sequences[SequenceKey(ship.People, ship.Kind)].Ship - 1).Draw(batch, frame, anchor, 1, playerColor);
        sail.Draw(batch, frame, anchor, 1, playerColor);
        foreach (var (at, draw) in items.Where(i => i.At.Y > mast))
            draw(at);
    }

    /// <summary>A place on the deck in world pixels: along the ship one field's step on the screen, across square to it (foreshortened as the ground).</summary>
    private static Vector2 Place(Ship ship, Vector2 anchor, (float Along, float Across) place)
    {
        var next = new GridPoint(10, 10).Neighbor(ship.Facing);
        var step = ScreenProjection.ToWorld(next.X, next.Y, 0) - ScreenProjection.ToWorld(10, 10, 0);
        var across = Vector2.Normalize(new Vector2(-step.Y, step.X)) * step.Length() * new Vector2(1, 0.56f);
        return anchor - new Vector2(0, DeckHeight) + step * place.Along + across * place.Across;
    }

    private (Civilization, ShipKind) SequenceKey(Civilization people, ShipKind kind) =>
        _sequences.ContainsKey((people, kind)) ? (people, kind) : (Civilization.Roman, kind);

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
