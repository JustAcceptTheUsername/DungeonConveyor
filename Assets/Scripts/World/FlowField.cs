using System.Collections.Generic;
using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>
    /// Steps-to-the-Core for every planet cell, so each enemy just walks downhill to take the shortest route.
    /// Enemies use the IsoGrid movement rule (to any of the 6 neighbours, climb at most MaxClimb) and walk over belts and mines;
    /// every other building is in the way, which is how walls lengthen their path. The goal is any walkable cell
    /// next to the Core.
    /// </summary>
    public class FlowField
    {
        public const int Unreachable = int.MaxValue;

        /// <summary>
        /// Ground: the normal enemy route to the Core. Air: fliers, straight over buildings, cliffs and spires to the
        /// Core. Belts: saboteurs, the ground route to the nearest belt tile.
        /// </summary>
        public enum Mode { Ground, Air, Belts }

        readonly IsoGrid map;
        readonly Mode mode;
        int[,] distance;
        readonly Queue<Vector2Int> queue = new Queue<Vector2Int>();

        public FlowField(IsoGrid map, Mode mode = Mode.Ground)
        {
            this.map = map;
            this.mode = mode;
            Rebuild();
        }

        bool Step(Vector2Int from, Vector2Int to) => mode == Mode.Air ? from != to : map.CanStep(from, to);

        public void Rebuild() => distance = Compute(null);

        public int Distance(Vector2Int c) => map.InBounds(c) ? distance[c.x, c.y] : Unreachable;

        /// <summary>The neighbouring cell one step closer to the Core, if there is one.</summary>
        public bool Next(Vector2Int c, out Vector2Int next)
        {
            next = c;
            int best = Distance(c);
            for (int d = 0; d < map.Sides(c); d++)
            {
                var n = map.Neighbour(c, d);
                int nd = Distance(n);
                if (nd < best && Step(c, n))
                {
                    best = nd;
                    next = n;
                }
            }
            return next != c;
        }

        /// <summary>Would blocking these cells cut any landing site off from the Core?</summary>
        public bool WouldBlock(IEnumerable<Vector2Int> cells)
        {
            var blocked = new HashSet<Vector2Int>(cells);
            var test = Compute(blocked);
            foreach (var s in map.Spawns)
                if (test[s.Landing.x, s.Landing.y] == Unreachable) return true;
            return false;
        }

        public bool Walkable(Vector2Int c, HashSet<Vector2Int> extraBlocked = null)
        {
            if (!map.HasCell(c) || map.IsBase(c)) return false;
            if (mode == Mode.Air) return true;
            if (map.IsPentagon(c)) return false;
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
            if (mode == Mode.Belts)
            {
                // Goals: every belt tile (belts are walkable).
                for (int x = 0; x < map.Width; x++)
                {
                    var c = new Vector2Int(x, 0);
                    if (!(map.BuildingAt(c) is Conveyor) || !Walkable(c, extraBlocked)) continue;
                    dist[c.x, c.y] = 0;
                    queue.Enqueue(c);
                }
            }
            else foreach (var b in map.BaseCells(0))
                for (int d = 0; d < map.Sides(b); d++)
                {
                    var c = map.Neighbour(b, d);
                    if (dist[c.x, c.y] == 0 || !Walkable(c, extraBlocked)) continue;
                    dist[c.x, c.y] = 0;
                    queue.Enqueue(c);
                }

            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                int next = dist[c.x, c.y] + 1;
                for (int d = 0; d < map.Sides(c); d++)
                {
                    var n = map.Neighbour(c, d);
                    if (dist[n.x, n.y] <= next || !Walkable(n, extraBlocked) || !Step(n, c)) continue;
                    dist[n.x, n.y] = next;
                    queue.Enqueue(n);
                }
            }
            return dist;
        }
    }
}
