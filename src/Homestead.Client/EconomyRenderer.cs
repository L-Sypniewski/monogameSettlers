using Homestead.Assets.Gfx;
using Homestead.Client.Terrain;
using Homestead.Simulation;
using Homestead.Simulation.Buildings;
using Homestead.Simulation.Economy;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Homestead.Client;

/// <summary>Draws buildings and piles of goods.</summary>
public sealed class EconomyRenderer : IDisposable
{
    private const int PileFile = 1;

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

    public void DrawBuilding(SpriteBatch batch, Building building, TerrainMesh terrain, Color playerColor)
    {
        var anchor = ScreenProjection.ToWorld(building.Position.X, building.Position.Y,
            terrain.HeightAt(building.Position.X, building.Position.Y));
        foreach (var image in building.Definition.FinalImages)
            Sprites(image.File, image.Sequence).Draw(batch, image.Image, anchor, 1, playerColor);
    }

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
