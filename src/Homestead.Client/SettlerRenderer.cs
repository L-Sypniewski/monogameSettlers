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
/// Standing idle, a settler looks around: a new direction every <see cref="LookAroundMinTicks"/> to <see cref="LookAroundMaxTicks"/>.
/// </summary>
public sealed class SettlerRenderer
{
    public const float WorkFramesPerTick = 0.5f;

    /// <summary>The frame of a bowman's shooting animation in which the arrow leaves the bow.</summary>
    private const int ReleaseFrame = 17;

    private const int LookAroundMinTicks = 2 * World.TicksPerSecond;
    private const int LookAroundMaxTicks = 30 * World.TicksPerSecond;

    /// <summary>Infantry in a window: per people the sequences after the bowmen's (swordsmen L1-3, pikemen L1-3), frame 0 looks left, 1 right.</summary>
    private const int WindowSequencesAfterBowmen = 2;

    private readonly SpriteLibrary _library;
    private readonly GfxDirectory _gfx;
    private readonly SettlerAnimations _animations;
    private readonly Func<int, Civilization> _peopleOf;
    private readonly Dictionary<int, int[]> _walkSequences = new();
    private readonly Dictionary<SettlerAnimation, bool> _exists = new();
    private readonly Dictionary<Civilization, int?> _windowSequences = new();
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<Settler, IdleLook> _idleLooks = new();
    private readonly Random _random = new();

    /// <summary>Where a settler standing idle looks: <see cref="Facing"/> until <see cref="Until"/>; it started out facing <see cref="From"/> at <see cref="At"/>.</summary>
    private sealed class IdleLook
    {
        public GridPoint At;
        public Direction From;
        public Direction Facing;
        public long Until;
    }

    /// <param name="peopleOf">People (civilization) of a player: settlers look like their people's.</param>
    public SettlerRenderer(SpriteLibrary library, SettlerAnimations animations, Func<int, Civilization> peopleOf)
    {
        _library = library;
        _gfx = library.Gfx;
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

    /// <summary>
    /// A soldier in a place of a military building at a given screen position: bowmen stand on top
    /// (shooting towards their target, else looking to <paramref name="facing"/>; without a shadow, as in the
    /// original), infantry look out of a window.
    /// </summary>
    public void DrawOccupant(SpriteBatch batch, Settler settler, Vector2 anchor, Direction facing, Color playerColor, long tick, float tickAlpha,
        Rectangle? clip = null)
    {
        if (settler.SoldierType != SoldierType.Bowman && WindowSequence(settler) is { } window)
        {
            Sprites(PeopleFile(PeopleOf(settler)), window).Draw(batch, facing == Direction.SouthEast ? 1 : 0, anchor, 1, playerColor, clip: clip);
            return;
        }
        if (settler.Activity == Activity.Shooting && Find(settler, "ACTION1", settler.Facing) is { } shooting)
        {
            Sprites(shooting).Draw(batch, ShotFrame(settler, shooting, tick, tickAlpha), anchor, 1, playerColor, shadow: false, clip: clip);
            return;
        }
        var (sprites, frame) = Look(settler, "NO_ACTION", facing, a => a.Start, 0);
        sprites.Draw(batch, frame, anchor, 1, playerColor, shadow: false, clip: clip);
    }

    /// <summary>A settler standing still at a screen position, looking to <paramref name="facing"/> (e.g. on a ship's deck), without a shadow.</summary>
    public void DrawStanding(SpriteBatch batch, Settler settler, Vector2 anchor, Direction facing, Color playerColor)
    {
        var (sprites, frame) = Look(settler, "NO_ACTION", facing, a => a.Start, 0);
        sprites.Draw(batch, frame, anchor, 1, playerColor, shadow: false);
    }

    /// <summary>
    /// The shooting animation fitted to the bowman's shot interval, so that the arrow leaves the bow
    /// (<see cref="ReleaseFrame"/>) in the tick it is shot.
    /// </summary>
    private static int ShotFrame(Settler settler, SettlerAnimation animation, long tick, float tickAlpha)
    {
        int frames = Math.Abs(animation.Duration);
        float sinceShot = Math.Max(0, tick - settler.LastShotTick + tickAlpha);
        return animation.Cycle(ReleaseFrame + (int)(sinceShot * frames / Math.Max(1, settler.ShotTicks)));
    }

    /// <summary>The window sequence of an infantry soldier, if its people has the window pictures.</summary>
    private int? WindowSequence(Settler settler)
    {
        var people = PeopleOf(settler);
        if (!_windowSequences.TryGetValue(people, out int? first))
        {
            first = null;
            if (_animations.Find(people, "BOWMAN_L3", "ACTION1", "NO_MATERIAL", Direction.SouthWest) is { File: var f } bowmen
                && f == PeopleFile(people) && _gfx.FileNumbers.Contains(f))
            {
                var file = _gfx.Open(f);
                int candidate = bowmen.Sequence + WindowSequencesAfterBowmen;
                if (candidate + 5 < file.SettlerCount
                    && Enumerable.Range(candidate, 6).All(s => file.GetSettlerFrameCount(s) == 2))
                    first = candidate;
            }
            _windowSequences[people] = first;
        }
        int type = settler.SoldierType == SoldierType.Pikeman ? 1 : 0;
        return first + type * 3 + Math.Clamp(settler.Level, 1, 3) - 1;
    }

    private static int PeopleFile(Civilization people) => ((int)people + 1) * 10 + 2;

    /// <summary>A standing settler of a type and people fitted into a box, e.g. as a menu icon.</summary>
    public void DrawIcon(SpriteBatch batch, Civilization people, string type, Rectangle box, float maxScale, Color playerColor)
    {
        if (Resolve(people, type, "NO_ACTION", "NO_MATERIAL", Direction.SouthWest) is not { } animation)
            return;
        var sprites = Sprites(animation);
        if (sprites.Bounds(animation.Start) is not { } bounds)
            return;
        var (anchor, scale) = SettlerSprites.Fit(bounds, box, maxScale);
        sprites.Draw(batch, animation.Start, anchor, scale, playerColor);
    }

    /// <param name="disguised">A scout seen by another player: drawn as a carrier of the people of <see cref="Viewer"/>.</param>
    public void Draw(SpriteBatch batch, Settler settler, TerrainMesh terrain, long tick, float tickAlpha, Color playerColor, bool disguised = false)
    {
        _disguised = disguised ? settler : null;
        var (sprites, frame) = Frame(settler, tick, tickAlpha);
        _disguised = null;
        sprites.Draw(batch, frame, Anchor(settler, terrain, tickAlpha), 1, playerColor);
    }

    /// <summary>The player looking at the map: disguised scouts look like its carriers.</summary>
    public int Viewer { get; set; }

    /// <summary>The settler being drawn in disguise, if any.</summary>
    private Settler? _disguised;

    private (SettlerSprites Sprites, int Frame) Frame(Settler settler, long tick, float tickAlpha)
    {
        int work = (int)((settler.ActivityTicks + tickAlpha) * WorkFramesPerTick);
        if (WorkAction(settler.Activity) is { } action && Find(settler, action, settler.Facing) is { } animation)
            return (Sprites(animation), settler.Activity == Activity.Shooting
                ? ShotFrame(settler, animation, tick, tickAlpha)
                : animation.Cycle(work));
        // Winegrowers, rice farmers and beekeepers plant with ACTION1 and a plant in their hands.
        if (settler.Activity == Activity.Sowing && TypeFor(settler) is { } planter
            && Resolve(PeopleOf(settler), planter, "ACTION1", "PLANT", settler.Facing) is { } planting)
            return (Sprites(planting), planting.Cycle(work));
        if (settler.IsStriking && settler.State != SettlerState.Stepping && Strike(settler, work) is { } strike)
            return strike;
        if (settler.State == SettlerState.Stepping)
        {
            float progress = StepProgress(settler, tickAlpha);
            return Look(settler, "WALKING", settler.Facing, a => a.Frame(progress),
                SettlerSprites.WalkFrame(settler.Facing, progress));
        }
        var facing = IdleFacing(settler, tick);
        return Look(settler, "NO_ACTION", facing, a => a.Start, SettlerSprites.StandFrame(facing));
    }

    /// <summary>
    /// Where a standing settler looks: while it has nothing to do (no activity, no way to go) it turns
    /// to another direction now and then; anything else it does turns it back to its own facing.
    /// </summary>
    private Direction IdleFacing(Settler settler, long tick)
    {
        if (settler.Activity != Activity.None || settler.IsStriking || settler.Destination is not null)
        {
            _idleLooks.Remove(settler);
            return settler.Facing;
        }
        if (!_idleLooks.TryGetValue(settler, out var look) || look.At != settler.Position || look.From != settler.Facing)
        {
            look = new IdleLook { At = settler.Position, From = settler.Facing, Facing = settler.Facing, Until = tick + LookAroundTicks() };
            _idleLooks.AddOrUpdate(settler, look);
        }
        else if (tick >= look.Until)
        {
            look.Facing = (Direction)(((int)look.Facing + _random.Next(1, DirectionExtensions.Count)) % DirectionExtensions.Count);
            look.Until = tick + LookAroundTicks();
        }
        return look.Facing;
    }

    private int LookAroundTicks() => _random.Next(LookAroundMinTicks, LookAroundMaxTicks + 1);

    /// <summary>Walking or standing: the settler's own animation, else a carrier's, else a varied carrier look.</summary>
    private (SettlerSprites Sprites, int Frame) Look(Settler settler, string action, Direction facing, Func<SettlerAnimation, int> frame, int plainFrame)
    {
        if (Find(settler, action, facing) is { } own)
            return (Sprites(own), frame(own));
        var people = PeopleOf(settler);
        if (Resolve(people, "BEARER", action, SettlerAnimations.MaterialName(settler.Carrying), facing) is { } carrier
            && settler.Profession != Profession.None)
            return (Sprites(carrier), frame(carrier));
        // Settlers without a profession: one of the leading carrier sequences of the people.
        int file = ((int)people + 1) * 10;
        var looks = WalkSequences(file);
        return (Sprites(file, looks[settler.Id % looks.Length]), plainFrame);
    }

    /// <summary>One round of a striker's loop, in work frames: what he does from which frame on.</summary>
    private static readonly (int From, string Action)[] StrikeLoop =
    [
        (0, "HOMELESS_IDLE"), (30, "HOMELESS2"), (44, "HOMELESS_IDLE"), (50, "HOMELESS3"), (60, "HOMELESS4"), (65, "HOMELESS_IDLE"),
    ];

    private const int StrikeLoopFrames = 75;

    /// <summary>
    /// A striking carrier (JSettlers' homeless bearer, MIT): puts his hands behind his back, then stands
    /// so, now and then shaking his head or kicking a pebble. Parts the people lacks are spent standing.
    /// </summary>
    private (SettlerSprites Sprites, int Frame)? Strike(Settler settler, int work)
    {
        var people = PeopleOf(settler);
        SettlerAnimation? Own(string action) =>
            _animations.Find(people, "BEARER", action, "NO_MATERIAL", settler.Facing) is { } a && Exists(a) ? a : null;
        if (Own("HOMELESS1") is { } start && work < start.Duration)
            return (Sprites(start), start.Cycle(work));
        int step = (work + settler.Id * 17) % StrikeLoopFrames;
        var (from, action) = StrikeLoop.Last(part => part.From <= step);
        if ((action == "HOMELESS_IDLE" ? null : Own(action)) is { } animation)
            return (Sprites(animation), animation.Cycle(step - from));
        return Own("HOMELESS_IDLE") is { } idle ? (Sprites(idle), idle.Start) : null;
    }

    /// <summary>Frames in one round of the settler's work animation, if it has one (sounds keep time with it).</summary>
    public int? WorkRoundFrames(Settler settler) =>
        WorkAction(settler.Activity) is { } action && Find(settler, action, settler.Facing) is { } animation ? Math.Abs(animation.Duration) : null;

    /// <summary>The action of an activity in the animation tables.</summary>
    private static string? WorkAction(Activity activity) => activity switch
    {
        Activity.Sowing or Activity.Signing => "ACTION2",
        Activity.Digging or Activity.Building or Activity.Chopping or Activity.Mining or Activity.Planting
            or Activity.Harvesting or Activity.Fishing or Activity.DrawingWater or Activity.Fighting
            or Activity.Shooting or Activity.Surveying or Activity.Casting or Activity.Healing or Activity.Claiming => "ACTION1",
        _ => null,
    };

    private SettlerAnimation? Find(Settler settler, string action, Direction facing) =>
        TypeFor(settler) is { } type
            ? Resolve(PeopleOf(settler), type, action, MaterialOf(settler, type), facing)
            : null;

    /// <summary>The type the settler is drawn as: a disguised scout as a carrier.</summary>
    private string? TypeFor(Settler settler) => settler == _disguised ? "BEARER" : TypeOf(settler);

    /// <summary>The people a settler looks like: the viewer's in disguise, its own (priests, won-over soldiers), else its player's.</summary>
    public Civilization PeopleOf(Settler settler) =>
        settler == _disguised ? _peopleOf(Viewer) : settler.People ?? _peopleOf(settler.Player);

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

    internal static string? TypeOf(Settler settler) => settler.Profession switch
    {
        Profession.Carrier => "BEARER",
        Profession.Digger => "DIGGER",
        Profession.Builder => "BRICKLAYER",
        Profession.Soldier => $"{(settler.SoldierType ?? SoldierType.Swordsman).ToString().ToUpperInvariant()}_L{Math.Clamp(settler.Level, 1, 3)}",
        Profession.Worker => settler.Trade,
        Profession.Geologist => "GEOLOGIST",
        Profession.Priest => "MAGE",
        Profession.Scout => "THIEF",
        Profession.Pioneer => "PIONEER",
        Profession.Donkey => "DONKEY",
        Profession.WarMachine => "WAR_MACHINE",
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
        Math.Clamp((settler.StepProgress + tickAlpha * settler.StepSpeed) / World.StepUnits, 0, 1);

    private static Vector2 ToWorld(GridPoint p, TerrainMesh terrain) =>
        ScreenProjection.ToWorld(p.X, p.Y, terrain.HeightAt(p.X, p.Y));

    private SettlerSprites Sprites(SettlerAnimation animation) => Sprites(animation.File, animation.Sequence);

    private SettlerSprites Sprites(int file, int sequence) => _library.Sequence(file, sequence);
}
