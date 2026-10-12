using Homestead.Assets.Gfx;
using Homestead.Client.Terrain;
using Homestead.Simulation;
using Homestead.Simulation.Map;
using Homestead.Simulation.Buildings;
using Homestead.Simulation.Economy;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Homestead.Client;

/// <summary>Draws buildings and piles of goods.</summary>
public sealed class EconomyRenderer : IDisposable
{
    private const int PileFile = 1;

    // Flags and work area marks (JSettlers' MapObjectDrawer, MIT): waving flag in the player color,
    // on the roof of an occupied workplace or at the door of buildings without a worker.
    private const int FlagFile = 13;
    private const int DoorFlagSequence = 63;
    private const int RoofFlagSequence = 64;
    private const float FlagFramesPerSecond = 10;
    private const int BorderPostSequence = 65; // file 13, in the player color
    private const int WorkAreaFile = 1;
    private const int WorkAreaSequence = 91; // 4 posts, green (inner ring) to red (outer ring)
    private const int SignFile = 1;            // geologist signs (JSettlers' MapObjectDrawer, MIT)
    private const int MaxResourceAmount = 15;  // amount per field in original maps

    private readonly GraphicsDevice _device;
    private readonly GfxDirectory _gfx;
    private readonly Dictionary<(int File, int Sequence), SettlerSprites> _sprites = new();

    public EconomyRenderer(GraphicsDevice device, GfxDirectory gfx)
    {
        _device = device;
        _gfx = gfx;
    }

    /// <summary>Grid row used for drawing order: the lowest row the building covers.</summary>
    public static int DepthRow(Building building) => building.Position.Y + building.Definition.BottomRow;

    /// <summary>
    /// Finished buildings show their final layers. Construction sites show the construction image,
    /// growing from the bottom with progress; while levelling there is nothing to see yet.
    /// </summary>
    public void DrawBuilding(SpriteBatch batch, Building building, TerrainMesh terrain, Color playerColor)
    {
        var anchor = Anchor(building.Position, terrain);
        switch (building.State)
        {
            case BuildingState.Finished:
                foreach (var image in building.Definition.FinalImages)
                    Sprites(image.File, image.Sequence).Draw(batch, image.Image, anchor, 1, playerColor);
                break;
            case BuildingState.Constructing when building.Definition.ConstructionImage is { } image:
                // A sliver at the start, so a fresh site is visible at all.
                float visible = 0.05f + 0.95f * building.ConstructionProgress;
                Sprites(image.File, image.Sequence).Draw(batch, image.Image, anchor, 1, playerColor, visible);
                break;
        }
    }

    /// <summary>The building's flag: on the roof once its worker has arrived; at the door if it has no worker.</summary>
    public void DrawFlag(SpriteBatch batch, Building building, bool hasWorker, TerrainMesh terrain, Color playerColor, TimeSpan time)
    {
        if (building.State != BuildingState.Finished || building.Definition.Flag is not { } offset)
            return;
        bool workplace = building.Definition.Worker is not null;
        if (workplace && !hasWorker)
            return;
        var sprites = Sprites(FlagFile, workplace ? RoofFlagSequence : DoorFlagSequence);
        if (sprites.FrameCount == 0)
            return;
        int frame = (int)(time.TotalSeconds * FlagFramesPerSecond + building.Id) % sprites.FrameCount;
        sprites.Draw(batch, frame, Anchor(building.Position + offset, terrain), 1, playerColor);
    }

    /// <summary>A border post of the player's land.</summary>
    public void DrawBorderPost(SpriteBatch batch, GridPoint p, int player, TerrainMesh terrain)
    {
        var sprites = Sprites(FlagFile, BorderPostSequence);
        if (sprites.FrameCount > 0)
            sprites.Draw(batch, 0, Anchor(p, terrain), 1, PlayerColors.Of(player));
    }

    /// <summary>
    /// Soldiers in the places of a military building, standing on it at the places' pixel offsets
    /// (y up, from the building anchor), facing south-east or south-west.
    /// </summary>
    public void DrawOccupants(SpriteBatch batch, Building building, IReadOnlyList<Settler?> occupants, SettlerRenderer settlers,
        TerrainMesh terrain)
    {
        var anchor = Anchor(building.Position, terrain);
        for (int i = 0; i < occupants.Count; i++)
        {
            if (occupants[i] is not { } soldier)
                continue;
            var place = building.Definition.Occupiers[i];
            settlers.DrawStanding(batch, soldier, anchor + new Vector2(place.Pixel.X, -place.Pixel.Y),
                place.LooksRight ? Direction.SouthEast : Direction.SouthWest, PlayerColors.Of(soldier.Player));
        }
    }

    /// <summary>One post of a work area marking; <paramref name="ring"/> 0 (inner) to 3 (outer).</summary>
    public void DrawWorkAreaMark(SpriteBatch batch, GridPoint p, int ring, TerrainMesh terrain)
    {
        var sprites = Sprites(WorkAreaFile, WorkAreaSequence);
        if (sprites.FrameCount > 0)
            sprites.Draw(batch, Math.Min(ring, sprites.FrameCount - 1), Anchor(p, terrain), 1, Color.White);
    }

    /// <summary>
    /// A geologist's sign: one sequence per resource (94 coal, 95 gems, 96 gold, 97 iron, 98 sulfur,
    /// 99 nothing), its frames from little to much.
    /// </summary>
    public void DrawResourceSign(SpriteBatch batch, GridPoint p, ResourceSign sign, TerrainMesh terrain)
    {
        int sequence = sign.Resource switch
        {
            ResourceType.Coal => 94,
            ResourceType.Gems => 95,
            ResourceType.GoldOre => 96,
            ResourceType.IronOre => 97,
            ResourceType.Sulfur => 98,
            _ => 99,
        };
        var sprites = Sprites(SignFile, sequence);
        if (sprites.FrameCount == 0)
            return;
        float progress = Math.Clamp(sign.Amount / (float)MaxResourceAmount, 0, 1);
        sprites.Draw(batch, Math.Min((int)(progress * sprites.FrameCount), sprites.FrameCount - 1), Anchor(p, terrain), 1, Color.White);
    }

    /// <summary>Semi-transparent preview of a building at a position, green if it fits, red if not.</summary>
    public void DrawPreview(SpriteBatch batch, BuildingDefinition definition, GridPoint position, TerrainMesh terrain, bool fits)
    {
        var anchor = Anchor(position, terrain);
        var tint = (fits ? new Color(160, 255, 160) : new Color(255, 120, 120)) * 0.6f;
        foreach (var image in definition.FinalImages)
            Sprites(image.File, image.Sequence).Draw(batch, image.Image, anchor, 1, Color.White, tint: tint);
    }

    private static Vector2 Anchor(GridPoint p, TerrainMesh terrain) =>
        ScreenProjection.ToWorld(p.X, p.Y, terrain.HeightAt(p.X, p.Y));

    public void DrawPile(SpriteBatch batch, Pile pile, TerrainMesh terrain)
    {
        if (pile.Count == 0 || pile.Material is not { } material || StackSequence(material) is not { } sequence)
            return;
        var sprites = Sprites(PileFile, sequence);
        int frame = Math.Min(pile.Count, sprites.FrameCount) - 1;
        var anchor = ScreenProjection.ToWorld(pile.Position.X, pile.Position.Y, terrain.HeightAt(pile.Position.X, pile.Position.Y));
        sprites.Draw(batch, frame, anchor, 1, Color.White);
    }

    /// <summary>Pile graphics in GFX file 01 (one image per pile size). Table from JSettlers' EMaterialType (MIT).</summary>
    private static int? StackSequence(Material material) => material switch
    {
        Material.Axe => 46,
        Material.Bow => 63,
        Material.Blade => 55,
        Material.Bread => 49,
        Material.Coal => 34,
        Material.Crop => 50,
        Material.Fish => 47,
        Material.FishingRod => 66,
        Material.Flour => 48,
        Material.Gold => 37,
        Material.GoldOre => 36,
        Material.Hammer => 51,
        Material.Iron => 42,
        Material.IronOre => 39,
        Material.Meat => 52,
        Material.Pick => 53,
        Material.Pig => 73,
        Material.Plank => 33,
        Material.Saw => 54,
        Material.Scythe => 56,
        Material.Spear => 60,
        Material.Stone => 43,
        Material.Sword => 59,
        Material.Trunk => 41,
        Material.Water => 77,
        Material.Wine => 69,
        Material.Mead => 90,
        Material.Honey => 89,
        Material.Gems => 79,
        Material.Sulfur => 80,
        Material.Rice => 78,
        Material.Keg => 70,
        Material.Liquor => 71,
        Material.GunPowder => 82,
        _ => null,
    };

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
