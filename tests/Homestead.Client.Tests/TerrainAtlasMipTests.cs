using Homestead.Client.Terrain;

namespace Homestead.Client.Tests;

public class TerrainAtlasMipTests
{
    private static uint Opaque(byte gray) => 0xFF000000u | gray;

    [Fact]
    public void A_mip_level_box_filters_every_2x2_block_to_its_average()
    {
        uint[] fourByFour =
        [
            Opaque(10), Opaque(20), Opaque(30), Opaque(40),
            Opaque(50), Opaque(60), Opaque(70), Opaque(80),
            Opaque(90), Opaque(100), Opaque(110), Opaque(120),
            Opaque(130), Opaque(140), Opaque(150), Opaque(160),
        ];

        var two = TerrainAtlas.BoxFilter(fourByFour, 4, 4, out int width, out int height);

        Assert.Equal(2, width);
        Assert.Equal(2, height);
        Assert.Equal(new[] { Opaque(35), Opaque(55), Opaque(115), Opaque(135) }, two);

        var one = TerrainAtlas.BoxFilter(two, 2, 2, out width, out height);

        Assert.Equal(1, width);
        Assert.Equal(1, height);
        Assert.Equal(new[] { Opaque(85) }, one);
    }

    [Fact]
    public void A_mip_level_averages_every_color_channel_on_its_own()
    {
        uint red = 0xFF0000FF, green = 0xFF00FF00, blue = 0xFFFF0000, white = 0xFFFFFFFF;

        var one = TerrainAtlas.BoxFilter([red, green, blue, white], 2, 2, out int width, out int height);

        Assert.Equal(1, width);
        Assert.Equal(1, height);
        // Every channel holds two 255s and two 0s: 510 / 4 = 127, alpha stays opaque.
        Assert.Equal(0xFF7F7F7Fu, one[0]);
    }
}
