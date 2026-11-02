using System.Collections.Generic;

namespace SimCore
{
    /// <summary>
    /// The whole game state and its rules. Contains no rendering and no floating point,
    /// so every device computes exactly the same result from the same commands.
    /// </summary>
    public sealed class World
    {
        public const int TicksPerSecond = 20;

        /// <summary>Distance between units when they spread out around a move target.</summary>
        public static readonly Fix64 FormationSpacing = Fix64.One;

        private readonly List<Unit> _units = new List<Unit>();
        private readonly Dictionary<int, Unit> _unitsById = new Dictionary<int, Unit>();
        private readonly List<MoveCommand> _pending = new List<MoveCommand>();
        private int _nextId = 1;

        public Fix64 Width { get; }
        public Fix64 Height { get; }
        public int Tick { get; private set; }
        public IReadOnlyList<Unit> Units => _units;

        public World(Fix64 width, Fix64 height)
        {
            Width = width;
            Height = height;
        }

        /// <param name="speedPerSecond">Map units per second.</param>
        public Unit SpawnUnit(Vec2 position, Fix64 speedPerSecond)
        {
            Fix64 step = Fix64.FromRaw(speedPerSecond.Raw / TicksPerSecond);
            var unit = new Unit(_nextId++, ClampToMap(position), step);
            _units.Add(unit);
            _unitsById[unit.Id] = unit;
            return unit;
        }

        public Unit GetUnit(int id) => _unitsById.TryGetValue(id, out Unit u) ? u : null;

        public void Enqueue(MoveCommand command) => _pending.Add(command);

        /// <summary>Advance the simulation by one tick.</summary>
        public void Step()
        {
            foreach (Unit u in _units)
                u.PrevPos = u.Pos;

            ApplyPendingCommands();

            foreach (Unit u in _units)
                MoveUnit(u);

            Tick++;
        }

        /// <summary>Fingerprint of the whole state; clients compare it to detect desyncs.</summary>
        public ulong StateHash()
        {
            StateHasher h = StateHasher.Create();
            h.Add(Tick);
            foreach (Unit u in _units)
            {
                h.Add(u.Id);
                h.Add(u.Pos);
                h.Add(u.Target);
                h.Add(u.Moving ? 1 : 0);
            }
            return h.Value;
        }

        /// <summary>Slot offsets for <paramref name="count"/> units, centred on (0,0), laid out in a grid.</summary>
        public static Vec2[] FormationOffsets(int count)
        {
            var offsets = new Vec2[count];
            if (count == 0) return offsets;

            int columns = 1;
            while (columns * columns < count) columns++;
            int rows = (count + columns - 1) / columns;

            for (int i = 0; i < count; i++)
            {
                int col = i % columns;
                int row = i / columns;
                // (2*col - (columns-1)) * spacing / 2 centres the grid on the target.
                Fix64 x = FormationSpacing * Fix64.FromInt(col * 2 - (columns - 1)) * Fix64.Half;
                Fix64 y = FormationSpacing * Fix64.FromInt(row * 2 - (rows - 1)) * Fix64.Half;
                offsets[i] = new Vec2(x, y);
            }
            return offsets;
        }

        private void ApplyPendingCommands()
        {
            foreach (MoveCommand cmd in _pending)
            {
                var units = new List<Unit>(cmd.UnitIds.Length);
                foreach (int id in cmd.UnitIds)
                {
                    Unit u = GetUnit(id);
                    if (u != null) units.Add(u);
                }

                Vec2[] offsets = FormationOffsets(units.Count);
                for (int i = 0; i < units.Count; i++)
                {
                    units[i].Target = ClampToMap(cmd.Target + offsets[i]);
                    units[i].Moving = true;
                }
            }
            _pending.Clear();
        }

        private static void MoveUnit(Unit u)
        {
            if (!u.Moving) return;

            Vec2 delta = u.Target - u.Pos;
            Fix64 step = u.StepPerTick;

            if (delta.SqrLength <= step * step)
            {
                u.Pos = u.Target;
                u.Moving = false;
            }
            else
            {
                u.Pos = u.Pos + delta.Normalized() * step;
            }
        }

        private Vec2 ClampToMap(Vec2 p)
        {
            Fix64 margin = Fix64.Half;
            return new Vec2(
                Fix64.Clamp(p.X, margin, Width - margin),
                Fix64.Clamp(p.Y, margin, Height - margin));
        }
    }
}
