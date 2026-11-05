using System;
using System.Diagnostics;
using SimCore;
using UnityEngine;

/// <summary>
/// The time for daily rewards and boosters (see <see cref="TrustedClock"/>). Every answer of the account server carries its time in the
/// HTTP Date header; <see cref="AccountService"/> hands it to <see cref="Observe"/>. Players who never sign in use their phone clock, but it cannot be set back.
/// </summary>
public static class GameClock
{
    private const string OffsetKey = "clock.offset";
    private const string FloorKey = "clock.floor";

    private static TrustedClock _clock;
    private static readonly Stopwatch Timer = Stopwatch.StartNew();

    private static TrustedClock Clock => _clock ?? (_clock = Load());

    private static long Mono => Timer.ElapsedMilliseconds;

    public static DateTime UtcNow => Clock.UtcNow(DateTime.UtcNow, Mono);

    /// <summary>The day number (days since 1970) in the player's time zone; a day of daily rewards.</summary>
    public static int Today => Clock.Today(DateTime.UtcNow, Mono, TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow));

    public static void Observe(DateTime serverUtc)
    {
        Clock.Observe(serverUtc, DateTime.UtcNow, Mono);
        Save();
    }

    public static void Save()
    {
        try
        {
            PlayerPrefs.SetString(OffsetKey, Clock.Offset.Ticks.ToString());
            PlayerPrefs.SetString(FloorKey, Clock.Floor.Ticks.ToString());
            PlayerPrefs.Save();
        }
        catch (Exception) { }
    }

    private static TrustedClock Load()
    {
        try
        {
            var offset = new TimeSpan(long.Parse(PlayerPrefs.GetString(OffsetKey, "0")));
            long floor = long.Parse(PlayerPrefs.GetString(FloorKey, "0"));
            return TrustedClock.Restore(offset, floor > 0 ? new DateTime(floor, DateTimeKind.Utc) : DateTime.MinValue);
        }
        catch (Exception) { return new TrustedClock(); }
    }
}
