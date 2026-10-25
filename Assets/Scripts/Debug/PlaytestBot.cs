using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DungeonConveyor
{
    /// <summary>
    /// Automated playtest, only active when Unity is started with -playtest (see Editor/PlaytestRunner). Plays a few
    /// games at high speed through the same BuildController calls the player uses: mines the nearest ore through a
    /// Refiner and a Splitter into the Core, checks the placement rules, lengthens the enemy path with walls, buys
    /// towers where they cover the enemy path, and lets waves run. The last game builds no defences, to check that the
    /// Core can fall. Writes a report of checks and metrics, then quits.
    /// Args: -playtestGames N, -playtestMinutes M (game time per game), -playtestSpeed S, -playtestReport path.
    /// </summary>
    public class PlaytestBot : MonoBehaviour
    {
        static readonly StringBuilder report = new StringBuilder();
        static readonly List<string> errorSamples = new List<string>();
        static int games, errors, gamesPlayed, passes, fails;
        static float minutes, speed;
        static string reportPath;
        static bool started;

        IsoGrid map;
        BuildController build;
        ItemFlow flow;
        WaveManager waves;
        Combat combat;
        readonly List<Conveyor> splitterOutputs = new List<Conveyor>();
        readonly bool[] splitterOutputSeen = new bool[2];

        // ---------------------------------------------------------------- bootstrapping

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoStart()
        {
            if (started || !HasArg("-playtest")) return;
            started = true;
            Meta.Disabled = true; // playtests share the player's PlayerPrefs: never touch their save
            Sfx.Muted = true;
            games = Mathf.Max(1, (int)ArgFloat("-playtestGames", 3));
            minutes = ArgFloat("-playtestMinutes", 10f);
            speed = ArgFloat("-playtestSpeed", 8f);
            reportPath = ArgString("-playtestReport", Path.Combine(Application.dataPath, "../PlaytestReport.txt"));
            Application.logMessageReceived += OnLog;
            SceneManager.sceneLoaded += (s, m) => Attach();
            report.AppendLine($"DungeonConveyor playtest  {System.DateTime.Now:yyyy-MM-dd HH:mm}  games={games} minutes={minutes} speed={speed}x");
            Attach();
        }

        static void Attach()
        {
            var grid = FindAnyObjectByType<IsoGrid>();
            if (grid == null) { Line("FATAL: no IsoGrid in the scene"); Finish(); return; }
            if (grid.GetComponent<PlaytestBot>() == null) grid.gameObject.AddComponent<PlaytestBot>();
        }

        static void OnLog(string message, string stack, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            // Editor-internal noise in batch mode (search indexer, licensing), not the game.
            if (stack != null && (stack.Contains("UnityEditor.Search") || stack.Contains("Licensing"))) return;
            errors++;
            if (errorSamples.Count < 12) errorSamples.Add($"[{type}] {message}\n      {FirstLine(stack)}");
        }

        static string FirstLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            int i = s.IndexOf('\n');
            return i < 0 ? s : s.Substring(0, i);
        }

        static bool HasArg(string name) => System.Array.IndexOf(System.Environment.GetCommandLineArgs(), name) >= 0;

        static string ArgString(string name, string fallback)
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        }

        static float ArgFloat(string name, float fallback) =>
            float.TryParse(ArgString(name, ""), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;

        static void Line(string s) => report.AppendLine(s);

        static void Check(string name, bool ok, string detail)
        {
            if (ok) passes++; else fails++;
            Line($"  [{(ok ? "PASS" : "FAIL")}] {name}: {detail}");
        }

        // ---------------------------------------------------------------- one game

        IEnumerator Start()
        {
            map = GetComponent<IsoGrid>();
            build = GetComponent<BuildController>();
            flow = GetComponent<ItemFlow>();
            waves = GetComponent<WaveManager>();
            combat = GetComponent<Combat>();
            yield return null; // let every component's Start run
            yield return null;

            bool defenceless = gamesPlayed == games - 1 && games > 1;
            Line("");
            Line($"=== Game {gamesPlayed + 1}/{games}{(defenceless ? "  (no defences: Core should fall)" : "")} ===");
            DescribeMap();
            GameSpeed.Speed = speed;
            waves.AutoPickCards = true;
            Run.UnlockAll(build.Catalog); // the bot tests every building, shop or not

            int elites = 0, siteShifts = 0, wrecked = 0, spiresClaimed = 0;
            waves.WaveStarted += (w, m) => { if (m != EliteModifier.None) { elites++; Line($"  {Time.time / 60f,4:0.0}m wave {w} is ELITE: {m}"); } };
            waves.LandingSitesChanged += added => { siteShifts++; Line($"  {Time.time / 60f,4:0.0}m landing site {(added ? "added" : "moved")} ({map.Spawns.Count} sites)"); };
            waves.BeltWrecked += at => wrecked++;
            var spires = GetComponent<SpireRelics>();
            if (spires != null) spires.Claimed += (at, relic) => { spiresClaimed++; Line($"  spire claimed: {(relic != null ? relic.Title : "(no relic left)")}"); };

            WallTests();
            WallLineTest();
            BuildEconomy();
            CheckRules();
            FeatureChecks();

            float end = Time.time + minutes * 60f;
            float nextBuy = 0f, nextSample = 0f;
            var minedPatch = build.Miners.Count > 0 && build.Miners[0].Patches.Count > 0 ? build.Miners[0].Patches[0] : null;
            int patch0Start = minedPatch != null ? minedPatch.Remaining : 0;
            float depletedAt = -1f;
            Line("  time  wave  gold  core  alive kills leaked sold(refined) oreGold dmg  buildings");
            while (Time.time < end && !build.Core.Destroyed)
            {
                if (defenceless && waves.Wave < 30 && waves.NextWaveIn > 3f) waves.CallEarly(); // hurry the end
                if (!defenceless && Time.time >= nextBuy)
                {
                    nextBuy = Time.time + 1.5f;
                    BuyDefences();
                }
                for (int i = 0; i < splitterOutputs.Count; i++)
                    if (splitterOutputs[i] != null && splitterOutputs[i].Items.Count > 0) splitterOutputSeen[i] = true;
                if (depletedAt < 0f && minedPatch != null && minedPatch.Remaining == 0) depletedAt = Time.time;
                if (Time.time >= nextSample)
                {
                    nextSample = Time.time + 30f;
                    Sample();
                }
                yield return null;
            }
            Sample();

            // ---- results
            Line("  checks:");
            Check("ore sold at the Core", flow.Delivered > 0, $"{flow.Delivered} crystals, {flow.GoldFromOre} gold");
            if (refinerBuilt) Check("refiner refines", flow.DeliveredRefined > 0, $"{flow.DeliveredRefined} refined crystals sold");
            if (sideJoinBuilt) Check("belts feed into each other from the side", flow.SideLoads > 0, $"{flow.SideLoads} crystals merged in from the side");
            if (splitterOutputs.Count == 2)
                Check("splitter feeds both outputs", splitterOutputSeen[0] && splitterOutputSeen[1], $"output A {(splitterOutputSeen[0] ? "used" : "idle")}, output B {(splitterOutputSeen[1] ? "used" : "idle")}");
            if (minedPatch != null)
                Check("ore patches deplete", minedPatch.Remaining < patch0Start,
                    $"mined patch {patch0Start} -> {minedPatch.Remaining}{(depletedAt >= 0f ? $", ran dry at {depletedAt / 60f:0.0} min" : "")}");
            Check("enemies spawned", waves.Kills + waves.Leaked + Enemy.All.Count > 0, $"{waves.Wave} waves, {waves.Kills} killed, {waves.Leaked} reached the Core");
            if (waves.Wave >= waves.EliteFrom + 3) Check("elite waves come", elites > 0, $"{elites} elite waves");
            if (waves.Wave - Run.FirstWave >= waves.ShiftLandingEvery + 1) Check("landing sites shift", siteShifts > 0, $"{siteShifts} changes, {map.Spawns.Count} sites now");
            Line($"  extras: {wrecked} belts wrecked by saboteurs, {spiresClaimed} spires claimed, relics: {Run.Relics.Count}, synergies: {Run.Synergies.Count}, alloys sold: {flow.DeliveredAlloys}, crystals fed to towers: {flow.FedToTowers}");
            if (Run.IsBossWave(waves.Wave) && !build.Core.Destroyed && waves.Alive == 0)
                Check("boss drops a relic", Run.Relics.Count > spiresClaimed, $"{Run.Relics.Count} relics ({spiresClaimed} from spires)");
            if (!defenceless)
                Check("towers kill enemies", waves.Kills > 0, $"{combat.DamageDealt:0} damage dealt");
            if (defenceless)
                Check("Core falls without defences", build.Core.Destroyed, $"Core health {build.Core.Health:0}/{build.Core.MaxHealth:0}");
            else
                Line($"  outcome: {(build.Core.Destroyed ? $"Core destroyed in wave {waves.Wave}" : $"survived to wave {waves.Wave} with Core at {build.Core.Health:0}/{build.Core.MaxHealth:0}")}");

            gamesPlayed++;
            GameSpeed.Speed = 1f;
            Run.Reset();
            if (gamesPlayed < games) SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            else Finish();
        }

        /// <summary>Direct checks of the new systems that the bot's normal play may not reach.</summary>
        void FeatureChecks()
        {
            if (map.Spawns.Count > 0)
            {
                var landing = map.Spawns[0].Landing;
                Check("fliers have a route", waves.Flows.Air.Distance(landing) != FlowField.Unreachable, $"air route {waves.Flows.Air.Distance(landing)} steps vs ground {waves.Flow.Distance(landing)}");
                if (build.Conveyors.Count > 0)
                    Check("saboteurs can reach belts", waves.Flows.Belts.Distance(landing) != FlowField.Unreachable, $"{waves.Flows.Belts.Distance(landing)} steps to the nearest belt");
            }
            Check("pentagon spires exist", CountPentagons() == 12, $"{CountPentagons()} spires");

            // A crystal fed to a tower boosts it.
            var turret = build.Catalog.Find("Turret");
            Tower tower = null;
            for (int x = 0; x < map.Width && tower == null; x++)
            {
                var c = new Vector2Int(x, 0);
                if (map.DistanceToBase(c) < 6f || !build.CanPlace(turret, c, out _, out _)) continue;
                tower = build.TryPlace(turret, c) as Tower;
            }
            if (tower != null)
            {
                bool took = tower.TryAccept(new BeltItem { Kind = 0 });
                Check("crystals boost towers", took && tower.AnyBoost, $"boost {tower.Boost[0]:0}s speed / {tower.Boost[1]:0}s range / {tower.Boost[2]:0}s damage");
                build.RemoveBuilding(tower, refund: true);
            }

            // Synergy and relic cards apply.
            int relicsBefore = Run.Relics.Count;
            var relic = Run.RandomRelic();
            if (relic != null)
            {
                Run.Apply(relic);
                Check("relics apply", Run.Relics.Count == relicsBefore + 1, relic.Title);
            }
            var syn = Run.MakeSynergy(Run.Synergy.ChainReaction);
            Run.Apply(syn);
            Check("synergies apply", Run.Has(Run.Synergy.ChainReaction), syn.Title);
        }

        int CountPentagons()
        {
            int n = 0;
            for (int x = 0; x < map.Width; x++) if (map.IsPentagon(new Vector2Int(x, 0))) n++;
            return n;
        }

        void DescribeMap()
        {
            Line($"  planet: {map.Width * map.Height} hex tiles (frequency {map.N}), {map.Spawns.Count} landing sites, {map.Patches.Count} ore patches");
            var kinds = new HashSet<int>();
            foreach (var p in map.Patches)
            {
                kinds.Add(p.Kind);
                var type = IsoGrid.OreTypeOf(p.Kind);
                Line($"    {type.Name,-9} {p.Cells.Count} tiles, {p.Initial,4} crystals, {type.Value} gold each, {map.DistanceToBase(p.Cells[0]):0} cells from base");
            }
            Check("ore patches vary", kinds.Count > 1 || map.Patches.Count < 3, $"{kinds.Count} ore types across {map.Patches.Count} patches");
            int unreachable = 0;
            foreach (var s in map.Spawns)
                if (waves.Flow.Distance(s.Landing) == FlowField.Unreachable) unreachable++;
            Check("every spawn can reach the Core", unreachable == 0, $"{map.Spawns.Count - unreachable}/{map.Spawns.Count} reachable");
            Check("planet neighbours are symmetric", NeighboursSymmetric(out string bad), bad ?? "every step can be walked back");
        }

        /// <summary>Stepping to any neighbour and back returns to the same cell.</summary>
        bool NeighboursSymmetric(out string bad)
        {
            bad = null;
            for (int x = 0; x < map.Width; x++)
                for (int y = 0; y < map.Height; y++)
                {
                    var c = new Vector2Int(x, y);
                    for (int d = 0; d < map.Sides(c); d++)
                    {
                        var n = map.Neighbour(c, d);
                        if (n == c || map.DirTo(n, c) < 0) { bad = $"{c} side {d} -> {n} doesn't lead back"; return false; }
                    }
                }
            return true;
        }

        void Sample()
        {
            Line($"  {Time.time / 60f,4:0.0}m {waves.Wave,4} {build.Gold,6} {build.Core.Health,5:0} {waves.Alive,5} {waves.Kills,5} {waves.Leaked,6} {flow.Delivered,6}({flow.DeliveredRefined}) {flow.GoldFromOre,7} {combat.DamageDealt,6:0} {build.Buildings.Count,6}");
        }

        // ---------------------------------------------------------------- economy

        bool refinerBuilt;

        void BuildEconomy()
        {
            var miner = build.Catalog.Find("Miner");
            var refiner = build.Catalog.Find(BuildingCatalog.RefinerId);
            var splitter = build.Catalog.Find(BuildingCatalog.SplitterId);
            var patches = new List<IsoGrid.OrePatch>(map.Patches);
            patches.Sort((a, b) => map.DistanceToBase(a.Cells[0]).CompareTo(map.DistanceToBase(b.Cells[0])));

            int lines = 0;
            foreach (var patch in patches)
            {
                if (lines >= 2) break;
                var m = PlaceMinerOn(miner, patch);
                if (m == null) continue;
                bool ok = lines == 0 ? MinerToCoreVia(m, refiner, out refinerBuilt) : MinerToCoreViaSplitter(m, splitter);
                if (!ok) ok = RouteBelt(Around(m), IsBesideCore, null) != null;
                Line($"  economy: miner on {IsoGrid.OreTypeOf(patch.Kind).Name} at {m.Origin} -> {(ok ? "belt to Core" : "NO ROUTE to Core")}");
                if (ok) lines++;
            }
            Check("miner lines built", lines > 0, $"{lines} lines, gold left {build.Gold}");
            if (lines > 0) SideJoin(miner, patches);
        }

        bool sideJoinBuilt;

        /// <summary>A third miner whose belt is dragged onto the side of an existing belt, as a player would.</summary>
        void SideJoin(BuildingCatalog.Entry miner, List<IsoGrid.OrePatch> patches)
        {
            foreach (var patch in patches)
            {
                bool used = false;
                foreach (var m in build.Miners) if (m.Patches.Contains(patch)) used = true;
                if (used) continue;
                var mine = PlaceMinerOn(miner, patch);
                if (mine == null) continue;

                // Route to a free tile beside an existing belt's side (not its input or output end), then onto that belt.
                Vector2Int joinBelt = default;
                bool BesideBeltSide(Vector2Int c)
                {
                    for (int d = 0; d < map.Sides(c); d++)
                        if (map.BuildingAt(map.Neighbour(c, d)) is Conveyor b && b.InCell != c && b.OutCell != c && Mathf.Abs(b.Level - map.Heights[c.x, c.y]) <= 1)
                        {
                            joinBelt = b.Cell;
                            return true;
                        }
                    return false;
                }
                var path = FindPath(Around(mine), BesideBeltSide, null);
                if (path == null) { build.RemoveBuilding(mine, refund: true); continue; }
                path.Add(joinBelt);
                int built = build.TryPlaceBelt(path);
                sideJoinBuilt = built == path.Count - 1;
                Line($"  economy: miner on {IsoGrid.OreTypeOf(patch.Kind).Name} at {mine.Origin} -> belt joins the side of the belt at {joinBelt} ({built} tiles)");
                return;
            }
        }

        /// <summary>Draws an L-shaped wall line in one go and checks the pieces snapped together, then removes it.</summary>
        void WallLineTest()
        {
            for (int x = 0; x < map.Width; x++)
                for (int y = 0; y < map.Height; y++)
                {
                    var s = new Vector2Int(x, y);
                    if (map.IsPentagon(s)) continue;
                    // Three steps straight on (out of the side opposite the one we came in by), a turn, two more.
                    var path = new List<Vector2Int> { s, map.Neighbour(s, 0) };
                    for (int i = 0; i < 4; i++)
                    {
                        var cur = path[path.Count - 1];
                        int back = map.DirTo(cur, path[path.Count - 2]);
                        if (back < 0) break;
                        path.Add(map.Neighbour(cur, i == 2 ? Dir.Wrap(back + 2) : Dir.Opposite(back)));
                    }
                    if (path.Count != 6) continue;
                    bool free = map.DistanceToBase(s) > 8f && new HashSet<Vector2Int>(path).Count == path.Count;
                    foreach (var c in path) free &= BeltFree(c, null) && !IsBesideCore(c);
                    if (!free) continue;

                    int built = build.TryPlaceWalls(path);
                    if (built == 0) continue;
                    var straight = map.BuildingAt(path[1]) as Wall;
                    var corner = map.BuildingAt(path[3]) as Wall;
                    Check("wall line drawn in one drag", built == path.Count, $"{built}/{path.Count} walls along an L at {s}");
                    Check("walls snap together", straight != null && corner != null && straight.PieceCount == 2 && corner.PieceCount == 3,
                        $"straight tile {(straight != null ? straight.PieceCount : 0)} pieces (want 2), corner {(corner != null ? corner.PieceCount : 0)} pieces (want 3: two halves + post)");
                    foreach (var c in path)
                        if (map.BuildingAt(c) is Wall w) build.RemoveBuilding(w, refund: true);
                    return;
                }
            Line("  (no room for the wall line test)");
        }

        Building PlaceMinerOn(BuildingCatalog.Entry miner, IsoGrid.OrePatch patch)
        {
            Building best = null;
            int bestOre = 0;
            Vector2Int bestOrigin = default;
            // Anchors on the patch and around it (a footprint anchored next to the ore can still cover it).
            var anchors = new HashSet<Vector2Int>();
            foreach (var c in patch.Cells)
            {
                anchors.Add(c);
                for (int d = 0; d < map.Sides(c); d++) anchors.Add(map.Neighbour(c, d));
            }
            foreach (var o in anchors)
            {
                if (!build.CanPlace(miner, o, out _, out _)) continue;
                int ore = 0;
                foreach (var c in map.Footprint(o, miner.Size))
                    if (map.IsOre(c)) ore++;
                if (ore > bestOre) { bestOre = ore; bestOrigin = o; }
            }
            if (bestOre > 0) best = build.TryPlace(miner, bestOrigin);
            return best;
        }

        /// <summary>Miner -> belt -> (1x1 Refiner) -> belt -> Core.</summary>
        bool MinerToCoreVia(Building m, BuildingCatalog.Entry refiner, out bool refined)
        {
            refined = false;
            var path = FindPath(Around(m), IsBesideCore, null);
            if (path == null) return false;
            if (refiner == null || refiner.Size != Vector2Int.one || path.Count < 6)
                return build.TryPlaceBelt(path) == path.Count;

            // Building first, so the belt ends hook onto it the way they do when a player draws them.
            int k = path.Count / 2;
            var first = path.GetRange(0, k);
            var rest = path.GetRange(k + 1, path.Count - k - 1);
            var r = build.TryPlace(refiner, path[k]);
            if (r == null)
            {
                Line($"  economy: refiner could not go at {path[k]} ({Reason(refiner, path[k])}); plain belt instead");
                return build.TryPlaceBelt(path) == path.Count;
            }
            refined = true;
            return build.TryPlaceBelt(first) == first.Count && build.TryPlaceBelt(rest) == rest.Count;
        }

        /// <summary>Miner -> belt -> Splitter -> two belts -> Core.</summary>
        bool MinerToCoreViaSplitter(Building m, BuildingCatalog.Entry splitter)
        {
            var path = FindPath(Around(m), IsBesideCore, null);
            if (path == null) return false;
            if (splitter == null || path.Count < 8) return build.TryPlaceBelt(path) == path.Count;

            int k = Mathf.Min(4, path.Count - 4);
            var input = path.GetRange(0, k);
            // Put the splitter so it touches the end of the input belt without overlapping it.
            Building s = null;
            var end = input[input.Count - 1];
            for (int d = 0; d < map.Sides(end) && s == null; d++)
            {
                var n = map.Neighbour(end, d);
                for (int e = -1; e < map.Sides(n) && s == null; e++)
                {
                    var o = e < 0 ? n : map.Neighbour(n, e);
                    var cells = map.Footprint(o, splitter.Size);
                    // It must touch the end of the input belt without overlapping the belt.
                    if (!cells.Contains(n) || cells.Exists(input.Contains) || !build.CanPlace(splitter, o, out _, out _)) continue;
                    s = build.TryPlace(splitter, o);
                }
            }
            if (s == null)
            {
                Line("  economy: no room for a splitter; plain belt instead");
                return build.TryPlaceBelt(path) == path.Count;
            }
            if (build.TryPlaceBelt(input) != input.Count) return false; // its end turns into the splitter

            // Two separate output belts from different sides of the splitter.
            var used = new HashSet<Vector2Int>(input);
            for (int i = 0; i < 2; i++)
            {
                var starts = new List<Vector2Int>();
                foreach (var c in Around(s))
                    if (!used.Contains(c) && !Touches(c, used)) starts.Add(c);
                var outPath = FindPath(starts, IsBesideCore, used);
                if (outPath == null) { Line($"  economy: splitter output {i + 1} has no route"); break; }
                if (build.TryPlaceBelt(outPath) == 0) break;
                if (map.BuildingAt(outPath[0]) is Conveyor first) splitterOutputs.Add(first);
                foreach (var c in outPath) used.Add(c);
            }
            Line($"  economy: splitter at {s.Origin} with {splitterOutputs.Count} output belts");
            return splitterOutputs.Count > 0;
        }

        bool Touches(Vector2Int c, HashSet<Vector2Int> cells)
        {
            for (int d = 0; d < map.Sides(c); d++) if (cells.Contains(map.Neighbour(c, d))) return true;
            return false;
        }

        bool IsBesideCore(Vector2Int c)
        {
            for (int d = 0; d < map.Sides(c); d++) if (map.IsBase(map.Neighbour(c, d))) return true;
            return false;
        }

        IEnumerable<Vector2Int> Around(Building b)
        {
            foreach (var cell in b.Cells)
                for (int d = 0; d < map.Sides(cell); d++)
                {
                    var c = map.Neighbour(cell, d);
                    if (!b.Covers(c)) yield return c;
                }
        }

        List<Vector2Int> RouteBelt(IEnumerable<Vector2Int> starts, System.Func<Vector2Int, bool> goal, HashSet<Vector2Int> avoid)
        {
            var path = FindPath(starts, goal, avoid);
            return path != null && build.TryPlaceBelt(path) == path.Count ? path : null;
        }

        /// <summary>Breadth-first belt route over free, non-ore tiles with climbable steps.</summary>
        List<Vector2Int> FindPath(IEnumerable<Vector2Int> starts, System.Func<Vector2Int, bool> goal, HashSet<Vector2Int> avoid)
        {
            var from = new Dictionary<Vector2Int, Vector2Int>();
            var queue = new Queue<Vector2Int>();
            foreach (var s in starts)
                if (BeltFree(s, avoid) && !from.ContainsKey(s)) { from[s] = s; queue.Enqueue(s); }
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                if (goal(c))
                {
                    var path = new List<Vector2Int> { c };
                    while (from[c] != c) { c = from[c]; path.Add(c); }
                    path.Reverse();
                    return path;
                }
                for (int d = 0; d < map.Sides(c); d++)
                {
                    var n = map.Neighbour(c, d);
                    if (from.ContainsKey(n) || !BeltFree(n, avoid) || Mathf.Abs(map.Heights[n.x, n.y] - map.Heights[c.x, c.y]) > 1) continue;
                    from[n] = c;
                    queue.Enqueue(n);
                }
            }
            return null;
        }

        bool BeltFree(Vector2Int c, HashSet<Vector2Int> avoid) =>
            map.HasCell(c) && !map.IsPentagon(c) && !map.IsBase(c) && !map.IsSpawn(c) && !map.IsOre(c) && map.BuildingAt(c) == null && (avoid == null || !avoid.Contains(c));

        string Reason(BuildingCatalog.Entry e, Vector2Int o)
        {
            build.CanPlace(e, o, out _, out var reason);
            return reason ?? "ok";
        }

        // ---------------------------------------------------------------- rules

        void CheckRules()
        {
            // Towers no longer need a conveyor nearby: one far from any belt must be placeable somewhere.
            var turret = build.Catalog.Find("Turret");
            string spot = null;
            for (int x = 0; x < map.Width && spot == null; x++)
                for (int y = 0; y < map.Height && spot == null; y++)
                {
                    var c = new Vector2Int(x, y);
                    if (!BeltFree(c, null) || NearBelt(c, 4 + Mathf.Max(turret.Size.x, turret.Size.y))) continue;
                    if (build.CanPlace(turret, c, out _, out _)) spot = c.ToString();
                }
            Check("towers can be placed away from conveyors", spot != null, spot ?? "no free spot away from belts accepted a turret");
        }

        bool NearBelt(Vector2Int c, int r)
        {
            foreach (var belt in build.Conveyors)
                if (map.Distance(c, belt.Cell) <= r) return true;
            return false;
        }

        /// <summary>Cells enemies walk from each landing to the Core, following the flow field.</summary>
        List<Vector2Int> EnemyPath()
        {
            waves.Flow.Rebuild();
            var cells = new List<Vector2Int>();
            foreach (var s in map.Spawns)
            {
                var c = s.Landing;
                for (int guard = 0; guard < 400; guard++)
                {
                    if (!cells.Contains(c)) cells.Add(c);
                    if (!waves.Flow.Next(c, out var next)) break;
                    c = next;
                }
            }
            return cells;
        }

        /// <summary>
        /// Walls every free tile next to the Core except the one furthest from the first spawn: enemies must walk
        /// around the Core, so their path gets longer, and walling that last gap must be refused. The test walls are
        /// removed (refunded) afterwards.
        /// </summary>
        void WallTests()
        {
            var wall = build.Catalog.Find("Wall");
            if (map.Spawns.Count == 0 || wall == null) return;
            var landing = map.Spawns[0].Landing;
            waves.Flow.Rebuild();
            int before = waves.Flow.Distance(landing);

            var ring = new List<Vector2Int>();
            foreach (var b in map.BaseCells(0))
                for (int d = 0; d < map.Sides(b); d++)
                {
                    var c = map.Neighbour(b, d);
                    if (!map.IsBase(c) && !ring.Contains(c) && waves.Flow.Distance(c) == 0) ring.Add(c);
                }
            ring.Sort((a, b) => map.Distance(b, landing).CompareTo(map.Distance(a, landing)));

            var walls = new List<Building>();
            for (int i = 1; i < ring.Count; i++)
            {
                var w = build.TryPlace(wall, ring[i]);
                if (w != null) walls.Add(w);
            }
            waves.Flow.Rebuild();
            int after = waves.Flow.Distance(landing);
            Check("walls lengthen the enemy path", walls.Count > 0 && after > before,
                $"{walls.Count} walls around the Core: path from spawn {before} -> {after} steps");

            if (ring.Count > 0)
            {
                build.CanPlace(wall, ring[0], out _, out var reason);
                Check("walls can't seal the enemy path", reason != null && reason.Contains("path"), $"last gap {ring[0]}: \"{reason ?? "allowed"}\"");
            }

            foreach (var w in walls) build.RemoveBuilding(w, refund: true);
            waves.Flow.Rebuild();
        }

        // ---------------------------------------------------------------- defence

        static readonly string[] BuyOrder =
            { "Turret", "Turret", "Flamethrower", "LandMine", "LaserTower", "MissileLauncher", "TeslaCoil", "Mortar", "ShieldGenerator", "Turret", "LaserTower", "MissileLauncher", "TeslaCoil", "Mortar", "Flamethrower" };
        int nextBuy;
        readonly Dictionary<string, int> placedTowers = new Dictionary<string, int>();

        void BuyDefences()
        {
            var id = BuyOrder[nextBuy % BuyOrder.Length];
            var e = build.Catalog.Find(id);
            if (e == null) { nextBuy++; return; }
            if (!build.CanAfford(e)) return;

            var path = EnemyPath();
            if (e.Attack == AttackKind.Mine)
            {
                // Mines go on the path, closest to the Core first.
                for (int i = path.Count - 1; i >= 0; i--)
                    if (build.TryPlace(e, path[i]) != null) { Bought(id); return; }
                nextBuy++;
                return;
            }

            if (!TryBestSpot(e, path))
            {
                // No belt near the enemy path: lay a short spur belt beside it so towers may stand there.
                LaySpur(path);
                if (!TryBestSpot(e, path)) nextBuy++;
            }
        }

        bool TryBestSpot(BuildingCatalog.Entry e, List<Vector2Int> path)
        {
            int bestScore = 0;
            Vector2Int best = default;
            foreach (var belt in build.Conveyors)
                for (int d = 0; d < map.Sides(belt.Cell); d++)
                {
                    var o = map.Neighbour(belt.Cell, d);
                    if (!build.CanPlace(e, o, out _, out _)) continue;
                    var center = map.FootprintUp(o, e.Size);
                    int score = 0;
                    foreach (var c in path)
                    {
                        float dist = map.Arc(map.Up(c), center);
                        if (dist <= e.Range && (e.Attack != AttackKind.Mortar || dist >= combat.MortarMinRange)) score++;
                    }
                    if (score > bestScore) { bestScore = score; best = o; }
                }
            if (bestScore < 2 || build.TryPlace(e, best) == null) return false;
            Bought(e.Id);
            return true;
        }

        void Bought(string id)
        {
            placedTowers[id] = placedTowers.TryGetValue(id, out var n) ? n + 1 : 1;
            nextBuy++;
            Line($"  {Time.time / 60f,4:0.0}m bought {id} (gold left {build.Gold})");
        }

        void LaySpur(List<Vector2Int> path)
        {
            // A free tile next to the enemy path, about a third of the way from the Core.
            for (int i = path.Count * 2 / 3; i >= 0; i--)
                for (int d = 0; d < map.Sides(path[i]); d++)
                {
                    var c = map.Neighbour(path[i], d);
                    if (!BeltFree(c, null) || path.Contains(c)) continue;
                    if (build.TryPlaceBelt(new List<Vector2Int> { c }) == 1) return;
                }
        }

        // ---------------------------------------------------------------- finish

        static void Finish()
        {
            Line("");
            Line($"=== Summary: {passes} checks passed, {fails} failed, {errors} errors/exceptions logged ===");
            foreach (var e in errorSamples) Line("  " + e);
            try { File.WriteAllText(reportPath, report.ToString()); }
            catch (System.Exception e) { Debug.Log("Playtest report could not be written: " + e.Message); }
            Debug.Log("PLAYTEST REPORT\n" + report);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(fails > 0 || errors > 0 ? 1 : 0);
#else
            Application.Quit();
#endif
        }
    }
}
