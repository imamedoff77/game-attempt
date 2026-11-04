using System;
using System.Collections.Generic;

namespace SimCore
{
    /// <summary>
    /// One player's side of a match. The game runs in TURNS of <see cref="TicksPerTurn"/> ticks. A command given now is
    /// sent for a turn <c>InputDelayTurns</c> in the future, so the other player has time to receive it. The world only
    /// advances through a turn once the server has sent that turn's commands from BOTH players; otherwise the client waits.
    /// Both clients then apply the same commands at the same tick, and the deterministic simulation gives the same game.
    /// Nothing here touches sockets or Unity: the host passes messages in with <see cref="Receive"/> and out through the send callback.
    /// </summary>
    public sealed class LockstepClient
    {
        public const int TicksPerTurn = 4;
        /// <summary>A fingerprint of the world is sent after every this-many turns (5 turns = 1 second).</summary>
        public const int HashIntervalTurns = 5;

        private readonly World _world;
        private readonly Action<NetMessage> _send;
        private readonly Dictionary<int, TurnBundle> _bundles = new Dictionary<int, TurnBundle>();
        private readonly List<Command> _outgoing = new List<Command>();
        private int _nextSubmitTurn;
        private int _turn;       // the turn being played now
        private int _tickInTurn;
        private readonly int _replayUntilTurn; // turns before this were played before we reconnected: no fingerprints for them

        public int Team { get; }
        public int InputDelayTurns { get; }

        /// <summary>Ticks played so far.</summary>
        public int TicksExecuted => _turn * TicksPerTurn + _tickInTurn;

        /// <summary>True while the next turn's commands have not arrived yet (the game is stalled by the network).</summary>
        public bool Waiting { get; private set; }

        public bool Desynced { get; private set; }
        public DesyncNotice DesyncInfo { get; private set; }

        /// <summary>Turns received but not played yet (grows when the phone is slower than the network).</summary>
        public int TurnsBuffered => _bundles.Count;

        /// <summary>True while replaying turns that were played before a reconnect.</summary>
        public bool CatchingUp => _turn < _replayUntilTurn;

        public LockstepClient(World world, int team, int inputDelayTurns, Action<NetMessage> send) : this(world, team, inputDelayTurns, send, 0, 0)
        {
        }

        /// <summary>
        /// A client that comes back after losing the link: <paramref name="world"/> is a brand-new starting world, the server sends every turn
        /// played so far and this client replays them (as fast as <see cref="TryStep"/> is called). Its own turns up to
        /// <paramref name="nextSubmitTurn"/> - 1 are already at the server, so it continues submitting from there.
        /// </summary>
        public static LockstepClient Resume(World world, MatchResume resume, Action<NetMessage> send) =>
            new LockstepClient(world, resume.Team, resume.InputDelayTurns, send, resume.NextSubmitTurn, resume.ReplayTurns);

        private LockstepClient(World world, int team, int inputDelayTurns, Action<NetMessage> send, int nextSubmitTurn, int replayUntilTurn)
        {
            if (team < 0 || team >= World.TeamCount) throw new ArgumentOutOfRangeException(nameof(team));
            if (inputDelayTurns < 1) throw new ArgumentOutOfRangeException(nameof(inputDelayTurns));
            _world = world; Team = team; InputDelayTurns = inputDelayTurns; _send = send;
            _nextSubmitTurn = nextSubmitTurn; _replayUntilTurn = replayUntilTurn;
        }

        /// <summary>Call once when the match begins: sends the first (empty) turns so the game can start moving.</summary>
        public void Start() => SubmitAhead();

        /// <summary>A command the local player just gave. It is sent with the next turn that can still be filled.</summary>
        public void QueueCommand(Command command)
        {
            if (command == null || Desynced) return;
            if (_outgoing.Count >= CommandCodec.MaxCommandsPerTurn) return; // more than a turn can carry: dropped, as a spamming client would be
            _outgoing.Add(command);
        }

        public void Receive(NetMessage message)
        {
            switch (message)
            {
                case TurnBundle bundle:
                    if (bundle.ByTeam == null || bundle.ByTeam.Length != World.TeamCount) return;
                    if (bundle.Turn >= _turn && !_bundles.ContainsKey(bundle.Turn)) _bundles[bundle.Turn] = bundle;
                    break;
                case DesyncNotice notice:
                    Desynced = true;
                    DesyncInfo = notice;
                    break;
            }
        }

        /// <summary>Plays one tick if the commands for it are here. Returns false when it has to wait (or the match is broken).</summary>
        public bool TryStep()
        {
            if (Desynced) return false;

            if (_tickInTurn == 0)
            {
                if (!_bundles.TryGetValue(_turn, out TurnBundle bundle))
                {
                    Waiting = true;
                    return false;
                }
                Waiting = false;
                for (int team = 0; team < World.TeamCount; team++)
                    foreach (Command c in bundle.ByTeam[team])
                        _world.Enqueue(c, team); // the team comes from the slot the server put it in, never from the sender
            }

            _world.Step();
            _tickInTurn++;

            if (_tickInTurn == TicksPerTurn)
            {
                _tickInTurn = 0;
                _bundles.Remove(_turn);
                if ((_turn + 1) % HashIntervalTurns == 0 && _turn >= _replayUntilTurn) _send(new HashReport(_turn, _world.StateHash()));
                _turn++;
                SubmitAhead();
            }
            return true;
        }

        /// <summary>Sends turns until <c>InputDelayTurns</c> turns ahead of the one being played are covered.</summary>
        private void SubmitAhead()
        {
            while (_nextSubmitTurn <= _turn + InputDelayTurns)
            {
                var commands = new List<Command>(_outgoing);
                _outgoing.Clear();
                _send(new TurnSubmit(_nextSubmitTurn, commands));
                _nextSubmitTurn++;
            }
        }
    }
}
