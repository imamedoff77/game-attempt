using SimCore;
using Xunit;

public class RatingTests
{
    [Fact]
    public void EqualPlayers_WinnerGainsHalfK_LoserLosesHalfK()
    {
        Rating.Apply(1000, 0, 1000, 0, out int w, out int l);
        Assert.Equal(1016, w);
        Assert.Equal(984, l);
    }

    [Fact]
    public void BeatingAStrongerPlayer_PaysMore_ThanBeatingAWeakerOne()
    {
        Rating.Apply(1000, 50, 1400, 50, out int upset, out _);
        Rating.Apply(1000, 50, 600, 50, out int expected, out _);
        Assert.True(upset - 1000 > expected - 1000);
        Assert.True(expected > 1000); // a win is always worth at least a point
    }

    [Fact]
    public void NewPlayersMoveFaster_AndTheLoserNeverFallsBelowTheFloor()
    {
        Rating.Apply(1000, 0, 1000, 0, out int newWin, out _);
        Rating.Apply(1000, Rating.SettledGames, 1000, Rating.SettledGames, out int settledWin, out _);
        Assert.True(newWin > settledWin);

        Rating.Apply(2000, 100, Rating.Floor, 100, out _, out int loserNew);
        Assert.Equal(Rating.Floor, loserNew);
    }

    [Fact]
    public void ExpectedScore_IsHalfForEqualPlayers_AndSumsToOne()
    {
        Assert.Equal(0.5, Rating.ExpectedScore(1200, 1200), 6);
        Assert.Equal(1.0, Rating.ExpectedScore(1500, 1100) + Rating.ExpectedScore(1100, 1500), 6);
    }
}
