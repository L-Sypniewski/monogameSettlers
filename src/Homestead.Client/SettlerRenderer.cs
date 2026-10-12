using Homestead.Assets.Gfx;
using Homestead.Client.Terrain;
using Homestead.Simulation;
using Homestead.Simulation.Buildings;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Homestead.Client;

/// <summary>
/// Draws simulation settlers: interpolates between ticks, follows terrain height and picks the
/// animation of the settler's people from <see cref="SettlerAnimations"/> (type, action, carried good,
/// direction). Specialists without an own animation look like carriers; settlers without a profession get a varied look.
/// </summary>
public sealed class SettlerRenderer : IDisposable
{
    private const float WorkFramesPerTick = 0.5f;

    private readonly GraphicsDevice _device;
    private readonly GfxDirectory _gfx;
    private readonly SettlerAnimations _animations;
    private readonly Func<int, Civilization> _peopleOf;
    private readonly Dictionary<int, int[]> _walkSequences = new();
    private readonly Dictionary<SettlerAnimation, bool> _exists = new();
    private readonly Dictionary<(int File, int Sequence), SettlerSprites> _sprites = new();

    /// <param name="peopleOf">People (civilization) of a player: settlers look like their people's.</param>
    public SettlerRenderer(GraphicsDevice device, GfxDirectory gfx, SettlerAnimations animations, Func<int, Civilization> peopleOf)
    {
        _device = device;
        _gfx = gfx;
        _animations = animations;
        _peopleOf = peopleOf;
    }

    /// <summary>Screen anchor (feet) of a settler in world pixels.</summary>
    public static Vector2 Anchor(Settler settler, TerrainMesh terrain, float tickAlpha)
    {
        var from = ToWorld(settler.Position, terrain);
        if (settler.State != SettlerState.Stepping)
            return from;
        var to = ToWorld(settler.StepTarget, terrain);
        return Vector2.Lerp(from, to, StepProgress(settler, tickAlpha));
    }

    /// <summary>Grid row used for depth sorting: the lower of the two rows a step connects.</summary>
    public static int DepthRow(Settler settler) => Math.Max(settler.Position.Y, settler.StepTarget.Y);

    /// <summary>A settler standing at a given screen position (soldiers on a tower).</summary>
    public void DrawStanding(SpriteBatch batch, Settler settler, Vector2 anchor, Direction facing, Color playerColor)
    {
        var (sprites, frame) = Look(settler, "NO_ACTION", facing, a => a.Start, 0);
        sprites.Draw(batch, frame, anchor, 1, playerColor);
    }

    public void Draw(SpriteBatch batch, Settler settler, TerrainMesh terrain, float tickAlpha, Color playerColor)
    {
        var (sprites, frame) = Frame(settler, tickAlpha);
        sprites.Draw(batch, frame, Anchor(settler, terrain, tickAlpha), 1, playerColor);
    }

    private (SettlerSprites Sprites, int Frame) Frame(Settler settler, float tickAlpha)
    {
        int work = (int)((settler.ActivityTicks + tickAlpha) * WorkFramesPerTick);
        if (WorkAction(settler.Activity) is { } action && Find(settler, action, settler.Facing) is { } animation)
            return (Sprites(animation), animation.Cycle(work));
        if (settler.State == SettlerState.Stepping)
        {
            float progress = StepProgress(settler, tickAlpha);
            return Look(settler, "WALKING", settler.Facing, a => a.Frame(progress),
                SettlerSprites.WalkFrame(settler.Facing, progress));
        }
        return Look(settler, "NO_ACTION", settler.Facing, a => a.Start, SettlerSprites.StandFrame(settler.Facing));
    }

    /// <summary>Walking or standing: the settler's own animation, else a carrier's, else a varied carrier look.</summary>
    private (SettlerSprites Sprites, int Frame) Look(Settler settler, string action, Direction facing, Func<SettlerAnimation, int> frame, int plainFrame)
    {
        if (Find(settler, action, facing) is { } own)
            return (Sprites(own), frame(own));
        var people = _peopleOf(settler.Player);
        if (Resolve(people, "BEARER", action, SettlerAnimations.MaterialName(settler.Carrying), facing) is { } carrier
            && settler.Profession != Profession.None)
            return (Sprites(carrier), frame(carrier));
        // Settlers without a profession: one of the leading carrier sequences of the people.
        int file = ((int)people + 1) * 10;
        var looks = WalkSequences(file);
        return (Sprites(file, looks[settler.Id % looks.Length]), plainFrame);
    }

    /// <summary>The action of an activity in the animation tables.</summary>
    private static string? WorkAction(Activity activity) => activity switch
    {
        Activity.Sowing => "ACTION2",
        Activity.Digging or Activity.Building or Activity.Chopping or Activity.Mining or Activity.Planting
            or Activity.Harvesting or Activity.Fishing or Activity.DrawingWater or Activity.Fighting
            or Activity.Shooting => "ACTION1",
        _ => null,
    };

    private SettlerAnimation? Find(Settler settler, string action, Direction facing) =>
        TypeOf(settler) is { } type
            ? Resolve(_peopleOf(settler.Player), type, action, MaterialOf(settler, type), facing)
            : null;

    /// <summary>
    /// The animation for a people, else the Romans' (like JSettlers: not every people has every sequence).
    /// Some work animations exist for four directions only (SW, NW, NE, SE): west and east use the nearer one.
    /// </summary>
    private SettlerAnimation? Resolve(Civilization people, string type, string action, string material, Direction facing)
    {
        foreach (var p in people == Civilization.Roman ? [people] : new[] { people, Civilization.Roman })
        foreach (var direction in new[] { facing, facing switch { Direction.West => Direction.SouthWest, Direction.East => Direction.SouthEast, _ => facing } })
            if (_animations.Find(p, type, action, material, direction) is { } animation && Exists(animation))
                return animation;
        return null;
    }

    private static string? TypeOf(Settler settler) => settler.Profession switch
    {
        Profession.Carrier => "BEARER",
        Profession.Digger => "DIGGER",
        Profession.Builder => "BRICKLAYER",
        Profession.Soldier => $"{(settler.SoldierType ?? SoldierType.Swordsman).ToString().ToUpperInvariant()}_L{Math.Clamp(settler.Level, 1, 3)}",
        Profession.Worker => settler.Trade,
        _ => null,
    };

    /// <summary>Carried material; a waterworker without water carries his empty bucket.</summary>
    private static string MaterialOf(Settler settler, string type) =>
        type == "WATERWORKER" && settler.Carrying is null ? "EMPTY_BUCKET" : SettlerAnimations.MaterialName(settler.Carrying);

    private bool Exists(SettlerAnimation animation)
    {
        if (!_exists.TryGetValue(animation, out bool exists))
        {
            exists = _gfx.FileNumbers.Contains(animation.File);
            if (exists)
            {
                var file = _gfx.Open(animation.File);
                exists = animation.Sequence < file.SettlerCount && animation.LastFrame < file.GetSettlerFrameCount(animation.Sequence);
            }
            _exists[animation] = exists;
        }
        return exists;
    }

    /// <summary>The leading carrier sequences of a file: walk sequences with 6 directions of 12 frames.</summary>
    private int[] WalkSequences(int file)
    {
        if (!_walkSequences.TryGetValue(file, out var sequences))
        {
            var carriers = _gfx.Open(file);
            _walkSequences[file] = sequences = Enumerable.Range(0, carriers.SettlerCount)
                .TakeWhile(s => carriers.GetSettlerFrameCount(s) == DirectionExtensions.Count * SettlerSprites.FramesPerDirection)
                .DefaultIfEmpty(0)
                .ToArray();
        }
        return sequences;
    }

    private static float StepProgress(Settler settler, float tickAlpha) =>
        Math.Clamp((settler.TicksIntoStep + tickAlpha) / World.StepTicks, 0, 1);

    private static Vector2 ToWorld(GridPoint p, TerrainMesh terrain) =>
        ScreenProjection.ToWorld(p.X, p.Y, terrain.HeightAt(p.X, p.Y));

    private SettlerSprites Sprites(SettlerAnimation animation) => Sprites(animation.File, animation.Sequence);

    private SettlerSprites Sprites(int file, int sequence)
    {
        if (!_sprites.TryGetValue((file, sequence), out var sprites))
            _sprites[(file, sequence)] = sprites = new SettlerSprites(_device, _gfx.Open(file), sequence);
        return sprites;
    }

    public void Dispose()
    {
        foreach (var sprites in _sprites.Values)
            sprites.Dispose();
    }
}
