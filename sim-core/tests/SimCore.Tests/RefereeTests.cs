using SimCore;
using Xunit;

public class RefereeTests
{
    [Fact]
    public void FreshMatch_IsOngoing()
    {
        Assert.Equal(MatchReferee.Ongoing, MatchReferee.Winner(OnlineScenario.Create()));
    }

    [Fact]
    public void ADestroyedHeadquarters_LosesTheMatch()
    {
        World w = OnlineScenario.Create();
        foreach (Building b in w.Buildings) if (b.Kind == BuildingKind.Hq && b.Team == 1) w.SetBuildingHealth(b.Id, 0);
        Assert.Equal(0, MatchReferee.Winner(w));
    }

    [Fact]
    public void BothHeadquartersGone_IsADraw()
    {
        World w = OnlineScenario.Create();
        foreach (Building b in w.Buildings) if (b.Kind == BuildingKind.Hq) w.SetBuildingHealth(b.Id, 0);
        Assert.Equal(MatchReferee.Draw, MatchReferee.Winner(w));
    }

    [Fact]
    public void Surrender_LosesTheMatch_AndComesBeforeAnythingElse()
    {
        World w = OnlineScenario.Create();
        w.Enqueue(new SurrenderCommand(), 1);
        w.Step();
        Assert.Equal(0, MatchReferee.Winner(w));
        foreach (Building b in w.Buildings) if (b.Kind == BuildingKind.Hq && b.Team == 0) w.SetBuildingHealth(b.Id, 0);
        Assert.Equal(0, MatchReferee.Winner(w)); // the surrender counts first
    }
}
