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
                       "<b>Drag</b> to turn the planet, <b>scroll</b> to zoom, <b>WASD</b> or the arrow keys also turn it.",
            },
            new Page
            {
                Title = "MINE AND HAUL",
                Body = "Gold comes from crystals. Put a <b>Miner</b> on an ore patch (the coloured hexes), then pick the <b>Conveyor</b>, " +
                       "press on a hex next to the miner and <b>drag</b> a belt to the Core. Every crystal sold at the Core pays gold and banks " +
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
            else if (!SettingsMenu.IsOpen) DrawChecklist();
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
