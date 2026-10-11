using Homestead.Assets.Gfx;
using Homestead.Client.Terrain;
using Homestead.Simulation;
using Homestead.Simulation.Economy;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Homestead.Client;

/// <summary>
/// Draws simulation settlers: interpolates between ticks, follows terrain height and picks the
/// walk or stand frame. Carriers show the good they carry; other settlers get a varied look.
/// </summary>
public sealed class SettlerRenderer : IDisposable
{
    private const int CarrierFile = 10; // Roman settlers
    private const int WorkerFile = 11;  // Roman specialists

    // Roman specialists in file 11 (tables from JSettlers' movables, MIT).
    private const int DiggerWalk = 13;
    private const int DiggerDigFirst = 206; // one sequence per direction, 12 frames
    private const int BuilderWalk = 21;
    private const int BuilderBuild = 22;    // walk layout: 6 directions x 12 frames

    private const float WorkFramesPerTick = 0.5f;

    private readonly GraphicsDevice _device;
    private readonly GfxDirectory _gfx;
    private readonly int[] _walkSequences;
    private readonly Dictionary<(int File, int Sequence), SettlerSprites> _sprites = new();

    public SettlerRenderer(GraphicsDevice device, GfxDirectory gfx)
    {
        _device = device;
        _gfx = gfx;
        // Varied looks for settlers without a profession: the leading carrier sequences.
        var carriers = gfx.Open(CarrierFile);
        _walkSequences = Enumerable.Range(0, carriers.SettlerCount)
            .TakeWhile(s => carriers.GetSettlerFrameCount(s) == DirectionExtensions.Count * SettlerSprites.FramesPerDirection)
            .ToArray();
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

    public void Draw(SpriteBatch batch, Settler settler, TerrainMesh terrain, float tickAlpha, Color playerColor)
    {
        var (sprites, frame) = Frame(settler, tickAlpha);
        sprites.Draw(batch, frame, Anchor(settler, terrain, tickAlpha), 1, playerColor);
    }

    private (SettlerSprites Sprites, int Frame) Frame(Settler settler, float tickAlpha)
    {
        int work = (int)((settler.ActivityTicks + tickAlpha) * WorkFramesPerTick);
        switch (settler.Activity)
        {
            case Activity.Digging:
            {
                var sprites = Sprites(WorkerFile, DiggerDigFirst + SettlerSprites.Block(settler.Facing));
                return (sprites, work % Math.Max(1, sprites.FrameCount));
            }
            case Activity.Building:
                return (Sprites(WorkerFile, BuilderBuild),
                    SettlerSprites.Block(settler.Facing) * SettlerSprites.FramesPerDirection + 1 + work % SettlerSprites.WalkCycleFrames);
        }

        var walk = settler.Profession switch
        {
            Profession.Carrier => Sprites(CarrierFile, CarrierSequence(settler.Carrying)),
            Profession.Digger => Sprites(WorkerFile, DiggerWalk),
            Profession.Builder => Sprites(WorkerFile, BuilderWalk),
            _ => Sprites(CarrierFile, _walkSequences[settler.Id % _walkSequences.Length]),
        };
        int frame = settler.State == SettlerState.Stepping
            ? SettlerSprites.WalkFrame(settler.Facing, StepProgress(settler, tickAlpha))
            : SettlerSprites.StandFrame(settler.Facing);
        return (walk, frame);
    }

    private static float StepProgress(Settler settler, float tickAlpha) =>
        Math.Clamp((settler.TicksIntoStep + tickAlpha) / World.StepTicks, 0, 1);

    private static Vector2 ToWorld(GridPoint p, TerrainMesh terrain) =>
        ScreenProjection.ToWorld(p.X, p.Y, terrain.HeightAt(p.X, p.Y));

    private SettlerSprites Sprites(int file, int sequence)
    {
        if (!_sprites.TryGetValue((file, sequence), out var sprites))
            _sprites[(file, sequence)] = sprites = new SettlerSprites(_device, _gfx.Open(file), sequence);
        return sprites;
    }

    /// <summary>Roman carrier animation per carried good (file 10). Table from JSettlers' movables (MIT).</summary>
    private static int CarrierSequence(Material? material) => material switch
    {
        null => 0,
        Material.Plank => 1,
        Material.Stone => 2,
        Material.Pig => 3,
        Material.Trunk => 4,
        Material.Bread => 5,
        Material.Coal => 6,
        Material.GoldOre => 7,
        Material.IronOre => 8,
        Material.Fish => 9,
        Material.Meat => 10,
        Material.Crop => 11,
        Material.Flour => 12,
        Material.Iron => 13,
        Material.Gold => 14,
        Material.Water => 15,
        Material.Hammer => 16,
        Material.Pick => 17,
        Material.FishingRod => 18,
        Material.Scythe => 19,
        Material.Saw => 20,
        Material.Blade => 21,
        Material.Bow => 22,
        Material.Spear => 23,
        Material.Sword => 24,
        Material.Axe => 35,
        Material.Wine => 54,
        Material.Keg or Material.Liquor => 55,
        Material.GunPowder => 56,
        Material.Gems => 57,
        Material.Rice => 58,
        Material.Sulfur => 59,
        _ => 0,
    };

    public void Dispose()
    {
        foreach (var sprites in _sprites.Values)
            sprites.Dispose();
    }
}
