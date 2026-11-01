using UnityEngine;
using UnityEngine.InputSystem;

namespace DungeonConveyor
{
    /// <summary>
    /// The tutorial. On the first map of a run (while ShowAtStart is on, which it is until the player turns it off)
    /// the game opens paused on a few pages explaining how to play: the goal, mining and belts, defending, the enemies,
    /// and the rewards. After that a first-steps checklist sits at the right edge and ticks itself off as the player
    /// does each thing (mine, deliver, build a tower, survive a wave, feed a tower, claim a spire); it goes away once
    /// everything is done or it is closed. The HUD's Help button reopens the pages any time.
    /// </summary>
    [RequireComponent(typeof(BuildController))]
    public class Tutorial : MonoBehaviour
    {
        const string ShowKey = "Tutorial.ShowAtStart";

        /// <summary>Open the explanation at the start of a run (saved; on by default).</summary>
        public static bool ShowAtStart
        {
            get => PlayerPrefs.GetInt(ShowKey, 1) == 1;
            set { PlayerPrefs.SetInt(ShowKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        static Tutorial current;

        /// <summary>The explanation pages are up (the game is paused).</summary>
        public static bool PagesOpen => current != null && current.page >= 0;

        /// <summary>Opens the explanation pages (the HUD's Help button).</summary>
        public static void OpenPages()
        {
            if (current == null) return;
            current.page = 0;
            current.opened = Time.unscaledTime;
            Sfx.Play(Sfx.Lib.OpenWindow);
        }

        struct Page { public string Title, Body; }

        static readonly Page[] Pages =
        {
            new Page
            {
                Title = "WELCOME, KEEPER",
                Body = "Your <b>Core</b> stands on the <color=#FFD040>gold hexes</color>. Every wave, meteors carry monsters down onto the " +
                       "<color=#FF5040>red landing sites</color>, and they march to your Core. If its health runs out, the run is over.\n\n" +
                       "Survive every wave of the map to move on to the next, bigger planet. Your gold, upgrades and unlocks come with you.\n\n" +
                       "<b>Drag</b> to turn the planet, <b>scroll</b> to zoom, <b>WASD</b> or the arrow keys also turn it. <b>Space</b> or <b>F</b> snaps back to your Core.",
            },
            new Page
            {
                Title = "MINE AND HAUL",
                Body = "Gold comes from crystals. Put a <b>Miner</b> on an ore patch (the coloured hexes), then pick the <b>Conveyor</b>, " +
                       "press on a hex next to the miner, <b>drag</b> to the Core and let go: the belt finds its own way there. Every crystal sold at the Core pays gold and banks " +
                       "crystals for the shop between maps.\n\n" +
                       "Rarer ores lie further out and are worth more. A <b>Refiner</b> on the belt makes crystals worth 2.5x, and if two " +
                       "<b>different</b> ores reach a Refiner it fuses them into an <b>alloy</b> worth far more.\n\n" +
                       "<b>Right-click</b> a building to remove it (refunded); hold right-click and sweep to clear belts.",
            },
            new Page
            {
                Title = "DEFEND",
                Body = "Build towers along the <color=#FF5040>red arrows</color>: that's the path the enemies will take. Walls and buildings " +
                       "bend the path, but you can never block it completely.\n\n" +
                       "Towers on <b>high ground</b> reach further, and enemies slow down climbing uphill.\n\n" +
                       "Run a belt into a tower to <b>feed it crystals</b> for a boost: sapphire = attack speed, emerald = range, " +
                       "amethyst = damage, gold = speed and damage, ruby or alloys = everything.",
            },
            new Page
            {
                Title = "KNOW YOUR ENEMY",
                Body = "<b>Fast</b> ones run past slow defences. <b>Armored</b> ones take half damage from bullets and lasers: use explosives " +
                       "and elements. <b>Fliers</b> cross walls and cliffs and ignore mines and flames. <b>Saboteurs</b> go for your belts first " +
                       "and wreck them.\n\n" +
                       "Some waves are <color=#D880FF>ELITE</color>: every enemy gets a shield, splits in two, sabotages, runs faster or " +
                       "regenerates. The wave panel warns you before one comes. Every few waves the meteors also pick a <b>new landing " +
                       "site</b>, so keep an eye on the red arrows at the screen edge.",
            },
            new Page
            {
                Title = "GROW STRONGER",
                Body = "Every 5 waves you pick an <b>upgrade card</b>; rare ones can be powerful <b>synergies</b>. Beating a <b>boss</b> lets " +
                       "you claim a <b>relic</b> that lasts the whole run.\n\n" +
                       "The 12 dark <b>rock spires</b> each hide a relic too: build anything next to one to claim it.\n\n" +
                       "Every run, won or lost, earns <color=#C09AFF>Essence</color>. Spend it in the main menu's <b>Upgrades</b> on permanent " +
                       "bonuses and new Core types.\n\nGood luck, Keeper.",
            },
        };

        static readonly string[] Steps =
        {
            "Place a Miner on ore",
            "Belt crystals to the Core",
            "Build a tower",
            "Survive the first wave",
            "Feed a tower crystals by belt",
            "Claim a rock spire",
        };

        BuildController build;
        WaveManager waves;
        ItemFlow flow;
        SpireRelics spires;
        int page = -1;
        float opened;
        bool checklist;
        readonly bool[] done = new bool[Steps.Length];
        readonly float[] doneAt = new float[Steps.Length];
        int spiresAtStart = -1;

        void Awake() => current = this;

        void OnDestroy()
        {
            if (current == this) current = null;
        }

        void Start()
        {
            build = GetComponent<BuildController>();
            waves = GetComponent<WaveManager>();
            flow = GetComponent<ItemFlow>();
            spires = GetComponent<SpireRelics>();
            bool firstMap = Run.Stage == Run.StartStage;
            if (ShowAtStart && firstMap && !Application.isBatchMode)
            {
                page = 0;
                opened = Time.unscaledTime;
                checklist = true;
            }
        }

        void Update()
        {
            var k = Keyboard.current;
            if (page >= 0 && k != null && !SettingsMenu.IsOpen)
            {
                if (k.rightArrowKey.wasPressedThisFrame || k.enterKey.wasPressedThisFrame || k.spaceKey.wasPressedThisFrame) Next();
                else if (k.leftArrowKey.wasPressedThisFrame && page > 0) { page--; Sfx.Play(Sfx.Lib.PageTurn); }
                else if (k.escapeKey.wasPressedThisFrame) page = -1;
            }
            if (!checklist) return;
            if (spiresAtStart < 0 && spires != null) spiresAtStart = spires.Unclaimed;
            Mark(0, build.Miners.Count > 0);
            Mark(1, flow != null && flow.Delivered > 0);
            bool tower = false;
            foreach (var b in build.Buildings) if (b is Tower) { tower = true; break; }
            Mark(2, tower);
            Mark(3, waves != null && waves.Wave >= waves.FirstWave && waves.Alive == 0);
            Mark(4, flow != null && flow.FedToTowers > 0);
            Mark(5, spires != null && spiresAtStart >= 0 && spires.Unclaimed < spiresAtStart);
            bool all = true;
            foreach (var d in done) all &= d;
            if (all && Time.unscaledTime - LastDone > 3f) checklist = false;
        }

        float LastDone
        {
            get
            {
                float t = 0f;
                foreach (var x in doneAt) t = Mathf.Max(t, x);
                return t;
            }
        }

        void Mark(int i, bool now)
        {
            if (done[i] || !now) return;
            done[i] = true;
            doneAt[i] = Time.unscaledTime;
            Sfx.Play(Sfx.Lib.StepDone);
        }

        void Next()
        {
            Sfx.Play(Sfx.Lib.PageTurn);
            if (page < Pages.Length - 1) page++;
            else page = -1;
        }

        void OnGUI()
        {
            if (build != null && build.Core != null && build.Core.Destroyed) return;
            if (page < 0 && !checklist) return;
            GUI.depth = -50; // above the HUD, below the settings window
            MenuGui.Begin();
            if (page >= 0) DrawPage();
            else if (!SettingsMenu.IsOpen)
            {
                DrawChecklist();
                DrawGuide();
            }
        }

        // ---------------------------------------------------------------- guide pointer

        IsoGrid map;
        Camera cam;
        FlowField guidePath;
        float guideRefresh;
        Vector2Int? guideCell;
        int guideStep = -1;
        static Texture2D pointer;

        /// <summary>
        /// Points at what to do next for the first unticked step: the build tab, then the building's slot, then the
        /// spot on the planet to put it, with a short instruction beside the arrow.
        /// </summary>
        void DrawGuide()
        {
            if (Time.timeScale == 0f) return; // a card pick or end screen is up
            int step = System.Array.IndexOf(done, false);
            if (step < 0) return;
            if (map == null) map = GetComponent<IsoGrid>();
            if (cam == null) cam = Camera.main;
            if (map == null || cam == null || build.Catalog == null) return;

            var placing = build.Placing;
            BuildingCatalog.Entry want = null;
            string pick = null, place = null;
            bool world = false;
            switch (step)
            {
                case 0:
                    want = FirstEntry(e => e.NeedsOre);
                    pick = "Pick the Miner";
                    place = "Put the Miner on ore (the coloured hexes)";
                    break;
                case 1:
                case 4:
                    want = build.Catalog.Find(BuildingCatalog.ConveyorId);
                    pick = "Pick the Conveyor";
                    place = step == 1 ? "Press next to the Miner, drag to the Core and let go" : "Drag a belt from a Miner into a tower";
                    break;
                case 2:
                    want = FirstEntry(e => e.Attack != AttackKind.None && e.Category == BuildCategory.Defense);
                    pick = "Pick a tower";
                    place = "Build it beside the red arrows";
                    break;
                case 3:
                    if (waves == null || waves.Wave >= waves.FirstWave) return;
                    if (HUD.Anchor("send", out var send)) DrawPointer(send, "Ready? Send the first wave (or wait for it)");
                    return;
                case 5:
                    world = true;
                    place = "Build anything next to a rock spire to claim its relic";
                    break;
            }
            if (want != null && placing != want)
            {
                if (HUD.Anchor("slot:" + want.Id, out var slot)) DrawPointer(slot, pick);
                else if (HUD.Anchor("tab:" + want.Category, out var tab)) DrawPointer(tab, $"Open {want.Category.ToString().ToUpper()}");
                return;
            }
            if (want != null) world = true;
            if (!world) return;

            if (guideStep != step || Time.unscaledTime > guideRefresh)
            {
                guideStep = step;
                guideRefresh = Time.unscaledTime + 1f;
                guideCell = GuideCell(step);
            }
            if (!guideCell.HasValue) return;
            var p = map.SurfaceCenter(guideCell.Value);
            var sp = cam.WorldToScreenPoint(p);
            bool facing = Vector3.Dot(map.Up(guideCell.Value), cam.transform.position - p) > 0f;
            if (sp.z <= 0f || !facing)
            {
                DrawPointer(new Rect(Screen.width * 0.5f - 1f, Screen.height * 0.3f, 2f, 2f), place + "  ·  press F to face your Core", false);
                return;
            }
            float r = 26f * MenuGui.S;
            var gp = new Vector2(Mathf.Clamp(sp.x, r, Screen.width - r), Mathf.Clamp(Screen.height - sp.y, r, Screen.height - r));
            DrawPointer(new Rect(gp.x - r, gp.y - r, 2f * r, 2f * r), place);
        }

        BuildingCatalog.Entry FirstEntry(System.Predicate<BuildingCatalog.Entry> match)
        {
            foreach (var e in build.Catalog.Entries)
                if (e.Purchasable && Run.IsUnlocked(e.Id) && match(e)) return e;
            return null;
        }

        /// <summary>Where on the planet the current step wants the player to build.</summary>
        Vector2Int? GuideCell(int step)
        {
            var core = map.BaseOrigin;
            switch (step)
            {
                case 0:
                    return Nearest(core, c => map.IsOre(c) && map.BuildingAt(c) == null);
                case 1:
                case 4:
                {
                    // A free hex beside the nearest miner.
                    Miner best = null;
                    foreach (var m in build.Miners)
                        if (best == null || Arc(m.Origin, core) < Arc(best.Origin, core)) best = m;
                    if (best == null) return null;
                    foreach (var c in best.Cells)
                        for (int d = 0; d < map.Sides(c); d++)
                        {
                            var n = map.Neighbour(c, d);
                            if (map.BuildingAt(n) == null && !map.IsOre(n) && !map.IsPentagon(n) && !map.IsBase(n)) return n;
                        }
                    return best.Origin;
                }
                case 2:
                {
                    // A few steps out from the Core along the enemies' path.
                    if (guidePath == null) guidePath = new FlowField(map);
                    guidePath.Rebuild();
                    if (map.Spawns.Count == 0) return null;
                    var c = map.Spawns[0].Landing;
                    int start = guidePath.Distance(c);
                    if (start == FlowField.Unreachable) return null;
                    while (guidePath.Distance(c) > Mathf.Min(6, start / 2) && guidePath.Next(c, out var next)) c = next;
                    for (int d = 0; d < map.Sides(c); d++)
                    {
                        var n = map.Neighbour(c, d);
                        if (map.BuildingAt(n) == null && !map.IsOre(n) && !map.IsPentagon(n) && !map.IsBase(n) && !map.IsSpawn(n)) return n;
                    }
                    return c;
                }
                case 5:
                {
                    var spires = GetComponent<SpireRelics>();
                    if (spires == null) return null;
                    Vector2Int? best = null;
                    foreach (var c in spires.UnclaimedCells)
                        if (best == null || Arc(c, core) < Arc(best.Value, core)) best = c;
                    return best;
                }
            }
            return null;
        }

        float Arc(Vector2Int a, Vector2Int b) => Vector3.Angle(map.Up(a), map.Up(b));

        Vector2Int? Nearest(Vector2Int from, System.Predicate<Vector2Int> match)
        {
            Vector2Int? best = null;
            float bestArc = float.MaxValue;
            for (int i = 0; i < map.CellCount; i++)
            {
                var c = new Vector2Int(i, 0);
                if (!match(c)) continue;
                float a = Arc(c, from);
                if (a < bestArc) { bestArc = a; best = c; }
            }
            return best;
        }

        /// <summary>
        /// A pulsing ring around the target (GUI pixels) and a bobbing arrow pointing at it from a note with the
        /// instruction, placed toward the screen centre. Doesn't block clicks.
        /// </summary>
        void DrawPointer(Rect target, string text, bool arrow = true)
        {
            if (Event.current.type != EventType.Repaint) return;
            float s = MenuGui.S, t = Time.unscaledTime;
            float pulse = 0.5f + 0.5f * Mathf.Sin(t * 5f);
            if (arrow)
            {
                GUI.color = new Color(UIStyle.Accent.r, UIStyle.Accent.g, UIStyle.Accent.b, 0.45f + 0.5f * pulse);
                UIStyle.GlowRing(target, s, 10f + 6f * pulse);
            }

            var center = new Vector2(Screen.width, Screen.height) * 0.5f;
            var dir = target.center - center;
            dir = dir.sqrMagnitude < 1f ? Vector2.down : dir.normalized;
            float reach = (Mathf.Max(target.width, target.height) * 0.5f + 30f * s);
            float bob = (HudFx.ReducedMotion ? 0f : Mathf.Sin(t * 6f) * 8f) * s;
            var tip = target.center - dir * (reach + bob);
            float aw = 46f * s;
            var tail = tip - dir * aw;

            // The note: centred beyond the arrow's tail, kept on screen.
            var style = MenuGui.Label;
            var content = new GUIContent(text);
            float w = Mathf.Min(style.CalcSize(content).x + 40f * s, 620f * s), h = 52f * s;
            var note = new Rect(tail.x - w * 0.5f - dir.x * w * 0.5f, tail.y - h * 0.5f - dir.y * (h * 0.5f + 6f * s), w, h);
            note.x = Mathf.Clamp(note.x, 8f * s, Screen.width - w - 8f * s);
            note.y = Mathf.Clamp(note.y, 8f * s, Screen.height - h - 8f * s);
            GUI.color = new Color(0.06f, 0.04f, 0.03f, 0.88f);
            GUI.DrawTexture(note, UIStyle.White);
            GUI.color = new Color(UIStyle.Accent.r, UIStyle.Accent.g, UIStyle.Accent.b, 0.9f);
            GUI.DrawTexture(new Rect(note.x, note.yMax - 3f * s, note.width, 3f * s), UIStyle.White);
            GUI.color = Color.white;
            MenuGui.Text(new Rect(note.x + 20f * s, note.y, note.width - 40f * s, note.height), text, style, UIStyle.Parchment);

            if (!arrow) return;
            if (pointer == null) pointer = MakePointer();
            var save = GUI.matrix;
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f; // the texture points down (+y on screen)
            GUIUtility.RotateAroundPivot(angle, tip);
            GUI.color = UIStyle.Accent;
            GUI.DrawTexture(new Rect(tip.x - aw * 0.5f, tip.y - aw, aw, aw), pointer);
            GUI.matrix = save;
            GUI.color = Color.white;
        }

        /// <summary>A white arrow pointing down (its tip at the bottom centre), with a dark outline.</summary>
        static Texture2D MakePointer()
        {
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    // Texture rows run bottom-up; GUI draws row n-1 at the top, so the tip (row 0) is at the bottom.
                    float u = (x + 0.5f) / n - 0.5f, v = (y + 0.5f) / n;
                    float head = v < 0.55f ? Mathf.Abs(u) - v * 0.85f : float.MaxValue;     // triangle, widest at v = 0.55
                    float shaft = v >= 0.5f ? Mathf.Abs(u) - 0.13f : float.MaxValue;        // stem above it
                    float d = Mathf.Min(head, shaft);
                    if (v > 0.97f) d = Mathf.Max(d, 0.01f);
                    px[y * n + x] = d < -0.04f ? Color.white : d < 0f ? new Color(0.1f, 0.06f, 0.02f, 1f) : Color.clear;
                }
            tex.SetPixels(px);
            tex.Apply(false, true);
            return tex;
        }

        void DrawPage()
        {
            MenuGui.Dim(0.55f);
            var p = Pages[page];
            const float w = 900f, h = 620f;
            float x = (MenuGui.VW - w) * 0.5f, y = (1080f - h) * 0.5f;
            float age = Time.unscaledTime - opened;
            var old = GUI.matrix;
            float sc = Mathf.Lerp(0.94f, 1f, HudFx.OutCubic(Mathf.Clamp01(age / 0.25f)));
            GUIUtility.ScaleAroundPivot(Vector2.one * sc, new Vector2(MenuGui.VW * 0.5f * MenuGui.S, 540f * MenuGui.S));
            MenuGui.Panel(MenuGui.R(x, y, w, h));
            MenuGui.Text(MenuGui.R(x, y + 26f, w, 60f), p.Title, MenuGui.Title, UIStyle.Accent);
            MenuGui.Text(MenuGui.R(x, y + 84f, w, 26f), $"HOW TO PLAY  ·  {page + 1} / {Pages.Length}", MenuGui.Small, UIStyle.Muted);
            MenuGui.Text(MenuGui.R(x + 70f, y + 126f, w - 140f, h - 250f), p.Body, MenuGui.Body, UIStyle.Parchment);

            // Page dots.
            float dx = x + w * 0.5f - Pages.Length * 12f;
            for (int i = 0; i < Pages.Length; i++)
            {
                if (Event.current.type != EventType.Repaint) break;
                GUI.color = i == page ? UIStyle.Accent : new Color(1f, 1f, 1f, 0.3f);
                GUI.DrawTexture(MenuGui.R(dx + i * 24f, y + h - 118f, 12f, 12f), UIStyle.White);
                GUI.color = Color.white;
            }

            float by = y + h - 92f;
            if (MenuGui.Button(MenuGui.R(x + 50f, by, 200f, 58f), "SKIP", true, 0.85f)) page = -1;
            if (page > 0 && MenuGui.Button(MenuGui.R(x + w - 470f, by, 200f, 58f), "BACK", true, 0.85f)) { page--; Sfx.Play(Sfx.Lib.PageTurn); }
            if (page >= 0 && MenuGui.Button(MenuGui.R(x + w - 250f, by, 200f, 58f), page == Pages.Length - 1 ? "PLAY" : "NEXT", true, 0.9f)) Next();
            GUI.matrix = old;
            MenuGui.SwallowClicks();
        }

        void DrawChecklist()
        {
            const float w = 340f, rowH = 34f;
            float h = 70f + Steps.Length * rowH;
            float x = MenuGui.VW - w - 14f, y = 180f;
            var panel = MenuGui.R(x, y, w, h);
            MenuGui.Panel(panel, 0.6f);
            HUD.AddBlocker(panel);
            MenuGui.Text(MenuGui.R(x + 24f, y + 14f, w - 80f, 30f), "FIRST STEPS", MenuGui.Label, UIStyle.Accent);
            if (MenuGui.Button(MenuGui.R(x + w - 56f, y + 14f, 36f, 32f), "X", true, 0.6f)) checklist = false;
            for (int i = 0; i < Steps.Length; i++)
            {
                float ry = y + 54f + i * rowH;
                bool d = done[i];
                float pop = d ? 1f + 0.4f * (1f - Mathf.Clamp01((Time.unscaledTime - doneAt[i]) / 0.35f)) : 1f;
                if (Event.current.type == EventType.Repaint)
                {
                    var box = MenuGui.R(x + 26f + 11f - 11f * pop, ry + 6f + 11f - 11f * pop, 22f * pop, 22f * pop);
                    GUI.color = d ? new Color(0.45f, 0.95f, 0.5f) : new Color(1f, 1f, 1f, 0.25f);
                    GUI.DrawTexture(box, UIStyle.White);
                    GUI.color = Color.white;
                }
                MenuGui.Text(MenuGui.R(x + 62f, ry, w - 80f, rowH), Steps[i], MenuGui.Small, d ? UIStyle.Muted : UIStyle.Parchment);
            }
        }
    }
}
