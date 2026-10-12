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
    private const int SoldierFile = 12; // Roman soldiers

    // Roman specialists in file 11 (tables from JSettlers' movables, MIT).
    private const int DiggerWalk = 13;
    private const int DiggerDigFirst = 206; // one sequence per direction, 12 frames
    private const int BuilderWalk = 21;
    private const int BuilderBuild = 22;    // walk layout: 6 directions x 12 frames

    private const int LumberjackChop = 30;      // 12 frames, all directions
    private const int LumberjackChopFrames = 12;
    private const int StonecutterWork = 25;     // walk layout
    private const int ForesterPlant = 36;       // 27 frames west, 27 frames east
    private const int ForesterPlantFrames = 27;
    private const int FarmerSowFirst = 94;      // one sequence each for SW, NW, NE, SE; 12 frames
    private const int FarmerHarvest = 99;       // 4 blocks (SW, NW, NE, SE) of 12 frames
    private const int FisherCastFirst = 101;    // one sequence per direction, 12 frames
    private const int WaterworkerDrawWest = 156; // 14 frames; 157 for the eastern directions

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

    /// <summary>A settler standing at a given screen position (soldiers on a tower).</summary>
    public void DrawStanding(SpriteBatch batch, Settler settler, Vector2 anchor, Direction facing, Color playerColor)
    {
        var sprites = WalkSprites(settler);
        sprites.Draw(batch, FitToBlocks(sprites, SettlerSprites.StandFrame(facing)), anchor, 1, playerColor);
    }

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
                return (Sprites(WorkerFile, BuilderBuild), WalkLayoutAction(settler, work));
            case Activity.Chopping:
                return (Sprites(WorkerFile, LumberjackChop), work % LumberjackChopFrames);
            case Activity.Mining:
                return (Sprites(WorkerFile, StonecutterWork), WalkLayoutAction(settler, work));
            case Activity.Planting:
            {
                // One animation for the western, one for the eastern directions.
                bool east = settler.Facing is Direction.East or Direction.SouthEast or Direction.NorthEast;
                return (Sprites(WorkerFile, ForesterPlant), (east ? ForesterPlantFrames : 0) + work % ForesterPlantFrames);
            }
            case Activity.Sowing:
                return (Sprites(WorkerFile, FarmerSowFirst + QuarterBlock(settler.Facing)), work % SettlerSprites.FramesPerDirection);
            case Activity.Harvesting:
                return (Sprites(WorkerFile, FarmerHarvest),
                    QuarterBlock(settler.Facing) * SettlerSprites.FramesPerDirection + work % SettlerSprites.FramesPerDirection);
            case Activity.Fishing:
                return (Sprites(WorkerFile, FisherCastFirst + SettlerSprites.Block(settler.Facing)), work % SettlerSprites.FramesPerDirection);
            case Activity.DrawingWater:
            {
                bool east = settler.Facing is Direction.East or Direction.SouthEast or Direction.NorthEast;
                var sprites = Sprites(WorkerFile, WaterworkerDrawWest + (east ? 1 : 0));
                return (sprites, work % Math.Max(1, sprites.FrameCount));
            }
        }

        var walk = WalkSprites(settler);
        int frame = settler.State == SettlerState.Stepping
            ? SettlerSprites.WalkFrame(settler.Facing, StepProgress(settler, tickAlpha))
            : SettlerSprites.StandFrame(settler.Facing);
        return (walk, FitToBlocks(walk, frame));
    }

    private SettlerSprites WalkSprites(Settler settler) => settler.Profession switch
        {
            Profession.Carrier => Sprites(CarrierFile, CarrierSequence(settler.Carrying)),
            Profession.Digger => Sprites(WorkerFile, DiggerWalk),
            Profession.Builder => Sprites(WorkerFile, BuilderWalk),
            Profession.Worker => WorkerWalk(settler),
            Profession.Soldier => Sprites(SoldierFile, SoldierSequence(settler)),
            _ => Sprites(CarrierFile, _walkSequences[settler.Id % _walkSequences.Length]),
        };

    /// <summary>Roman soldier walk animations per type and level (file 12; JSettlers' movables, MIT).</summary>
    private static int SoldierSequence(Settler soldier) => (soldier.SoldierType, soldier.Level) switch
    {
        (SoldierType.Swordsman, 1) => 9,
        (SoldierType.Swordsman, 2) => 11,
        (SoldierType.Swordsman, _) => 10,
        (SoldierType.Pikeman, 1) => 15,
        (SoldierType.Pikeman, 2) => 17,
        (SoldierType.Pikeman, _) => 16,
        (SoldierType.Bowman, 1) => 21,
        (SoldierType.Bowman, 2) => 23,
        _ => 22,
    };

    /// <summary>
    /// Walk frames are computed for 12 frames per direction; some sequences (the farmer) have 13.
    /// Stretches the cycle onto the actual block length.
    /// </summary>
    private static int FitToBlocks(SettlerSprites sprites, int frame)
    {
        int perBlock = sprites.FrameCount / DirectionExtensions.Count;
        if (perBlock == SettlerSprites.FramesPerDirection || perBlock < 2)
            return frame;
        int block = frame / SettlerSprites.FramesPerDirection, inner = frame % SettlerSprites.FramesPerDirection;
        return block * perBlock + (inner == 0 ? 0 : 1 + (inner - 1) * (perBlock - 1) / SettlerSprites.WalkCycleFrames);
    }

    /// <summary>Animations with only four directions: SW, NW, NE, SE (west and east fall to the nearer one).</summary>
    private static int QuarterBlock(Direction facing) => facing switch
    {
        Direction.SouthWest or Direction.West => 0,
        Direction.NorthWest => 1,
        Direction.NorthEast => 2,
        _ => 3,
    };

    /// <summary>Work animations laid out like walk sequences: 6 direction blocks of 12 frames.</summary>
    private static int WalkLayoutAction(Settler settler, int work) =>
        SettlerSprites.Block(settler.Facing) * SettlerSprites.FramesPerDirection + 1 + work % SettlerSprites.WalkCycleFrames;

    /// <summary>Specialists with their own walk animations; others look like carriers with the good they hold.</summary>
    private SettlerSprites WorkerWalk(Settler settler) => (settler.Trade, settler.Carrying) switch
    {
        ("LUMBERJACK", Material.Trunk) => Sprites(WorkerFile, 31),
        ("LUMBERJACK", _) => Sprites(WorkerFile, 29),
        ("STONECUTTER", Material.Stone) => Sprites(WorkerFile, 26),
        ("STONECUTTER", _) => Sprites(WorkerFile, 24),
        ("FORESTER", _) => Sprites(WorkerFile, 34),
        ("SAWMILLER", Material.Trunk) => Sprites(WorkerFile, 16),
        ("SAWMILLER", Material.Plank) => Sprites(WorkerFile, 14),
        ("SAWMILLER", _) => Sprites(WorkerFile, 19),
        ("FARMER", Material.Crop) => Sprites(WorkerFile, 87),
        ("FARMER", _) => Sprites(WorkerFile, 90),
        ("FISHERMAN", Material.Fish) => Sprites(WorkerFile, 113),
        ("FISHERMAN", _) => Sprites(WorkerFile, 100),
        ("WATERWORKER", Material.Water) => Sprites(WorkerFile, 155),
        ("WATERWORKER", _) => Sprites(WorkerFile, 154),
        ("MINER", Material.Coal) => Sprites(WorkerFile, 49),
        ("MINER", Material.GoldOre) => Sprites(WorkerFile, 51),
        ("MINER", Material.IronOre) => Sprites(WorkerFile, 53),
        ("MINER", _) => Sprites(WorkerFile, 55),
        ("MILLER", Material.Crop) => Sprites(WorkerFile, 74),
        ("MILLER", Material.Flour) => Sprites(WorkerFile, 75),
        ("MILLER", _) => Sprites(WorkerFile, 69),
        ("SLAUGHTERER", Material.Meat) => Sprites(WorkerFile, 81),
        ("SLAUGHTERER", Material.Pig) => Sprites(WorkerFile, 84),
        ("SLAUGHTERER", _) => Sprites(WorkerFile, 78),
        ("BAKER", Material.Bread) => Sprites(WorkerFile, 127),
        ("BAKER", Material.Flour) => Sprites(WorkerFile, 132),
        ("BAKER", Material.Water) => Sprites(WorkerFile, 133),
        ("BAKER", _) => Sprites(WorkerFile, 134),
        ("SMITH", _) => Sprites(WorkerFile, 40),
        _ => Sprites(CarrierFile, CarrierSequence(settler.Carrying)),
    };

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
