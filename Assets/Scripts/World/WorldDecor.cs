using System.Collections.Generic;
using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>
    /// Everything on the map that isn't a tile or building: rocks enclosing the map (a wall, or the whole visible
    /// void when IsoGrid.FillVoidWithRocks is on), cliff pieces hanging under the camera-facing outer edges, and a
    /// crystal deposit on every ore tile (hidden while a building stands on it). Rocks covering a building or bridge
    /// turn see-through.
    /// </summary>
    [RequireComponent(typeof(IsoGrid))]
    public class WorldDecor : MonoBehaviour
    {
        [Tooltip("Rings of rock around the sides and back of the map when not filling the whole void. The front gets one low ring so it never hides tiles.")]
        [Range(1, 4)] public int WallThickness = 2;
        [Tooltip("Opacity of rocks that stand in front of a building or bridge.")]
        [Range(0f, 1f)] public float CoveringAlpha = 0.3f;

        IsoGrid map;
        MapView view;
        BuildController build;
        Material material;
        SpriteRenderer[,] deposits;
        readonly Dictionary<Vector2Int, int> wall = new Dictionary<Vector2Int, int>(); // rock cell -> ground level
        readonly List<(SpriteRenderer rock, SpriteRenderer column)> rocks = new List<(SpriteRenderer, SpriteRenderer)>();
        Transform wallRoot, cliffRoot;
        bool builtFilled, fadeDirty;

        void Start()
        {
            map = GetComponent<IsoGrid>();
            view = GetComponent<MapView>();
            build = GetComponent<BuildController>();
            material = view.SpriteMaterial;
            GameArt.EnsureRendered(transform);
            if (build != null) build.BuildingsChanged += MarkFadeDirty;

            BuildEnclosure();
            BuildDeposits();
        }

        void OnDestroy()
        {
            if (build != null) build.BuildingsChanged -= MarkFadeDirty;
        }

        void MarkFadeDirty() => fadeDirty = true;

        void LateUpdate()
        {
            if (map.FillVoidWithRocks != builtFilled) BuildEnclosure(); // toggled in the Inspector during Play
            if (fadeDirty)
            {
                fadeDirty = false;
                UpdateFade();
            }
        }

        public void SetDepositVisible(Vector2Int c, bool visible)
        {
            if (deposits != null && map.InBounds(c) && deposits[c.x, c.y] != null) deposits[c.x, c.y].enabled = visible;
        }

        /// <summary>(Re)builds the rocks and the cliffs under them.</summary>
        void BuildEnclosure()
        {
            if (wallRoot != null) Destroy(wallRoot.gameObject);
            if (cliffRoot != null) Destroy(cliffRoot.gameObject);
            rocks.Clear();
            builtFilled = map.FillVoidWithRocks;
            BuildRockWall();
            BuildCliffs();
            fadeDirty = true;
        }

        // ---------------------------------------------------------------- rocks

        /// <summary>
        /// Puts 1x1 rocks on rock columns in the void around the map. Rings grow outward from the map: just in front
        /// of it (lower on screen) only the lowest rocks are used, elsewhere they grow taller outward, with heights
        /// following smooth noise. Either WallThickness rings, or everything the camera can see when filling the void.
        /// Bridges keep a clear path through.
        /// </summary>
        void BuildRockWall()
        {
            wall.Clear();
            if (GameArt.Rocks.Count == 0) return;

            var bridges = new HashSet<Vector2Int>();
            foreach (var b in map.Bridges) bridges.UnionWith(b.Cells);

            bool fill = map.FillVoidWithRocks;
            var visible = VisibleArea();
            int maxRings = fill ? int.MaxValue : WallThickness;

            // Breadth-first rings around the map (8-connected, so corners close up).
            var ring = new Dictionary<Vector2Int, int>();
            var frontier = new List<Vector2Int>();
            for (int x = 0; x < map.Width; x++)
                for (int y = 0; y < map.Height; y++)
                    if (map.Exists[x, y]) frontier.Add(new Vector2Int(x, y));
            for (int r = 1; r <= maxRings && frontier.Count > 0; r++)
            {
                var next = new List<Vector2Int>();
                foreach (var c in frontier)
                    for (int dx = -1; dx <= 1; dx++)
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            var n = c + new Vector2Int(dx, dy);
                            if (map.HasCell(n) || ring.ContainsKey(n) || bridges.Contains(n)) continue;
                            if (fill ? !InArea(n, visible) : r > 1 && InFront(n)) continue; // a wall's front is a single ring
                            ring[n] = r;
                            next.Add(n);
                        }
                frontier = next;
            }

            // Ground level: the highest neighbouring land (map tile or inner rock), so rocks sit on the terrain.
            var cells = new List<Vector2Int>(ring.Keys);
            cells.Sort((a, b) => ring[a].CompareTo(ring[b]));
            foreach (var c in cells)
            {
                int level = int.MinValue;
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        var n = c + new Vector2Int(dx, dy);
                        if (map.HasCell(n)) level = Mathf.Max(level, map.Heights[n.x, n.y]);
                        else if (wall.TryGetValue(n, out int l) && ring[n] < ring[c]) level = Mathf.Max(level, l);
                    }
                wall[c] = Mathf.Clamp(level == int.MinValue ? 0 : level, IsoGrid.MinLevel, IsoGrid.MaxLevel);
            }

            var root = wallRoot = new GameObject("Rocks").transform;
            root.SetParent(transform, false);
            var offset = new Vector2(Random.Range(0f, 1000f), Random.Range(0f, 1000f));
            int count = GameArt.Rocks.Count;
            foreach (var pair in wall)
            {
                var c = pair.Key;
                int level = pair.Value;
                var surface = map.CellCenter(c) + Vector3.up * (level * IsoGrid.StepHeight);

                var column = NewSprite("RockBase", map.SortOrder(c), root);
                column.sprite = BlockArt.Column(level, (c.x * 73856093) ^ (c.y * 19349663));
                float shade = 0.75f * (1f + level * view.LevelShade);
                column.color = new Color(view.Rock.r * shade, view.Rock.g * shade, view.Rock.b * shade, 1f);
                column.transform.position = surface;

                // Pick a rock: front ring from the lowest few, otherwise taller the further out it stands.
                // (Perlin noise can stray slightly outside 0..1.)
                float n = Mathf.Clamp01(Mathf.PerlinNoise(c.x * 0.22f + offset.x, c.y * 0.22f + offset.y));
                int index;
                if (InFront(c)) index = Mathf.Clamp(Mathf.FloorToInt(n * 2.99f), 0, count - 1);
                else
                {
                    float t = Mathf.Clamp01(n * 0.8f + (Mathf.Min(ring[c], 3) - 1) * 0.35f);
                    index = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(count * 0.4f, count - 1, t)), 0, count - 1);
                }
                var sprite = GameArt.Rocks[index];
                if (!InFront(c) && index >= count / 2 && GameArt.RockAccents.Count > 0 && Random.value < GameArt.Library.RockAccentChance)
                    sprite = GameArt.RockAccents[Random.Range(0, GameArt.RockAccents.Count)];

                var rock = NewSprite("Rock", map.SortOrder(c) + 1, root);
                rock.sprite = sprite;
                rock.flipX = Random.value < 0.5f;
                rock.transform.position = surface;
                rocks.Add((rock, column));
            }
        }

        /// <summary>World area the camera can ever show (map bounds plus the camera's margin), plus a little slack.</summary>
        Bounds VisibleArea()
        {
            float margin = 2f;
            var cam = Camera.main;
            var controller = cam != null ? cam.GetComponent<CameraController>() : null;
            if (controller != null) margin = controller.Margin;
            var area = map.WorldBounds;
            area.Expand(new Vector3((margin + 1.5f) * 2f, (margin + 1.5f) * 2f, 0f));
            return area;
        }

        bool InArea(Vector2Int c, Bounds area)
        {
            var p = map.CellCenter(c);
            return p.x >= area.min.x && p.x <= area.max.x && p.y >= area.min.y && p.y <= area.max.y;
        }

        // ---------------------------------------------------------------- see-through rocks

        /// <summary>
        /// A rock fades when it is drawn in front of a building or bridge and its pixels actually cover one of a few
        /// sample points on it (centre, base, top, left and right), so rocks merely near something stay solid.
        /// </summary>
        void UpdateFade()
        {
            var targets = new List<SpriteRenderer>(view.BridgeRenderers);
            if (build != null)
                foreach (var b in build.Buildings)
                    if (b != null && b.Renderer != null) targets.Add(b.Renderer);

            var samples = new Vector3[5];
            foreach (var (rock, column) in rocks)
            {
                var rb = rock.bounds;
                bool covers = false;
                foreach (var t in targets)
                {
                    if (t.sortingOrder >= rock.sortingOrder) continue; // drawn in front of the rock anyway
                    var tb = t.bounds;
                    if (tb.min.x > rb.max.x || tb.max.x < rb.min.x || tb.min.y > rb.max.y || tb.max.y < rb.min.y) continue;
                    samples[0] = tb.center;
                    samples[1] = t.transform.position;
                    samples[2] = new Vector3(tb.center.x, tb.max.y - tb.size.y * 0.15f, 0f);
                    samples[3] = new Vector3(tb.min.x + tb.size.x * 0.2f, tb.center.y, 0f);
                    samples[4] = new Vector3(tb.max.x - tb.size.x * 0.2f, tb.center.y, 0f);
                    foreach (var p in samples)
                        if (SpriteHit.OpaqueAt(rock, p)) { covers = true; break; }
                    if (covers) break;
                }
                float a = covers ? CoveringAlpha : 1f;
                rock.color = new Color(1f, 1f, 1f, a);
                var cc = column.color;
                column.color = new Color(cc.r, cc.g, cc.b, a);
            }
        }

        /// <summary>Is the map behind this void cell (along +x or +y), i.e. would a rock here stand in front of tiles?</summary>
        bool InFront(Vector2Int c)
        {
            for (int k = 1; k <= 3; k++)
                if (map.HasCell(c + new Vector2Int(k, 0)) || map.HasCell(c + new Vector2Int(0, k))) return true;
            return false;
        }

        bool IsLand(Vector2Int c) => map.HasCell(c) || wall.ContainsKey(c);

        // ---------------------------------------------------------------- cliffs

        /// <summary>
        /// Only edges facing the camera (open -x or -y side) get cliffs; the far edges are hidden behind the land.
        /// Land here is the map plus its rock wall. Each cliff hangs from the bottom of its column and sorts just
        /// behind it.
        /// </summary>
        void BuildCliffs()
        {
            if (GameArt.CliffFacingY.Count == 0 && GameArt.CliffCorner == null) return;
            var root = cliffRoot = new GameObject("Cliffs").transform;
            root.SetParent(transform, false);
            float hang = (IsoGrid.MinLevel - 1) * IsoGrid.StepHeight; // bottom face of every column

            var land = new List<Vector2Int>(wall.Keys);
            for (int x = 0; x < map.Width; x++)
                for (int y = 0; y < map.Height; y++)
                    if (map.Exists[x, y]) land.Add(new Vector2Int(x, y));

            foreach (var c in land)
            {
                bool openX = !IsLand(c + Vector2Int.left), openY = !IsLand(c + Vector2Int.down);
                if (!openX && !openY) continue;

                int pick = Mathf.Abs(c.x * 7 + c.y * 13);
                Sprite sprite;
                if (openX && openY && GameArt.CliffCorner != null) sprite = GameArt.CliffCorner;
                else if (openX && GameArt.CliffFacingX.Count > 0) sprite = GameArt.CliffFacingX[pick % GameArt.CliffFacingX.Count];
                else if (GameArt.CliffFacingY.Count > 0) sprite = GameArt.CliffFacingY[pick % GameArt.CliffFacingY.Count];
                else continue;

                var sr = NewSprite("Cliff", map.SortOrder(c) - 1, root);
                sr.sprite = sprite;
                sr.transform.position = map.CellCenter(c) + Vector3.up * hang;
            }
        }

        // ---------------------------------------------------------------- crystal deposits

        /// <summary>Crystals of each patch's ore type on its tiles; they vanish when the patch is mined out.</summary>
        void BuildDeposits()
        {
            deposits = new SpriteRenderer[map.Width, map.Height];
            var root = new GameObject("Deposits").transform;
            root.SetParent(transform, false);
            var xStep = map.CellCenter(Vector2Int.right) - map.CellCenter(Vector2Int.zero);
            var yStep = map.CellCenter(Vector2Int.up) - map.CellCenter(Vector2Int.zero);

            foreach (var patch in map.Patches)
            {
                var art = GameArt.Ore(patch.Kind);
                if (art == null || art.Deposits.Count == 0) continue;
                foreach (var c in patch.Cells)
                {
                    var sr = NewSprite("Crystal", map.SortOrder(c) + 1, root);
                    sr.sprite = art.Deposits[Random.Range(0, art.Deposits.Count)];
                    sr.flipX = Random.value < 0.5f;
                    var jitter = xStep * Random.Range(-0.12f, 0.12f) + yStep * Random.Range(-0.12f, 0.12f);
                    sr.transform.position = map.SurfaceCenter(c) + jitter;
                    sr.enabled = map.BuildingAt(c) == null;
                    deposits[c.x, c.y] = sr;
                }
            }
            map.PatchDepleted += patch =>
            {
                foreach (var c in patch.Cells)
                    if (deposits[c.x, c.y] != null) deposits[c.x, c.y].enabled = false;
            };
        }

        SpriteRenderer NewSprite(string name, int order, Transform parent = null)
        {
            var sr = new GameObject(name).AddComponent<SpriteRenderer>();
            sr.transform.SetParent(parent != null ? parent : transform, false);
            if (material != null) sr.sharedMaterial = material;
            sr.sortingOrder = order;
            return sr;
        }
    }
}
