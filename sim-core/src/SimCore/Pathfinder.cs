using System;
using System.Collections.Generic;

namespace SimCore
{
    /// <summary>
    /// Grid A* with integer costs only (straight 10, diagonal 14) and a fixed neighbour order, so every
    /// device finds the identical path. Diagonals never cut corners. The result is then straightened
    /// so units walk in lines instead of staircases.
    /// </summary>
    public static class Pathfinder
    {
        private const int Straight = 10;
        private const int Diagonal = 14;

        // N, E, S, W first, then the diagonals: the order is part of the determinism contract.
        private static readonly int[] Dx = { 0, 1, 0, -1, 1, 1, -1, -1 };
        private static readonly int[] Dy = { -1, 0, 1, 0, -1, 1, 1, -1 };

        /// <summary>Cells from start to goal inclusive, or null when the goal cannot be reached.</summary>
        public static List<(int x, int y)> FindPath(MapGrid map, int sx, int sy, int gx, int gy)
        {
            if (!map.InBounds(sx, sy) || !map.IsWalkable(gx, gy)) return null;
            if (sx == gx && sy == gy) return new List<(int, int)> { (sx, sy) };

            int w = map.Width;
            int cells = w * map.Height;
            var gScore = new int[cells];
            var parent = new int[cells];
            var closed = new bool[cells];
            for (int i = 0; i < cells; i++) { gScore[i] = int.MaxValue; parent[i] = -1; }

            var heap = new MinHeap();
            int start = sy * w + sx;
            int goal = gy * w + gx;
            gScore[start] = 0;
            heap.Push(Key(Heuristic(sx, sy, gx, gy), Heuristic(sx, sy, gx, gy), start));

            while (heap.Count > 0)
            {
                int current = (int)(heap.Pop() & 0xFFFFF);
                if (closed[current]) continue;
                closed[current] = true;
                if (current == goal) break;

                int cx = current % w, cy = current / w;
                for (int d = 0; d < 8; d++)
                {
                    int nx = cx + Dx[d], ny = cy + Dy[d];
                    if (!map.IsWalkable(nx, ny)) continue;

                    bool diagonal = d >= 4;
                    if (diagonal && (!map.IsWalkable(cx + Dx[d], cy) || !map.IsWalkable(cx, cy + Dy[d]))) continue;

                    int n = ny * w + nx;
                    if (closed[n]) continue;

                    int g = gScore[current] + (diagonal ? Diagonal : Straight);
                    if (g >= gScore[n]) continue;

                    gScore[n] = g;
                    parent[n] = current;
                    int h = Heuristic(nx, ny, gx, gy);
                    heap.Push(Key(g + h, h, n));
                }
            }

            if (!closed[goal]) return null;

            var path = new List<(int, int)>();
            for (int c = goal; c != -1; c = parent[c]) path.Add((c % w, c / w));
            path.Reverse();
            return Straighten(map, path);
        }

        /// <summary>The closest walkable cell to (x, y), searching outwards in rings; null if the map has none.</summary>
        public static (int x, int y)? NearestWalkable(MapGrid map, int x, int y)
        {
            if (map.IsWalkable(x, y)) return (x, y);

            int maxRadius = Math.Max(map.Width, map.Height);
            for (int r = 1; r <= maxRadius; r++)
            {
                int bestDist = int.MaxValue, bx = 0, by = 0;
                for (int dy = -r; dy <= r; dy++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                        if (!map.IsWalkable(x + dx, y + dy)) continue;
                        int dist = dx * dx + dy * dy;
                        if (dist < bestDist) { bestDist = dist; bx = x + dx; by = y + dy; }
                    }
                }
                if (bestDist != int.MaxValue) return (bx, by);
            }
            return null;
        }

        /// <summary>True when a straight walk between two cell centres stays on walkable cells.</summary>
        public static bool HasLineOfSight(MapGrid map, int x0, int y0, int x1, int y1)
        {
            int dx = Math.Abs(x1 - x0), dy = Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;
            int x = x0, y = y0;

            while (x != x1 || y != y1)
            {
                int px = x, py = y;
                int e2 = 2 * err;
                if (e2 > -dy) { err -= dy; x += sx; }
                if (e2 < dx) { err += dx; y += sy; }

                if (!map.IsWalkable(x, y)) return false;
                if (x != px && y != py && (!map.IsWalkable(x, py) || !map.IsWalkable(px, y))) return false;
            }
            return true;
        }

        private static List<(int x, int y)> Straighten(MapGrid map, List<(int x, int y)> path)
        {
            if (path.Count <= 2) return path;

            var result = new List<(int, int)> { path[0] };
            int i = 0;
            while (i < path.Count - 1)
            {
                int j = path.Count - 1;
                while (j > i + 1 && !HasLineOfSight(map, path[i].x, path[i].y, path[j].x, path[j].y)) j--;
                result.Add(path[j]);
                i = j;
            }
            return result;
        }

        // Octile distance: 10 * max + 4 * min.
        private static int Heuristic(int x, int y, int gx, int gy)
        {
            int dx = Math.Abs(x - gx), dy = Math.Abs(y - gy);
            return Straight * Math.Max(dx, dy) + (Diagonal - Straight) * Math.Min(dx, dy);
        }

        // Priority: f, then h (prefer nodes closer to the goal), then cell index. All integers, so no ties are left to chance.
        private static long Key(int f, int h, int cell) => ((long)f << 40) | ((long)h << 20) | (long)cell;

        private sealed class MinHeap
        {
            private long[] _items = new long[256];
            public int Count { get; private set; }

            public void Push(long value)
            {
                if (Count == _items.Length) Array.Resize(ref _items, Count * 2);
                int i = Count++;
                while (i > 0)
                {
                    int parent = (i - 1) / 2;
                    if (_items[parent] <= value) break;
                    _items[i] = _items[parent];
                    i = parent;
                }
                _items[i] = value;
            }

            public long Pop()
            {
                long top = _items[0];
                long last = _items[--Count];
                int i = 0;
                while (true)
                {
                    int child = 2 * i + 1;
                    if (child >= Count) break;
                    if (child + 1 < Count && _items[child + 1] < _items[child]) child++;
                    if (_items[child] >= last) break;
                    _items[i] = _items[child];
                    i = child;
                }
                if (Count > 0) _items[i] = last;
                return top;
            }
        }
    }
}
