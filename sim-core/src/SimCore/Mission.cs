using System;
using System.Collections.Generic;

namespace SimCore
{
    public enum ObjectiveKind
    {
        /// <summary>The enemy headquarters is destroyed.</summary>
        DestroyEnemyHq,
        /// <summary>Every enemy building is destroyed.</summary>
        DestroyEnemyBuildings,
        /// <summary>Stay alive for <see cref="Objective.Target"/> seconds.</summary>
        Survive,
        /// <summary>Own <see cref="Objective.Target"/> finished buildings of <see cref="Objective.Building"/>.</summary>
        BuildBuildings,
        /// <summary>The army (unit limit used) reaches <see cref="Objective.Target"/>.</summary>
        ArmyPop,
        /// <summary>Destroy <see cref="Objective.Target"/> enemy units.</summary>
        KillEnemyUnits,
    }

    public readonly struct Objective
    {
        public ObjectiveKind Kind { get; }
        public int Target { get; }
        public BuildingKind Building { get; }

        public Objective(ObjectiveKind kind, int target = 1, BuildingKind building = BuildingKind.Hq)
        {
            Kind = kind; Target = target; Building = building;
        }
    }

    /// <summary>A scripted enemy attack: units appear at a map spot at a given time and go for the player's headquarters.</summary>
    public readonly struct AttackWave
    {
        public int Seconds { get; }
        public UnitKind Kind { get; }
        public int Count { get; }
        public int X { get; }
        public int Y { get; }

        public AttackWave(int seconds, UnitKind kind, int count, int x, int y)
        {
            Seconds = seconds; Kind = kind; Count = count; X = x; Y = y;
        }
    }

    /// <summary>
    /// One campaign mission: how the map starts, what the enemy does, what has to be done and what it is worth.
    /// Texts come from the string table under "mission.{Number}.*", so nothing readable lives here.
    /// </summary>
    public sealed class Mission
    {
        public int Number { get; }
        /// <summary>Builds the starting world. The player is team 0, the enemy team 1.</summary>
        public Func<ulong, World> BuildWorld { get; }
        /// <summary>The computer opponent's skill, or null when the enemy only has the units and waves the mission gives it.</summary>
        public AiDifficulty? Ai { get; }
        public IReadOnlyList<Objective> Objectives { get; }
        public IReadOnlyList<AttackWave> Waves { get; }
        /// <summary>0 = no limit. Running out of time loses the mission.</summary>
        public int TimeLimitSeconds { get; }
        /// <summary>Winning at least this fast earns the second star.</summary>
        public int ParSeconds { get; }
        /// <summary>Winning while losing no more than this many units earns the third star.</summary>
        public int MaxLosses { get; }
        /// <summary>Credits for each star earned for the first time (spent in the store, Faza 4).</summary>
        public int CreditsPerStar { get; }

        public string Key => "mission." + Number;

        public Mission(int number, Func<ulong, World> buildWorld, AiDifficulty? ai, IReadOnlyList<Objective> objectives,
            IReadOnlyList<AttackWave> waves, int timeLimitSeconds, int parSeconds, int maxLosses, int creditsPerStar)
        {
            Number = number; BuildWorld = buildWorld; Ai = ai; Objectives = objectives; Waves = waves;
            TimeLimitSeconds = timeLimitSeconds; ParSeconds = parSeconds; MaxLosses = maxLosses; CreditsPerStar = creditsPerStar;
        }
    }

    public enum MissionStatus { Running, Won, Lost }

    /// <summary>Progress on one objective, for the screen: <c>Current</c> of <c>Target</c>.</summary>
    public struct ObjectiveState
    {
        public Objective Objective;
        public int Current;
        public int Target;
        public bool Done;
    }

    /// <summary>
    /// A mission being played: owns the world, the opponent and the scripted waves, and decides when it is won or lost.
    /// Call <see cref="Advance"/> once per tick instead of <see cref="World.Step"/>.
    /// </summary>
    public sealed class MissionRun
    {
        public Mission Mission { get; }
        public World World { get; }
        public MissionStatus Status { get; private set; } = MissionStatus.Running;
        public ObjectiveState[] Objectives { get; }

        public int UnitsBuilt { get; private set; }
        public int UnitsLost { get; private set; }
        public int EnemyUnitsDestroyed { get; private set; }
        /// <summary>The seconds the mission took, valid once it is decided.</summary>
        public int Seconds => World.Tick / World.TicksPerSecond;
        /// <summary>Seconds until the next scripted attack, or -1 when there are no more.</summary>
        public int SecondsToNextWave => _nextWave >= Mission.Waves.Count ? -1 : System.Math.Max(0, Mission.Waves[_nextWave].Seconds - Seconds);

        private readonly AiPlayer _ai;
        private readonly List<Command> _aiBuffer = new List<Command>();
        private readonly Dictionary<int, int> _teamOfUnit = new Dictionary<int, int>();
        private readonly List<int> _gone = new List<int>();
        private int _nextWave;
        private int _enemyBuildingsAtStart;

        public MissionRun(Mission mission, ulong seed)
        {
            Mission = mission;
            World = mission.BuildWorld(seed);
            if (mission.Ai.HasValue) _ai = new AiPlayer(1, mission.Ai.Value, seed);
            foreach (Unit u in World.Units) _teamOfUnit[u.Id] = u.Team;
            foreach (Building b in World.Buildings) if (b.Team == 1) _enemyBuildingsAtStart++;
            Objectives = new ObjectiveState[mission.Objectives.Count];
            for (int i = 0; i < Objectives.Length; i++) Objectives[i].Objective = mission.Objectives[i];
            Evaluate();
        }

        /// <summary>One simulation tick: the opponent and the waves act, the world steps, the objectives are checked. Does nothing once decided.</summary>
        public void Advance()
        {
            if (Status != MissionStatus.Running) return;

            while (_nextWave < Mission.Waves.Count && World.Tick >= Mission.Waves[_nextWave].Seconds * World.TicksPerSecond)
                SpawnWave(Mission.Waves[_nextWave++]);

            if (_ai != null)
            {
                _aiBuffer.Clear();
                _ai.Update(World, _aiBuffer);
                foreach (Command c in _aiBuffer) World.Enqueue(c, 1);
            }

            World.Step();
            CountUnits();
            Evaluate();
        }

        private void SpawnWave(AttackWave wave)
        {
            var ids = new List<int>();
            for (int i = 0; i < wave.Count; i++)
            {
                // a short row of units, not all on one spot
                var pos = new Vec2(Fix64.FromInt(wave.X + i % 4), Fix64.FromInt(wave.Y + i / 4));
                Unit u = World.SpawnUnit(wave.Kind, pos, 1);
                _teamOfUnit[u.Id] = 1;
                ids.Add(u.Id);
            }
            Building hq = null;
            foreach (Building b in World.Buildings) if (b.Team == 0 && b.Kind == BuildingKind.Hq) { hq = b; break; }
            if (hq != null) World.Enqueue(new AttackCommand(ids.ToArray(), AttackKind.Building, hq.Id), 1);
        }

        private void CountUnits()
        {
            foreach (Unit u in World.Units)
            {
                if (_teamOfUnit.ContainsKey(u.Id)) continue;
                _teamOfUnit[u.Id] = u.Team;
                if (u.Team == 0) UnitsBuilt++;
            }
            _gone.Clear();
            foreach (KeyValuePair<int, int> pair in _teamOfUnit)
                if (World.GetUnit(pair.Key) == null) _gone.Add(pair.Key);
            foreach (int id in _gone)
            {
                if (_teamOfUnit[id] == 0) UnitsLost++; else EnemyUnitsDestroyed++;
                _teamOfUnit.Remove(id);
            }
        }

        private void Evaluate()
        {
            if (!World.HasBuilding(BuildingKind.Hq, 0)) { Status = MissionStatus.Lost; return; }
            if (Mission.TimeLimitSeconds > 0 && Seconds >= Mission.TimeLimitSeconds) { Status = MissionStatus.Lost; return; }

            bool all = true;
            for (int i = 0; i < Objectives.Length; i++)
            {
                ref ObjectiveState s = ref Objectives[i];
                Objective o = s.Objective;
                s.Target = o.Target;
                switch (o.Kind)
                {
                    case ObjectiveKind.DestroyEnemyHq:
                        s.Target = 1;
                        s.Current = World.HasBuilding(BuildingKind.Hq, 1) ? 0 : 1;
                        break;
                    case ObjectiveKind.DestroyEnemyBuildings:
                    {
                        int left = 0;
                        foreach (Building b in World.Buildings) if (b.Team == 1) left++;
                        s.Target = _enemyBuildingsAtStart;
                        s.Current = _enemyBuildingsAtStart - left;
                        break;
                    }
                    case ObjectiveKind.Survive:
                        s.Current = Math.Min(o.Target, Seconds);
                        break;
                    case ObjectiveKind.BuildBuildings:
                    {
                        int count = 0;
                        foreach (Building b in World.Buildings) if (b.Team == 0 && b.Kind == o.Building && !b.UnderConstruction) count++;
                        s.Current = Math.Min(o.Target, count);
                        break;
                    }
                    case ObjectiveKind.ArmyPop:
                        s.Current = Math.Min(o.Target, World.Player(0).PopUsed);
                        break;
                    case ObjectiveKind.KillEnemyUnits:
                        s.Current = Math.Min(o.Target, EnemyUnitsDestroyed);
                        break;
                }
                s.Done = s.Current >= s.Target;
                if (!s.Done) all = false;
            }
            if (all) Status = MissionStatus.Won;
        }

        /// <summary>0 while running or lost; 1 for a win, +1 within the par time, +1 for few losses.</summary>
        public int Stars
        {
            get
            {
                if (Status != MissionStatus.Won) return 0;
                int stars = 1;
                if (Seconds <= Mission.ParSeconds) stars++;
                if (UnitsLost <= Mission.MaxLosses) stars++;
                return stars;
            }
        }
    }
}
