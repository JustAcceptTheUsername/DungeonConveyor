using System.Collections.Generic;
using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>
    /// Steps-to-the-Core for every map cell, so each enemy just walks downhill to take the shortest route.
    /// Enemies use the IsoGrid movement rule (4 directions, climb at most MaxClimb) and walk over belts and mines;
    /// every other building is in the way, which is how walls lengthen their path. The goal is any walkable cell
    /// next to the Core.
    /// </summary>
    public class FlowField
    {
        public const int Unreachable = int.MaxValue;

        readonly IsoGrid map;
        int[,] distance;
        readonly Queue<Vector2Int> queue = new Queue<Vector2Int>();

        public FlowField(IsoGrid map)
        {
            this.map = map;
            Rebuild();
        }

        public void Rebuild() => distance = Compute(null);

        public int Distance(Vector2Int c) => map.InBounds(c) ? distance[c.x, c.y] : Unreachable;

        /// <summary>The neighbouring cell one step closer to the Core, if there is one.</summary>
        public bool Next(Vector2Int c, out Vector2Int next)
        {
            next = c;
            int best = Distance(c);
            for (int d = 0; d < 4; d++)
            {
                var n = c + Dir.Offsets[d];
                int nd = Distance(n);
                if (nd < best && map.CanStep(c, n))
                {
                    best = nd;
                    next = n;
                }
            }
            return next != c;
        }

        /// <summary>Would blocking these cells cut any spawn off from the Core?</summary>
        public bool WouldBlock(IEnumerable<Vector2Int> cells)
        {
            var blocked = new HashSet<Vector2Int>(cells);
            var test = Compute(blocked);
            foreach (var b in map.Bridges)
                if (test[b.Landing.x, b.Landing.y] == Unreachable) return true;
            return false;
        }

        public bool Walkable(Vector2Int c, HashSet<Vector2Int> extraBlocked = null)
        {
            if (!map.HasCell(c) || map.IsBase(c)) return false;
            if (extraBlocked != null && extraBlocked.Contains(c)) return false;
            var b = map.BuildingAt(c);
            return b == null || b is Conveyor || !b.Def.BlocksEnemies;
        }

        int[,] Compute(HashSet<Vector2Int> extraBlocked)
        {
            var dist = new int[map.Width, map.Height];
            for (int x = 0; x < map.Width; x++)
                for (int y = 0; y < map.Height; y++)
                    dist[x, y] = Unreachable;

            queue.Clear();
            for (int x = -1; x <= IsoGrid.BaseSize; x++)
                for (int y = -1; y <= IsoGrid.BaseSize; y++)
                {
                    var c = map.BaseOrigin + new Vector2Int(x, y);
                    if (map.IsBase(c) || !Walkable(c, extraBlocked) || !TouchesBase(c)) continue;
                    dist[c.x, c.y] = 0;
                    queue.Enqueue(c);
                }

            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                int next = dist[c.x, c.y] + 1;
                for (int d = 0; d < 4; d++)
                {
                    var n = c + Dir.Offsets[d];
                    if (!map.InBounds(n) || dist[n.x, n.y] <= next || !Walkable(n, extraBlocked) || !map.CanStep(n, c)) continue;
                    dist[n.x, n.y] = next;
                    queue.Enqueue(n);
                }
            }
            return dist;
        }

        bool TouchesBase(Vector2Int c)
        {
            for (int d = 0; d < 4; d++)
                if (map.IsBase(c + Dir.Offsets[d])) return true;
            return false;
        }
    }
}
