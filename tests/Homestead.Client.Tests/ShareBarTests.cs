using Homestead.Client;
using Microsoft.Xna.Framework;

namespace Homestead.Client.Tests;

/// <summary><see cref="ShareBar.ValueAt"/>: the share under the cursor, quantized as the bars show it.</summary>
public class ShareBarTests
{
    // Inner(bar) = (12, 102, 96, 14): the frame is 2 pixels wide on each side.
    private readonly Rectangle _bar = new(10, 100, 100, 18);

    [Fact]
    public void Clicks_between_the_ends_round_to_steps_of_five_percent()
    {
        Assert.Equal(0.25, ShareBar.ValueAt(_bar, 36));   // a quarter of the inner width
        Assert.Equal(0.5, ShareBar.ValueAt(_bar, 60));    // the middle
        Assert.Equal(0.75, ShareBar.ValueAt(_bar, 84));   // three quarters
        Assert.Equal(0.1, ShareBar.ValueAt(_bar, 24));    // a tenth
    }

    [Fact]
    public void Every_click_between_the_ends_lands_on_a_step_of_five_percent()
    {
        for (int x = 19; x <= 101; x++)
            Assert.Equal(0, ShareBar.ValueAt(_bar, x) * 20 % 1, precision: 9);
    }

    [Fact]
    public void Near_either_end_it_snaps_to_zero_or_one()
    {
        Assert.Equal(0, ShareBar.ValueAt(_bar, 17));      // 5 pixels inside the left snap zone
        Assert.Equal(0, ShareBar.ValueAt(_bar, 18));      // the last x of the zone
        Assert.Equal(1, ShareBar.ValueAt(_bar, 102));     // the first x of the right zone
        Assert.Equal(1, ShareBar.ValueAt(_bar, 103));
    }

    [Fact]
    public void Beyond_the_bar_it_snaps_to_the_nearest_end()
    {
        Assert.Equal(0, ShareBar.ValueAt(_bar, -50));
        Assert.Equal(0, ShareBar.ValueAt(_bar, 0));
        Assert.Equal(1, ShareBar.ValueAt(_bar, 150));
    }
}
