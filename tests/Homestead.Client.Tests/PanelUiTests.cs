using Homestead.Client;
using Microsoft.Xna.Framework;

namespace Homestead.Client.Tests;

/// <summary><see cref="PanelUi.SpinnerHalf"/>: the split of an up/down spinner at its center.</summary>
public class PanelUiTests
{
    [Fact]
    public void The_halves_of_an_even_spinner_add_up_to_it()
    {
        var spinner = new Rectangle(10, 20, 19, 28);
        Assert.Equal(new Rectangle(10, 20, 19, 14), PanelUi.SpinnerHalf(spinner, upper: true));
        Assert.Equal(new Rectangle(10, 34, 19, 14), PanelUi.SpinnerHalf(spinner, upper: false));
    }

    [Fact]
    public void The_halves_of_an_odd_spinner_split_below_the_center()
    {
        var spinner = new Rectangle(0, 0, 20, 29); // center y = 14
        Assert.Equal(new Rectangle(0, 0, 20, 14), PanelUi.SpinnerHalf(spinner, upper: true));
        Assert.Equal(new Rectangle(0, 14, 20, 14), PanelUi.SpinnerHalf(spinner, upper: false));
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(5, 0)]
    [InlineData(-1, 14)]
    [InlineData(-5, 14)]
    public void A_change_belongs_to_the_half_its_sign_points_at(int delta, int y) =>
        Assert.Equal(new Rectangle(0, y, 20, 14), PanelUi.SpinnerHalf(new Rectangle(0, 0, 20, 29), delta));
}
