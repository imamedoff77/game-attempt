using System;

namespace SimCore
{
    public enum DailyTaskKind { PlayMatches = 0, WinMission = 1, WinSkirmish = 2 }

    public readonly struct DailyTask
    {
        public readonly DailyTaskKind Kind;
        public readonly int Target;
        public readonly int Reward;

        public DailyTask(DailyTaskKind kind, int target, int reward)
        {
            Kind = kind;
            Target = target;
            Reward = reward;
        }
    }

    /// <summary>
    /// The daily login reward (a streak that pays more every day, up to a week) and three daily tasks.
    /// Days are plain numbers (days since 1970) given by the caller, so the logic has no clock of its own and is easy to test.
    /// The local version trusts the phone's clock; the real one will use server time (Faza 5).
    /// </summary>
    public sealed class DailyProgress
    {
        public const int TaskCount = 3;
        private static readonly int[] StreakRewards = { 20, 30, 40, 50, 60, 80, 120 };

        private int _lastClaimDay = -1;
        private int _streak;
        private int _taskDay = -1;
        private readonly int[] _progress = new int[TaskCount];
        private readonly bool[] _claimed = new bool[TaskCount];

        /// <summary>The days in a row the reward was claimed (the streak as of the last claim).</summary>
        public int Streak => _streak;

        /// <summary>The streak the player would be on after claiming today (broken if a day was missed).</summary>
        public int StreakIfClaimed(int day) => _lastClaimDay == day - 1 ? _streak + 1 : 1;

        public bool CanClaimReward(int day) => day != _lastClaimDay;

        public int RewardFor(int streak) => StreakRewards[(Math.Max(1, streak) - 1) % StreakRewards.Length];

        /// <summary>Claims today's login reward into the wallet and returns the credits (0 if it was already claimed today).</summary>
        public int ClaimReward(int day, CampaignProgress wallet)
        {
            if (!CanClaimReward(day)) return 0;
            _streak = StreakIfClaimed(day);
            _lastClaimDay = day;
            int reward = RewardFor(_streak);
            wallet.Add(reward);
            return reward;
        }

        /// <summary>The three tasks of a day: how many matches to finish, missions to win, and one win against the computer.</summary>
        public static DailyTask[] TasksFor(int day)
        {
            int variant = Math.Abs(day) % 3;
            return new[]
            {
                new DailyTask(DailyTaskKind.PlayMatches, 2 + variant, 20),
                new DailyTask(DailyTaskKind.WinMission, 1 + (variant == 2 ? 1 : 0), 30),
                new DailyTask(DailyTaskKind.WinSkirmish, 1, 40),
            };
        }

        public int Progress(int day, int task)
        {
            Roll(day);
            return Math.Min(_progress[task], TasksFor(day)[task].Target);
        }

        public bool IsDone(int day, int task) => Progress(day, task) >= TasksFor(day)[task].Target;

        public bool IsClaimed(int day, int task)
        {
            Roll(day);
            return _claimed[task];
        }

        public bool CanClaimTask(int day, int task) => IsDone(day, task) && !_claimed[task];

        /// <summary>True when there is something to collect today: the login reward or a finished task.</summary>
        public bool HasSomethingToClaim(int day)
        {
            if (CanClaimReward(day)) return true;
            for (int i = 0; i < TaskCount; i++) if (CanClaimTask(day, i)) return true;
            return false;
        }

        /// <summary>Counts an event (a finished match, a won mission, a won match against the computer) towards today's tasks.</summary>
        public void Report(int day, DailyTaskKind kind, int amount = 1)
        {
            Roll(day);
            DailyTask[] tasks = TasksFor(day);
            for (int i = 0; i < TaskCount; i++)
                if (tasks[i].Kind == kind && amount > 0) _progress[i] += amount;
        }

        /// <summary>Pays a finished task into the wallet; 0 if it is not finished or was already paid.</summary>
        public int ClaimTask(int day, int task, CampaignProgress wallet)
        {
            if (!CanClaimTask(day, task)) return 0;
            _claimed[task] = true;
            int reward = TasksFor(day)[task].Reward;
            wallet.Add(reward);
            return reward;
        }

        // A new day starts the tasks from zero.
        private void Roll(int day)
        {
            if (day == _taskDay) return;
            _taskDay = day;
            Array.Clear(_progress, 0, _progress.Length);
            Array.Clear(_claimed, 0, _claimed.Length);
        }

        /// <summary>Joins another copy (another device): the more recent claim and the more recent task day win; on the same day the better progress counts.</summary>
        public void MergeFrom(DailyProgress other)
        {
            if (other._lastClaimDay > _lastClaimDay) { _lastClaimDay = other._lastClaimDay; _streak = other._streak; }
            else if (other._lastClaimDay == _lastClaimDay) _streak = Math.Max(_streak, other._streak);

            if (other._taskDay > _taskDay)
            {
                _taskDay = other._taskDay;
                Array.Copy(other._progress, _progress, TaskCount);
                Array.Copy(other._claimed, _claimed, TaskCount);
            }
            else if (other._taskDay == _taskDay)
            {
                for (int i = 0; i < TaskCount; i++)
                {
                    _progress[i] = Math.Max(_progress[i], other._progress[i]);
                    _claimed[i] |= other._claimed[i];
                }
            }
        }

        public string Serialize()
        {
            string text = _lastClaimDay + "," + _streak + "," + _taskDay;
            for (int i = 0; i < TaskCount; i++) text += "," + _progress[i] + "," + (_claimed[i] ? 1 : 0);
            return text;
        }

        public static DailyProgress Parse(string text)
        {
            var daily = new DailyProgress();
            if (string.IsNullOrEmpty(text)) return daily;
            string[] p = text.Split(',');
            if (p.Length != 3 + TaskCount * 2) return daily;
            var v = new int[p.Length];
            for (int i = 0; i < p.Length; i++) if (!int.TryParse(p[i], out v[i])) return daily;
            daily._lastClaimDay = v[0];
            daily._streak = Math.Max(0, v[1]);
            daily._taskDay = v[2];
            for (int i = 0; i < TaskCount; i++)
            {
                daily._progress[i] = Math.Max(0, v[3 + i * 2]);
                daily._claimed[i] = v[4 + i * 2] != 0;
            }
            return daily;
        }
    }
}
