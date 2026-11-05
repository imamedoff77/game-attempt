using System.Collections.Generic;

namespace SimCore
{
    /// <summary>Unit collisions and group formation.</summary>
    public sealed partial class World
    {
        private const int BucketSize = 2;
        private const int SeparationPasses = 2;
        private static readonly Fix64 Quarter = Fix64.FromFraction(1, 4);
        private static readonly Fix64 ThreeQuarters = Fix64.FromFraction(3, 4);
        private static readonly Fix64 FormationGap = Fix64.FromFraction(2, 10);

        private int[] _bucketHead = new int[0];
        private int[] _bucketNext = new int[0];

        /// <summary>
        /// Pushes overlapping units apart so they never stand inside each other. A unit that is walking pushes
        /// harder against a standing one (the standing one gives way). Neighbours are found with a coarse grid,
        /// pairs are handled in unit-list order, and nothing is pushed onto an unwalkable cell: the result is
        /// the same on every device.
        /// </summary>
        private void SeparateUnits()
        {
            int n = _units.Count;
            if (n < 2) return;

            int bw = (Map.Width + BucketSize - 1) / BucketSize;
            int bh = (Map.Height + BucketSize - 1) / BucketSize;
            if (_bucketHead.Length != bw * bh) _bucketHead = new int[bw * bh];
            if (_bucketNext.Length < n) _bucketNext = new int[n * 2];

            for (int pass = 0; pass < SeparationPasses; pass++)
            {
                for (int i = 0; i < _bucketHead.Length; i++) _bucketHead[i] = -1;
                for (int i = n - 1; i >= 0; i--)
                {
                    int b = BucketIndex(_units[i].Pos, bw, bh);
                    _bucketNext[i] = _bucketHead[b];
                    _bucketHead[b] = i;
                }

                for (int i = 0; i < n; i++)
                {
                    Unit a = _units[i];
                    int bx = BucketCoord(a.Pos.X, bw), by = BucketCoord(a.Pos.Y, bh);
                    for (int cy = by - 1; cy <= by + 1; cy++)
                    {
                        if (cy < 0 || cy >= bh) continue;
                        for (int cx = bx - 1; cx <= bx + 1; cx++)
                        {
                            if (cx < 0 || cx >= bw) continue;
                            for (int j = _bucketHead[cy * bw + cx]; j != -1; j = _bucketNext[j])
                                if (j > i) ResolveOverlap(a, _units[j]);
                        }
                    }
                }
            }
        }

        private static int BucketCoord(Fix64 v, int count)
        {
            int c = v.ToInt() / BucketSize;
            return c < 0 ? 0 : c >= count ? count - 1 : c;
        }

        private static int BucketIndex(Vec2 p, int bw, int bh) => BucketCoord(p.Y, bh) * bw + BucketCoord(p.X, bw);

        private void ResolveOverlap(Unit a, Unit b)
        {
            Vec2 d = b.Pos - a.Pos;
            Fix64 min = a.Radius + b.Radius;
            Fix64 sq = d.SqrLength;
            if (sq >= min * min) return;

            Vec2 dir;
            Fix64 dist;
            if (sq.Raw == 0)
            {
                dir = ((a.Id + b.Id) & 1) == 0 ? new Vec2(Fix64.One, Fix64.Zero) : new Vec2(Fix64.Zero, Fix64.One);
                dist = Fix64.Zero;
            }
            else
            {
                dist = Fix64.Sqrt(sq);
                dir = new Vec2(d.X / dist, d.Y / dist);
            }

            Fix64 overlap = min - dist;
            Fix64 shareA = Fix64.Half;
            if (a.Moving && !b.Moving) shareA = Quarter;
            else if (!a.Moving && b.Moving) shareA = ThreeQuarters;

            Fix64 pushA = overlap * shareA;
            Fix64 pushB = overlap - pushA;
            bool movedA = TryShift(a, -dir * pushA);
            bool movedB = TryShift(b, dir * pushB);
            // A wall behind one of them: the other gives way for both.
            if (!movedA && movedB) TryShift(b, dir * pushA);
            else if (!movedB && movedA) TryShift(a, -dir * pushB);
        }

        private bool TryShift(Unit u, Vec2 delta)
        {
            if (delta.X.Raw == 0 && delta.Y.Raw == 0) return true;

            Vec2 p = u.Pos + delta;
            if (Map.IsWalkable(p.X.ToInt(), p.Y.ToInt())) { u.Pos = p; return true; }

            var alongX = new Vec2(p.X, u.Pos.Y);
            if (Map.IsWalkable(alongX.X.ToInt(), alongX.Y.ToInt())) { u.Pos = alongX; return true; }

            var alongY = new Vec2(u.Pos.X, p.Y);
            if (Map.IsWalkable(alongY.X.ToInt(), alongY.Y.ToInt())) { u.Pos = alongY; return true; }
            return false;
        }

        /// <summary>
        /// Gives each unit a formation slot so the group arrives without crossing itself: the unit that is
        /// furthest ahead (towards the destination) takes the front slot, and so on back.
        /// </summary>
        private static Vec2[] AssignSlots(List<Unit> units, Vec2[] offsets, Vec2 target)
        {
            int n = units.Count;
            var assigned = new Vec2[n];
            if (n == 0) return assigned;

            Vec2 centre = Vec2.Zero;
            foreach (Unit u in units) centre = centre + u.Pos;
            centre = new Vec2(centre.X / Fix64.FromInt(n), centre.Y / Fix64.FromInt(n));
            Vec2 forward = (target - centre).Normalized();

            var unitOrder = new int[n];
            var slotOrder = new int[n];
            var unitKey = new Fix64[n];
            var slotKey = new Fix64[n];
            for (int i = 0; i < n; i++)
            {
                unitOrder[i] = slotOrder[i] = i;
                unitKey[i] = (units[i].Pos - centre).Dot(forward);
                slotKey[i] = offsets[i].Dot(forward);
            }

            // Front first. Ties are broken by id / slot number, so the order is total and the same everywhere.
            System.Array.Sort(unitOrder, (x, y) =>
            {
                int c = unitKey[y].CompareTo(unitKey[x]);
                return c != 0 ? c : units[x].Id.CompareTo(units[y].Id);
            });
            System.Array.Sort(slotOrder, (x, y) =>
            {
                int c = slotKey[y].CompareTo(slotKey[x]);
                return c != 0 ? c : x.CompareTo(y);
            });

            for (int k = 0; k < n; k++) assigned[unitOrder[k]] = offsets[slotOrder[k]];
            return assigned;
        }
    }
}
