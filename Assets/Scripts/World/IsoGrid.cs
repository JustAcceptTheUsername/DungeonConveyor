using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace DungeonConveyor
{
    /// <summary>
    /// The planet the game is played on: a hex sphere (see HexSphere). Every tile is a hexagon with 6 neighbours,
    /// except 12 pentagons, which are raised rock spires nothing can stand on or walk over. A cell is a Vector2Int
    /// (tile index, 0), so arrays are [Width = tile count, Height = 1].
    /// Each cell has its own frame: up out of the planet, forward toward side 0 (the neighbour nearest "north"),
    /// sides 0..5 counter-clockwise seen from above (see <see cref="Dir"/>). Offsets inside a cell (CellPoint) are in
    /// that frame, x = right, y = forward, in cells (neighbouring centres are 1 apart). Moving between cells goes
    /// through <see cref="Neighbour"/> / <see cref="DirTo"/>. Footprints are 1, 3 (a triangle) or 7 (a flower)
    /// cells, see <see cref="Footprint"/>.
    /// At runtime this generates elevation, places the bases, ore and the meteor landing sites (IsoGrid.Spawns.cs),
    /// and MapView/TileHover draw it. The planet is centred on the world origin.
    /// </summary>
    public partial class IsoGrid : MonoBehaviour
    {
        public const float StepHeight = 0.25f; // world units per elevation level
        public const int MinLevel = -3, MaxLevel = 6;
        /// <summary>Catalog size of the Core (any size of 3 or more is the 7-cell flower).</summary>
        public const int BaseSize = 4;
        public const int MaxClimb = 1;          // enemies can step up or down this many levels
        /// <summary>How far the pentagon spires stand above their highest neighbour.</summary>
        public const int SpireHeight = 4;

        /// <summary>The planet in the current scene.</summary>
        public static IsoGrid Current { get; private set; }

        [Tooltip("Planet size when not in a roguelike run (the run picks it per stage): about 6 x FaceSize² tiles, " +
                 "as many as the old cube planet with this face size.")]
        public int FaceSize = 16;
        [Tooltip("Unused on the planet; kept so the scene's editor-painted grid still loads.")]
        public Tilemap Ground;
        [Tooltip("Roguelike run: planet size, landing sites and Core counts come from the current stage (see Run). " +
                 "Off = FaceSize with the settings below and one Core.")]
        public bool RoguelikeRun = true;
        [Tooltip("Number of Cores (bases) when not in a roguelike run.")]
        public int CoreCount = 1;
        [Tooltip("Preferred minimum distance in cells between the base centre and each meteor landing site.")]
        public float SpawnMinDistance = 20f;
        [Tooltip("Number of meteor landing sites (min..max, picked at random).")]
        public Vector2Int SpawnCount = new Vector2Int(1, 4);

        [Header("Terrain")]
        [Tooltip("Noise frequency per cell: smaller = broader hills and valleys.")]
        public float NoiseScale = 0.07f;
        [Range(1, 6)] public int Octaves = 4;
        [Tooltip(">1 favours lowlands with fewer, steeper peaks.")]
        public float HeightCurve = 1.4f;
        [Tooltip("Level of the flat plateau the base sits on.")]
        public int BaseLevel = 1;
        [Tooltip("Terrain is flat within the first value of the base and blends back to noise by the second.")]
        public Vector2 BaseFlatten = new Vector2(3.5f, 8f);

        [Header("Ore")]
        public Vector2Int OrePatches = new Vector2Int(5, 8); // min..max count per 2500 cells
        [Tooltip("Ore patches keep at least this distance from the base centre.")]
        public float OreMinBaseDistance = 6f;

        /// <summary>Hex sphere frequency (icosahedron edge subdivisions).</summary>
        public int N { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }
        public int CellCount => Width;
        /// <summary>Radius of level-0 ground, chosen so neighbouring cell centres are about one world unit apart.</summary>
        public float Radius { get; private set; }

        /// <summary>Every cell exists on a planet; kept for code written against the flat map.</summary>
        public bool[,] Exists { get; private set; }
        public int[,] Heights { get; private set; }
        /// <summary>Centre cell of each base (a 7-cell flower), one per Core. The first is the main base.</summary>
        public readonly List<Vector2Int> BaseOrigins = new List<Vector2Int>();
        public Vector2Int BaseOrigin => BaseOrigins[0];
        public bool[,] Ore { get; private set; }
        public Building[,] Buildings { get; private set; }

        int[,] neighbours;      // [cell, side]; a pentagon's side 5 is itself
        int[] sides;            // 6, or 5 for a pentagon
        Vector3[] cellUp, cellForward;
        Vector3[,] corners;     // [cell, k]: corner between sides k and k + 1

        public bool InBounds(Vector2Int c) => c.y == 0 && c.x >= 0 && c.x < Width;
        public bool HasCell(Vector2Int c) => InBounds(c);
        public bool IsOre(Vector2Int c) => InBounds(c) && Ore[c.x, c.y];
        public Building BuildingAt(Vector2Int c) => InBounds(c) ? Buildings[c.x, c.y] : null;
        /// <summary>One of the 12 pentagons: a rock spire, never built on or walked over.</summary>
        public bool IsPentagon(Vector2Int c) => InBounds(c) && sides[c.x] == 5;
        public int Sides(Vector2Int c) => sides[c.x];

        /// <summary>Enemy movement rule: steps to a neighbour, climbing or dropping at most MaxClimb levels; never onto a spire.</summary>
        public bool CanStep(Vector2Int from, Vector2Int to) =>
            HasCell(from) && HasCell(to) && from != to && !IsPentagon(to) && !IsPentagon(from) &&
            Mathf.Abs(Heights[to.x, to.y] - Heights[from.x, from.y]) <= MaxClimb;
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
            return c == o || DirTo(o, c) >= 0;
        }
        /// <summary>The cells of base i.</summary>
        public List<Vector2Int> BaseCells(int i) => Footprint(BaseOrigins[i], Vector2Int.one * BaseSize);
        /// <summary>Distance in cells from a cell to the nearest base centre, measured over the planet.</summary>
        public float DistanceToBase(Vector2Int c) => DistanceToBase(Up(c));
        public float DistanceToBase(Vector3 up)
        {
            float best = float.MaxValue;
            for (int i = 0; i < BaseOrigins.Count; i++) best = Mathf.Min(best, Arc(up, BaseUp(i)));
            return best;
        }
        /// <summary>Unit vector to the centre of base i.</summary>
        public Vector3 BaseUp(int i) => Up(BaseOrigins[i]);
        /// <summary>The Core enemies from this landing site attack: sites are shared out between the Cores in turn.</summary>
        public int CoreFor(SpawnSite s) => Mathf.Max(0, Spawns.IndexOf(s)) % Mathf.Max(1, BaseOrigins.Count);

        // ---------------------------------------------------------------- hex geometry

        /// <summary>The cell across side d of c (a pentagon's side 5 is c itself).</summary>
        public Vector2Int Neighbour(Vector2Int c, int d) => new Vector2Int(neighbours[c.x, d], 0);

        /// <summary>Which side of `from` the neighbouring cell `to` lies on, or -1 if they aren't neighbours.</summary>
        public int DirTo(Vector2Int from, Vector2Int to)
        {
            if (!InBounds(from) || !InBounds(to) || from == to) return -1;
            for (int d = 0; d < Dir.Count; d++)
                if (neighbours[from.x, d] == to.x) return d;
            return -1;
        }

        /// <summary>Unit vector from the planet centre through the cell's centre (its "up").</summary>
        public Vector3 Up(Vector2Int c) => cellUp[c.x];

        /// <summary>The cell's frame: +Y up, +Z toward side 0, +X to its right.</summary>
        public Quaternion Frame(Vector2Int c) => Quaternion.LookRotation(cellForward[c.x], cellUp[c.x]);

        /// <summary>Unit vector to corner k of a cell (between sides k and k + 1); a side d runs from corner d - 1 to corner d.</summary>
        public Vector3 Corner(Vector2Int c, int k) => corners[c.x, ((k % sides[c.x]) + sides[c.x]) % sides[c.x]];

        /// <summary>Unit vector through a point offset from a cell's centre (in cells, in its own frame: x right, y forward).</summary>
        public Vector3 CellDir(Vector2Int c, Vector2 offset)
        {
            var up = cellUp[c.x];
            var f = cellForward[c.x];
            var r = Vector3.Cross(up, f);
            return (up * Radius + r * offset.x + f * offset.y).normalized;
        }

        /// <summary>World position of a point offset from a cell's centre, at an elevation in levels.</summary>
        public Vector3 CellPoint(Vector2Int c, Vector2 offset, float level) => CellDir(c, offset) * RadiusAt(level);

        /// <summary>Like Frame, for a point offset from a cell's centre.</summary>
        public Quaternion CellFrame(Vector2Int c, Vector2 offset)
        {
            var up = CellDir(c, offset);
            return Quaternion.LookRotation(Vector3.ProjectOnPlane(cellForward[c.x], up), up);
        }

        public float RadiusAt(float level) => Radius + level * StepHeight;

        /// <summary>Centre of a cell on level-0 ground.</summary>
        public Vector3 CellCenter(Vector2Int c) => Up(c) * Radius;
        public float HeightAt(Vector2Int c) => HasCell(c) ? Heights[c.x, c.y] : 0f;
        /// <summary>Centre of the cell's top face, including elevation.</summary>
        public Vector3 SurfaceCenter(Vector2Int c) => Up(c) * RadiusAt(Heights[c.x, c.y]);

        /// <summary>Distance in cells along the surface between two directions from the centre.</summary>
        public float Arc(Vector3 upA, Vector3 upB) => Vector3.Angle(upA, upB) * Mathf.Deg2Rad * Radius;
        public float Distance(Vector2Int a, Vector2Int b) => Arc(Up(a), Up(b));

        // ---------------------------------------------------------------- footprints

        /// <summary>Cells a footprint of this catalog size covers: 1x1 = 1, 2x2 = 3 (a triangle), 3x3 and up = 7 (a flower).</summary>
        public static int FootprintCount(Vector2Int size)
        {
            int n = Mathf.Max(size.x, size.y);
            return n <= 1 ? 1 : n == 2 ? 3 : 7;
        }

        /// <summary>
        /// The cells of a footprint anchored on a cell: the cell itself, plus its side 0 and 1 neighbours (a triangle
        /// of 3), or plus all its neighbours (a flower of 7).
        /// </summary>
        public List<Vector2Int> Footprint(Vector2Int anchor, Vector2Int size)
        {
            var cells = new List<Vector2Int> { anchor };
            int count = FootprintCount(size);
            if (!InBounds(anchor) || count == 1) return cells;
            int around = count == 3 ? 2 : Sides(anchor);
            for (int d = 0; d < around; d++)
            {
                var n = Neighbour(anchor, d);
                if (!cells.Contains(n)) cells.Add(n);
            }
            return cells;
        }

        /// <summary>
        /// Scale for a model made to fill a square footprint of this catalog size so it fits the hex footprint instead
        /// (about 1, 1.7 and 3 cells across for 1, 3 and 7 cells).
        /// </summary>
        public static float FootprintScale(Vector2Int size)
        {
            int n = Mathf.Max(1, Mathf.Max(size.x, size.y));
            int count = FootprintCount(size);
            float span = count == 1 ? 1f : count == 3 ? 1.7f : 3f;
            return Mathf.Min(1f, span / n);
        }

        /// <summary>Does the footprint get all its cells (no spire in it)?</summary>
        public bool FootprintFits(Vector2Int anchor, Vector2Int size)
        {
            if (!InBounds(anchor)) return false;
            var cells = Footprint(anchor, size);
            if (cells.Count != FootprintCount(size)) return false;
            foreach (var c in cells)
                if (IsPentagon(c)) return false;
            return true;
        }

        /// <summary>Unit vector through the middle of a footprint.</summary>
        public Vector3 FootprintUp(Vector2Int anchor, Vector2Int size)
        {
            if (FootprintCount(size) != 3) return Up(anchor);
            var sum = Vector3.zero;
            foreach (var c in Footprint(anchor, size)) sum += Up(c);
            return sum.normalized;
        }

        public Vector3 FootprintCenter(Vector2Int anchor, Vector2Int size, float level) => FootprintUp(anchor, size) * RadiusAt(level);

        /// <summary>Orientation of a footprint: up through its middle, forward along its anchor's side 0.</summary>
        public Quaternion FootprintFrame(Vector2Int anchor, Vector2Int size)
        {
            var up = FootprintUp(anchor, size);
            return Quaternion.LookRotation(Vector3.ProjectOnPlane(cellForward[anchor.x], up), up);
        }

        /// <summary>Builds the tiles, their neighbours (sorted counter-clockwise from the one nearest "north") and corners.</summary>
        void BuildGeometry()
        {
            HexSphere.Generate(N, out var centres, out var links);
            Width = centres.Count;
            Height = 1;
            Radius = Mathf.Sqrt(Width * 0.866f / (4f * Mathf.PI)); // a hexagon with centres 1 apart covers √3/2
            cellUp = centres.ToArray();
            cellForward = new Vector3[Width];
            neighbours = new int[Width, Dir.Count];
            sides = new int[Width];
            corners = new Vector3[Width, Dir.Count];

            var angles = new List<(float angle, int cell)>(6);
            for (int i = 0; i < Width; i++)
            {
                var up = cellUp[i];
                var north = Vector3.ProjectOnPlane(Mathf.Abs(up.y) > 0.999f ? Vector3.forward : Vector3.up, up).normalized;
                // Side 0 is the neighbour closest to north; the frame's forward points straight at it.
                int first = links[i][0];
                float best = float.MaxValue;
                foreach (int n in links[i])
                {
                    float a = Vector3.Angle(Vector3.ProjectOnPlane(cellUp[n], up), north);
                    if (a < best) { best = a; first = n; }
                }
                var forward = Vector3.ProjectOnPlane(cellUp[first], up).normalized;
                var right = Vector3.Cross(up, forward);
                cellForward[i] = forward;

                // Counter-clockwise seen from above: the math angle in the (x = right, y = forward) plane, measured
                // from forward (90°).
                angles.Clear();
                foreach (int n in links[i])
                {
                    var p = cellUp[n];
                    float a = Mathf.Atan2(Vector3.Dot(p, forward), Vector3.Dot(p, right)) * Mathf.Rad2Deg - 90f;
                    angles.Add((Mathf.Repeat(a + 1f, 360f) - 1f, n)); // forward itself sorts first even with rounding
                }
                angles.Sort((a, b) => a.angle.CompareTo(b.angle));
                sides[i] = angles.Count;
                for (int d = 0; d < Dir.Count; d++) neighbours[i, d] = d < angles.Count ? angles[d].cell : i;
            }
            for (int i = 0; i < Width; i++)
                for (int k = 0; k < sides[i]; k++)
                    corners[i, k] = (cellUp[i] + cellUp[neighbours[i, k]] + cellUp[neighbours[i, (k + 1) % sides[i]]]).normalized;
        }

        // ---------------------------------------------------------------- setup

        void Awake()
        {
            Current = this;
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            transform.localScale = Vector3.one;
            if (Ground != null) Ground.gameObject.SetActive(false);
            if (RoguelikeRun)
            {
                var stage = Run.Current;
                FaceSize = stage.Size;
                SpawnCount = new Vector2Int(stage.Bridges, stage.Bridges);
                CoreCount = stage.Cores;
            }
            // 6 x FaceSize² square cells ≈ 10 N² hexes.
            N = Mathf.Max(6, Mathf.RoundToInt(FaceSize * Mathf.Sqrt(0.6f)));
            BuildGeometry();
            Exists = new bool[Width, Height];
            Heights = new int[Width, Height];
            for (int x = 0; x < Width; x++) Exists[x, 0] = true;
            Ore = new bool[Width, Height];
            Buildings = new Building[Width, Height];

            PlaceBases();
            GenerateTerrain();
            for (int i = 1; i < BaseOrigins.Count; i++) EnsurePathToBase(BesideBase(i), 0); // Cores linked up
            PlaceSpawns();
            PlaceOres();
            // Each landing site must reach the Core its enemies attack. Grading one route can disturb another, so recheck.
            for (int pass = 0; pass < 3; pass++)
                foreach (var site in Spawns) EnsurePathToBase(site.Landing, CoreFor(site));
            RaiseSpires();

            if (GetComponent<MapView>() == null) gameObject.AddComponent<MapView>();
            if (GetComponent<TileHover>() == null) gameObject.AddComponent<TileHover>();
            if (GetComponent<BuildController>() == null) gameObject.AddComponent<BuildController>();
            if (GetComponent<HUD>() == null) gameObject.AddComponent<HUD>();
            if (GetComponent<WorldDecor>() == null) gameObject.AddComponent<WorldDecor>();
            if (GetComponent<ItemFlow>() == null) gameObject.AddComponent<ItemFlow>();
            if (GetComponent<WaveManager>() == null) gameObject.AddComponent<WaveManager>();
            if (GetComponent<Combat>() == null) gameObject.AddComponent<Combat>();
            if (GetComponent<EnemyPathView>() == null) gameObject.AddComponent<EnemyPathView>();
            if (GetComponent<SpawnIndicators>() == null) gameObject.AddComponent<SpawnIndicators>();
            if (GetComponent<SpireRelics>() == null) gameObject.AddComponent<SpireRelics>();
            if (GetComponent<Tutorial>() == null) gameObject.AddComponent<Tutorial>();
        }

        void OnDestroy()
        {
            if (Current == this) Current = null;
        }

        // ---------------------------------------------------------------- terrain

        /// <summary>
        /// Rolling hills from layered 3D noise over the sphere (so there are no seams), cleaned of one-cell spikes and
        /// pits, with a flat plateau under each base that blends smoothly into the surroundings.
        /// </summary>
        void GenerateTerrain()
        {
            var offset = new Vector3(Random.Range(-1000f, 1000f), Random.Range(-1000f, 1000f), Random.Range(-1000f, 1000f));
            var noise = new float[Width];
            float min = float.MaxValue, max = float.MinValue;
            float scale = NoiseScale * Radius;
            for (int x = 0; x < Width; x++)
            {
                float n = Fbm(cellUp[x] * scale + offset);
                noise[x] = n;
                min = Mathf.Min(min, n);
                max = Mathf.Max(max, n);
            }

            for (int x = 0; x < Width; x++)
            {
                float n = Mathf.Pow(Mathf.InverseLerp(min, max, noise[x]), HeightCurve);
                float h = Mathf.Lerp(MinLevel, MaxLevel, n);
                float d = DistanceToBase(cellUp[x]);
                h = Mathf.Lerp(BaseLevel, h, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(BaseFlatten.x, BaseFlatten.y, d)));
                Heights[x, 0] = Mathf.Clamp(Mathf.RoundToInt(h), MinLevel, MaxLevel);
            }

            for (int i = 0; i < 2; i++) RemoveSpikes();
            for (int i = 0; i < BaseOrigins.Count; i++)
                foreach (var c in BaseCells(i)) Heights[c.x, c.y] = BaseLevel;
        }

        /// <summary>Pentagons become rock spires standing well above everything around them.</summary>
        void RaiseSpires()
        {
            for (int x = 0; x < Width; x++)
            {
                if (sides[x] != 5) continue;
                int top = Heights[x, 0];
                for (int d = 0; d < 5; d++) top = Mathf.Max(top, Heights[neighbours[x, d], 0]);
                Heights[x, 0] = top + SpireHeight;
            }
        }

        /// <summary>A cell right next to base i.</summary>
        Vector2Int BesideBase(int i)
        {
            foreach (var c in BaseCells(i))
                for (int d = 0; d < Sides(c); d++)
                {
                    var n = Neighbour(c, d);
                    if (!IsBase(n) && !IsPentagon(n)) return n;
                }
            return BaseOrigins[i];
        }

        float Fbm(Vector3 p)
        {
            float sum = 0f, amp = 1f, freq = 1f;
            for (int o = 0; o < Octaves; o++)
            {
                sum += Noise3(p * freq) * amp;
                amp *= 0.5f;
                freq *= 2f;
            }
            return sum;
        }

        /// <summary>Cheap smooth 3D noise: Perlin noise on the three axis planes, averaged.</summary>
        static float Noise3(Vector3 p) =>
            (Mathf.PerlinNoise(p.x, p.y) + Mathf.PerlinNoise(p.y + 31.7f, p.z) + Mathf.PerlinNoise(p.z + 57.3f, p.x + 11.1f)) / 3f;

        /// <summary>A cell sticking up above (or sunk below) all its neighbours is pulled to the nearest one.</summary>
        void RemoveSpikes()
        {
            var next = (int[,])Heights.Clone();
            for (int x = 0; x < Width; x++)
            {
                int h = Heights[x, 0], lo = int.MaxValue, hi = int.MinValue;
                for (int d = 0; d < sides[x]; d++)
                {
                    int n = neighbours[x, d];
                    lo = Mathf.Min(lo, Heights[n, 0]);
                    hi = Mathf.Max(hi, Heights[n, 0]);
                }
                if (h > hi) next[x, 0] = hi;
                else if (h < lo) next[x, 0] = lo;
            }
            Heights = next;
        }

        /// <summary>A random base centre whose flower and the ring around it are free of spires.</summary>
        Vector2Int RandomBaseOrigin()
        {
            for (int tries = 0; tries < 1000; tries++)
            {
                var c = new Vector2Int(Random.Range(0, Width), 0);
                if (!FootprintFits(c, Vector2Int.one * BaseSize)) continue;
                bool clear = true;
                foreach (var b in Footprint(c, Vector2Int.one * BaseSize))
                    for (int d = 0; d < Sides(b); d++)
                        if (IsPentagon(Neighbour(b, d))) clear = false;
                if (clear) return c;
            }
            return new Vector2Int(0, 0);
        }

        /// <summary>The main base anywhere; any further bases spread around the planet, well apart from each other.</summary>
        void PlaceBases()
        {
            BaseOrigins.Clear();
            BaseOrigins.Add(RandomBaseOrigin());
            float halfway = Mathf.PI * Radius * 0.5f; // a quarter of the way round
            for (int i = 1; i < CoreCount; i++)
            {
                float apart = halfway;
                for (int pass = 0; pass < 4 && BaseOrigins.Count <= i; pass++, apart *= 0.8f)
                    for (int tries = 0; tries < 1000; tries++)
                    {
                        var o = RandomBaseOrigin();
                        bool far = true;
                        foreach (var other in BaseOrigins)
                            if (Distance(o, other) < apart) far = false;
                        if (!far) continue;
                        BaseOrigins.Add(o);
                        break;
                    }
            }
            if (BaseOrigins.Count < CoreCount) Debug.LogWarning($"IsoGrid: room for only {BaseOrigins.Count} of {CoreCount} Cores.");
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
        /// A handful of 1 / 3 / 7-cell ore patches (the footprint shapes), each flattened to its average height. Each
        /// patch is one ore type picked by weight among those allowed at its distance (so rich ores lie further out)
        /// and holds a random amount per tile. The first patch is always the most common ore, close to the base.
        /// </summary>
        void PlaceOres()
        {
            Patches.Clear();
            PatchAt = new OrePatch[Width, Height];
            // OrePatches is per 2500 cells; scale it with the planet's surface, at least 3.
            float area = Width * Height / 2500f;
            int target = Mathf.Max(3, Mathf.RoundToInt(Random.Range(OrePatches.x, OrePatches.y + 1) * area));
            float minDistance = Mathf.Min(OreMinBaseDistance, Radius * 0.6f);
            for (int placed = 0, tries = 0; placed < target && tries < 6000; tries++)
            {
                var size = Vector2Int.one * Random.Range(1, 4);
                var o = new Vector2Int(Random.Range(0, Width), 0);
                if (!FootprintFits(o, size)) continue;
                float distance = DistanceToBase(FootprintUp(o, size));
                if (distance < minDistance || (placed == 0 && (distance > minDistance + 6f || size.x < 2))) continue;
                var cells = Footprint(o, size);
                if (!OreFits(cells) || NearSpawn(o, 4f)) continue;

                var patch = new OrePatch { Kind = placed == 0 ? 0 : PickOre(distance) };
                var type = OreTypeOf(patch.Kind);
                int sum = 0;
                foreach (var c in cells) sum += Heights[c.x, c.y];
                int level = Mathf.RoundToInt(sum / (float)cells.Count);
                foreach (var c in cells)
                {
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

        /// <summary>No cell of the patch or around it (two steps out) is ore, base, a landing site or a spire.</summary>
        bool OreFits(List<Vector2Int> cells)
        {
            foreach (var c in cells)
            {
                if (Taken(c)) return false;
                for (int d = 0; d < Sides(c); d++)
                {
                    var n = Neighbour(c, d);
                    if (Taken(n)) return false;
                    for (int e = 0; e < Sides(n); e++)
                        if (Taken(Neighbour(n, e))) return false;
                }
            }
            return true;

            bool Taken(Vector2Int c) => IsOre(c) || IsBase(c) || IsSpawn(c) || IsPentagon(c);
        }

        // ---------------------------------------------------------------- reachability

        /// <summary>
        /// Guarantees enemies can walk from this cell to the base. Finds the route needing the least earthwork
        /// (steps steeper than MaxClimb are expensive, ore is avoided, spires are impassable) to the area already
        /// reachable from the base, and grades it into a ramp working outward. Only cells outside that area change,
        /// so routes made for earlier landing sites stay intact.
        /// </summary>
        void EnsurePathToBase(Vector2Int start, int onlyBase = -1)
        {
            var reachable = ReachableFromBase(onlyBase);
            if (!HasCell(start) || reachable[start.x, start.y]) return;

            var cost = new int[Width];
            var from = new int[Width];
            var done = new bool[Width];
            var open = new MinHeap();
            for (int x = 0; x < Width; x++)
            {
                cost[x] = int.MaxValue;
                if (reachable[x, 0])
                {
                    cost[x] = 0;
                    open.Push(new Vector2Int(x, 0), 0);
                }
            }

            while (open.Count > 0)
            {
                int c = open.Pop().x;
                if (done[c]) continue;
                done[c] = true;
                if (c == start.x) break;

                for (int d = 0; d < sides[c]; d++)
                {
                    int n = neighbours[c, d];
                    if (done[n] || sides[n] == 5) continue;
                    int excess = Mathf.Max(0, Mathf.Abs(Heights[n, 0] - Heights[c, 0]) - MaxClimb);
                    int step = 1 + excess * 4 + (Ore[n, 0] ? 6 : 0);
                    if (cost[c] + step < cost[n])
                    {
                        cost[n] = cost[c] + step;
                        from[n] = c;
                        open.Push(new Vector2Int(n, 0), cost[n]);
                    }
                }
            }

            if (!done[start.x])
            {
                Debug.LogWarning($"IsoGrid: spawn {start} is not connected to the base.");
                return;
            }

            var path = new List<int>();
            for (int c = start.x; !reachable[c, 0]; c = from[c]) path.Add(c);
            for (int i = path.Count - 1; i >= 0; i--)
            {
                int c = path[i];
                int ph = Heights[from[c], 0]; // one step closer to the base, already graded
                Heights[c, 0] = Mathf.Clamp(Heights[c, 0], ph - MaxClimb, ph + MaxClimb);
            }

            if (!CanReachBase(start, onlyBase)) Debug.LogError($"IsoGrid: failed to connect {start} to the base.");
        }

        /// <summary>Binary min-heap of cells by cost, for Dijkstra.</summary>
        class MinHeap
        {
            readonly List<(Vector2Int cell, int cost)> items = new List<(Vector2Int, int)>();
            public int Count => items.Count;

            public void Push(Vector2Int cell, int cost)
            {
                items.Add((cell, cost));
                for (int i = items.Count - 1; i > 0;)
                {
                    int p = (i - 1) / 2;
                    if (items[p].cost <= items[i].cost) break;
                    (items[p], items[i]) = (items[i], items[p]);
                    i = p;
                }
            }

            public Vector2Int Pop()
            {
                var top = items[0].cell;
                items[0] = items[items.Count - 1];
                items.RemoveAt(items.Count - 1);
                for (int i = 0; ;)
                {
                    int l = i * 2 + 1, r = l + 1, m = i;
                    if (l < items.Count && items[l].cost < items[m].cost) m = l;
                    if (r < items.Count && items[r].cost < items[m].cost) m = r;
                    if (m == i) break;
                    (items[m], items[i]) = (items[i], items[m]);
                    i = m;
                }
                return top;
            }
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
                foreach (var b in BaseCells(i))
                {
                    seen[b.x, b.y] = true;
                    queue.Enqueue(b);
                }
            }
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                for (int d = 0; d < Sides(c); d++)
                {
                    var n = Neighbour(c, d);
                    if (seen[n.x, n.y] || !CanStep(c, n)) continue;
                    if (onlyBase >= 0 && IsBase(n) && !InBase(n, onlyBase)) continue; // can't walk through another Core
                    seen[n.x, n.y] = true;
                    queue.Enqueue(n);
                }
            }
            return seen;
        }

        // ---------------------------------------------------------------- picking

        /// <summary>The cell under a screen point (the ground, or whatever stands on it).</summary>
        public bool Pick(Camera cam, Vector2 screen, out Vector2Int cell)
        {
            var view = GetComponent<MapView>();
            cell = default;
            return view != null && view.Pick(cam.ScreenPointToRay(screen), out cell);
        }

        void OnDrawGizmosSelected()
        {
            if (N <= 0) return;
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(Vector3.zero, Radius);
        }
    }
}
