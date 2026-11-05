using SimCore;
using Xunit;

public class TrustedClockTests
{
    private static readonly DateTime Noon = new DateTime(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void WithoutServer_FollowsThePhoneClock()
    {
        var clock = new TrustedClock();
        Assert.Equal(Noon, clock.UtcNow(Noon, 0));
        Assert.Equal(Noon.AddHours(1), clock.UtcNow(Noon.AddHours(1), 3_600_000));
    }

    [Fact]
    public void AfterServerAnswer_PhoneClockChangesDoNotMoveTime()
    {
        var clock = new TrustedClock();
        clock.Observe(Noon, Noon.AddDays(3), 1000); // the phone is 3 days fast
        Assert.Equal(Noon.AddSeconds(10), clock.UtcNow(Noon.AddDays(30), 11_000)); // the phone jumps again: ignored, only the timer counts
        Assert.Equal(TimeSpan.FromDays(-3), clock.Offset);
    }

    [Fact]
    public void TimeNeverRunsBackwards()
    {
        var clock = new TrustedClock();
        DateTime a = clock.UtcNow(Noon, 0);
        DateTime b = clock.UtcNow(Noon.AddDays(-5), 1000); // the phone clock was set back
        Assert.Equal(a, b);
    }

    [Fact]
    public void AfterRestart_OfflineClockCannotBeSetBack()
    {
        var first = new TrustedClock();
        first.Observe(Noon, Noon, 0);
        var restarted = TrustedClock.Restore(first.Offset, first.UtcNow(Noon, 60_000));
        Assert.Equal(Noon.AddMinutes(1), restarted.UtcNow(Noon.AddDays(-2), 0)); // phone clock set 2 days back while the game was closed
        Assert.Equal(Noon.AddDays(2), restarted.UtcNow(Noon.AddDays(2), 0));       // moving forward while offline is allowed until the next server answer
    }

    [Fact]
    public void ServerAnswerCorrectsAFastClockEvenAfterTheFloorWentUp()
    {
        var clock = TrustedClock.Restore(TimeSpan.Zero, Noon.AddDays(10)); // an earlier run saw a wrong, far-future phone clock
        clock.Observe(Noon, Noon.AddDays(10), 0);
        Assert.Equal(Noon, clock.UtcNow(Noon.AddDays(10), 0));
    }

    [Fact]
    public void Today_UsesTheLocalDayOfTrustedTime()
    {
        var clock = new TrustedClock();
        var late = new DateTime(2026, 11, 5, 22, 30, 0, DateTimeKind.Utc);
        int utcDay = clock.Today(late, 0, TimeSpan.Zero);
        var azerbaijan = new TrustedClock();
        Assert.Equal(utcDay + 1, azerbaijan.Today(late, 0, TimeSpan.FromHours(4))); // 02:30 the next morning there
    }
}
