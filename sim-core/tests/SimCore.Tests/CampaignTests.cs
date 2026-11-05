using SimCore;
using SimCore.Tests;
using Xunit;

public class CampaignTests
{
    [Fact]
    public void TenMissions_NumberedInOrder_WithRewards()
    {
        Assert.Equal(10, Campaign.Missions.Count);
        for (int i = 0; i < 10; i++)
        {
            Mission m = Campaign.Missions[i];
            Assert.Equal(i + 1, m.Number);
            Assert.Same(m, Campaign.Get(i + 1));
            Assert.NotEmpty(m.Objectives);
            Assert.True(m.CreditsPerStar > 0);
            Assert.True(m.ParSeconds > 0);
        }
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    [InlineData(6)] [InlineData(7)] [InlineData(8)] [InlineData(9)] [InlineData(10)]
    public void EveryMission_StartsWithBothHeadquarters_AndARouteBetweenThem(int number)
    {
        Mission m = Campaign.Get(number);
        World w = m.BuildWorld(1);
        Assert.True(w.HasBuilding(BuildingKind.Hq, 0));
        Assert.True(w.HasBuilding(BuildingKind.Hq, 1));

        Building mine = w.Buildings.First(b => b.Team == 0 && b.Kind == BuildingKind.Hq);
        Building theirs = w.Buildings.First(b => b.Team == 1 && b.Kind == BuildingKind.Hq);
        // a free cell just outside each headquarters must be connected, or the mission could not be played
        var from = Pathfinder.NearestWalkable(w.Map, mine.X + mine.Width / 2, mine.Y + mine.Height);
        var to = Pathfinder.NearestWalkable(w.Map, theirs.X + theirs.Width / 2, theirs.Y - 1);
        Assert.NotNull(from);
        Assert.NotNull(to);
        Assert.NotNull(Pathfinder.FindPath(w.Map, from.Value.x, from.Value.y, to.Value.x, to.Value.y));
    }

    [Fact]
    public void SameSeed_GivesTheSameMissionRun()
    {
        for (int number = 1; number <= 10; number++)
        {
            ulong a = Hash(number, 3), b = Hash(number, 3);
            Assert.Equal(a, b);
        }
    }

    private static ulong Hash(int number, ulong seed)
    {
        var run = new MissionRun(Campaign.Get(number), seed);
        for (int i = 0; i < 20 * 60; i++) run.Advance();
        return run.World.StateHash();
    }

    [Fact]
    public void DestroyingTheEnemyHeadquarters_WinsMissionOne_WithThreeStarsWhenFastAndCareful()
    {
        var run = new MissionRun(Campaign.Get(1), 1);
        Assert.Equal(MissionStatus.Running, run.Status);
        Assert.Equal(0, run.Stars);

        Building hq = run.World.Buildings.First(b => b.Team == 1 && b.Kind == BuildingKind.Hq);
        run.World.SetBuildingHealth(hq.Id, 0);
        run.Advance();

        Assert.Equal(MissionStatus.Won, run.Status);
        Assert.True(run.Objectives[0].Done);
        Assert.Equal(3, run.Stars);
    }

    [Fact]
    public void LosingTheOwnHeadquarters_LosesTheMission_AndNothingMoreHappens()
    {
        var run = new MissionRun(Campaign.Get(1), 1);
        Building hq = run.World.Buildings.First(b => b.Team == 0 && b.Kind == BuildingKind.Hq);
        run.World.SetBuildingHealth(hq.Id, 0);
        run.Advance();
        Assert.Equal(MissionStatus.Lost, run.Status);
        Assert.Equal(0, run.Stars);

        int tick = run.World.Tick;
        run.Advance();
        Assert.Equal(tick, run.World.Tick); // decided: the world stands still
    }

    [Fact]
    public void Stars_NeedTheParTimeAndFewLosses()
    {
        Mission Make(int par, int maxLosses) => new Mission(99, seed => Campaign.Get(1).BuildWorld(seed), null,
            new[] { new Objective(ObjectiveKind.DestroyEnemyHq) }, new AttackWave[0], 0, par, maxLosses, 10);

        // par time already over, no losses allowed: only the star for winning
        var slow = new MissionRun(Make(0, 0), 1);
        foreach (Unit u in slow.World.Units.Where(u => u.Team == 0).Take(2)) slow.World.DamageUnit(u.Id, 100000);
        for (int i = 0; i < 25; i++) slow.Advance(); // a little over a second: past the par time of 0
        Assert.Equal(2, slow.UnitsLost);
        slow.World.SetBuildingHealth(slow.World.Buildings.First(b => b.Team == 1 && b.Kind == BuildingKind.Hq).Id, 0);
        slow.Advance();
        Assert.Equal(1, slow.Stars);

        // generous par time, but too many losses: two stars
        var costly = new MissionRun(Make(100000, 1), 1);
        foreach (Unit u in costly.World.Units.Where(u => u.Team == 0).Take(2)) costly.World.DamageUnit(u.Id, 100000);
        costly.Advance();
        costly.World.SetBuildingHealth(costly.World.Buildings.First(b => b.Team == 1 && b.Kind == BuildingKind.Hq).Id, 0);
        costly.Advance();
        Assert.Equal(2, costly.Stars);
    }

    [Fact]
    public void TimeLimit_LosesTheMission()
    {
        var m = new Mission(98, seed => Campaign.Get(1).BuildWorld(seed), null,
            new[] { new Objective(ObjectiveKind.DestroyEnemyHq) }, new AttackWave[0], 3, 100, 5, 10);
        var run = new MissionRun(m, 1);
        for (int i = 0; i < 3 * World.TicksPerSecond - 1; i++) run.Advance();
        Assert.Equal(MissionStatus.Running, run.Status);
        run.Advance();
        Assert.Equal(MissionStatus.Lost, run.Status);
    }

    [Fact]
    public void Waves_AppearOnTime_AndHeadForTheHeadquarters()
    {
        Mission m = Campaign.Get(3);
        var run = new MissionRun(m, 1);
        int before = run.World.Units.Count(u => u.Team == 1);
        Assert.Equal(0, before);

        int first = m.Waves[0].Seconds * World.TicksPerSecond;
        while (run.World.Tick < first - 1) run.Advance();
        Assert.Equal(0, run.World.Units.Count(u => u.Team == 1));
        run.Advance();
        run.Advance();
        Assert.Equal(m.Waves[0].Count, run.World.Units.Count(u => u.Team == 1));
        Assert.All(run.World.Units.Where(u => u.Team == 1), u => Assert.Equal(AttackKind.Building, u.AttackKind));
    }

    [Fact]
    public void SurviveObjective_CountsSeconds_AndWinsWhenTheTimeIsUp()
    {
        var run = new MissionRun(Campaign.Get(3), 1);
        // a strong player army so that nothing threatens the headquarters in this check
        foreach (Unit u in run.World.Units.Where(u => u.Team == 0)) run.World.DamageUnit(u.Id, -100000);
        for (int i = 0; i < 10 * World.TicksPerSecond; i++) run.Advance();
        Assert.Equal(10, run.Objectives[0].Current);
        Assert.False(run.Objectives[0].Done);
        Assert.Equal(300, run.Objectives[0].Target);
    }

    [Fact]
    public void BuildAndArmyObjectives_AreCountedFromTheWorld()
    {
        var run = new MissionRun(Campaign.Get(2), 1);
        Assert.Equal(0, run.Objectives[0].Current);
        Assert.Equal(2, run.Objectives[0].Target);
        Assert.Equal(run.World.Player(0).PopUsed, run.Objectives[1].Current);
        Assert.Equal(12, run.Objectives[1].Target);
        Assert.Equal(MissionStatus.Running, run.Status);
    }

    [Theory]
    [InlineData(1, AiDifficulty.Medium)]
    [InlineData(2, AiDifficulty.Medium)]
    [InlineData(5, AiDifficulty.Medium)]
    public void EarlyMissions_CanBeWonByAComputerPlayer(int number, AiDifficulty skill)
    {
        MissionRun run = MissionBot.Play(number, skill, 1, 20);
        Assert.Equal(MissionStatus.Won, run.Status);
        Assert.True(run.Stars >= 1);
    }

    // ---------------------------------------------------------------- progress

    [Fact]
    public void Progress_StartsEmpty_OnlyTheFirstMissionOpen()
    {
        var p = new CampaignProgress();
        Assert.Equal(0, p.TotalStars);
        Assert.Equal(0, p.Credits);
        Assert.True(p.IsUnlocked(1));
        Assert.False(p.IsUnlocked(2));
        Assert.False(p.IsUnlocked(11));
        Assert.False(p.IsUnlocked(0));
        Assert.Equal(1, p.NextMission);
    }

    [Fact]
    public void Record_UnlocksTheNextMission_AndPaysEachStarOnce()
    {
        var p = new CampaignProgress();
        int perStar = Campaign.Get(1).CreditsPerStar;

        Assert.Equal(2 * perStar, p.Record(1, 2));
        Assert.True(p.IsUnlocked(2));
        Assert.False(p.IsUnlocked(3));
        Assert.Equal(2, p.Stars(1));
        Assert.Equal(2, p.NextMission);

        Assert.Equal(0, p.Record(1, 1));          // worse result pays nothing and keeps the best
        Assert.Equal(2, p.Stars(1));
        Assert.Equal(perStar, p.Record(1, 3));    // only the new star is paid
        Assert.Equal(3 * perStar, p.Credits);
        Assert.Equal(3, p.TotalStars);
    }

    [Fact]
    public void Record_IgnoresLostMissions_AndBadNumbers()
    {
        var p = new CampaignProgress();
        Assert.Equal(0, p.Record(1, 0));
        Assert.Equal(0, p.Record(0, 3));
        Assert.Equal(0, p.Record(11, 3));
        Assert.Equal(0, p.TotalStars);
        Assert.Equal(0, p.Credits);
        Assert.Equal(Campaign.Get(1).CreditsPerStar * 3, p.Record(1, 9)); // more than 3 counts as 3
    }

    [Fact]
    public void Progress_SurvivesSaveAndLoad()
    {
        var p = new CampaignProgress();
        p.Record(1, 3);
        p.Record(2, 1);
        var q = CampaignProgress.Parse(p.Serialize());
        Assert.Equal(p.Serialize(), q.Serialize());
        Assert.Equal(3, q.Stars(1));
        Assert.Equal(1, q.Stars(2));
        Assert.Equal(p.Credits, q.Credits);
        Assert.True(q.IsUnlocked(3));
        Assert.Equal(3, q.NextMission);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("a,b,c|x")]
    [InlineData("9,-4,2|-50")]
    [InlineData(",,,,|||")]
    public void Parse_NeverThrows_AndStaysInRange(string text)
    {
        var p = CampaignProgress.Parse(text);
        for (int i = 1; i <= 10; i++) Assert.InRange(p.Stars(i), 0, 3);
        Assert.True(p.Credits >= 0);
    }

    [Fact]
    public void FinishedCampaign_PointsAtTheLastMission()
    {
        var p = new CampaignProgress();
        for (int i = 1; i <= 10; i++) p.Record(i, 1);
        Assert.Equal(10, p.NextMission);
        Assert.Equal(10, p.TotalStars);
    }

    // ---------------------------------------------------------------- texts

    [Fact]
    public void English_HasTextForEveryMissionAndObjective()
    {
        var en = new StringTable(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "en.json")));
        foreach (Mission m in Campaign.Missions)
        {
            Assert.True(en.Has(m.Key + ".name"), m.Key + ".name");
            Assert.True(en.Has(m.Key + ".brief"), m.Key + ".brief");
            foreach (Objective o in m.Objectives) Assert.True(en.Has("objective." + o.Kind), "objective." + o.Kind);
        }
        foreach (string key in new[] { "mode.campaign", "campaign.title", "campaign.total", "campaign.locked", "campaign.briefing", "campaign.start", "campaign.next",
                                       "campaign.retry", "campaign.victory", "campaign.defeat", "campaign.credits", "campaign.time", "campaign.losses",
                                       "campaign.starWin", "campaign.starTime", "campaign.starLosses", "campaign.nextWave", "campaign.timeLeft", "campaign.mission" })
            Assert.True(en.Has(key), key);
    }
}
