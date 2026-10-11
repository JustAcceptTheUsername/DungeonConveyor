using System.Collections.Generic;
using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>
    /// Dijkstra distance field toward the core. Blocking buildings are passable at a high cost,
    /// so enemies route around defenses when they can and chew through them when they can't.
    /// </summary>
    public class FlowField
    {
        public const float Unreachable = float.MaxValue;
        const float BuildingCost = 14f, WallCost = 30f;
        static readonly float Diagonal = Mathf.Sqrt(2f);

        readonly GridMap map;
        readonly float[,] dist = new float[GridMap.Width, GridMap.Height];
        bool dirty = true;

        public FlowField(GridMap map)
        {
            this.map = map;
            map.Changed += () => dirty = true;
        }

        public float Distance(Vector2Int c)
        {
            Refresh();
            return GridMap.InBounds(c) ? dist[c.x, c.y] : Unreachable;
        }

        bool Walkable(Vector2Int c) => map.IsFloor(c);

        float StepCost(Vector2Int c)
        {
            var b = map.Buildings[c.x, c.y];
            if (b == null || !b.BlocksEnemies) return 1f;
            return b is Wall ? WallCost : BuildingCost;
        }

        /// <summary>Best neighbouring cell to move into, or null when there is no path.</summary>
        public Vector2Int? Next(Vector2Int c)
        {
            Refresh();
            if (!GridMap.InBounds(c)) return null;
            float best = dist[c.x, c.y];
            Vector2Int? result = null;
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var n = new Vector2Int(c.x + dx, c.y + dy);
                    if (!Walkable(n)) continue;
                    // No corner cutting past rock or buildings.
                    if (dx != 0 && dy != 0 && (!Free(new Vector2Int(c.x + dx, c.y)) || !Free(new Vector2Int(c.x, c.y + dy)))) continue;
                    float d = dist[n.x, n.y];
                    if (d < best) { best = d; result = n; }
                }
            return result;
        }

        bool Free(Vector2Int c)
        {
            if (!Walkable(c)) return false;
            var b = map.Buildings[c.x, c.y];
            return b == null || !b.BlocksEnemies;
        }

        void Refresh()
        {
            if (!dirty) return;
            dirty = false;

            for (int x = 0; x < GridMap.Width; x++)
                for (int y = 0; y < GridMap.Height; y++)
                    dist[x, y] = Unreachable;

            var heap = new MinHeap();
            var core = map.CoreOrigin;
            for (int x = 0; x < 3; x++)
                for (int y = 0; y < 3; y++)
                {
                    var c = new Vector2Int(core.x + x, core.y + y);
                    dist[c.x, c.y] = 0;
                    heap.Push(c, 0);
                }

            while (heap.Count > 0)
            {
                var (c, d) = heap.Pop();
                if (d > dist[c.x, c.y]) continue;
                // Leaving cell c costs whatever it takes to get through c.
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        var n = new Vector2Int(c.x + dx, c.y + dy);
                        if (!Walkable(n)) continue;
                        bool diag = dx != 0 && dy != 0;
                        if (diag && (!Free(new Vector2Int(c.x + dx, c.y)) || !Free(new Vector2Int(c.x, c.y + dy)))) continue;
                        float nd = d + StepCost(n) * (diag ? Diagonal : 1f);
                        if (nd < dist[n.x, n.y]) { dist[n.x, n.y] = nd; heap.Push(n, nd); }
                    }
            }
        }

        class MinHeap
        {
            readonly List<(Vector2Int c, float d)> items = new List<(Vector2Int, float)>();
            public int Count => items.Count;

            public void Push(Vector2Int c, float d)
            {
                items.Add((c, d));
                int i = items.Count - 1;
                while (i > 0)
                {
                    int p = (i - 1) / 2;
                    if (items[p].d <= items[i].d) break;
                    (items[p], items[i]) = (items[i], items[p]);
                    i = p;
                }
            }

            public (Vector2Int, float) Pop()
            {
                var top = items[0];
                int last = items.Count - 1;
                items[0] = items[last];
                items.RemoveAt(last);
                int i = 0;
                while (true)
                {
                    int l = i * 2 + 1, r = l + 1, m = i;
                    if (l < items.Count && items[l].d < items[m].d) m = l;
                    if (r < items.Count && items[r].d < items[m].d) m = r;
                    if (m == i) break;
                    (items[m], items[i]) = (items[i], items[m]);
                    i = m;
                }
                return top;
            }
        }
    }
}
