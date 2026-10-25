using System.Text.Json;
using Homestead.Client.Terrain;
using Homestead.Simulation;
using Homestead.Simulation.Buildings;
using Homestead.Simulation.Military;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Homestead.Client;

/// <summary>
/// Draws war machines and their shots (<c>warMachines</c> in <c>data/ui.json</c>, per people its soldier file). Catapult,
/// ballista and cannon: the body (one frame per direction, SW, W, NW, NE, E, SE) with the wheels over it (rolling while it
/// drives), firing a sequence of its own per direction; the shot flies in an arc (one sequence turning, or one per
/// direction). The gong drives like a settler walks (six blocks of 12 frames) and is struck in a sequence per direction.
/// </summary>
public sealed class WarMachineRenderer
{
    /// <summary>Frames of the firing animation per tick.</summary>
    private const float FireFramesPerTick = 0.5f;

    /// <summary>Frames of the rolling wheels per step.</summary>
    private const int WheelFramesPerStep = 6;

    private readonly SpriteLibrary _library;
    private readonly Dictionary<Civilization, Look> _looks = new();

    private sealed record Look(int File, int Body, int? Wheels, int Fire, int? Shot, bool ShotPerDirection, bool Walks);

    public WarMachineRenderer(SpriteLibrary library)
    {
        _library = library;
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(BuildingCatalog.DefaultDataDirectory, "ui.json")));
        if (!document.RootElement.TryGetProperty("warMachines", out var machines))
            return;
        foreach (var people in machines.EnumerateObject().Where(p => !p.Name.StartsWith('_')))
        {
            var e = people.Value;
            int? Optional(string name) => e.TryGetProperty(name, out var v) ? v.GetInt32() : null;
            _looks[Enum.Parse<Civilization>(people.Name)] = new Look(e.GetProperty("file").GetInt32(), e.GetProperty("body").GetInt32(),
                Optional("wheels"), e.GetProperty("fire").GetInt32(), Optional("shot"),
                e.TryGetProperty("shotPerDirection", out var perDirection) && perDirection.GetBoolean(),
                e.TryGetProperty("walks", out var walks) && walks.GetBoolean());
        }
    }

    /// <summary>Index of a direction in the original's order (SW, W, NW, NE, E, SE).</summary>
    private static int Block(Direction facing) => SettlerSprites.StandFrame(facing) / SettlerSprites.FramesPerDirection;

    public void Draw(SpriteBatch batch, Settler machine, Civilization people, TerrainMesh terrain, long tick, float tickAlpha, Color playerColor)
    {
        if (!_looks.TryGetValue(people, out var look))
            return;
        var anchor = SettlerRenderer.Anchor(machine, terrain, tickAlpha);
        int block = Block(machine.Facing);
        // Firing: once through its own sequence, from the shot on.
        var fire = _library.Sequence(look.File, look.Fire + block);
        float sinceShot = tick - machine.LastShotTick + tickAlpha;
        if (sinceShot >= 0 && sinceShot * FireFramesPerTick < fire.FrameCount)
        {
            fire.Draw(batch, (int)(sinceShot * FireFramesPerTick), anchor, 1, playerColor);
            return;
        }
        bool driving = machine.State == SettlerState.Stepping;
        float progress = driving ? Math.Clamp((machine.StepProgress + tickAlpha * machine.StepSpeed) / World.StepUnits, 0, 1) : 0;
        var body = _library.Sequence(look.File, look.Body);
        if (look.Walks)
        {
            body.Draw(batch, driving ? SettlerSprites.WalkFrame(machine.Facing, progress) : SettlerSprites.StandFrame(machine.Facing), anchor, 1,
                playerColor);
            return;
        }
        body.Draw(batch, block, anchor, 1, playerColor);
        if (look.Wheels is { } wheels)
        {
            var sprites = _library.Sequence(look.File, wheels);
            int perDirection = sprites.FrameCount / DirectionExtensions.Count;
            // The wheels turn on with every field: where it is on the map counts, not the time.
            int turned = driving ? (int)((machine.Position.X + machine.Position.Y + progress) * WheelFramesPerStep) : 0;
            sprites.Draw(batch, block * perDirection + turned % Math.Max(1, perDirection), anchor, 1, playerColor, shadow: false);
        }
    }

    /// <summary>A war machine as an icon, fitted into a box (selection, menus).</summary>
    public void DrawIcon(SpriteBatch batch, Civilization people, Rectangle box, Color playerColor)
    {
        if (!_looks.TryGetValue(people, out var look))
            return;
        var body = _library.Sequence(look.File, look.Body);
        int frame = look.Walks ? SettlerSprites.StandFrame(Direction.SouthWest) : Block(Direction.SouthWest);
        if (body.Bounds(frame) is not { } bounds)
            return;
        var (anchor, scale) = SettlerSprites.Fit(bounds, box, 1);
        body.Draw(batch, frame, anchor, scale, playerColor);
    }

    // Effects of file 13 (the same in every people's building file): a small gray explosion, light and dark smoke, flames.
    private const int EffectFile = 13;
    private const int ExplosionSequence = 49;
    private const int SmokeSequence = 42;
    private const int DarkSmokeSequence = 43;
    private const int SmallFlameSequence = 41;
    private const int FlameSequence = 40;
    private const int BigFlameSequence = 39;
    private const float EffectFramesPerSecond = 12;

    /// <summary>
    /// Where on a damaged building smoke and flames go, as shares of its picture (x from the left, y from the top): the first
    /// with the first damage, more the worse it gets.
    /// </summary>
    private static readonly Vector2[] DamageSpots = [new(0.5f, 0.3f), new(0.3f, 0.55f), new(0.7f, 0.5f), new(0.45f, 0.7f)];

    /// <summary>The point at a share of the building's picture (see <see cref="DamageSpots"/>), in world pixels.</summary>
    private Vector2 OnBuilding(Building building, Vector2 share, TerrainMesh terrain)
    {
        var anchor = ScreenProjection.ToWorld(building.Position.X, building.Position.Y, terrain.HeightAt(building.Position.X, building.Position.Y));
        if (building.Definition.FinalImages.Count == 0)
            return anchor;
        var image = building.Definition.FinalImages[0];
        if (_library.Sequence(image.File, image.Sequence).Bounds(image.Image) is not { } bounds)
            return anchor;
        return anchor + new Vector2(bounds.X + bounds.Width * share.X, bounds.Y + bounds.Height * share.Y);
    }

    /// <summary>A shot that landed: a small gray explosion on the building it hit (or on the ground), <paramref name="seconds"/> ago.</summary>
    public void DrawImpact(SpriteBatch batch, MachineImpact impact, TerrainMesh terrain, float seconds)
    {
        var sprites = _library.Sequence(EffectFile, ExplosionSequence);
        int frame = (int)(seconds * EffectFramesPerSecond * 1.5f);
        if (frame >= sprites.FrameCount)
            return;
        var at = impact.Building is { } building
            ? OnBuilding(building, DamageSpots[(int)(impact.Tick % 3)], terrain)
            : ScreenProjection.ToWorld(impact.Position.X, impact.Position.Y, terrain.HeightAt(impact.Position.X, impact.Position.Y));
        sprites.Draw(batch, frame, at, 1, Color.White, shadow: false);
    }

    /// <summary>
    /// A building hit by war machines: light smoke from the first hit, then flames and dark smoke the worse it is (<paramref name="damage"/>
    /// 0-1); as it mends they go out again.
    /// </summary>
    public void DrawDamage(SpriteBatch batch, Building building, double damage, TerrainMesh terrain, float seconds)
    {
        if (damage <= 0)
            return;
        void Loop(int sequence, Vector2 share, int offset)
        {
            var sprites = _library.Sequence(EffectFile, sequence);
            if (sprites.FrameCount > 0)
                sprites.Draw(batch, ((int)(seconds * EffectFramesPerSecond) + offset) % sprites.FrameCount, OnBuilding(building, share, terrain), 1,
                    Color.White, shadow: false);
        }
        int seed = building.Id * 7;
        Loop(damage >= 0.5 ? DarkSmokeSequence : SmokeSequence, DamageSpots[0], seed);
        if (damage >= 0.25)
            Loop(SmallFlameSequence, DamageSpots[1], seed + 5);
        if (damage >= 0.5)
            Loop(FlameSequence, DamageSpots[2], seed + 11);
        if (damage >= 0.75)
            Loop(BigFlameSequence, DamageSpots[3], seed + 3);
    }

    /// <summary>A shot in flight: in an arc from the machine to where it lands (the gong's sound has nothing to draw).</summary>
    public void DrawShot(SpriteBatch batch, MachineShot shot, Civilization people, TerrainMesh terrain, long tick, float tickAlpha)
    {
        if (shot.FlightTicks <= 0 || !_looks.TryGetValue(people, out var look) || look.Shot is not { } first)
            return;
        var start = ScreenProjection.ToWorld(shot.From.X, shot.From.Y, terrain.HeightAt(shot.From.X, shot.From.Y)) - new Vector2(0, 16);
        var end = ScreenProjection.ToWorld(shot.To.X, shot.To.Y, terrain.HeightAt(shot.To.X, shot.To.Y)) - new Vector2(0, 12);
        float progress = Math.Clamp((tick - shot.Tick + tickAlpha) / shot.FlightTicks, 0, 1);
        float arc = 8 + 0.25f * Vector2.Distance(start, end);
        var position = Vector2.Lerp(start, end, progress) - new Vector2(0, arc * 4 * progress * (1 - progress));
        if (look.ShotPerDirection)
        {
            var sprites = _library.Sequence(look.File, first + Block(Military.FacingTowards(shot.From, shot.To)));
            sprites.Draw(batch, Math.Min(sprites.FrameCount - 1, (int)(progress * sprites.FrameCount)), position, 1, Color.White, shadow: false);
        }
        else
        {
            var sprites = _library.Sequence(look.File, first);
            sprites.Draw(batch, (int)(tick - shot.Tick) % Math.Max(1, sprites.FrameCount), position, 1, Color.White, shadow: false);
        }
    }
}
