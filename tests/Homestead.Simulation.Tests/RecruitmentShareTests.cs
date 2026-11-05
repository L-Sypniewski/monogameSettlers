using Homestead.Simulation.Economy;
using Homestead.Simulation.Map;

namespace Homestead.Simulation.Tests;

/// <summary>The shares of Siedler › Berufe: together they may never exceed 100 % (a carrier floor of
/// 100 % would brick specialist recruitment), single shares can still take the whole.</summary>
public class RecruitmentShareTests
{
    /// <summary>The real balancing, so the shares' defaults are the player-facing 30/25/25 %.</summary>
    private static World World() => new(new GameMap(70), ProductionConfig.Load());

    [Fact]
    public void A_share_is_capped_by_what_the_other_two_leave()
    {
        var world = World();
        world.Recruitment.SetShare(0, Share.Carriers, 0.3);
        world.Recruitment.SetShare(0, Share.Builders, 0.25);
        world.Recruitment.SetShare(0, Share.Diggers, 1.0);
        Assert.Equal(0.45, world.Recruitment.ShareOf(0, Share.Diggers));
    }

    [Fact]
    public void A_single_share_can_still_take_the_whole()
    {
        var world = World();
        foreach (var share in Enum.GetValues<Share>())
        {
            foreach (var other in Enum.GetValues<Share>())
                world.Recruitment.SetShare(0, other, 0);
            world.Recruitment.SetShare(0, share, 1.0);
            Assert.Equal(1.0, world.Recruitment.ShareOf(0, share));
        }
    }

    [Fact]
    public void No_sequence_of_sets_ever_exceeds_the_whole()
    {
        var world = World();
        var random = new Random(7);
        for (int i = 0; i < 500; i++)
        {
            world.Recruitment.SetShare(0, (Share)random.Next(3), random.NextDouble());
            double sum = Enum.GetValues<Share>().Sum(s => world.Recruitment.ShareOf(0, s));
            Assert.True(sum <= 1.0 + 1e-9, $"sum {sum:0.####} after set {i}");
        }
    }

    [Fact]
    public void Untouched_shares_stay_at_their_defaults()
    {
        var world = World();
        world.Recruitment.SetShare(0, Share.Diggers, 0.25);
        Assert.Equal(0.3, world.Recruitment.ShareOf(0, Share.Carriers));
        Assert.Equal(0.25, world.Recruitment.ShareOf(0, Share.Builders));
        Assert.Equal(0.25, world.Recruitment.ShareOf(0, Share.Diggers));
    }
}
