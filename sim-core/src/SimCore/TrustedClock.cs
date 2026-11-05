using System;

namespace SimCore
{
    /// <summary>
    /// The time the game uses for daily rewards and boosters. The phone's clock is not trusted: whenever the account server answers,
    /// its time is taken (<see cref="Observe"/>) and carried forward with a timer that the player cannot set; offline, the last known
    /// difference to the server is applied to the phone's clock; and time never runs backwards (setting the phone clock back gains nothing).
    /// Pure logic: the caller passes the phone's clock and a monotonic millisecond counter, so tests can drive it.
    /// </summary>
    public sealed class TrustedClock
    {
        private bool _synced;
        private DateTime _serverAtSync;
        private long _monoAtSync;

        /// <summary>Server time minus phone time at the last answer from the server (zero before any).</summary>
        public TimeSpan Offset { get; private set; }

        /// <summary>The latest moment ever returned (after a restart this is what the phone clock must have passed).</summary>
        public DateTime Floor { get; private set; } = DateTime.MinValue;

        /// <summary>A clock that remembers an earlier run: the last offset and the latest moment reached.</summary>
        public static TrustedClock Restore(TimeSpan offset, DateTime floor) => new TrustedClock { Offset = offset, Floor = floor };

        /// <summary>The account server said it is <paramref name="serverUtc"/> now.</summary>
        public void Observe(DateTime serverUtc, DateTime phoneUtc, long monotonicMs)
        {
            _synced = true;
            _serverAtSync = serverUtc;
            _monoAtSync = monotonicMs;
            Offset = serverUtc - phoneUtc;
            Floor = serverUtc; // the server is the truth, even when an earlier (wrong) phone clock had pushed the floor higher
        }

        public DateTime UtcNow(DateTime phoneUtc, long monotonicMs)
        {
            DateTime now = _synced ? _serverAtSync.AddMilliseconds(monotonicMs - _monoAtSync) : phoneUtc + Offset;
            if (now < Floor) return Floor;
            Floor = now;
            return now;
        }

        /// <summary>Whole days since 1970-01-01 in the player's own time zone (<paramref name="localUtcOffset"/>): the "day" of daily rewards.</summary>
        public int Today(DateTime phoneUtc, long monotonicMs, TimeSpan localUtcOffset) =>
            (int)Math.Floor((UtcNow(phoneUtc, monotonicMs) + localUtcOffset - new DateTime(1970, 1, 1)).TotalDays);
    }
}
