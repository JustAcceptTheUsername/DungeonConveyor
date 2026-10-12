using System.Collections.Generic;
using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>
    /// Enemy spawns: 1-4 winding bridges that come in from beyond the camera's reach and land on the map edge.
    /// Enemies appear at the far (off-screen) end and walk across onto the landing cell.
    /// </summary>
    public partial class IsoGrid
    {
        public class Bridge
        {
            public Vector2Int Landing;                                  // map cell the bridge arrives on (red)
            public Vector2Int Direction;                                // overall heading from the landing outward
            public readonly List<Vector2Int> Cells = new List<Vector2Int>(); // landing+1 .. off-screen end
            public Vector2Int SpawnPoint => Cells[Cells.Count - 1];    // where enemies appear
        }

        /// <summary>World units past the map bounds a bridge must reach; more than the camera's margin.</summary>
        const float BridgeOverhang = 5f;
        const float SpawnSpacing = 10f;

        public readonly List<Bridge> Bridges = new List<Bridge>();

        public bool IsSpawn(Vector2Int c)
        {
            foreach (var b in Bridges)
                if (b.Landing == c) return true;
            return false;
        }

        bool NearSpawn(Vector2 p, float distance)
        {
            foreach (var b in Bridges)
                if (Vector2.Distance(p, (Vector2)b.Landing + Vector2.one * 0.5f) < distance) return true;
            return false;
        }

        /// <summary>Bridge decks sit level with their landing cell.</summary>
        public int BridgeLevel(Bridge b) => Heights[b.Landing.x, b.Landing.y];
        public Vector3 BridgeSurface(Bridge b, Vector2Int c) => CellCenter(c) + Vector3.up * (BridgeLevel(b) * StepHeight);

        void PlaceSpawns()
        {
            Bridges.Clear();
            var candidates = new List<Bridge>();
            for (int x = 0; x < Width; x++)
                for (int y = 0; y < Height; y++)
                {
                    var c = new Vector2Int(x, y);
                    if (!Exists[x, y] || IsBase(c)) continue;
                    foreach (var d in Neighbours)
                        if (!HasCell(c + d) && TryLayBridge(c, d, out var bridge))
                            candidates.Add(bridge);
                }
            for (int i = candidates.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
            }

            // Prefer spawns far from the base; relax the distance if the map shape doesn't allow it.
            int target = Random.Range(SpawnCount.x, SpawnCount.y + 1);
            float minBase = SpawnMinDistance;
            var taken = new HashSet<Vector2Int>(); // chosen bridge cells plus a one-cell border
            for (int pass = 0; pass < 6 && Bridges.Count < target; pass++, minBase *= 0.75f)
                foreach (var cand in candidates)
                {
                    if (Bridges.Count >= target) break;
                    if (Bridges.Contains(cand)) continue;
                    var landing = (Vector2)cand.Landing + Vector2.one * 0.5f;
                    if (DistanceToBase(landing) < minBase || NearSpawn(landing, SpawnSpacing)) continue;
                    if (cand.Cells.Exists(taken.Contains)) continue;
                    Bridges.Add(cand);
                    foreach (var c in cand.Cells)
                    {
                        taken.Add(c);
                        foreach (var d in Neighbours) taken.Add(c + d);
                    }
                }

            if (Bridges.Count == 0) Debug.LogWarning("IsoGrid: no room for an enemy spawn bridge on this map shape.");
        }

        /// <summary>
        /// Lays a winding bridge out from an edge cell until past the camera's reach: runs of 2-5 cells heading
        /// outward, broken by 1-3 cell sidesteps. Fails if it would cross or brush against land, so it always
        /// reads as a lone bridge over the void. A few random layouts are tried before giving up.
        /// </summary>
        bool TryLayBridge(Vector2Int landing, Vector2Int dir, out Bridge bridge)
        {
            for (int attempt = 0; attempt < 3; attempt++)
                if (TryWindingBridge(landing, dir, out bridge)) return true;
            bridge = null;
            return false;
        }

        bool TryWindingBridge(Vector2Int landing, Vector2Int dir, out Bridge bridge)
        {
            bridge = new Bridge { Landing = landing, Direction = dir };
            var side = new Vector2Int(dir.y, dir.x);
            var c = landing;
            int run = Random.Range(2, 4); // leave the shore straight before the first turn
            while (bridge.Cells.Count < 200)
            {
                for (int i = 0; i < run; i++)
                {
                    if (!Extend(bridge, ref c, dir)) return false;
                    if (BeyondReach(c)) return true;
                }

                // Sidestep left or right; at least 2 outward cells follow, so the bridge never touches itself.
                if (Random.value < 0.65f)
                {
                    var step = Random.value < 0.5f ? side : -side;
                    int steps = Random.Range(1, 4);
                    for (int i = 0; i < steps; i++)
                        if (!Extend(bridge, ref c, step)) return false;
                }
                run = Random.Range(2, 6);
            }
            return false;
        }

        /// <summary>Adds the next cell if it and all its neighbours (other than where it came from) are void.</summary>
        bool Extend(Bridge bridge, ref Vector2Int c, Vector2Int step)
        {
            var next = c + step;
            if (HasCell(next)) return false;
            foreach (var d in Neighbours)
                if (d != -step && HasCell(next + d)) return false;
            bridge.Cells.Add(next);
            c = next;
            return true;
        }

        bool BeyondReach(Vector2Int c)
        {
            var reach = WorldBounds;
            var p = CellCenter(c);
            return p.x < reach.min.x - BridgeOverhang || p.x > reach.max.x + BridgeOverhang ||
                   p.y < reach.min.y - BridgeOverhang || p.y > reach.max.y + BridgeOverhang;
        }

        /// <summary>
        /// Which sides of bridge cell i connect onward (bit 1 = +x, 2 = -x, 4 = +y, 8 = -y), and whether its
        /// planks run across the grid x axis (it was entered along x).
        /// </summary>
        public static void BridgeShape(Bridge b, int i, out int connections, out bool alongX)
        {
            var c = b.Cells[i];
            var prev = i == 0 ? b.Landing : b.Cells[i - 1];
            var next = i + 1 < b.Cells.Count ? b.Cells[i + 1] : c + (c - prev); // the far end keeps going off-screen
            connections = SideBit(prev - c) | SideBit(next - c);
            alongX = (c - prev).x != 0;
        }

        static int SideBit(Vector2Int d) => d.x > 0 ? 1 : d.x < 0 ? 2 : d.y > 0 ? 4 : 8;
    }
}
