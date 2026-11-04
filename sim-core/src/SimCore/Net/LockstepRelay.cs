using System;
using System.Collections.Generic;

namespace SimCore
{
    /// <summary>
    /// What the server does for one match, with no sockets in it (so it is tested like any other code).
    /// It collects each player's commands for a turn, and once BOTH have arrived sends everybody the combined turn.
    /// It never simulates the game; it only keeps order, decides who sent what, and compares the two fingerprints.
    /// </summary>
    public sealed class LockstepRelay
    {
        /// <summary>A client may run this many turns ahead of the slowest one (about 2 are normal; more means a broken or cheating client).</summary>
        public const int MaxTurnsAhead = 32;
        /// <summary>Unmatched fingerprints kept in memory per player.</summary>
        private const int MaxPendingHashes = 256;

        private readonly Action<int, NetMessage> _deliver;
        private readonly Dictionary<int, IReadOnlyList<Command>>[] _submitted = new Dictionary<int, IReadOnlyList<Command>>[World.TeamCount];
        private readonly Dictionary<int, ulong>[] _hashes = new Dictionary<int, ulong>[World.TeamCount];
        private readonly int[] _nextExpected = new int[World.TeamCount];
        private readonly List<TurnBundle> _log = new List<TurnBundle>();

        /// <param name="deliver">Sends a message to a team's client (0 or 1).</param>
        public LockstepRelay(Action<int, NetMessage> deliver)
        {
            _deliver = deliver;
            for (int t = 0; t < World.TeamCount; t++)
            {
                _submitted[t] = new Dictionary<int, IReadOnlyList<Command>>();
                _hashes[t] = new Dictionary<int, ulong>();
            }
        }

        /// <summary>Every turn sent so far, in order. A returning player gets this to catch up.</summary>
        public IReadOnlyList<TurnBundle> Log => _log;

        /// <summary>Turns completed so far (both players' commands were in).</summary>
        public int TurnsBundled => _log.Count;

        /// <summary>The first turn this team has not submitted yet (a returning player continues from here).</summary>
        public int NextExpectedTurn(int team) => _nextExpected[team];

        public bool Desynced { get; private set; }
        public DesyncNotice DesyncInfo { get; private set; }

        /// <summary>How many fingerprint pairs matched.</summary>
        public int HashesCompared { get; private set; }

        /// <summary>Messages that were refused (out of order, too far ahead, wrong team, duplicates...).</summary>
        public int Violations { get; private set; }

        public void Receive(int team, NetMessage message)
        {
            if (team < 0 || team >= World.TeamCount || message == null) { Violations++; return; }
            if (Desynced) return;

            switch (message)
            {
                case TurnSubmit submit: OnSubmit(team, submit); break;
                case HashReport report: OnHash(team, report); break;
                default: Violations++; break; // clients may only send these two
            }
        }

        private void OnSubmit(int team, TurnSubmit submit)
        {
            // Turns must come one after another; anything else is a bug or a cheat.
            if (submit.Turn != _nextExpected[team] || submit.Turn > _log.Count + MaxTurnsAhead || submit.Commands == null)
            {
                Violations++;
                return;
            }
            _submitted[team][submit.Turn] = submit.Commands;
            _nextExpected[team]++;
            SendCompleteTurns();
        }

        private void SendCompleteTurns()
        {
            while (true)
            {
                int turn = _log.Count;
                var lists = new IReadOnlyList<Command>[World.TeamCount];
                for (int t = 0; t < lists.Length; t++)
                    if (!_submitted[t].TryGetValue(turn, out lists[t])) return;

                for (int t = 0; t < lists.Length; t++) _submitted[t].Remove(turn);
                var bundle = new TurnBundle(turn, lists);
                _log.Add(bundle);
                for (int t = 0; t < lists.Length; t++) _deliver(t, bundle);
            }
        }

        private void OnHash(int team, HashReport report)
        {
            if (report.Turn < 0 || _hashes[team].ContainsKey(report.Turn) || _hashes[team].Count >= MaxPendingHashes)
            {
                Violations++;
                return;
            }
            _hashes[team][report.Turn] = report.Hash;

            int other = 1 - team;
            if (!_hashes[other].TryGetValue(report.Turn, out ulong otherHash)) return;

            _hashes[0].Remove(report.Turn);
            _hashes[1].Remove(report.Turn);
            if (otherHash == report.Hash)
            {
                HashesCompared++;
                return;
            }

            Desynced = true;
            ulong h0 = team == 0 ? report.Hash : otherHash;
            ulong h1 = team == 0 ? otherHash : report.Hash;
            DesyncInfo = new DesyncNotice(report.Turn, h0, h1);
            for (int t = 0; t < World.TeamCount; t++) _deliver(t, DesyncInfo);
        }
    }
}
