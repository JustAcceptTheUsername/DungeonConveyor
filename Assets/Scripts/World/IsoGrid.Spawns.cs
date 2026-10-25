using System.Collections.Generic;
using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>
    /// Enemy spawns: 1-4 meteor landing sites (scorched red cells) spread around the planet, away from the bases.
    /// Each enemy rides a meteor down from space onto its site, then walks to the Core (see Enemy).
    /// </summary>
    public partial class IsoGrid
    {
        public class SpawnSite
        {
            public Vector2Int Landing;   // cell the meteors strike (red)
        }

        public readonly List<SpawnSite> Spawns = new List<SpawnSite>();

        public bool IsSpawn(Vector2Int c)
        {
            foreach (var s in Spawns)
                if (s.Landing == c) return true;
            return false;
        }

        bool NearSpawn(Vector2Int c, float distance)
        {
            foreach (var s in Spawns)
                if (Distance(c, s.Landing) < distance) return true;
            return false;
        }

        /// <summary>
        /// Picks landing sites far from every base (preferably on the far side of the planet) and far from each other;
        /// the distances relax if the planet is too small for them.
        /// </summary>
        void PlaceSpawns()
        {
            Spawns.Clear();
            int target = Random.Range(SpawnCount.x, SpawnCount.y + 1);
            float halfRound = Mathf.PI * Radius;                      // distance to the opposite point
            float minBase = Mathf.Max(SpawnMinDistance, halfRound * 0.6f);
            float spacing = halfRound * 0.45f;
            for (int pass = 0; pass < 8 && Spawns.Count < target; pass++, minBase *= 0.85f, spacing *= 0.8f)
                for (int tries = 0; tries < 2000 && Spawns.Count < target; tries++)
                {
                    var c = new Vector2Int(Random.Range(0, Width), Random.Range(0, Height));
                    if (IsBase(c) || IsPentagon(c) || DistanceToBase(c) < Mathf.Min(minBase, halfRound * 0.95f) || NearSpawn(c, spacing)) continue;
                    Spawns.Add(new SpawnSite { Landing = c });
                }

            if (Spawns.Count == 0) Debug.LogWarning("IsoGrid: no room for a meteor landing site.");
        }

        /// <summary>Raised after a landing site was added or moved during play.</summary>
        public event System.Action SpawnsChanged;

        /// <summary>
        /// Mid-game: adds a landing site (add) or moves a random one somewhere new. The new site is a free cell the
        /// enemies can already walk to the Core from (the terrain doesn't change mid-game), far from the bases and the
        /// other sites. False if no such cell was found.
        /// </summary>
        public bool MoveOrAddLandingSite(bool add)
        {
            if (!add && Spawns.Count == 0) return false;
            var reachable = ReachableFromBase();
            float halfRound = Mathf.PI * Radius;
            float minBase = Mathf.Min(SpawnMinDistance, halfRound * 0.5f), spacing = halfRound * 0.25f;
            var moving = add ? null : Spawns[Random.Range(0, Spawns.Count)];
            for (int pass = 0; pass < 4; pass++, minBase *= 0.8f, spacing *= 0.7f)
                for (int tries = 0; tries < 1500; tries++)
                {
                    var c = new Vector2Int(Random.Range(0, Width), 0);
                    if (!reachable[c.x, c.y] || IsBase(c) || IsPentagon(c) || IsOre(c) || IsSpawn(c) || BuildingAt(c) != null) continue;
                    if (DistanceToBase(c) < minBase) continue;
                    bool crowded = false;
                    foreach (var s in Spawns)
                        if (s != moving && Distance(c, s.Landing) < spacing) crowded = true;
                    if (crowded) continue;
                    if (add) Spawns.Add(new SpawnSite { Landing = c });
                    else moving.Landing = c;
                    SpawnsChanged?.Invoke();
                    return true;
                }
            return false;
        }
    }
}
