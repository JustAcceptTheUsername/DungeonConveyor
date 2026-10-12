using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DungeonConveyor
{
    /// <summary>
    /// Buying, placing and deleting buildings.
    /// - Pick a building in the build menu; a ghost follows the cursor showing its footprint in green (ok) or red
    ///   (blocked); left-click buys it.
    /// - Conveyors are drawn instead: press on a tile and drag, and the belt follows the cursor, curving at turns.
    ///   Releasing builds every tile up to the first blocked or unaffordable one. Belt ends hook onto loose ends
    ///   of existing belts, take output from an adjacent miner and feed an adjacent Core.
    /// - Right-click a building to delete it (refunded); hold right-click and sweep to clear belts and walls.
    ///   A right-click that removes nothing, or Esc, stops placing.
    /// Rules: miners go on ore, shooting towers near a conveyor (ConveyorReach), and nothing may cut the enemies' path to the Core.
    /// </summary>
    [RequireComponent(typeof(IsoGrid))]
    public class BuildController : MonoBehaviour
    {
        public int StartingGold = 1500;
        [Tooltip("Largest height difference allowed under a building's footprint, and between neighbouring belt tiles.")]
        public int MaxFootprintSlope = 1;
        [Tooltip("Shooting towers must have a conveyor within this many tiles of their footprint (diagonals count as 1).")]
        public int ConveyorReach = 3;
        [Range(0f, 1f)] public float RefundFraction = 1f;
        [Tooltip("Belt animation speed in frames per second.")]
        public float BeltFps = 10f;

        public int Gold { get; private set; }
        public BuildingCatalog Catalog { get; private set; }
        public CoreBuilding Core { get; private set; }
        /// <summary>Footprint origin of the placement ghost this frame, if one is shown.</summary>
        public Vector2Int? GhostOrigin { get; private set; }
        public IReadOnlyList<Conveyor> Conveyors => conveyors;
        public IReadOnlyList<Miner> Miners => miners;
        public IReadOnlyList<Building> Buildings => buildings;
        /// <summary>Raised whenever a building or belt is placed or removed.</summary>
        public event System.Action BuildingsChanged;
        public BuildingCatalog.Entry Placing { get; private set; }
        /// <summary>Why the current ghost position can't be built on (null when it can).</summary>
        public string BlockedReason { get; private set; }
        /// <summary>While drawing belts or walls the left button belongs to us, not to camera panning.</summary>
        public bool CapturesLeftDrag => Placing != null && IsLine(Placing);

        static readonly Color GhostOk = new Color(0.6f, 1f, 0.6f, 0.75f), GhostBad = new Color(1f, 0.4f, 0.35f, 0.65f);
        static readonly Color MarkOk = new Color(0.4f, 1f, 0.4f, 0.9f), MarkBad = new Color(1f, 0.3f, 0.25f, 0.9f);

        IsoGrid map;
        Camera cam;
        CameraController camController;
        Material spriteMaterial;
        Transform buildingsRoot;
        SpriteRenderer ghost;
        readonly List<SpriteRenderer> marks = new List<SpriteRenderer>();
        readonly List<SpriteRenderer> beltGhosts = new List<SpriteRenderer>();
        readonly List<Building> buildings = new List<Building>();
        readonly List<Conveyor> conveyors = new List<Conveyor>();
        readonly List<Miner> miners = new List<Miner>();
        readonly List<Vector2Int> beltPath = new List<Vector2Int>();
        WorldDecor decor;
        FlowField pathCheck;
        bool drawingBelt;
        int beltFrame;

        public bool CanAfford(BuildingCatalog.Entry e) => Gold >= e.Cost;

        /// <summary>Income: ore sold at the Core, enemy bounties, early-wave bonuses.</summary>
        public void Earn(int gold) => Gold += gold;

        /// <summary>Placed buildings show their 3D model rather than a sprite (BuildingCatalog.LiveModels).</summary>
        bool LiveModels => Catalog != null && Catalog.LiveModels;

        static bool IsConveyor(BuildingCatalog.Entry e) => e.Id == BuildingCatalog.ConveyorId;
        static bool IsWall(BuildingCatalog.Entry e) => e.Id == BuildingCatalog.WallId;
        /// <summary>Belts and walls are drawn by dragging a line of tiles.</summary>
        static bool IsLine(BuildingCatalog.Entry e) => IsConveyor(e) || IsWall(e);

        void Start()
        {
            map = GetComponent<IsoGrid>();
            cam = Camera.main;
            if (cam != null) camController = cam.GetComponent<CameraController>();
            var view = GetComponent<MapView>();
            if (view != null) spriteMaterial = view.SpriteMaterial;
            Gold = StartingGold;

            Catalog = BuildingCatalog.Instance;
            if (Catalog == null)
                Debug.LogWarning("BuildController: Resources/BuildingCatalog.asset not found (Dungeon > Sync Building Catalog).");
            GameArt.EnsureRendered(transform);
            if (LiveModels) BuildingModels.MatchBakeLighting();
            decor = GetComponent<WorldDecor>();
            pathCheck = new FlowField(map);

            buildingsRoot = new GameObject("Buildings").transform;
            buildingsRoot.SetParent(transform, false);
            ghost = NewSprite("Ghost", transform);
            ghost.sortingOrder = short.MaxValue - 1;
            ghost.enabled = false;

            // The Core stands on the yellow base from the start.
            var core = Catalog != null ? Catalog.Find(BuildingCatalog.CoreId) : null;
            if (core != null) Core = (CoreBuilding)Place(core, map.BaseOrigin, map.Heights[map.BaseOrigin.x, map.BaseOrigin.y], free: true);
        }

        public void Select(BuildingCatalog.Entry entry)
        {
            Placing = entry == Placing ? null : entry;
            drawingBelt = false;
        }

        public void Cancel()
        {
            Placing = null;
            drawingBelt = false;
        }

        void Update()
        {
            AnimateBelts();
            HideGhosts();
            BlockedReason = null;
            GhostOrigin = null;
            if (Core != null && Core.Destroyed) return;

            var mouse = Mouse.current;
            if (mouse == null || cam == null) return;
            Vector2 screen = mouse.position.ReadValue();
            bool overUI = HUD.IsPointerOverUI(screen);
            var world = cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 0f));

            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) Cancel();
            UpdateErase(mouse, overUI, world);
            if (Placing == null) return;

            bool panning = camController != null && camController.IsDragging;
            Vector2Int cell = default;
            bool hit = !overUI && !panning && map.Pick(world, out cell);

            if (IsLine(Placing)) UpdateLine(mouse, hit, cell);
            else UpdateFootprint(mouse, hit, cell);
        }

        // ---------------------------------------------------------------- removing

        bool erasing, erasedAny;
        Vector2Int? lastEraseCell;

        /// <summary>
        /// Right-click removes the building under the cursor (refunded; the Core stays). Holding right-click and
        /// moving sweeps away every belt and wall tile the cursor passes over, including tiles skipped between frames.
        /// Releasing without having removed anything cancels placing.
        /// </summary>
        void UpdateErase(Mouse mouse, bool overUI, Vector3 world)
        {
            if (mouse.rightButton.wasPressedThisFrame && !overUI)
            {
                erasing = true;
                erasedAny = false;
                lastEraseCell = null;
                drawingBelt = false;
                var target = PickBuilding(world);
                if (target != null && target.Def.Purchasable && !IsSweepable(target))
                {
                    RemoveBuilding(target, refund: true);
                    erasedAny = true;
                }
            }
            if (!erasing) return;

            if (mouse.rightButton.isPressed)
            {
                var target = PickBuilding(world);
                Vector2Int? cell = target != null ? target.Origin : map.Pick(world, out var c) ? c : (Vector2Int?)null;
                if (cell.HasValue)
                {
                    // Walk from the last cell so a quick swipe doesn't leave gaps.
                    var from = lastEraseCell ?? cell.Value;
                    for (var p = from; ; )
                    {
                        if (map.BuildingAt(p) is Building b && IsSweepable(b))
                        {
                            RemoveBuilding(b, refund: true);
                            erasedAny = true;
                        }
                        if (p == cell.Value) break;
                        var d = cell.Value - p;
                        p += Mathf.Abs(d.x) >= Mathf.Abs(d.y) ? new Vector2Int((int)Mathf.Sign(d.x), 0) : new Vector2Int(0, (int)Mathf.Sign(d.y));
                    }
                    lastEraseCell = cell;
                }
                return;
            }

            erasing = false;
            if (!erasedAny) Cancel();
        }

        static bool IsSweepable(Building b) => b is Conveyor || b is Wall;

        // ---------------------------------------------------------------- regular buildings

        void UpdateFootprint(Mouse mouse, bool hit, Vector2Int cell)
        {
            if (!hit) return;
            // Centre the footprint on the cursor.
            var origin = cell - (Placing.Size - Vector2Int.one) / 2;
            bool ok = CanPlace(Placing, origin, out int level, out string reason);
            if (ok && !CanAfford(Placing)) { ok = false; reason = "Not enough gold"; }
            BlockedReason = reason;
            GhostOrigin = origin;
            ShowGhost(Placing, origin, level, ok);

            // Released (not pressed) so a camera drag that starts here doesn't also build.
            if (ok && mouse.leftButton.wasReleasedThisFrame) Place(Placing, origin, level);
        }

        public bool CanPlace(BuildingCatalog.Entry e, Vector2Int origin, out int level, out string reason)
        {
            int lo = int.MaxValue, hi = int.MinValue;
            bool ore = false;
            level = 0;
            foreach (var c in Footprint(origin, e.Size))
            {
                if (!CellFree(c, out reason)) return false;
                ore |= map.IsOre(c);
                lo = Mathf.Min(lo, map.Heights[c.x, c.y]);
                hi = Mathf.Max(hi, map.Heights[c.x, c.y]);
            }
            level = hi;
            if (e.NeedsOre && !ore) { reason = "Must be placed on ore"; return false; }
            if (!e.NeedsOre && ore) { reason = "Ore is reserved for mining"; return false; }
            if (hi - lo > MaxFootprintSlope) { reason = "Ground too uneven"; return false; }
            if (e.NeedsConveyor && !ConveyorNear(origin, e.Size)) { reason = $"Must be within {ConveyorReach} tiles of a conveyor"; return false; }
            if (e.BlocksEnemies)
            {
                foreach (var enemy in Enemy.All)
                    if (IsInside(enemy.Cell, origin, e.Size)) { reason = "An enemy is in the way"; return false; }
                if (pathCheck != null && pathCheck.WouldBlock(Footprint(origin, e.Size))) { reason = "Would block the enemies' path"; return false; }
            }
            reason = null;
            return true;
        }

        /// <summary>Is there a conveyor within ConveyorReach tiles of this footprint?</summary>
        bool ConveyorNear(Vector2Int origin, Vector2Int size)
        {
            for (int x = -ConveyorReach; x < size.x + ConveyorReach; x++)
                for (int y = -ConveyorReach; y < size.y + ConveyorReach; y++)
                    if (map.BuildingAt(origin + new Vector2Int(x, y)) is Conveyor) return true;
            return false;
        }

        static bool IsInside(Vector2Int c, Vector2Int origin, Vector2Int size) =>
            c.x >= origin.x && c.y >= origin.y && c.x < origin.x + size.x && c.y < origin.y + size.y;

        /// <summary>Buys and places a building if allowed (used by scripts such as the playtest bot).</summary>
        public Building TryPlace(BuildingCatalog.Entry e, Vector2Int origin)
        {
            if (e == null || !CanAfford(e) || !CanPlace(e, origin, out int level, out _)) return null;
            return Place(e, origin, level);
        }

        /// <summary>Buys and lays a belt along a path, as if drawn by hand; returns how many tiles were built.</summary>
        public int TryPlaceBelt(IList<Vector2Int> path)
        {
            var belt = Catalog != null ? Catalog.Find(BuildingCatalog.ConveyorId) : null;
            if (belt == null || path.Count == 0) return 0;
            var previous = Placing;
            Placing = belt;
            beltPath.Clear();
            beltPath.AddRange(path);
            int count = CountBuildable(out _);
            PlaceBelt(count);
            beltPath.Clear();
            Placing = previous;
            return count;
        }

        bool CellFree(Vector2Int c, out string reason)
        {
            reason = !map.HasCell(c) ? "Off the map"
                : map.IsBase(c) || map.IsSpawn(c) ? "Reserved"
                : map.BuildingAt(c) != null ? "Occupied"
                : null;
            return reason == null;
        }

        Building Place(BuildingCatalog.Entry e, Vector2Int origin, int level, bool free = false)
        {
            if (!free) Gold -= e.Cost;
            var sr = NewSprite(e.Name, buildingsRoot);
            sr.sprite = e.Sprite;
            sr.transform.position = FootprintCenter(origin, e.Size, level);
            sr.sortingOrder = map.SortOrder(origin) + 1; // origin is the front-most cell of the footprint
            var go = sr.gameObject;
            Building building =
                e.Id == BuildingCatalog.CoreId ? go.AddComponent<CoreBuilding>() :
                e.NeedsOre ? go.AddComponent<Miner>() :
                e.Id == BuildingCatalog.SplitterId ? go.AddComponent<Splitter>() :
                e.Id == BuildingCatalog.RefinerId ? go.AddComponent<Refiner>() :
                e.Attack != AttackKind.None ? go.AddComponent<Tower>() :
                e.Id == BuildingCatalog.WallId ? go.AddComponent<Wall>() :
                go.AddComponent<Building>();
            building.Init(e, origin, level, sr);
            if (LiveModels && !(building is Wall)) building.ShowModel(map);
            if (building is Tower tower) tower.SetupHead(Catalog.AimingHeads);
            if (building is Wall)
            {
                sr.sprite = null; // drawn by its snapped pieces instead
                Register(building, e.Size);
                RefreshWalls(origin, e.Size);
                return building;
            }
            if (building is Miner miner)
            {
                foreach (var c in Footprint(origin, e.Size))
                {
                    var patch = map.InBounds(c) ? map.PatchAt[c.x, c.y] : null;
                    if (patch != null && !miner.Patches.Contains(patch)) miner.Patches.Add(patch);
                }
                miners.Add(miner);
            }
            Register(building, e.Size);
            return building;
        }

        // ---------------------------------------------------------------- lines (belts and walls)

        /// <summary>Press on a tile and drag: the line follows the cursor; releasing builds it.</summary>
        void UpdateLine(Mouse mouse, bool hit, Vector2Int cell)
        {
            bool walls = IsWall(Placing);
            if (!drawingBelt)
            {
                if (!hit) return;
                beltPath.Clear();
                beltPath.Add(cell);
                if (mouse.leftButton.wasPressedThisFrame) drawingBelt = true;
                else
                {
                    // Hover preview of a single tile.
                    if (walls) PreviewWalls(); else PreviewBelt();
                    return;
                }
            }

            if (hit) ExtendBelt(cell);
            int buildable = walls ? PreviewWalls() : PreviewBelt();

            if (mouse.leftButton.wasReleasedThisFrame || !mouse.leftButton.isPressed)
            {
                drawingBelt = false;
                if (walls) PlaceWalls(buildable); else PlaceBelt(buildable);
            }
        }

        // ---------------------------------------------------------------- walls

        /// <summary>
        /// How many tiles of the drawn wall line can be built, in order. Tiles that already hold a wall are free and
        /// simply continue the line; each new tile must be free, affordable and must not seal off the enemies'
        /// path together with the tiles before it.
        /// </summary>
        int CountWalls(out string reason, List<Vector2Int> newCells)
        {
            reason = null;
            newCells.Clear();
            int gold = Gold;
            for (int i = 0; i < beltPath.Count; i++)
            {
                var c = beltPath[i];
                if (map.BuildingAt(c) is Wall) continue;
                if (!CellFree(c, out reason)) return i;
                if (map.IsOre(c)) { reason = "Ore is reserved for mining"; return i; }
                foreach (var enemy in Enemy.All)
                    if (enemy.Cell == c) { reason = "An enemy is in the way"; return i; }
                if (gold < Placing.Cost) { reason = "Not enough gold"; return i; }
                newCells.Add(c);
                if (pathCheck != null && pathCheck.WouldBlock(newCells))
                {
                    newCells.RemoveAt(newCells.Count - 1);
                    reason = "Would block the enemies' path";
                    return i;
                }
                gold -= Placing.Cost;
            }
            return beltPath.Count;
        }

        readonly List<Vector2Int> wallScratch = new List<Vector2Int>();
        readonly List<SpriteRenderer> wallGhosts = new List<SpriteRenderer>();

        /// <summary>Shows the wall line already snapped together: green where it will be built, red after that.</summary>
        int PreviewWalls()
        {
            int buildable = CountWalls(out string reason, wallScratch);
            BlockedReason = reason;
            var line = new HashSet<Vector2Int>(beltPath);
            bool IsWallOrPlanned(Vector2Int c) => line.Contains(c) || map.BuildingAt(c) is Wall;
            int used = 0;
            for (int i = 0; i < beltPath.Count; i++)
            {
                var c = beltPath[i];
                if (map.BuildingAt(c) is Wall) continue;
                int level = map.HasCell(c) ? map.Heights[c.x, c.y] : 0;
                used = Wall.Build(map, c, level, IsWallOrPlanned, spriteMaterial, transform, wallGhosts, used,
                    short.MaxValue - 3, i < buildable ? GhostOk : GhostBad);
            }
            for (int i = used; i < wallGhosts.Count; i++) wallGhosts[i].enabled = false;
            return buildable;
        }

        void PlaceWalls(int count)
        {
            var wall = Placing;
            for (int i = 0; i < count; i++)
            {
                var c = beltPath[i];
                if (map.BuildingAt(c) != null) continue;
                Place(wall, c, map.Heights[c.x, c.y]);
            }
        }

        /// <summary>Re-snaps the walls on and around these cells to their neighbours.</summary>
        void RefreshWalls(Vector2Int origin, Vector2Int size)
        {
            for (int x = -1; x <= size.x; x++)
                for (int y = -1; y <= size.y; y++)
                    if (map.BuildingAt(origin + new Vector2Int(x, y)) is Wall w)
                        w.Shape(map, c => map.BuildingAt(c) is Wall, spriteMaterial, LiveModels);
        }

        /// <summary>Builds a wall line along a path, as if drawn by hand (for scripts); returns walls built.</summary>
        public int TryPlaceWalls(IList<Vector2Int> path)
        {
            var wall = Catalog != null ? Catalog.Find(BuildingCatalog.WallId) : null;
            if (wall == null || path.Count == 0) return 0;
            var previous = Placing;
            Placing = wall;
            beltPath.Clear();
            beltPath.AddRange(path);
            int count = CountWalls(out _, wallScratch);
            int before = buildings.Count;
            PlaceWalls(count);
            beltPath.Clear();
            Placing = previous;
            return buildings.Count - before;
        }

        // ---------------------------------------------------------------- conveyors

        /// <summary>Follow the cursor: walk to it one axis at a time, backtracking if it returns over the belt.</summary>
        void ExtendBelt(Vector2Int target)
        {
            int guard = 0;
            while (beltPath[beltPath.Count - 1] != target && guard++ < 200)
            {
                int existing = beltPath.IndexOf(target);
                if (existing >= 0)
                {
                    beltPath.RemoveRange(existing + 1, beltPath.Count - existing - 1);
                    return;
                }
                var last = beltPath[beltPath.Count - 1];
                var d = target - last;
                var step = Mathf.Abs(d.x) >= Mathf.Abs(d.y) ? new Vector2Int((int)Mathf.Sign(d.x), 0) : new Vector2Int(0, (int)Mathf.Sign(d.y));
                var next = last + step;
                int loop = beltPath.IndexOf(next);
                if (loop >= 0) beltPath.RemoveRange(loop + 1, beltPath.Count - loop - 1);
                else beltPath.Add(next);
            }
        }

        /// <summary>Draws the belt ghost and returns how many tiles from the start can actually be built.</summary>
        int PreviewBelt()
        {
            int buildable = CountBuildable(out string reason);
            BlockedReason = reason;
            ResolveBeltSides(buildable, out var ins, out var outs, out var feeder, out _, out var endInto);
            if (buildable > 0 && buildable < beltPath.Count && endInto.HasValue) BlockedReason = null; // ends by joining what it ran into
            int n = beltPath.Count;
            int last = Mathf.Max(1, buildable) - 1;
            for (int i = 0; i < n; i++)
            {
                var c = beltPath[i];
                if (i >= buildable && endInto == c) break; // the belt or building it joins: nothing to draw from here
                var from = i > 0 ? beltPath[i - 1] : feeder != null ? feeder.Cell : (Vector2Int?)null;
                var to = i == last ? endInto : i < n - 1 ? beltPath[i + 1] : (Vector2Int?)null;
                var g = BeltGhost(i);
                g.enabled = true;
                g.sprite = ConveyorArt.Get(ins[i], outs[i], LiftTo(c, from), LiftTo(c, to), beltFrame);
                g.color = i < buildable ? GhostOk : GhostBad;
                g.transform.position = BeltPosition(c);
            }
            return buildable;
        }

        int CountBuildable(out string reason)
        {
            reason = null;
            int gold = Gold;
            for (int i = 0; i < beltPath.Count; i++)
            {
                var c = beltPath[i];
                if (!CellFree(c, out reason)) return i;
                if (map.IsOre(c)) { reason = "Ore is reserved for mining"; return i; }
                if (i > 0)
                {
                    var p = beltPath[i - 1];
                    if (Mathf.Abs(map.Heights[c.x, c.y] - map.Heights[p.x, p.y]) > MaxFootprintSlope) { reason = "Too steep for a belt"; return i; }
                }
                if (gold < Placing.Cost) { reason = "Not enough gold"; return i; }
                gold -= Placing.Cost;
            }
            return beltPath.Count;
        }

        /// <summary>
        /// In/out sides for the first `count` tiles of the drawn belt (the ones that will be built). Interior tiles
        /// face their neighbours on the path. The start takes input from a belt pointing at it (or re-aims a belt
        /// whose output is loose), else from an adjacent miner, splitter or refiner. The end, in order of preference:
        /// - joins whatever the drag ran into: a belt (fed from the side or head-on), the Core, a splitter or refiner;
        /// - feeds a belt expecting input from it (or re-aims a belt whose input is loose);
        /// - feeds an adjacent Core, splitter or refiner;
        /// - feeds into the side of a belt straight ahead;
        /// - otherwise carries straight on.
        /// endInto is the cell the last tile feeds, if any.
        /// </summary>
        void ResolveBeltSides(int count, out int[] ins, out int[] outs, out Conveyor feeder, out Conveyor receiver, out Vector2Int? endInto)
        {
            int n = beltPath.Count;
            ins = new int[n];
            outs = new int[n];
            for (int i = 0; i < n; i++)
            {
                ins[i] = i > 0 ? Dir.Of(beltPath[i - 1] - beltPath[i]) : (n > 1 ? Dir.Of(beltPath[0] - beltPath[1]) : 1);
                outs[i] = i < n - 1 ? Dir.Of(beltPath[i + 1] - beltPath[i]) : Dir.Opposite(ins[i]);
            }
            count = Mathf.Clamp(count, 1, n);

            var first = beltPath[0];
            var last = beltPath[count - 1];
            feeder = FindFeeder(first);
            receiver = null;
            endInto = null;
            // None of these can sit on the path itself, so their sides never clash with the path's own neighbours.
            int start = feeder != null ? Dir.Of(feeder.Cell - first) : SideToward(first, b => b is Miner || b is Splitter || b is Refiner);
            int end = -1;
            if (count < n)
            {
                var next = beltPath[count];
                if (AcceptsFrom(map.BuildingAt(next), last)) { end = Dir.Of(next - last); endInto = next; }
            }
            else
            {
                receiver = FindReceiver(last, feeder);
                if (receiver != null) { end = Dir.Of(receiver.Cell - last); endInto = receiver.Cell; }
                else end = SideToward(last, b => b.IsCore || b is Splitter || b is Refiner);
                if (end < 0 && count > 1)
                {
                    var ahead = last + Dir.Offsets[Dir.Opposite(ins[count - 1])];
                    if (map.BuildingAt(ahead) is Conveyor side && !beltPath.Contains(ahead) && AcceptsFrom(side, last))
                    {
                        end = Dir.Of(ahead - last);
                        endInto = ahead;
                    }
                }
            }
            if (count == 1 && end == start) { end = -1; endInto = null; receiver = null; }

            if (count == 1)
            {
                if (start < 0 && end < 0) { start = n > 1 ? ins[0] : 1; end = Dir.Opposite(start); } // lone tile flows toward +x
                else if (start < 0) start = Dir.Opposite(end);
                else if (end < 0) end = Dir.Opposite(start);
            }
            else
            {
                if (start < 0) start = Dir.Opposite(outs[0]);
                if (end < 0) end = Dir.Opposite(ins[count - 1]);
            }
            ins[0] = start;
            outs[count - 1] = end;
        }

        /// <summary>Can a belt on `from` hand items to this building? Belts take them from any side but their output.</summary>
        bool AcceptsFrom(Building b, Vector2Int from)
        {
            if (b is Conveyor c) return c.OutCell != from && !TooSteep(c, from);
            return b != null && (b.IsCore || b is Splitter || b is Refiner);
        }

        /// <summary>The side of this cell touching a matching building, or -1.</summary>
        int SideToward(Vector2Int cell, System.Predicate<Building> match)
        {
            for (int d = 0; d < 4; d++)
            {
                var b = map.BuildingAt(cell + Dir.Offsets[d]);
                if (b != null && !(b is Conveyor) && match(b)) return d;
            }
            return -1;
        }

        Conveyor FindFeeder(Vector2Int cell)
        {
            Conveyor loose = null;
            for (int d = 0; d < 4; d++)
            {
                if (!(map.BuildingAt(cell + Dir.Offsets[d]) is Conveyor c) || beltPath.Contains(c.Cell) || TooSteep(c, cell)) continue;
                if (c.OutCell == cell) return c;
                if (loose == null && map.BuildingAt(c.OutCell) == null && c.InCell != cell) loose = c;
            }
            return loose;
        }

        Conveyor FindReceiver(Vector2Int cell, Conveyor exclude)
        {
            Conveyor loose = null;
            for (int d = 0; d < 4; d++)
            {
                if (!(map.BuildingAt(cell + Dir.Offsets[d]) is Conveyor c) || c == exclude || beltPath.Contains(c.Cell) || TooSteep(c, cell)) continue;
                if (c.InCell == cell) return c;
                if (loose == null && map.BuildingAt(c.InCell) == null && c.OutCell != cell) loose = c;
            }
            return loose;
        }

        void PlaceBelt(int count)
        {
            if (count <= 0) return;
            ResolveBeltSides(count, out var ins, out var outs, out var feeder, out var receiver, out var endInto);

            var placed = new List<Conveyor>();
            for (int i = 0; i < count; i++)
            {
                var c = beltPath[i];
                Gold -= Placing.Cost;
                var sr = NewSprite("Conveyor", buildingsRoot);
                sr.transform.position = BeltPosition(c);
                sr.sortingOrder = map.SortOrder(c) + 1;
                var belt = sr.gameObject.AddComponent<Conveyor>();
                belt.Init(Placing, c, map.Heights[c.x, c.y], sr);
                belt.In = ins[i];
                belt.Out = outs[i];
                Register(belt, Vector2Int.one);
                conveyors.Add(belt);
                placed.Add(belt);
            }

            if (feeder != null) feeder.Out = Dir.Of(beltPath[0] - feeder.Cell);
            if (receiver != null) receiver.In = Dir.Of(beltPath[count - 1] - receiver.Cell);
            // Sides are final now; ramps depend on which neighbours actually connect.
            foreach (var belt in placed) RefreshBelt(belt);
            if (feeder != null) RefreshBelt(feeder);
            if (receiver != null) RefreshBelt(receiver);
            if (endInto.HasValue && map.BuildingAt(endInto.Value) is Conveyor joined) RefreshBelt(joined);
        }

        /// <summary>Re-derive a belt's ramps: an end rises a level when the belt it connects to there is higher.</summary>
        void RefreshBelt(Conveyor c)
        {
            int inLift = map.BuildingAt(c.InCell) is Conveyor from && from.OutCell == c.Cell ? LiftTo(c.Cell, from.Cell) : 0;
            // Head-on or into its side, a belt ramps up to a higher belt it feeds.
            int outLift = map.BuildingAt(c.OutCell) is Conveyor to && to.OutCell != c.Cell ? LiftTo(c.Cell, to.Cell) : 0;
            c.SetShape(c.In, c.Out, inLift, outLift, beltFrame);
        }

        /// <summary>1 when the neighbour stands one level higher (this tile ramps up to it), otherwise 0.</summary>
        int LiftTo(Vector2Int cell, Vector2Int? neighbour) =>
            neighbour.HasValue ? Mathf.Clamp(HeightAt(neighbour.Value) - HeightAt(cell), 0, 1) : 0;

        bool TooSteep(Conveyor c, Vector2Int cell) => Mathf.Abs(c.Level - HeightAt(cell)) > MaxFootprintSlope;

        int HeightAt(Vector2Int c) => map.InBounds(c) ? map.Heights[c.x, c.y] : 0;

        Vector3 BeltPosition(Vector2Int c) => map.HasCell(c) ? map.SurfaceCenter(c) : map.CellCenter(c);

        void AnimateBelts()
        {
            int frame = (int)(Time.time * BeltFps) % ConveyorArt.Frames;
            if (frame == beltFrame) return;
            beltFrame = frame;
            foreach (var c in conveyors) c.ShowFrame(frame);
        }

        // ---------------------------------------------------------------- deleting

        /// <summary>
        /// The building drawn at this point: tall sprites are hit-tested per pixel (front-most wins, unless terrain in
        /// front hides them); otherwise whatever stands on the tile under the cursor.
        /// </summary>
        Building PickBuilding(Vector3 world)
        {
            bool terrain = map.Pick(world, out var cell);
            int occluder = terrain ? map.SortOrder(cell) : int.MinValue;
            Building best = null;
            int bestOrder = occluder;
            foreach (var b in buildings)
            {
                var sr = b.Renderer;
                if (sr.sortingOrder <= bestOrder || !sr.bounds.Contains(new Vector3(world.x, world.y, sr.bounds.center.z))) continue;
                if (SpriteHit.OpaqueAt(sr, world))
                {
                    best = b;
                    bestOrder = sr.sortingOrder;
                }
            }
            return best != null ? best : terrain ? map.BuildingAt(cell) : null;
        }

        /// <summary>Removes a building (refunding it if asked); anything it carries or holds goes with it.</summary>
        public void RemoveBuilding(Building b, bool refund)
        {
            if (b == null || b.IsCore) return;
            foreach (var c in Footprint(b.Origin, b.Size))
            {
                if (map.BuildingAt(c) == b) map.Buildings[c.x, c.y] = null;
                if (decor != null && map.IsOre(c)) decor.SetDepositVisible(c, true);
            }
            buildings.Remove(b);
            if (b is Miner miner) miners.Remove(miner);
            if (refund) Gold += Mathf.RoundToInt(b.Def.Cost * RefundFraction);
            Destroy(b.gameObject); // takes any crystals riding a belt or held inside with it
            if (b is Conveyor belt)
            {
                conveyors.Remove(belt);
                // Neighbours that ramped up to it flatten out again.
                foreach (var d in Dir.Offsets)
                    if (map.BuildingAt(belt.Cell + d) is Conveyor n) RefreshBelt(n);
            }
            if (b is Wall) RefreshWalls(b.Origin, b.Size); // neighbours let go of it
            BuildingsChanged?.Invoke();
        }

        void Register(Building b, Vector2Int size)
        {
            buildings.Add(b);
            foreach (var c in Footprint(b.Origin, size))
            {
                map.Buildings[c.x, c.y] = b;
                if (decor != null) decor.SetDepositVisible(c, false); // the miner sits on the crystals
            }
            BuildingsChanged?.Invoke();
        }

        // ---------------------------------------------------------------- ghosts

        void ShowGhost(BuildingCatalog.Entry e, Vector2Int origin, int level, bool ok)
        {
            ghost.enabled = e.Sprite != null;
            ghost.sprite = e.Sprite;
            ghost.color = ok ? GhostOk : GhostBad;
            ghost.transform.position = FootprintCenter(origin, e.Size, level);

            int i = 0;
            foreach (var c in Footprint(origin, e.Size))
            {
                if (i == marks.Count)
                {
                    var m = NewSprite("Mark", transform);
                    m.sprite = BlockArt.Highlight;
                    m.sortingOrder = short.MaxValue - 2;
                    marks.Add(m);
                }
                var mark = marks[i++];
                mark.enabled = true;
                mark.color = ok ? MarkOk : MarkBad;
                mark.transform.position = map.HasCell(c) ? map.SurfaceCenter(c) : map.CellCenter(c) + Vector3.up * (level * IsoGrid.StepHeight);
            }
        }

        SpriteRenderer BeltGhost(int i)
        {
            while (beltGhosts.Count <= i)
            {
                var g = NewSprite("BeltGhost", transform);
                g.sortingOrder = short.MaxValue - 3;
                beltGhosts.Add(g);
            }
            return beltGhosts[i];
        }

        void HideGhosts()
        {
            if (ghost != null) ghost.enabled = false;
            foreach (var m in marks) m.enabled = false;
            foreach (var g in beltGhosts) g.enabled = false;
            foreach (var g in wallGhosts) g.enabled = false;
        }

        // ---------------------------------------------------------------- helpers

        static IEnumerable<Vector2Int> Footprint(Vector2Int origin, Vector2Int size)
        {
            for (int x = 0; x < size.x; x++)
                for (int y = 0; y < size.y; y++)
                    yield return origin + new Vector2Int(x, y);
        }

        Vector3 FootprintCenter(Vector2Int origin, Vector2Int size, int level)
        {
            var sum = Vector3.zero;
            foreach (var c in Footprint(origin, size)) sum += map.CellCenter(c);
            return sum / (size.x * size.y) + Vector3.up * (level * IsoGrid.StepHeight);
        }

        SpriteRenderer NewSprite(string name, Transform parent)
        {
            var sr = new GameObject(name).AddComponent<SpriteRenderer>();
            sr.transform.SetParent(parent, false);
            if (spriteMaterial != null) sr.sharedMaterial = spriteMaterial;
            return sr;
        }
    }
}
