using Homestead.Assets;
using Homestead.Assets.Gfx;
using Xunit.Abstractions;

namespace Homestead.Simulation.Tests;

public class GfxShadowTests(ITestOutputHelper output)
{
    /// <summary>Pairings checked by eye: the shadow fits snugly around the body without covering it.</summary>
    [Theory]
    [InlineData(1, 27, 26)]  // decoration (bushes, plants, flowers …), not the wrecks' shadow
    [InlineData(1, 28, 27)]  // wrecks
    [InlineData(1, 29, 28)]  // stones
    [InlineData(1, 26, -1)]  // water sparkles
    [InlineData(36, 3, 0)]   // Roman trade ship, not the Egyptian ferry's shadow
    [InlineData(36, 7, 1)]   // Roman ferry
    [InlineData(36, 11, 2)]  // Egyptian trade ship
    [InlineData(36, 6, -1)]  // the ferry's front part, drawn over its passengers
    [InlineData(36, 29, -1)] // a sail
    [InlineData(43, 33, -1)] // Amazon market place: shadow in its image, not the hospital's
    [InlineData(43, 34, 33)] // Amazon hospital
    [InlineData(43, 37, 36)] // Amazon barrack
    [InlineData(43, 38, 37)] // Amazon donkey farm
    [InlineData(43, 47, 38)] // Amazon shipyard
    [InlineData(43, 48, 39)] // Amazon harbor
    [InlineData(23, 45, 35)] // Egyptian shipyard
    [InlineData(23, 46, 36)] // Egyptian harbor
    [InlineData(33, 47, 37)] // Asian shipyard
    [InlineData(33, 48, 38)] // Asian harbor
    [InlineData(13, 16, 19)] // Roman castle
    [InlineData(13, 45, 39)] // Roman harbor
    [InlineData(12, 28, -1)] // Roman swordsman in a tower window
    [InlineData(22, 33, -1)] // Egyptian swordsman in a tower window
    [InlineData(42, 26, -1)] // Amazon swordsman in a tower window
    public void Images_get_their_own_shadow(int file, int sequence, int shadow)
    {
        if (GameInstallation.TryLocate(out _) is not { } installation)
        {
            output.WriteLine("No installation found, skipped.");
            return;
        }
        Assert.Equal(shadow, new GfxDirectory(installation.GfxDirectory).Open(file).ShadowSequenceFor(sequence));
    }
}
