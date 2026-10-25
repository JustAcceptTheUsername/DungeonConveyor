using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DungeonConveyor
{
    /// <summary>
    /// Buying, placing and deleting buildings on the planet.
    /// - Pick a building in the build menu; a see-through copy follows the cursor with its footprint marked in green
    ///   (ok) or red (blocked); left-click buys it. Footprints are 1, 3 or 7 hexes anchored on the cell under
    ///   the cursor (see IsoGrid.Footprint); the 12 pentagon spires can't be built on.
    /// - Conveyors are drawn instead: press on a tile and drag, and the belt follows the cursor, curving at turns.
    ///   Releasing builds every tile up to the first blocked or unaffordable one. Belt ends hook onto loose ends
    ///   of existing belts, take output from an adjacent miner and feed an adjacent Core.
    /// - Right-click a building to delete it (refunded); hold right-click and sweep to clear belts and walls.
    ///   A right-click that removes nothing, or Esc, stops placing.
    /// Rules: miners go on ore, towers anywhere else, and nothing may cut the enemies' path to the Core.
    /// </summary>
    [RequireComponent(typeof(IsoGrid))]
    public class BuildController : MonoBehaviour
    {
        public int StartingGold = 1500;
        [Tooltip("Largest height difference allowed under a building's footprint, and between neighbouring belt tiles.")]
        public int MaxFootprintSlope = 1;
        [Range(0f, 1f)] public float RefundFraction = 1f;
        [Tooltip("Belt animation speed in frames per second.")]
        public float BeltFps = 10f;

        public int Gold { get; private set; }
        public BuildingCatalog Catalog { get; private set; }
        public CoreBuilding Core { get; private set; }
        /// <summary>Footprint origin of the placement ghost this frame, if one is shown.</summary>
        public Vector2Int? GhostOrigin { get; private set; }
        /// <summary>Level the placement ghost stands on this frame.</summary>
        public int GhostLevel { get; private set; }
        public IReadOnlyList<Conveyor> Conveyors => conveyors;
        public IReadOnlyList<Miner> Miners => miners;
        public IReadOnlyList<Building> Buildings => buildings;
        /// <summary>Raised whenever a building or belt is placed or removed.</summary>
        public event System.Action BuildingsChanged;
        public BuildingCatalog.Entry Placing { get; private set; }
        /// <summary>Why the current ghost position can't be built on (null when it can).</summary>
        public string BlockedReason { get; private set; }
        /// <summary>While drawing belts or walls the left button belongs to us, not to turning the camera.</summary>
        public bool CapturesLeftDrag => Placing != null && IsLine(Placing);

        static readonly Color GhostOk = new Color(0.6f, 1f, 0.6f, 0.75f), GhostBad = new Color(1f, 0.4f, 0.35f, 0.65f);
        static readonly Color ModelOk = new Color(0.55f, 1f, 0.6f, 0.5f), ModelBad = new Color(1f, 0.4f, 0.35f, 0.5f);
        static readonly Color MarkOk = new Color(0.4f, 1f, 0.4f, 0.9f), MarkBad = new Color(1f, 0.3f, 0.25f, 0.9f);

        IsoGrid map;
        MapView view;
        Camera cam;
        CameraController camController;
        Material spriteMaterial;
        Transform buildingsRoot, ghostRoot;
        readonly Dictionary<BuildingCatalog.Entry, GameObject> ghostModels = new Dictionary<BuildingCatalog.Entry, GameObject>();
        GameObject shownGhost;
        readonly List<SpriteRenderer> marks = new List<SpriteRenderer>();
        readonly List<SpriteRenderer> beltGhosts = new List<SpriteRenderer>();
        readonly List<Building> buildings = new List<Building>();
        readonly List<Conveyor> conveyors = new List<Conveyor>();
        readonly List<Miner> miners = new List<Miner>();
        readonly List<Vector2Int> beltPath = new List<Vector2Int>();
        WorldDecor decor;
        FlowField pathCheck;
        bool drawingBelt;
        int beltFrame, usedMarks;

        public bool CanAfford(BuildingCatalog.Entry e) => Gold >= e.Cost;
        /// <summary>Gold earned this map (bounties, crystals, bonuses), for the map summary.</summary>
        public int GoldEarned { get; private set; }

        /// <summary>Raised whenever Gold changes: the delta (+ earned, - spent) and the world position it came from, if any.</summary>
        public event System.Action<int, Vector3?> GoldChanged;

        /// <summary>Income: ore sold at the Core, enemy bounties, early-wave bonuses. `from` is where it came from in the world.</summary>
        public void Earn(int gold, Vector3? from = null) => ChangeGold(gold, from);

        void ChangeGold(int delta, Vector3? from = null)
        {
            if (delta == 0) return;
            Gold += delta;
            if (delta > 0) GoldEarned += delta;
            GoldChanged?.Invoke(delta, from);
        }

        static bool IsConveyor(BuildingCatalog.Entry e) => e.Id == BuildingCatalog.ConveyorId;
        static bool IsWall(BuildingCatalog.Entry e) => e.Id == BuildingCatalog.WallId;
        /// <summary>Belts and walls are drawn by dragging a line of tiles.</summary>
        static bool IsLine(BuildingCatalog.Entry e) => IsConveyor(e) || IsWall(e);

        void Start()
        {
            map = GetComponent<IsoGrid>();
            view = GetComponent<MapView>();
            cam = Camera.main;
            if (cam != null) camController = cam.GetComponent<CameraController>();
            if (view != null) spriteMaterial = view.SpriteMaterial;
            Gold = StartingGold + Run.CarriedGold + (Run.Stage == 0 ? Meta.StartingGoldBonus : 0); // later maps start with the gold left over from the last one

            Catalog = BuildingCatalog.Instance;
            if (Catalog == null)
                Debug.LogWarning("BuildController: Resources/BuildingCatalog.asset not found (Dungeon > Sync Building Catalog).");
            GameArt.EnsureRendered(transform);
            decor = GetComponent<WorldDecor>();
            pathCheck = new FlowField(map);

            buildingsRoot = new GameObject("Buildings").transform;
            buildingsRoot.SetParent(transform, false);
            ghostRoot = new GameObject("Ghosts").transform;
            ghostRoot.SetParent(transform, false);

            // The Core stands on the yellow base from the start.
            var core = Catalog != null ? Catalog.Find(BuildingCatalog.CoreId) : null;
            if (core != null) Core = (CoreBuilding)Place(core, map.BaseOrigin, map.Heights[map.BaseOrigin.x, map.BaseOrigin.y], free: true);
            if (Core != null)
            {
                Core.MaxHealth = Core.Health = 1000f * Meta.CoreHealthMultiplier * Run.CoreHealthMultiplier;
                Run.Applied += OnCardApplied;
            }
        }

        void OnDestroy() => Run.Applied -= OnCardApplied;

        /// <summary>Iron Heart: the Core grows and heals to full on the spot.</summary>
        void OnCardApplied(Run.Card c)
        {
            if (Core == null || c.Kind != Run.CardKind.Relic || c.RelicKind != Run.Relic.IronHeart) return;
            Core.MaxHealth = 1000f * Meta.CoreHealthMultiplier * Run.CoreHealthMultiplier;
            Core.Health = Core.MaxHealth;
        }

        public void Select(BuildingCatalog.Entry entry)
        {
            if (entry != null && !Run.IsUnlocked(entry.Id)) return; // still locked in the shop
            Placing = entry == Placing ? null : entry;
            Sfx.Play(Placing != null ? Sfx.Lib.Select : Sfx.Lib.Cancel);
            drawingBelt = false;
        }

        public void Cancel()
        {
            if (Placing != null) Sfx.Play(Sfx.Lib.Cancel);
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

            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) Cancel();
            UpdateErase(mouse, overUI, screen);
            if (Placing == null) return;

            bool turning = camController != null && camController.IsDragging;
            Vector2Int cell = default;
            bool hit = !overUI && !turning && map.Pick(cam, screen, out cell);

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
        void UpdateErase(Mouse mouse, bool overUI, Vector2 screen)
        {
            if (mouse.rightButton.wasPressedThisFrame && !overUI)
            {
                erasing = true;
                erasedAny = false;
                lastEraseCell = null;
                drawingBelt = false;
                var target = PickBuilding(screen);
                if (target != null && target.Def.Purchasable && !IsSweepable(target))
                {
                    RemoveBuilding(target, refund: true);
                    erasedAny = true;
                }
            }
            if (!erasing) return;

            if (mouse.rightButton.isPressed)
            {
                var target = PickBuilding(screen);
                Vector2Int? cell = target != null ? target.Origin : map.Pick(cam, screen, out var c) ? c : (Vector2Int?)null;
                if (cell.HasValue)
                {
                    // Walk from the last cell so a quick swipe doesn't leave gaps.
                    var p = lastEraseCell ?? cell.Value;
                    for (int guard = 0; guard < 200; guard++)
                    {
                        if (map.BuildingAt(p) is Building b && IsSweepable(b))
                        {
                            RemoveBuilding(b, refund: true);
                            erasedAny = true;
                        }
                        if (p == cell.Value) break;
                        p = StepToward(p, cell.Value);
                    }
                    lastEraseCell = cell;
                }
                return;
            }

            erasing = false;
            if (!erasedAny) Cancel();
        }

        static bool IsSweepable(Building b) => b is Conveyor || b is Wall;

        /// <summary>The neighbour of `from` closest to `to` over the planet.</summary>
        Vector2Int StepToward(Vector2Int from, Vector2Int to)
        {
            var target = map.Up(to);
            var best = from;
            float bestAngle = float.MaxValue;
            for (int d = 0; d < map.Sides(from); d++)
            {
                var n = map.Neighbour(from, d);
                float a = Vector3.Angle(map.Up(n), target);
                if (a < bestAngle) { bestAngle = a; best = n; }
            }
            return best;
        }

        // ---------------------------------------------------------------- regular buildings

        void UpdateFootprint(Mouse mouse, bool hit, Vector2Int cell)
        {
            if (!hit) return;
            var origin = cell; // the footprint is anchored on the cell under the cursor
            bool ok = CanPlace(Placing, origin, out int level, out string reason);
            if (ok && !CanAfford(Placing)) { ok = false; reason = "Not enough gold"; }
            BlockedReason = reason;
            GhostOrigin = origin;
            GhostLevel = level;
            ShowGhost(Placing, origin, level, ok);

            // Released (not pressed) so a camera drag that starts here doesn't also build.
            if (ok && mouse.leftButton.wasReleasedThisFrame)
            {
                Place(Placing, origin, level);
                Sfx.Play(Sfx.Lib.Place, map.FootprintCenter(origin, Placing.Size, level));
            }
            else if (!ok && mouse.leftButton.wasReleasedThisFrame) Sfx.Play(Sfx.Lib.CantPlace);
        }

        public bool CanPlace(BuildingCatalog.Entry e, Vector2Int origin, out int level, out string reason)
        {
            level = 0;
            if (!map.FootprintFits(origin, e.Size)) { reason = "Doesn't fit here"; return false; }
            int lo = int.MaxValue, hi = int.MinValue;
            bool ore = false;
            foreach (var c in map.Footprint(origin, e.Size))
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
            if (e.BlocksEnemies)
            {
                var cells = map.Footprint(origin, e.Size);
                foreach (var enemy in Enemy.All)
                    if (enemy.Landed && cells.Contains(enemy.Cell)) { reason = "An enemy is in the way"; return false; }
                if (pathCheck != null && pathCheck.WouldBlock(cells)) { reason = "Would block the enemies' path"; return false; }
            }
            reason = null;
            return true;
        }

        /// <summary>Buys and places a building if allowed (used by scripts such as the playtest bot).</summary>
        public Building TryPlace(BuildingCatalog.Entry e, Vector2Int origin)
        {
            if (e == null || !CanAfford(e) || !CanPlace(e, origin, out int level, out _)) return null;
            return Place(e, origin, level);
        }

        /// <summary>Buys and lays a belt along a path of neighbouring cells, as if drawn by hand; returns how many tiles were built.</summary>
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
                : map.IsPentagon(c) ? "Rock spire"
                : map.IsBase(c) || map.IsSpawn(c) ? "Reserved"
                : map.BuildingAt(c) != null ? "Occupied"
                : null;
            return reason == null;
        }

        Building Place(BuildingCatalog.Entry e, Vector2Int origin, int level, bool free = false)
        {
            if (!free) ChangeGold(-e.Cost);
            var go = new GameObject(e.Name);
            go.transform.SetParent(buildingsRoot, false);
            go.transform.SetPositionAndRotation(map.FootprintCenter(origin, e.Size, level), map.FootprintFrame(origin, e.Size));
            Building building =
                e.Id == BuildingCatalog.CoreId ? go.AddComponent<CoreBuilding>() :
                e.NeedsOre ? go.AddComponent<Miner>() :
                e.Id == BuildingCatalog.SplitterId ? go.AddComponent<Splitter>() :
                e.Id == BuildingCatalog.RefinerId ? go.AddComponent<Refiner>() :
                e.Attack != AttackKind.None ? go.AddComponent<Tower>() :
                e.Id == BuildingCatalog.WallId ? go.AddComponent<Wall>() :
                go.AddComponent<Building>();
            building.Init(e, origin, level, null);
            if (building is Wall)
            {
                Register(building);
                RefreshWalls(building.Cells);
                return building;
            }
            building.ShowModel(map);
            if (building is Tower tower) tower.SetupHead(Catalog.AimingHeads);
            if (building is Miner miner)
            {
                foreach (var c in building.Cells)
                {
                    var patch = map.PatchAt[c.x, c.y];
                    if (patch != null && !miner.Patches.Contains(patch)) miner.Patches.Add(patch);
                }
                miners.Add(miner);
            }
            Register(building);
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
                if (buildable > 0) Sfx.Play(walls ? Sfx.Lib.Place : Sfx.Lib.PlaceBelt, map.SurfaceCenter(beltPath[0]));
                else Sfx.Play(Sfx.Lib.CantPlace);
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
                    if (enemy.Landed && enemy.Cell == c) { reason = "An enemy is in the way"; return i; }
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

        /// <summary>Marks the wall line's tiles: green where it will be built, red after that.</summary>
        int PreviewWalls()
        {
            int buildable = CountWalls(out string reason, wallScratch);
            BlockedReason = reason;
            for (int i = 0; i < beltPath.Count; i++)
            {
                var c = beltPath[i];
                if (map.BuildingAt(c) is Wall) continue;
                Mark(c, map.Heights[c.x, c.y], i < buildable);
            }
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

        /// <summary>Re-snaps the walls on and around a footprint to their neighbours.</summary>
        void RefreshWalls(List<Vector2Int> footprint)
        {
            var cells = new HashSet<Vector2Int>();
            foreach (var c in footprint)
            {
                cells.Add(c);
                for (int d = 0; d < map.Sides(c); d++) cells.Add(map.Neighbour(c, d));
            }
            foreach (var c in cells)
                if (map.BuildingAt(c) is Wall w) w.Shape(map, n => map.BuildingAt(n) is Wall);
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

        /// <summary>Follow the cursor: walk to it one cell at a time, backtracking if it returns over the belt.</summary>
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
                var next = StepToward(beltPath[beltPath.Count - 1], target);
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
                g.sprite = ConveyorArt.Get(ins[i], outs[i], 0, 0, beltFrame);
                g.color = i < buildable ? GhostOk : GhostBad;
                LayBelt(g.transform, c, HeightAt(c), ins[i], outs[i], LiftTo(c, from), LiftTo(c, to), 0.01f);
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
                    if (map.DirTo(p, c) < 0) { reason = "Belt tiles must touch"; return i; }
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
                ins[i] = Side(beltPath[i], i > 0 ? beltPath[i - 1] : (Vector2Int?)null);
                outs[i] = Side(beltPath[i], i < n - 1 ? beltPath[i + 1] : (Vector2Int?)null);
                if (ins[i] < 0 && outs[i] < 0) { ins[i] = Dir.Opposite(0); outs[i] = 0; }
                else if (ins[i] < 0) ins[i] = Dir.Opposite(outs[i]);
                else if (outs[i] < 0) outs[i] = Dir.Opposite(ins[i]);
            }
            count = Mathf.Clamp(count, 1, n);

            var first = beltPath[0];
            var last = beltPath[count - 1];
            feeder = FindFeeder(first);
            receiver = null;
            endInto = null;
            // None of these can sit on the path itself, so their sides never clash with the path's own neighbours.
            int start = feeder != null ? map.DirTo(first, feeder.Cell) : SideToward(first, b => b is Miner || b is Splitter || b is Refiner);
            int end = -1;
            if (count < n)
            {
                var next = beltPath[count];
                if (AcceptsFrom(map.BuildingAt(next), last)) { end = map.DirTo(last, next); endInto = next; }
            }
            else
            {
                receiver = FindReceiver(last, feeder);
                if (receiver != null) { end = map.DirTo(last, receiver.Cell); endInto = receiver.Cell; }
                else end = SideToward(last, b => b.IsCore || b is Splitter || b is Refiner || b is Tower t && t.TakesCrystals);
                if (end < 0 && count > 1)
                {
                    var ahead = map.Neighbour(last, Dir.Opposite(ins[count - 1]));
                    if (map.BuildingAt(ahead) is Conveyor side && !beltPath.Contains(ahead) && AcceptsFrom(side, last))
                    {
                        end = map.DirTo(last, ahead);
                        endInto = ahead;
                    }
                }
            }
            if (count == 1 && end == start) { end = -1; endInto = null; receiver = null; }

            if (count == 1)
            {
                if (start < 0 && end < 0) { start = ins[0]; end = Dir.Opposite(start); } // lone tile flows toward side 0
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

        /// <summary>Side of `cell` toward a neighbouring cell, or -1 if there is none (or it doesn't touch).</summary>
        int Side(Vector2Int cell, Vector2Int? other) => other.HasValue ? map.DirTo(cell, other.Value) : -1;

        /// <summary>Can a belt on `from` hand items to this building? Belts take them from any side but their output.</summary>
        bool AcceptsFrom(Building b, Vector2Int from)
        {
            if (b is Conveyor c) return c.OutCell != from && !TooSteep(c, from);
            return b != null && (b.IsCore || b is Splitter || b is Refiner || b is Tower t && t.TakesCrystals);
        }

        /// <summary>The side of this cell touching a matching building, or -1.</summary>
        int SideToward(Vector2Int cell, System.Predicate<Building> match)
        {
            for (int d = 0; d < map.Sides(cell); d++)
            {
                var b = map.BuildingAt(map.Neighbour(cell, d));
                if (b != null && !(b is Conveyor) && match(b)) return d;
            }
            return -1;
        }

        Conveyor FindFeeder(Vector2Int cell)
        {
            Conveyor loose = null;
            for (int d = 0; d < map.Sides(cell); d++)
            {
                if (!(map.BuildingAt(map.Neighbour(cell, d)) is Conveyor c) || beltPath.Contains(c.Cell) || TooSteep(c, cell)) continue;
                if (c.OutCell == cell) return c;
                if (loose == null && map.BuildingAt(c.OutCell) == null && c.InCell != cell) loose = c;
            }
            return loose;
        }

        Conveyor FindReceiver(Vector2Int cell, Conveyor exclude)
        {
            Conveyor loose = null;
            for (int d = 0; d < map.Sides(cell); d++)
            {
                if (!(map.BuildingAt(map.Neighbour(cell, d)) is Conveyor c) || c == exclude || beltPath.Contains(c.Cell) || TooSteep(c, cell)) continue;
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
            ChangeGold(-Placing.Cost * count);
            for (int i = 0; i < count; i++)
            {
                var c = beltPath[i];
                var sr = NewSprite("Conveyor", buildingsRoot);
                var belt = sr.gameObject.AddComponent<Conveyor>();
                belt.Init(Placing, c, map.Heights[c.x, c.y], sr);
                belt.In = ins[i];
                belt.Out = outs[i];
                Register(belt);
                conveyors.Add(belt);
                placed.Add(belt);
            }

            if (feeder != null) feeder.Out = map.DirTo(feeder.Cell, beltPath[0]);
            if (receiver != null) receiver.In = map.DirTo(receiver.Cell, beltPath[count - 1]);
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
            LayBelt(c.transform, c.Cell, c.Level, c.In, c.Out, inLift, outLift);
        }

        /// <summary>
        /// Lays a belt sprite flat on its cell; a belt with a raised end is lifted and tilted into a ramp from its
        /// input side to its output side.
        /// </summary>
        void LayBelt(Transform t, Vector2Int cell, int level, int inSide, int outSide, int inLift, int outLift, float extraLift = 0f)
        {
            PlanetArt.LayFlat(t, map, cell, level, ConveyorArt.SurfaceHeight + extraLift);
            if (inLift == 0 && outLift == 0) return;
            var up = map.Up(cell);
            var flow = Vector3.ProjectOnPlane(
                map.CellDir(cell, Dir.Mid(outSide)) - map.CellDir(cell, Dir.Mid(inSide)), up).normalized;
            float slope = (outLift - inLift) * IsoGrid.StepHeight;
            var tilted = (up - flow * slope).normalized;
            t.SetPositionAndRotation(t.position + up * ((inLift + outLift) * 0.5f * IsoGrid.StepHeight),
                Quaternion.FromToRotation(up, tilted) * t.rotation);
        }

        /// <summary>1 when the neighbour stands one level higher (this tile ramps up to it), otherwise 0.</summary>
        int LiftTo(Vector2Int cell, Vector2Int? neighbour) =>
            neighbour.HasValue ? Mathf.Clamp(HeightAt(neighbour.Value) - HeightAt(cell), 0, 1) : 0;

        bool TooSteep(Conveyor c, Vector2Int cell) => Mathf.Abs(c.Level - HeightAt(cell)) > MaxFootprintSlope;

        int HeightAt(Vector2Int c) => map.InBounds(c) ? map.Heights[c.x, c.y] : 0;

        void AnimateBelts()
        {
            int frame = (int)(Time.time * BeltFps) % ConveyorArt.Frames;
            if (frame == beltFrame) return;
            beltFrame = frame;
            foreach (var c in conveyors) c.ShowFrame(frame);
        }

        // ---------------------------------------------------------------- deleting

        /// <summary>The building under a screen point: a model the ray hits first, or whatever stands on the ground it hits.</summary>
        Building PickBuilding(Vector2 screen)
        {
            var ray = cam.ScreenPointToRay(screen);
            var hits = Physics.RaycastAll(ray, 10000f);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits)
            {
                if (view != null && view.IsGround(hit.collider)) break;
                var b = hit.collider.GetComponentInParent<Building>();
                if (b != null && buildings.Contains(b)) return b;
            }
            return map.Pick(cam, screen, out var cell) ? map.BuildingAt(cell) : null;
        }

        /// <summary>Removes a building (refunding it if asked); anything it carries or holds goes with it.</summary>
        public void RemoveBuilding(Building b, bool refund)
        {
            if (b == null || b.IsCore) return;
            foreach (var c in b.Cells)
            {
                if (map.BuildingAt(c) == b) map.Buildings[c.x, c.y] = null;
                if (decor != null && map.IsOre(c)) decor.SetDepositVisible(c, true);
            }
            buildings.Remove(b);
            if (b is Miner miner) miners.Remove(miner);
            if (refund)
            {
                ChangeGold(Mathf.RoundToInt(b.Def.Cost * RefundFraction));
                Sfx.Play(Sfx.Lib.Demolish, b.Center);
            }
            Sfx.StopLoop(b);
            Destroy(b.gameObject); // takes any crystals riding a belt or held inside with it
            if (b is Conveyor belt)
            {
                conveyors.Remove(belt);
                // Neighbours that ramped up to it flatten out again.
                for (int d = 0; d < map.Sides(belt.Cell); d++)
                    if (map.BuildingAt(map.Neighbour(belt.Cell, d)) is Conveyor n) RefreshBelt(n);
            }
            if (b is Wall) RefreshWalls(b.Cells); // neighbours let go of it
            BuildingsChanged?.Invoke();
        }

        void Register(Building b)
        {
            buildings.Add(b);
            foreach (var c in b.Cells)
            {
                map.Buildings[c.x, c.y] = b;
                if (decor != null) decor.SetDepositVisible(c, false); // the miner sits on the crystals
            }
            BuildingsChanged?.Invoke();
        }

        // ---------------------------------------------------------------- ghosts

        void ShowGhost(BuildingCatalog.Entry e, Vector2Int origin, int level, bool ok)
        {
            var model = GhostModel(e);
            if (model != null)
            {
                model.transform.parent.SetPositionAndRotation(map.FootprintCenter(origin, e.Size, level), map.FootprintFrame(origin, e.Size));
                PlanetArt.Paint(model, PlanetArt.Ghost(ok ? ModelOk : ModelBad));
                model.transform.parent.gameObject.SetActive(true);
                shownGhost = model.transform.parent.gameObject;
            }
            foreach (var c in map.Footprint(origin, e.Size)) Mark(c, level, ok);
        }

        /// <summary>A see-through copy of the entry's model, made on first use.</summary>
        GameObject GhostModel(BuildingCatalog.Entry e)
        {
            if (e.Model == null) return null;
            if (ghostModels.TryGetValue(e, out var model) && model != null) return model;
            model = ghostModels[e] = BuildingModels.Spawn(e.Model, Vector3.zero, Quaternion.identity, Vector3.one * (e.Scale * IsoGrid.FootprintScale(e.Size)), 0f, ghostRoot);
            var fx = model.GetComponent<BuildingFx>();
            if (fx != null) fx.enabled = false;
            model.transform.parent.gameObject.SetActive(false);
            return model;
        }

        /// <summary>Marks one cell flat on the ground, green or red.</summary>
        void Mark(Vector2Int c, int level, bool ok)
        {
            if (usedMarks == marks.Count)
            {
                var m = NewSprite("Mark", transform);
                m.sprite = BlockArt.Highlight;
                marks.Add(m);
            }
            var mark = marks[usedMarks++];
            mark.enabled = true;
            mark.color = ok ? MarkOk : MarkBad;
            PlanetArt.LayFlat(mark.transform, map, c, Mathf.Max(level, HeightAt(c)), 0.04f);
        }

        SpriteRenderer BeltGhost(int i)
        {
            while (beltGhosts.Count <= i) beltGhosts.Add(NewSprite("BeltGhost", transform));
            return beltGhosts[i];
        }

        void HideGhosts()
        {
            if (shownGhost != null) shownGhost.SetActive(false);
            shownGhost = null;
            usedMarks = 0;
            foreach (var m in marks) m.enabled = false;
            foreach (var g in beltGhosts) g.enabled = false;
        }

        // ---------------------------------------------------------------- helpers

        SpriteRenderer NewSprite(string name, Transform parent)
        {
            var sr = new GameObject(name).AddComponent<SpriteRenderer>();
            sr.transform.SetParent(parent, false);
            if (spriteMaterial != null) sr.sharedMaterial = spriteMaterial;
            return sr;
        }
    }
}
