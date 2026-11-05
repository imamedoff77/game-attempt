using SimCore;
using Xunit;

public class DailyTests
{
    private const int Day = 20000;

    [Fact]
    public void Reward_CanBeClaimedOncePerDay()
    {
        var d = new DailyProgress();
        var wallet = new CampaignProgress();
        Assert.True(d.CanClaimReward(Day));
        Assert.Equal(20, d.ClaimReward(Day, wallet));
        Assert.Equal(20, wallet.Credits);
        Assert.False(d.CanClaimReward(Day));
        Assert.Equal(0, d.ClaimReward(Day, wallet));
        Assert.Equal(20, wallet.Credits);
    }

    [Fact]
    public void Streak_GrowsOnConsecutiveDays_AndResetsAfterAMissedDay()
    {
        var d = new DailyProgress();
        var wallet = new CampaignProgress();
        int[] expected = { 20, 30, 40, 50, 60, 80, 120, 20 }; // the eighth day starts the week again
        for (int i = 0; i < expected.Length; i++) Assert.Equal(expected[i], d.ClaimReward(Day + i, wallet));
        Assert.Equal(8, d.Streak);

        Assert.Equal(1, d.StreakIfClaimed(Day + 10)); // a day was skipped
        Assert.Equal(20, d.ClaimReward(Day + 10, wallet));
        Assert.Equal(1, d.Streak);
    }

    [Fact]
    public void Tasks_AreCountedClaimedOnce_AndStartOverTheNextDay()
    {
        var d = new DailyProgress();
        var wallet = new CampaignProgress();
        DailyTask play = DailyProgress.TasksFor(Day)[0];
        Assert.Equal(DailyTaskKind.PlayMatches, play.Kind);

        for (int i = 0; i < play.Target - 1; i++) d.Report(Day, DailyTaskKind.PlayMatches);
        Assert.False(d.CanClaimTask(Day, 0));
        Assert.Equal(0, d.ClaimTask(Day, 0, wallet));
        d.Report(Day, DailyTaskKind.PlayMatches);
        Assert.True(d.CanClaimTask(Day, 0));
        Assert.Equal(play.Reward, d.ClaimTask(Day, 0, wallet));
        Assert.Equal(0, d.ClaimTask(Day, 0, wallet));
        Assert.Equal(play.Reward, wallet.Credits);

        Assert.Equal(0, d.Progress(Day + 1, 0));
        Assert.False(d.IsClaimed(Day + 1, 0));
    }

    [Fact]
    public void Progress_StopsAtTheTarget_AndOnlyMatchingEventsCount()
    {
        var d = new DailyProgress();
        d.Report(Day, DailyTaskKind.WinSkirmish, 5);
        DailyTask[] tasks = DailyProgress.TasksFor(Day);
        Assert.Equal(tasks[2].Target, d.Progress(Day, 2));
        Assert.Equal(0, d.Progress(Day, 0));
        Assert.Equal(0, d.Progress(Day, 1));
    }

    [Fact]
    public void HasSomethingToClaim_FollowsRewardAndTasks()
    {
        var d = new DailyProgress();
        var wallet = new CampaignProgress();
        Assert.True(d.HasSomethingToClaim(Day));
        d.ClaimReward(Day, wallet);
        Assert.False(d.HasSomethingToClaim(Day));
        d.Report(Day, DailyTaskKind.WinSkirmish);
        Assert.True(d.HasSomethingToClaim(Day));
        d.ClaimTask(Day, 2, wallet);
        Assert.False(d.HasSomethingToClaim(Day));
    }

    [Fact]
    public void Serialize_RoundTrips_AndBadTextGivesAnEmptyState()
    {
        var d = new DailyProgress();
        var wallet = new CampaignProgress();
        d.ClaimReward(Day, wallet);
        d.Report(Day, DailyTaskKind.WinSkirmish);
        d.ClaimTask(Day, 2, wallet);
        d.Report(Day, DailyTaskKind.PlayMatches);

        DailyProgress copy = DailyProgress.Parse(d.Serialize());
        Assert.Equal(d.Serialize(), copy.Serialize());
        Assert.False(copy.CanClaimReward(Day));
        Assert.True(copy.IsClaimed(Day, 2));
        Assert.Equal(1, copy.Progress(Day, 0));

        Assert.True(DailyProgress.Parse("garbage").CanClaimReward(Day));
        Assert.True(DailyProgress.Parse("").CanClaimReward(Day));
        Assert.True(DailyProgress.Parse("1,2,3").CanClaimReward(Day));
    }
}
