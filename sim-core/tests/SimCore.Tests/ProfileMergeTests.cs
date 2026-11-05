using SimCore;
using Xunit;

public class ProfileMergeTests
{
    private const int Day = 20000;

    private static ProfileData Make(int[] stars, int credits, int rifle, int tank, int claimDay, bool tutorial)
    {
        CampaignProgress campaign = CampaignProgress.Parse(string.Join(",", stars) + "|" + credits); // exactly this many credits, whatever the stars paid
        var armory = new Armory();
        var rich = new CampaignProgress();
        for (int i = 1; i <= 10; i++) rich.Record(i, 3);
        for (int i = 0; i < rifle; i++) armory.Buy(UnitKind.Rifleman, rich);
        for (int i = 0; i < tank; i++) armory.Buy(UnitKind.Tank, rich);
        var daily = new DailyProgress();
        if (claimDay > 0) daily.ClaimReward(claimDay, new CampaignProgress());
        return new ProfileData { Campaign = campaign.Serialize(), Armory = armory.Serialize(), Daily = daily.Serialize(), Tutorial = tutorial ? "1" : "" };
    }

    [Fact]
    public void Merge_KeepsTheBestOfBothSides()
    {
        ProfileData phone = Make(new[] { 3, 2, 0 }, 100, 2, 0, Day, false);
        ProfileData server = Make(new[] { 1, 3, 1 }, 40, 1, 3, Day + 1, true);
        ProfileData merged = ProfileMerge.Merge(phone, server);

        CampaignProgress c = CampaignProgress.Parse(merged.Campaign);
        Assert.Equal(3, c.Stars(1));
        Assert.Equal(3, c.Stars(2));
        Assert.Equal(1, c.Stars(3));
        Assert.Equal(100, c.Credits);
        Armory a = Armory.Parse(merged.Armory);
        Assert.Equal(2, a.Level(UnitKind.Rifleman));
        Assert.Equal(3, a.Level(UnitKind.Tank));
        Assert.False(DailyProgress.Parse(merged.Daily).CanClaimReward(Day + 1)); // the newer claim (from the server) is kept
        Assert.Equal("1", merged.Tutorial);
    }

    [Fact]
    public void Merge_IsTheSameInBothDirections_AndRepeatable()
    {
        ProfileData a = Make(new[] { 3, 0, 2 }, 70, 1, 2, Day, true);
        ProfileData b = Make(new[] { 1, 2, 0 }, 90, 3, 0, Day, false);
        Assert.True(ProfileMerge.Merge(a, b).SameAs(ProfileMerge.Merge(b, a)));
        ProfileData once = ProfileMerge.Merge(a, b);
        Assert.True(ProfileMerge.Merge(once, b).SameAs(once));
        Assert.True(ProfileMerge.Merge(once, a).SameAs(once));
    }

    [Fact]
    public void Merge_WithAnEmptySide_GivesTheOtherSide()
    {
        ProfileData full = Make(new[] { 2, 2 }, 55, 1, 1, Day, true);
        ProfileData empty = new ProfileData();
        Assert.True(ProfileMerge.Merge(empty, full).SameAs(ProfileMerge.Merge(full, full)));
        Assert.True(ProfileMerge.Merge(full, empty).SameAs(ProfileMerge.Merge(full, full)));
        Assert.Equal("", ProfileMerge.Merge(empty, empty).Tutorial);
        Assert.Equal(0, CampaignProgress.Parse(ProfileMerge.Merge(empty, empty).Campaign).TotalStars);
    }

    [Fact]
    public void Daily_SameDay_KeepsTheBetterTaskProgress_AndClaimedFlags()
    {
        var a = new DailyProgress();
        var b = new DailyProgress();
        var wallet = new CampaignProgress();
        a.Report(Day, DailyTaskKind.PlayMatches, 1);
        b.Report(Day, DailyTaskKind.PlayMatches, 3);
        b.Report(Day, DailyTaskKind.WinSkirmish);
        b.ClaimTask(Day, 2, wallet);
        a.MergeFrom(b);
        Assert.Equal(3, a.Progress(Day, 0));
        Assert.True(a.IsClaimed(Day, 2));
    }

    [Fact]
    public void Daily_AnOlderCopy_DoesNotOverwriteANewerOne()
    {
        var newer = new DailyProgress();
        newer.ClaimReward(Day + 5, new CampaignProgress());
        newer.Report(Day + 5, DailyTaskKind.WinSkirmish);
        var older = new DailyProgress();
        older.ClaimReward(Day, new CampaignProgress());
        older.MergeFrom(newer);
        Assert.False(older.CanClaimReward(Day + 5));
        Assert.Equal(1, older.Progress(Day + 5, 2));

        newer.MergeFrom(DailyProgress.Parse(""));
        Assert.False(newer.CanClaimReward(Day + 5));
    }

    [Fact]
    public void Stats_Merge_TakesTheLargerOfEachNumber_AndRoundTrips()
    {
        var a = new PlayerStats();
        a.RecordMatch(true, true, true, 10, 4);
        a.RecordMatch(true, false, false, 3, 1);
        var b = new PlayerStats();
        b.RecordMatch(true, true, false, 20, 2);
        a.MergeFrom(b);
        Assert.Equal(2, a.MatchesPlayed);
        Assert.Equal(1, a.MatchesWon);
        Assert.Equal(1, a.MissionsWon);
        Assert.Equal(20, a.UnitsBuilt);
        Assert.Equal(5, a.EnemiesDestroyed);
        Assert.Equal(50, a.WinPercent);
        Assert.Equal(a.Serialize(), PlayerStats.Parse(a.Serialize()).Serialize());
        Assert.Equal("0,0,0,0,0", PlayerStats.Parse("garbage").Serialize());

        var profileA = new ProfileData { Stats = a.Serialize() };
        var profileB = new ProfileData { Stats = b.Serialize() };
        Assert.True(ProfileMerge.Merge(profileA, profileB).SameAs(ProfileMerge.Merge(profileB, profileA)));
    }
}
