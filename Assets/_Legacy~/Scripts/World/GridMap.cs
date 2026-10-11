using System.Collections.Generic;
using UnityEngine;

namespace DungeonConveyor
{
    public enum Tile : byte { Floor, Rock, Lava }

    /// <summary>The dungeon grid: terrain, ore deposits and building occupancy. Cell (x,y) spans world [x,x+1] x [y,y+1].</summary>
    public class GridMap : MonoBehaviour
    {
        public const int Width = 64, Height = 64;
        const int OuterRing = 10;

        public Tile[,] Tiles { get; private set; }
        public sbyte[,] Ore { get; private set; }          // -1 = none, else ResourceType
        public Building[,] Buildings { get; private set; }
        public Vector2Int CoreOrigin { get; private set; }
        public readonly List<Vector2Int> SpawnCells = new List<Vector2Int>();

        /// <summary>Raised whenever buildings are placed or removed (flow field, minimap listen to this).</summary>
        public event System.Action Changed;

        System.Random rng;

        public static Vector3 CellCenter(Vector2Int c) => new Vector3(c.x + 0.5f, 0, c.y + 0.5f);
        public static Vector2Int WorldToCell(Vector3 p) => new Vector2Int(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.z));
        public static bool InBounds(Vector2Int c) => c.x >= 0 && c.y >= 0 && c.x < Width && c.y < Height;

        public bool IsFloor(Vector2Int c) => InBounds(c) && Tiles[c.x, c.y] == Tile.Floor;
        public Building GetBuilding(Vector2Int c) => InBounds(c) ? Buildings[c.x, c.y] : null;
        public int OreAt(Vector2Int c) => InBounds(c) ? Ore[c.x, c.y] : -1;

        public void SetBuilding(Building b, bool present)
        {
            foreach (var c in b.Cells())
                Buildings[c.x, c.y] = present ? b : null;
            Changed?.Invoke();
        }

        // ---------------------------------------------------------------- generation

        public void Generate(int seed)
        {
            rng = new System.Random(seed);
            Tiles = new Tile[Width, Height];
            Ore = new sbyte[Width, Height];
            Buildings = new Building[Width, Height];

            for (int x = 0; x < Width; x++)
                for (int y = 0; y < Height; y++)
                {
                    bool border = x < 2 || y < 2 || x >= Width - 2 || y >= Height - 2;
                    Tiles[x, y] = border || rng.NextDouble() < 0.46 ? Tile.Rock : Tile.Floor;
                    Ore[x, y] = -1;
                }

            for (int i = 0; i < 5; i++) Smooth();

            CoreOrigin = new Vector2Int(42, 29);
            var coreCenter = CoreOrigin + Vector2Int.one;
            CarveCircle(coreCenter, 7.5f);

            SpawnCells.Clear();
            SpawnCells.Add(new Vector2Int(1, 16));
            SpawnCells.Add(new Vector2Int(1, 44));
            SpawnCells.Add(new Vector2Int(20, Height - 2));
            foreach (var s in SpawnCells)
            {
                CarveCircle(s, 2.5f);
                CarveTunnel(s, coreCenter);
            }
            // A couple of extra side caves for ore.
            CarveTunnel(new Vector2Int(54, 54), coreCenter);
            CarveTunnel(new Vector2Int(50, 6), coreCenter);

            RemoveUnreachable(coreCenter);
            PlaceLava();
            PlaceOre(coreCenter);
        }

        void Smooth()
        {
            var next = (Tile[,])Tiles.Clone();
            for (int x = 2; x < Width - 2; x++)
                for (int y = 2; y < Height - 2; y++)
                {
                    int rock = 0;
                    for (int dx = -1; dx <= 1; dx++)
                        for (int dy = -1; dy <= 1; dy++)
                            if ((dx != 0 || dy != 0) && Tiles[x + dx, y + dy] != Tile.Floor) rock++;
                    if (rock >= 5) next[x, y] = Tile.Rock;
                    else if (rock <= 3) next[x, y] = Tile.Floor;
                }
            Tiles = next;
        }

        void CarveCircle(Vector2Int c, float r)
        {
            int ri = Mathf.CeilToInt(r);
            for (int dx = -ri; dx <= ri; dx++)
                for (int dy = -ri; dy <= ri; dy++)
                {
                    var p = new Vector2Int(c.x + dx, c.y + dy);
                    if (dx * dx + dy * dy <= r * r && p.x >= 1 && p.y >= 1 && p.x < Width - 1 && p.y < Height - 1)
                        Tiles[p.x, p.y] = Tile.Floor;
                }
        }

        void CarveTunnel(Vector2Int from, Vector2Int to)
        {
            Vector2 p = from;
            int guard = 0;
            while (Vector2.Distance(p, to) > 3f && guard++ < 2000)
            {
                Vector2 dir = ((Vector2)to - p).normalized;
                float angle = ((float)rng.NextDouble() - 0.5f) * 140f;
                dir = Quaternion.Euler(0, 0, angle) * dir;
                p += dir;
                CarveCircle(Vector2Int.RoundToInt(p), 1.2f + (float)rng.NextDouble() * 0.9f);
            }
        }

        void RemoveUnreachable(Vector2Int start)
        {
            var seen = new bool[Width, Height];
            var q = new Queue<Vector2Int>();
            q.Enqueue(start);
            seen[start.x, start.y] = true;
            while (q.Count > 0)
            {
                var c = q.Dequeue();
                for (int d = 0; d < 4; d++)
                {
                    var n = c + ((Dir)d).Offset();
                    if (IsFloor(n) && !seen[n.x, n.y]) { seen[n.x, n.y] = true; q.Enqueue(n); }
                }
            }
            for (int x = 0; x < Width; x++)
                for (int y = 0; y < Height; y++)
                    if (!seen[x, y]) Tiles[x, y] = Tile.Rock;
        }

        void PlaceLava()
        {
            // Lava pools sit inside solid rock masses so they never block paths.
            int pools = 0;
            for (int tries = 0; tries < 400 && pools < 7; tries++)
            {
                var c = new Vector2Int(rng.Next(3, Width - 3), rng.Next(3, Height - 3));
                if (DistanceToFloor(c, 2) < 2) continue;
                pools++;
                int r = rng.Next(1, 3);
                for (int dx = -r; dx <= r; dx++)
                    for (int dy = -r; dy <= r; dy++)
                    {
                        var p = new Vector2Int(c.x + dx, c.y + dy);
                        if (InBounds(p) && Tiles[p.x, p.y] == Tile.Rock && rng.NextDouble() < 0.8 && DistanceToFloor(p, 1) >= 1)
                            Tiles[p.x, p.y] = Tile.Lava;
                    }
            }
        }

        int DistanceToFloor(Vector2Int c, int max)
        {
            for (int r = 0; r <= max; r++)
                for (int dx = -r; dx <= r; dx++)
                    for (int dy = -r; dy <= r; dy++)
                        if (IsFloor(new Vector2Int(c.x + dx, c.y + dy))) return r;
            return max + 1;
        }

        void PlaceOre(Vector2Int core)
        {
            // (type, patches, minDist, maxDist)
            PlacePatches(ResourceType.Gold, 1, 5, 8, core);
            PlacePatches(ResourceType.Gold, 3, 8, 22, core);
            PlacePatches(ResourceType.Iron, 1, 6, 10, core);
            PlacePatches(ResourceType.Iron, 2, 10, 24, core);
            PlacePatches(ResourceType.Crystal, 3, 10, 30, core);
            PlacePatches(ResourceType.Amethyst, 3, 15, 40, core);
        }

        void PlacePatches(ResourceType type, int count, float minDist, float maxDist, Vector2Int core)
        {
            for (int placed = 0, tries = 0; placed < count && tries < 500; tries++)
            {
                var c = new Vector2Int(rng.Next(2, Width - 2), rng.Next(2, Height - 2));
                float d = Vector2Int.Distance(c, core);
                if (!IsFloor(c) || d < minDist || d > maxDist || Ore[c.x, c.y] >= 0) continue;
                bool nearSpawn = false;
                foreach (var s in SpawnCells) if (Vector2Int.Distance(s, c) < 8) nearSpawn = true;
                if (nearSpawn) continue;

                placed++;
                float r = 1.4f + (float)rng.NextDouble() * 1.0f;
                for (int dx = -3; dx <= 3; dx++)
                    for (int dy = -3; dy <= 3; dy++)
                    {
                        var p = new Vector2Int(c.x + dx, c.y + dy);
                        if (IsFloor(p) && dx * dx + dy * dy <= r * r && rng.NextDouble() < 0.9
                            && Vector2Int.Distance(p, core) > 3.5f)
                            Ore[p.x, p.y] = (sbyte)type;
                    }
            }
        }

        // ---------------------------------------------------------------- visuals

        public void BuildVisuals()
        {
            var vrng = new System.Random(1234);
            var mb = new MeshBuilder();
            var floorMats = new[] { Art.FloorA, Art.FloorB, Art.FloorC };

            var rockDist = RockDepth();
            for (int x = -OuterRing; x < Width + OuterRing; x++)
                for (int y = -OuterRing; y < Height + OuterRing; y++)
                {
                    var c = new Vector2Int(x, y);
                    var t = InBounds(c) ? Tiles[x, y] : Tile.Rock;
                    float jitter = (float)vrng.NextDouble();
                    var center = new Vector3(x + 0.5f, 0, y + 0.5f);

                    if (t == Tile.Floor)
                    {
                        float h = jitter * 0.05f;
                        bool spawnZone = false;
                        foreach (var s in SpawnCells) if (Vector2Int.Distance(s, c) < 4.5f) spawnZone = true;
                        var top = spawnZone && jitter < 0.7f ? Art.Moss : floorMats[(x * 7 + y * 13 + (int)(jitter * 3)) % 3];
                        mb.Box(center + Vector3.up * ((h - 0.5f) * 0.5f), new Vector3(1, 0.5f + h, 1), Art.RockDark, top);
                        int ore = Ore[x, y];
                        if (ore >= 0) AddOreDeposit(mb, center + Vector3.up * h, (ResourceType)ore, vrng);
                        else if (jitter > 0.93f) mb.Box(center + new Vector3(jitter - 0.9f, 0.05f, 0.2f), new Vector3(0.18f, 0.12f, 0.15f), Art.RockTop, null, Quaternion.Euler(0, jitter * 360, 0));
                    }
                    else if (t == Tile.Lava)
                    {
                        mb.Box(center + Vector3.up * 0.1f, new Vector3(1, 0.4f, 1), Art.Lava);
                    }
                    else
                    {
                        int depth = InBounds(c) ? rockDist[x, y] : 6;
                        float h = 0.9f + Mathf.Min(depth, 5) * 0.45f + jitter * 0.6f;
                        float inset = depth <= 1 ? 0.04f + jitter * 0.06f : 0f;
                        var top = depth >= 2 && jitter > 0.92f ? Art.LavaCrack : Art.RockTop;
                        mb.Box(center + Vector3.up * ((h - 0.5f) * 0.5f), new Vector3(1 - inset, h + 0.5f, 1 - inset), Art.RockSide, top);
                        // Chunky voxel rubble on cliff edges.
                        if (depth == 1 && jitter < 0.35f)
                            mb.Box(center + new Vector3(jitter - 0.17f, h + 0.12f, 0.1f), new Vector3(0.45f, 0.3f, 0.4f), Art.RockSide, Art.RockTop, Quaternion.Euler(0, jitter * 200, 0));
                    }
                }

            var go = new GameObject("Terrain");
            go.transform.SetParent(transform, false);
            mb.Attach(go, "Terrain");

            AddLights();
        }

        void AddOreDeposit(MeshBuilder mb, Vector3 top, ResourceType type, System.Random r)
        {
            var mat = Art.Resource[(int)type];
            int n = 2 + r.Next(3);
            for (int i = 0; i < n; i++)
            {
                var off = new Vector3((float)r.NextDouble() - 0.5f, 0, (float)r.NextDouble() - 0.5f) * 0.7f;
                var rot = Quaternion.Euler((float)r.NextDouble() * 30 - 15, (float)r.NextDouble() * 360, (float)r.NextDouble() * 30 - 15);
                bool crystal = type == ResourceType.Crystal || type == ResourceType.Amethyst;
                var size = crystal
                    ? new Vector3(0.14f, 0.3f + (float)r.NextDouble() * 0.3f, 0.14f)
                    : Vector3.one * (0.14f + (float)r.NextDouble() * 0.14f);
                mb.Box(top + off + Vector3.up * size.y * 0.3f, size, mat, null, rot);
            }
        }

        int[,] RockDepth()
        {
            var d = new int[Width, Height];
            var q = new Queue<Vector2Int>();
            for (int x = 0; x < Width; x++)
                for (int y = 0; y < Height; y++)
                {
                    d[x, y] = Tiles[x, y] == Tile.Floor ? 0 : int.MaxValue;
                    if (d[x, y] == 0) q.Enqueue(new Vector2Int(x, y));
                }
            while (q.Count > 0)
            {
                var c = q.Dequeue();
                for (int i = 0; i < 4; i++)
                {
                    var n = c + ((Dir)i).Offset();
                    if (InBounds(n) && d[n.x, n.y] > d[c.x, c.y] + 1) { d[n.x, n.y] = d[c.x, c.y] + 1; q.Enqueue(n); }
                }
            }
            return d;
        }

        void AddLights()
        {
            // One light per lava pool cluster (approximate by sampling).
            var lit = new List<Vector2Int>();
            for (int x = 0; x < Width; x++)
                for (int y = 0; y < Height; y++)
                {
                    if (Tiles[x, y] != Tile.Lava) continue;
                    var c = new Vector2Int(x, y);
                    bool close = false;
                    foreach (var l in lit) if (Vector2Int.Distance(l, c) < 5) close = true;
                    if (close) continue;
                    lit.Add(c);
                    MakeLight(CellCenter(c) + Vector3.up * 1.5f, new Color(1f, 0.45f, 0.1f), 7f, 3f);
                }
            foreach (var s in SpawnCells)
                MakeLight(CellCenter(s) + new Vector3(2, 1.5f, 0), new Color(0.6f, 1f, 0.2f), 7f, 2f);
        }

        void MakeLight(Vector3 pos, Color color, float range, float intensity)
        {
            var go = new GameObject("Light");
            go.transform.SetParent(transform, false);
            go.transform.position = pos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = color;
            l.range = range;
            l.intensity = intensity;
            l.shadows = LightShadows.None;
        }
    }
}
