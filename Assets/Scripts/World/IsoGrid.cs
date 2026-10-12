using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace DungeonConveyor
{
    /// <summary>
    /// The isometric dungeon grid. Cells (0,0)..(Width-1, Height-1) are painted on the Ground tilemap in the editor
    /// (Dungeon > Build Isometric Grid); at runtime this cuts a random map shape, generates elevation, places the
    /// base, ore and enemy spawn bridges (IsoGrid.Spawns.cs), and MapView/TileHover draw it.
    /// </summary>
    [RequireComponent(typeof(Grid))]
    public partial class IsoGrid : MonoBehaviour
    {
        public const float StepHeight = 0.25f; // world units per elevation level
        public const int MinLevel = -3, MaxLevel = 6;
        public const int BaseSize = 4;
        public const int MaxClimb = 1;          // enemies can step up or down this many levels

        public int Width = 50, Height = 50;
        public Tilemap Ground;
        [Tooltip("Roguelike run: map size, bridge and Core counts come from the current stage (see Run), and every " +
                 "cell up to that size exists (the painted Ground tilemap is ignored). Off = the painted grid with " +
                 "the settings below and one Core.")]
        public bool RoguelikeRun = true;
        [Tooltip("Number of Cores (bases) when not in a roguelike run.")]
        public int CoreCount = 1;
        [Tooltip("Base centre is placed this many cells from the map centre (min..max).")]
        public Vector2 BaseDistance = new Vector2(3f, 8f);
        [Tooltip("Preferred minimum distance in cells between the base centre and each enemy spawn.")]
        public float SpawnMinDistance = 20f;
        [Tooltip("Number of enemy spawn bridges (min..max, picked at random).")]
        public Vector2Int SpawnCount = new Vector2Int(1, 4);

        [Header("Map shape")]
        [Tooltip("Cut the painted grid down to a random shape every Play.")]
        public bool RandomShape = true;
        [Tooltip("Retry the shape if fewer than this fraction of the grid survives.")]
        [Range(0.2f, 0.9f)] public float ShapeMinCoverage = 0.45f;
        [Tooltip("Cells within this radius of the map centre are never cut (room for the base).")]
        public float ShapeSolidCenter = 9f;

        [Header("Void")]
        [Tooltip("Fill all the empty space the camera can see with rocks (otherwise just a wall around the map). Can be toggled during Play.")]
        public bool FillVoidWithRocks = true;

        [Header("Terrain")]
        [Tooltip("Noise frequency: smaller = broader hills and valleys.")]
        public float NoiseScale = 0.07f;
        [Range(1, 6)] public int Octaves = 4;
        [Tooltip(">1 favours lowlands with fewer, steeper peaks.")]
        public float HeightCurve = 1.4f;
        [Tooltip("Level of the flat plateau the base sits on.")]
        public int BaseLevel = 1;
        [Tooltip("Terrain is flat within the first value of the base and blends back to noise by the second.")]
        public Vector2 BaseFlatten = new Vector2(3.5f, 8f);

        [Header("Ore")]
        public Vector2Int OrePatches = new Vector2Int(5, 8); // min..max count
        [Tooltip("Ore patches keep at least this distance from the base centre.")]
        public float OreMinBaseDistance = 6f;

        Grid grid;
        public Grid Grid => grid != null ? grid : grid = GetComponent<Grid>();

        public bool[,] Exists { get; private set; }
        public int[,] Heights { get; private set; }
        /// <summary>Bottom-left cell of each 4x4 base, one per Core. The first is the main base.</summary>
        public readonly List<Vector2Int> BaseOrigins = new List<Vector2Int>();
        public Vector2Int BaseOrigin => BaseOrigins[0];
        public bool[,] Ore { get; private set; }
        public Building[,] Buildings { get; private set; }

        public bool InBounds(Vector2Int c) => c.x >= 0 && c.y >= 0 && c.x < Width && c.y < Height;
        public bool HasCell(Vector2Int c) => InBounds(c) && Exists[c.x, c.y];
        public bool IsOre(Vector2Int c) => InBounds(c) && Ore[c.x, c.y];
        public Building BuildingAt(Vector2Int c) => InBounds(c) ? Buildings[c.x, c.y] : null;

        /// <summary>Enemy movement rule: 4-directional steps, climbing or dropping at most MaxClimb levels.</summary>
        public bool CanStep(Vector2Int from, Vector2Int to) =>
            HasCell(from) && HasCell(to) && Mathf.Abs(Heights[to.x, to.y] - Heights[from.x, from.y]) <= MaxClimb;
        public bool IsBase(Vector2Int c) => BaseIndexAt(c) >= 0;
        /// <summary>Which base covers this cell, or -1.</summary>
        public int BaseIndexAt(Vector2Int c)
        {
            for (int i = 0; i < BaseOrigins.Count; i++)
                if (InBase(c, i)) return i;
            return -1;
        }
        public bool InBase(Vector2Int c, int i)
        {
            var o = BaseOrigins[i];
            return c.x >= o.x && c.y >= o.y && c.x < o.x + BaseSize && c.y < o.y + BaseSize;
        }
        public Vector2 BaseCenter => BaseCenterOf(0);
        public Vector2 BaseCenterOf(int i) => (Vector2)BaseOrigins[i] + Vector2.one * (BaseSize * 0.5f);
        /// <summary>Distance from a grid point to the nearest base centre.</summary>
        public float DistanceToBase(Vector2 p)
        {
            float best = float.MaxValue;
            for (int i = 0; i < BaseOrigins.Count; i++) best = Mathf.Min(best, Vector2.Distance(p, BaseCenterOf(i)));
            return best;
        }
        /// <summary>The Core enemies from this bridge attack: bridges are shared out between the Cores in turn.</summary>
        public int CoreFor(Bridge b) => Mathf.Max(0, Bridges.IndexOf(b)) % Mathf.Max(1, BaseOrigins.Count);

        public Vector3 CellCenter(Vector2Int c) => Grid.GetCellCenterWorld((Vector3Int)c);
        /// <summary>World position of a fractional grid position (cell (x, y) centred at (x, y)) at an elevation in levels.</summary>
        public Vector3 GridToWorld(Vector2 g, float level = 0f)
        {
            var origin = CellCenter(Vector2Int.zero);
            var xStep = CellCenter(Vector2Int.right) - origin;
            var yStep = CellCenter(Vector2Int.up) - origin;
            return origin + xStep * g.x + yStep * g.y + Vector3.up * (level * StepHeight);
        }
        public float HeightAt(Vector2Int c) => HasCell(c) ? Heights[c.x, c.y] : 0f;
        /// <summary>Centre of the cell's top face, including elevation.</summary>
        public Vector3 SurfaceCenter(Vector2Int c) => CellCenter(c) + Vector3.up * (Heights[c.x, c.y] * StepHeight);
        public Vector2Int WorldToCell(Vector3 world) => (Vector2Int)Grid.WorldToCell(new Vector3(world.x, world.y, 0f));

        /// <summary>
        /// Painter's order: cells nearer the bottom of the screen (smaller x+y) draw on top. Spaced by 4 so things
        /// standing on a cell (buildings: +1) sort above its terrain but below the next row.
        /// </summary>
        public int SortOrder(Vector2Int c) => (Width + Height - (c.x + c.y)) * 4;

        /// <summary>World-space box around the whole diamond-shaped map.</summary>
        public Bounds WorldBounds
        {
            get
            {
                var b = new Bounds(Grid.CellToWorld(Vector3Int.zero), Vector3.zero);
                b.Encapsulate(Grid.CellToWorld(new Vector3Int(Width, 0, 0)));
                b.Encapsulate(Grid.CellToWorld(new Vector3Int(0, Height, 0)));
                b.Encapsulate(Grid.CellToWorld(new Vector3Int(Width, Height, 0)));
                return b;
            }
        }

        void Awake()
        {
            if (RoguelikeRun)
            {
                var stage = Run.Current;
                Width = Height = stage.Size;
                SpawnCount = new Vector2Int(stage.Bridges, stage.Bridges);
                CoreCount = stage.Cores;
            }
            Exists = new bool[Width, Height];
            Heights = new int[Width, Height];
            for (int x = 0; x < Width; x++)
                for (int y = 0; y < Height; y++)
                    Exists[x, y] = RoguelikeRun || (Ground != null && Ground.HasTile(new Vector3Int(x, y, 0)));

            Ore = new bool[Width, Height];
            Buildings = new Building[Width, Height];

            if (RandomShape) CutShape();
            PlaceBases();
            GenerateTerrain();
            for (int i = 1; i < BaseOrigins.Count; i++) EnsurePathToBase(BesideBase(i), 0); // Cores linked up
            PlaceSpawns();
            PlaceOres();
            // Each bridge must reach the Core its enemies attack. Grading one route can disturb another, so recheck.
            for (int pass = 0; pass < 3; pass++)
                foreach (var bridge in Bridges) EnsurePathToBase(bridge.Landing, CoreFor(bridge));

            if (GetComponent<MapView>() == null) gameObject.AddComponent<MapView>();
            if (GetComponent<TileHover>() == null) gameObject.AddComponent<TileHover>();
            if (GetComponent<BuildController>() == null) gameObject.AddComponent<BuildController>();
            if (GetComponent<HUD>() == null) gameObject.AddComponent<HUD>();
            if (GetComponent<WorldDecor>() == null) gameObject.AddComponent<WorldDecor>();
            if (GetComponent<ItemFlow>() == null) gameObject.AddComponent<ItemFlow>();
            if (GetComponent<WaveManager>() == null) gameObject.AddComponent<WaveManager>();
            if (GetComponent<Combat>() == null) gameObject.AddComponent<Combat>();
            if (GetComponent<EnemyPathView>() == null) gameObject.AddComponent<EnemyPathView>();
        }

        // ---------------------------------------------------------------- terrain

        /// <summary>
        /// Rolling hills from layered Perlin noise, cleaned of one-cell spikes and pits,
        /// with a flat plateau under the base that blends smoothly into the surroundings.
        /// </summary>
        void GenerateTerrain()
        {
            var offset = new Vector2(Random.Range(-10000f, 10000f), Random.Range(-10000f, 10000f));
            var noise = new float[Width, Height];
            float min = float.MaxValue, max = float.MinValue;
            for (int x = 0; x < Width; x++)
                for (int y = 0; y < Height; y++)
                {
                    float n = Fbm(x * NoiseScale + offset.x, y * NoiseScale + offset.y);
                    noise[x, y] = n;
                    min = Mathf.Min(min, n);
                    max = Mathf.Max(max, n);
                }

            for (int x = 0; x < Width; x++)
                for (int y = 0; y < Height; y++)
                {
                    float n = Mathf.Pow(Mathf.InverseLerp(min, max, noise[x, y]), HeightCurve);
                    float h = Mathf.Lerp(MinLevel, MaxLevel, n);
                    float d = DistanceToBase(new Vector2(x + 0.5f, y + 0.5f));
                    h = Mathf.Lerp(BaseLevel, h, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(BaseFlatten.x, BaseFlatten.y, d)));
                    Heights[x, y] = Mathf.Clamp(Mathf.RoundToInt(h), MinLevel, MaxLevel);
                }

            for (int i = 0; i < 2; i++) RemoveSpikes();
            foreach (var o in BaseOrigins)
                for (int x = 0; x < BaseSize; x++)
                    for (int y = 0; y < BaseSize; y++)
                        Heights[o.x + x, o.y + y] = BaseLevel;
        }

        /// <summary>A map cell right next to base i (left of it, or failing that any side).</summary>
        Vector2Int BesideBase(int i)
        {
            var o = BaseOrigins[i];
            for (int k = 0; k < BaseSize; k++)
            {
                Vector2Int[] ring = { o + new Vector2Int(-1, k), o + new Vector2Int(k, -1), o + new Vector2Int(BaseSize, k), o + new Vector2Int(k, BaseSize) };
                foreach (var c in ring)
                    if (HasCell(c) && !IsBase(c)) return c;
            }
            return o;
        }

        float Fbm(float x, float y)
        {
            float sum = 0f, amp = 1f, freq = 1f;
            for (int o = 0; o < Octaves; o++)
            {
                sum += Mathf.PerlinNoise(x * freq, y * freq) * amp;
                amp *= 0.5f;
                freq *= 2f;
            }
            return sum;
        }

        /// <summary>A cell sticking up above (or sunk below) all four neighbours is pulled to the nearest one.</summary>
        void RemoveSpikes()
        {
            var next = (int[,])Heights.Clone();
            for (int x = 0; x < Width; x++)
                for (int y = 0; y < Height; y++)
                {
                    int h = Heights[x, y], lo = int.MaxValue, hi = int.MinValue;
                    foreach (var n in Neighbours)
                    {
                        var c = new Vector2Int(x, y) + n;
                        if (!InBounds(c)) continue;
                        lo = Mathf.Min(lo, Heights[c.x, c.y]);
                        hi = Mathf.Max(hi, Heights[c.x, c.y]);
                    }
                    if (h > hi) next[x, y] = hi;
                    else if (h < lo) next[x, y] = lo;
                }
            Heights = next;
        }

        static readonly Vector2Int[] Neighbours = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };

        /// <summary>
        /// The main base near the middle of the map; any further bases out toward the edges, well apart from each
        /// other, with room around them.
        /// </summary>
        void PlaceBases()
        {
            BaseOrigins.Clear();
            var mapCenter = new Vector2(Width, Height) * 0.5f;
            float half = Mathf.Min(Width, Height) * 0.5f;
            var main = Vector2Int.FloorToInt(mapCenter) - Vector2Int.one * (BaseSize / 2);
            var near = new Vector2(Mathf.Min(BaseDistance.x, half * 0.15f), Mathf.Min(BaseDistance.y, half * 0.3f));
            for (int tries = 0; tries < 2000; tries++)
            {
                var o = new Vector2Int(Random.Range(0, Width - BaseSize + 1), Random.Range(0, Height - BaseSize + 1));
                float d = Vector2.Distance(CenterOf(o), mapCenter);
                if (d >= near.x && d <= near.y && BaseFits(o)) { main = o; break; }
            }
            BaseOrigins.Add(main);

            for (int i = 1; i < CoreCount; i++)
            {
                float apart = half * 0.8f;
                for (int pass = 0; pass < 4 && BaseOrigins.Count <= i; pass++, apart *= 0.8f)
                    for (int tries = 0; tries < 1000; tries++)
                    {
                        var o = new Vector2Int(Random.Range(2, Width - BaseSize - 1), Random.Range(2, Height - BaseSize - 1));
                        float d = Vector2.Distance(CenterOf(o), mapCenter);
                        if (d < half * 0.35f || d > half * 0.7f || !BaseFits(o)) continue;
                        bool far = true;
                        foreach (var other in BaseOrigins)
                            if (Vector2.Distance(CenterOf(o), CenterOf(other)) < apart) far = false;
                        if (!far) continue;
                        BaseOrigins.Add(o);
                        break;
                    }
            }
            if (BaseOrigins.Count < CoreCount) Debug.LogWarning($"IsoGrid: room for only {BaseOrigins.Count} of {CoreCount} Cores.");
        }

        static Vector2 CenterOf(Vector2Int origin) => (Vector2)origin + Vector2.one * (BaseSize * 0.5f);

        /// <summary>The footprint plus a one-cell border all exists.</summary>
        bool BaseFits(Vector2Int origin)
        {
            for (int x = -1; x <= BaseSize; x++)
                for (int y = -1; y <= BaseSize; y++)
                    if (!HasCell(origin + new Vector2Int(x, y))) return false;
            return true;
        }

        // ---------------------------------------------------------------- map shape

        /// <summary>
        /// Cuts the full grid down to a random organic shape: a noisy blob (anywhere from round to squarish),
        /// a few bites taken out of its edge and a few chasms inside, smoothed, and reduced to the single
        /// piece connected to the centre. The middle is always kept so the base has room.
        /// </summary>
        void CutShape()
        {
            var painted = (bool[,])Exists.Clone();
            for (int attempt = 0; attempt < 10; attempt++)
            {
                var keep = MakeShape();
                int count = 0;
                for (int x = 0; x < Width; x++)
                    for (int y = 0; y < Height; y++)
                    {
                        Exists[x, y] = painted[x, y] && keep[x, y];
                        if (Exists[x, y]) count++;
                    }
                if (count >= Width * Height * ShapeMinCoverage) return;
            }
        }

        bool[,] MakeShape()
        {
            var center = new Vector2(Width, Height) * 0.5f;
            float halfSize = Mathf.Min(Width, Height) * 0.5f;
            float radius = Random.Range(0.72f, 0.95f);       // how much of the square the blob reaches
            float wobble = Random.Range(0.35f, 0.75f);       // how ragged the outline is
            float squareness = Random.Range(0f, 1f);         // 0 = round, 1 = square
            float scale = Random.Range(0.06f, 0.11f);
            var offset = new Vector2(Random.Range(-10000f, 10000f), Random.Range(-10000f, 10000f));

            var keep = new bool[Width, Height];
            for (int x = 0; x < Width; x++)
                for (int y = 0; y < Height; y++)
                {
                    var p = (new Vector2(x + 0.5f, y + 0.5f) - center) / halfSize;
                    float d = Mathf.Lerp(p.magnitude, Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.y)), squareness);
                    float n = Mathf.PerlinNoise(x * scale + offset.x, y * scale + offset.y) - 0.5f;
                    keep[x, y] = d < radius + n * wobble;
                }

            // Bites out of the outline: circles centred near the edge of the square.
            int bites = Random.Range(1, 5);
            for (int i = 0; i < bites; i++)
            {
                float angle = Random.Range(0f, Mathf.PI * 2f);
                var at = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * halfSize * Random.Range(0.85f, 1.1f);
                Carve(keep, at, Random.Range(5f, 11f));
            }

            // Chasms inside the map, kept well away from the middle.
            int holes = Random.Range(0, 4);
            for (int i = 0; i < holes; i++)
            {
                float angle = Random.Range(0f, Mathf.PI * 2f);
                var at = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * halfSize * Random.Range(0.45f, 0.7f);
                Carve(keep, at, Random.Range(1.5f, 3.5f));
            }

            for (int i = 0; i < 2; i++) keep = SmoothShape(keep);

            // The base area always survives.
            for (int x = 0; x < Width; x++)
                for (int y = 0; y < Height; y++)
                    if (Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center) < Mathf.Min(ShapeSolidCenter, halfSize * 0.45f)) keep[x, y] = true;

            return KeepConnected(keep, Vector2Int.FloorToInt(center));
        }

        void Carve(bool[,] keep, Vector2 at, float radius)
        {
            for (int x = 0; x < Width; x++)
                for (int y = 0; y < Height; y++)
                    if (Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), at) < radius) keep[x, y] = false;
        }

        /// <summary>Majority vote over the 8 neighbours: removes single-cell spurs and fills pinholes.</summary>
        bool[,] SmoothShape(bool[,] keep)
        {
            var next = new bool[Width, Height];
            for (int x = 0; x < Width; x++)
                for (int y = 0; y < Height; y++)
                {
                    int solid = 0;
                    for (int dx = -1; dx <= 1; dx++)
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if ((dx != 0 || dy != 0) && nx >= 0 && ny >= 0 && nx < Width && ny < Height && keep[nx, ny]) solid++;
                        }
                    next[x, y] = solid > 4 || (solid == 4 && keep[x, y]);
                }
            return next;
        }

        /// <summary>Drops every cell not 4-connected to start, so there are no floating islands.</summary>
        bool[,] KeepConnected(bool[,] keep, Vector2Int start)
        {
            var result = new bool[Width, Height];
            var queue = new Queue<Vector2Int>();
            queue.Enqueue(start);
            result[start.x, start.y] = true;
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                foreach (var d in Neighbours)
                {
                    var n = c + d;
                    if (InBounds(n) && keep[n.x, n.y] && !result[n.x, n.y])
                    {
                        result[n.x, n.y] = true;
                        queue.Enqueue(n);
                    }
                }
            }
            return result;
        }

        // ---------------------------------------------------------------- ore

        /// <summary>A deposit of one ore type; it runs dry once Remaining crystals have been mined.</summary>
        public class OrePatch
        {
            public int Kind;
            public int Initial, Remaining;
            public readonly List<Vector2Int> Cells = new List<Vector2Int>();
        }

        public readonly List<OrePatch> Patches = new List<OrePatch>();
        public OrePatch[,] PatchAt { get; private set; }
        /// <summary>Raised when a patch is mined out; its cells are no longer ore.</summary>
        public event System.Action<OrePatch> PatchDepleted;

        public static IReadOnlyList<OreType> OreTypes => ArtLibrary.Instance != null && ArtLibrary.Instance.Ores.Count > 0
            ? ArtLibrary.Instance.Ores : FallbackOres;
        static readonly List<OreType> FallbackOres = new List<OreType> { new OreType() };
        public static OreType OreTypeOf(int kind) => OreTypes[Mathf.Clamp(kind, 0, OreTypes.Count - 1)];

        /// <summary>Takes one crystal from the patch; false if it is empty.</summary>
        public bool MineFrom(OrePatch patch)
        {
            if (patch == null || patch.Remaining <= 0) return false;
            if (--patch.Remaining == 0)
            {
                foreach (var c in patch.Cells) Ore[c.x, c.y] = false;
                PatchDepleted?.Invoke(patch);
            }
            return true;
        }

        /// <summary>
        /// A handful of square 1x1/2x2/3x3 ore patches, each flattened to its average height. Each patch is one ore
        /// type picked by weight among those allowed at its distance (so rich ores lie further out) and holds a random
        /// amount per tile. The first patch is always the most common ore, close to the base, to get started.
        /// </summary>
        void PlaceOres()
        {
            Patches.Clear();
            PatchAt = new OrePatch[Width, Height];
            // OrePatches is for a 50x50 map; scale it with the area, at least 3.
            float area = Width * Height / 2500f;
            int target = Mathf.Max(3, Mathf.RoundToInt(Random.Range(OrePatches.x, OrePatches.y + 1) * area));
            float minDistance = Mathf.Min(OreMinBaseDistance, Mathf.Min(Width, Height) * 0.2f);
            for (int placed = 0, tries = 0; placed < target && tries < 4000; tries++)
            {
                int size = Random.Range(1, 4);
                var o = new Vector2Int(Random.Range(0, Width - size + 1), Random.Range(0, Height - size + 1));
                var center = (Vector2)o + Vector2.one * (size * 0.5f);
                float distance = DistanceToBase(center);
                if (distance < minDistance || (placed == 0 && (distance > minDistance + 6f || size < 2))) continue;
                if (!OreFits(o, size) || NearSpawn(center, 4f)) continue;

                var patch = new OrePatch { Kind = placed == 0 ? 0 : PickOre(distance) };
                var type = OreTypeOf(patch.Kind);
                int sum = 0;
                for (int x = 0; x < size; x++)
                    for (int y = 0; y < size; y++)
                        sum += Heights[o.x + x, o.y + y];
                int level = Mathf.RoundToInt(sum / (float)(size * size));
                for (int x = 0; x < size; x++)
                    for (int y = 0; y < size; y++)
                    {
                        var c = new Vector2Int(o.x + x, o.y + y);
                        Ore[c.x, c.y] = true;
                        Heights[c.x, c.y] = level;
                        PatchAt[c.x, c.y] = patch;
                        patch.Cells.Add(c);
                        patch.Initial += Random.Range(type.AmountPerTile.x, type.AmountPerTile.y + 1);
                    }
                patch.Remaining = patch.Initial;
                Patches.Add(patch);
                placed++;
            }
        }

        static int PickOre(float distance)
        {
            float total = 0f;
            for (int i = 0; i < OreTypes.Count; i++)
                if (distance >= OreTypes[i].MinBaseDistance) total += OreTypes[i].Weight;
            float roll = Random.value * total;
            for (int i = 0; i < OreTypes.Count; i++)
            {
                if (distance < OreTypes[i].MinBaseDistance) continue;
                roll -= OreTypes[i].Weight;
                if (roll <= 0f) return i;
            }
            return 0;
        }

        /// <summary>All cells exist and are free, with a one-cell gap to other patches, the base and the spawns.</summary>
        bool OreFits(Vector2Int o, int size)
        {
            for (int x = -1; x <= size; x++)
                for (int y = -1; y <= size; y++)
                {
                    var c = o + new Vector2Int(x, y);
                    bool inside = x >= 0 && y >= 0 && x < size && y < size;
                    if (inside && !HasCell(c)) return false;
                    if (IsOre(c) || IsBase(c) || IsSpawn(c)) return false;
                }
            return true;
        }

        // ---------------------------------------------------------------- reachability

        /// <summary>
        /// Guarantees enemies can walk from this cell to the base. Finds the route needing the least earthwork
        /// (steps steeper than MaxClimb are expensive, ore is avoided) to the area already reachable from the
        /// base, and grades it into a ramp working outward. Only cells outside that area change, so routes made
        /// for earlier spawns stay intact.
        /// </summary>
        void EnsurePathToBase(Vector2Int start, int onlyBase = -1)
        {
            var reachable = ReachableFromBase(onlyBase);
            if (!HasCell(start) || reachable[start.x, start.y]) return;

            var cost = new int[Width, Height];
            var from = new Vector2Int[Width, Height];
            var done = new bool[Width, Height];
            var open = new List<Vector2Int>();
            for (int x = 0; x < Width; x++)
                for (int y = 0; y < Height; y++)
                {
                    cost[x, y] = int.MaxValue;
                    if (reachable[x, y])
                    {
                        cost[x, y] = 0;
                        open.Add(new Vector2Int(x, y));
                    }
                }

            // Dijkstra from the reachable area; the grid is small enough for a linear scan of the open list.
            while (open.Count > 0)
            {
                int best = 0;
                for (int i = 1; i < open.Count; i++)
                    if (cost[open[i].x, open[i].y] < cost[open[best].x, open[best].y]) best = i;
                var c = open[best];
                open[best] = open[open.Count - 1];
                open.RemoveAt(open.Count - 1);
                if (done[c.x, c.y]) continue;
                done[c.x, c.y] = true;
                if (c == start) break;

                foreach (var d in Neighbours)
                {
                    var n = c + d;
                    if (!HasCell(n) || done[n.x, n.y]) continue;
                    int excess = Mathf.Max(0, Mathf.Abs(Heights[n.x, n.y] - Heights[c.x, c.y]) - MaxClimb);
                    int step = 1 + excess * 4 + (Ore[n.x, n.y] ? 6 : 0);
                    if (cost[c.x, c.y] + step < cost[n.x, n.y])
                    {
                        cost[n.x, n.y] = cost[c.x, c.y] + step;
                        from[n.x, n.y] = c;
                        open.Add(n);
                    }
                }
            }

            if (!done[start.x, start.y])
            {
                Debug.LogWarning($"IsoGrid: spawn {start} is not connected to the base by any painted cells.");
                return;
            }

            var path = new List<Vector2Int>();
            for (var c = start; !reachable[c.x, c.y]; c = from[c.x, c.y]) path.Add(c);
            for (int i = path.Count - 1; i >= 0; i--)
            {
                var c = path[i];
                var prev = from[c.x, c.y]; // one step closer to the base, already graded
                int ph = Heights[prev.x, prev.y];
                Heights[c.x, c.y] = Mathf.Clamp(Heights[c.x, c.y], ph - MaxClimb, ph + MaxClimb);
            }

            if (!CanReachBase(start, onlyBase)) Debug.LogError($"IsoGrid: failed to connect {start} to the base.");
        }

        public bool CanReachBase(Vector2Int start, int onlyBase = -1) => HasCell(start) && ReachableFromBase(onlyBase)[start.x, start.y];

        /// <summary>Flood fill from every base (or just base onlyBase) using the enemy movement rule (steps are symmetric).</summary>
        public bool[,] ReachableFromBase(int onlyBase = -1)
        {
            var seen = new bool[Width, Height];
            var queue = new Queue<Vector2Int>();
            for (int i = 0; i < BaseOrigins.Count; i++)
            {
                if (onlyBase >= 0 && i != onlyBase) continue;
                for (int x = 0; x < BaseSize; x++)
                    for (int y = 0; y < BaseSize; y++)
                    {
                        var b = BaseOrigins[i] + new Vector2Int(x, y);
                        if (!HasCell(b)) continue;
                        seen[b.x, b.y] = true;
                        queue.Enqueue(b);
                    }
            }
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                foreach (var d in Neighbours)
                {
                    var n = c + d;
                    if (!InBounds(n) || seen[n.x, n.y] || !CanStep(c, n)) continue;
                    if (onlyBase >= 0 && IsBase(n) && !InBase(n, onlyBase)) continue; // can't walk through another Core
                    seen[n.x, n.y] = true;
                    queue.Enqueue(n);
                }
            }
            return seen;
        }

        /// <summary>
        /// Which cell's column is drawn at this world point? Tests each column's on-screen silhouette
        /// (top face plus side walls) and keeps the frontmost hit, so it matches what the player sees.
        /// </summary>
        public bool Pick(Vector3 world, out Vector2Int cell)
        {
            cell = default;
            int best = int.MinValue;
            const float halfW = 0.5f, halfH = 0.25f;
            for (int x = 0; x < Width; x++)
                for (int y = 0; y < Height; y++)
                {
                    if (!Exists[x, y]) continue;
                    var c = new Vector2Int(x, y);
                    int order = SortOrder(c);
                    if (order <= best) continue;
                    var top = SurfaceCenter(c);
                    float dx = Mathf.Abs(world.x - top.x);
                    if (dx > halfW) continue;
                    float dy = world.y - top.y;
                    float edge = halfH * (1f - dx / halfW);
                    float wallDepth = (Heights[x, y] - MinLevel + 1) * StepHeight;
                    if (dy > edge || dy < -edge - wallDepth) continue;
                    best = order;
                    cell = c;
                }
            return best != int.MinValue;
        }

        void OnDrawGizmosSelected()
        {
            // Outline of the playable area.
            var g = Grid;
            Vector3 a = g.CellToWorld(Vector3Int.zero), b = g.CellToWorld(new Vector3Int(Width, 0, 0));
            Vector3 c = g.CellToWorld(new Vector3Int(Width, Height, 0)), d = g.CellToWorld(new Vector3Int(0, Height, 0));
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(a, b);
            Gizmos.DrawLine(b, c);
            Gizmos.DrawLine(c, d);
            Gizmos.DrawLine(d, a);
        }
    }
}
