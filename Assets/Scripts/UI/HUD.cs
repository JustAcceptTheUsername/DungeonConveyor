using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DungeonConveyor
{
    /// <summary>
    /// IMGUI heads-up display on a 1080p-high virtual canvas: gold and Core health at the top, the wave panel at the
    /// top left, build menu with category tabs at the bottom (tooltips show combat stats), ore info when hovering a
    /// deposit, a placement hint while placing, and the game-over screen.
    /// </summary>
    [RequireComponent(typeof(BuildController))]
    public class HUD : MonoBehaviour
    {
        static readonly List<Rect> blockers = new List<Rect>();
        static readonly (BuildCategory tab, string label)[] Tabs =
        {
            (BuildCategory.Defense, "DEFENSES"), (BuildCategory.Logistics, "LOGISTICS"), (BuildCategory.Mining, "MINING"),
        };

        float s;           // pixels per virtual unit
        float vw, vh;      // virtual canvas size
        BuildCategory tab = BuildCategory.Defense;
        BuildController build;
        WaveManager waves;
        ItemFlow flow;
        TileHover hover;
        IsoGrid map;
        readonly List<BuildingCatalog.Entry> shown = new List<BuildingCatalog.Entry>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => blockers.Clear();

        /// <summary>True when the screen position (Input System, origin bottom-left) is over a HUD panel.</summary>
        public static bool IsPointerOverUI(Vector2 screenPos)
        {
            var p = new Vector2(screenPos.x, Screen.height - screenPos.y);
            foreach (var r in blockers) if (r.Contains(p)) return true;
            return false;
        }

        void Awake()
        {
            UIStyle.Init();
            build = GetComponent<BuildController>();
        }

        Rect R(float x, float y, float w, float h) => new Rect(x * s, y * s, w * s, h * s);

        Rect Panel(float x, float y, float w, float h, Texture2D tex = null)
        {
            var r = R(x, y, w, h);
            UIStyle.DrawPanel(r, tex);
            if (Event.current.type == EventType.Repaint) blockers.Add(r);
            return r;
        }

        void OnGUI()
        {
            if (waves == null) waves = GetComponent<WaveManager>();
            if (flow == null) flow = GetComponent<ItemFlow>();
            if (hover == null) hover = GetComponent<TileHover>();
            if (map == null) map = GetComponent<IsoGrid>();

            s = Screen.height / 1080f;
            vw = Screen.width / s;
            vh = 1080f;
            UIStyle.EnsureStyles(s);
            if (Event.current.type == EventType.Repaint) blockers.Clear();

            DrawTopBar();
            if (waves != null) DrawWavePanel();
            DrawPlacementHint();
            DrawHoverInfo();
            if (build.Catalog != null) DrawBuildBar();
            if (build.Core != null && build.Core.Destroyed) DrawGameOver();
        }

        // ---------------------------------------------------------------- top

        void DrawTopBar()
        {
            const float w = 420, h = 56;
            float x = (vw - w) * 0.5f;
            Panel(x, 12, w, h);
            GUI.DrawTexture(R(x + 18, 24, 32, 32), UIStyle.GoldIcon);
            GUI.Label(R(x + 60, 12, 130, h), build.Gold.ToString("N0"), UIStyle.Big);

            var core = build.Core;
            if (core == null) return;
            GUI.Label(R(x + 190, 14, 110, 22), "CORE", UIStyle.Small);
            float f = core.Health / core.MaxHealth;
            var bar = R(x + 190, 38, 210, 14);
            UIStyle.DrawBar(bar, f, Color.Lerp(UIStyle.Danger, new Color(0.35f, 0.85f, 0.25f), f));
            GUI.Label(R(x + 250, 14, 150, 22), $"{Mathf.CeilToInt(core.Health)} / {core.MaxHealth:0}", new GUIStyle(UIStyle.Small) { alignment = TextAnchor.MiddleRight });
        }

        void DrawWavePanel()
        {
            const float x = 12, y = 12, w = 240, h = 150;
            Panel(x, y, w, h);
            GUI.Label(R(x + 16, y + 8, w - 32, 36), waves.Wave == 0 ? "BUILD PHASE" : "WAVE " + waves.Wave, UIStyle.Big);
            GUI.Label(R(x + 16, y + 44, w - 32, 24), $"Enemies: {waves.Alive}    Kills: {waves.Kills}", UIStyle.Small);
            GUI.Label(R(x + 16, y + 66, w - 32, 24), $"Next wave in {Mathf.CeilToInt(Mathf.Max(0f, waves.NextWaveIn))}s", UIStyle.Text);
            var button = R(x + 16, y + 100, w - 32, 36);
            if (Event.current.type == EventType.Repaint) blockers.Add(button);
            if (GUI.Button(button, $"SEND NOW  (+{waves.EarlyBonus})", UIStyle.Tab)) waves.CallEarly();
        }

        void DrawPlacementHint()
        {
            var e = build.Placing;
            if (e == null) return;
            const float w = 520, h = 62;
            float x = (vw - w) * 0.5f, y = 78;
            Panel(x, y, w, h);
            GUI.Label(R(x + 16, y + 6, w - 32, 26), "Placing: " + e.Name, UIStyle.Title);
            if (build.BlockedReason != null)
                GUI.Label(R(x + 16, y + 32, w - 32, 24), build.BlockedReason, Colored(UIStyle.Small, UIStyle.Danger));
            else
                GUI.Label(R(x + 16, y + 32, w - 32, 24), !build.CapturesLeftDrag
                    ? "Left click to build  ·  Right click / Esc to cancel"
                    : e.Id == BuildingCatalog.WallId
                        ? "Drag to build a wall line  ·  Hold right click to clear  ·  Esc to cancel"
                        : "Drag to lay a belt (onto a belt to join it)  ·  Hold right click to clear  ·  Esc", UIStyle.Small);
        }

        /// <summary>What's under the cursor: ore patch contents, or a building's status.</summary>
        void DrawHoverInfo()
        {
            if (hover == null || !hover.HasCell || map == null || build.Placing != null) return;
            var c = hover.Cell;
            string title = null, body = null;
            var b = map.BuildingAt(c);
            if (b is Miner m)
            {
                int left = 0;
                foreach (var p in m.Patches) left += p.Remaining;
                title = m.Def.Name;
                body = left > 0 ? $"{left} crystals left to dig" : "Ore used up";
            }
            else if (b is Refiner r)
            {
                title = r.Def.Name;
                body = $"Refining {r.Input.Count}, ready {r.Output.Count}  ·  value ×{(flow != null ? flow.RefinedMultiplier : 1f):0.#}";
            }
            else if (b is Tower t && t.Def.Attack != AttackKind.None)
            {
                title = t.Def.Name;
                body = Stats(t.Def);
            }
            else if (b == null && map.IsOre(c))
            {
                var patch = map.PatchAt[c.x, c.y];
                var type = IsoGrid.OreTypeOf(patch.Kind);
                title = type.Name + " ore";
                body = $"{patch.Remaining} / {patch.Initial} left  ·  {type.Value} gold each";
            }
            if (title == null) return;

            const float w = 340, h = 64;
            float x = vw - w - 12, y = 12;
            Panel(x, y, w, h);
            GUI.Label(R(x + 14, y + 6, w - 28, 26), title, UIStyle.Title);
            GUI.Label(R(x + 14, y + 32, w - 28, 24), body, UIStyle.Small);
        }

        // ---------------------------------------------------------------- build menu

        void DrawBuildBar()
        {
            const float cardW = 118, cardH = 140, tabW = 128, tabH = 40;
            float y = vh - 12 - cardH - 16;
            float x = 12;

            for (int i = 0; i < Tabs.Length; i++)
            {
                var rect = R(x + i * (tabW + 4), y - tabH - 4, tabW, tabH);
                if (Event.current.type == EventType.Repaint) blockers.Add(rect);
                if (GUI.Button(rect, Tabs[i].label, tab == Tabs[i].tab ? UIStyle.TabActive : UIStyle.Tab)) tab = Tabs[i].tab;
            }

            shown.Clear();
            foreach (var e in build.Catalog.Entries)
                if (e.Purchasable && e.Category == tab) shown.Add(e);
            float w = 16 + Mathf.Max(1, shown.Count) * (cardW + 6) + 4;
            Panel(x, y, w, cardH + 16);
            if (shown.Count == 0)
                GUI.Label(R(x + 20, y + 8, cardW * 2, cardH), "Nothing here yet", UIStyle.Small);

            BuildingCatalog.Entry hovered = null;
            for (int i = 0; i < shown.Count; i++)
            {
                var e = shown[i];
                float cx = x + 10 + i * (cardW + 6);
                var rect = R(cx, y + 8, cardW, cardH);
                bool active = build.Placing == e;
                bool afford = build.CanAfford(e);
                if (GUI.Button(rect, GUIContent.none, active ? UIStyle.CardActive : UIStyle.Card)) build.Select(e);
                if (rect.Contains(Event.current.mousePosition)) hovered = e;

                var old = GUI.color;
                if (!afford) GUI.color = new Color(1, 1, 1, 0.45f);
                if (e.Sprite != null) GUI.DrawTexture(R(cx + 10, y + 14, cardW - 20, 78), e.Sprite.texture, ScaleMode.ScaleToFit);
                GUI.Label(R(cx, y + 92, cardW, 24), e.Name, UIStyle.CenterSmall);
                GUI.color = old;
                DrawCost(cx, y + 116, cardW, e.Cost, afford);
            }

            if (hovered != null) DrawTooltip(hovered, x, y - tabH - 12);
        }

        void DrawCost(float x, float y, float width, int cost, bool afford)
        {
            var text = cost.ToString("N0");
            float tw = UIStyle.Text.CalcSize(new GUIContent(text)).x / s;
            float total = 20 + 6 + tw;
            float cx = x + (width - total) * 0.5f;
            GUI.DrawTexture(R(cx, y + 2, 20, 20), UIStyle.GoldIcon);
            GUI.Label(R(cx + 26, y, tw + 4, 24), text, Colored(UIStyle.Text, afford ? Color.white : UIStyle.Danger));
        }

        void DrawTooltip(BuildingCatalog.Entry e, float x, float bottom)
        {
            string stats = e.Attack != AttackKind.None ? Stats(e) : null;
            string rules = e.NeedsConveyor ? $"Must be placed within {build.ConveyorReach} tiles of a conveyor." : e.NeedsOre ? "Must be placed on ore." : null;
            float w = 400, h = 112 + (stats != null ? 22 : 0) + (rules != null ? 20 : 0);
            float y = bottom - h;
            Panel(x, y, w, h);
            GUI.Label(R(x + 14, y + 8, w - 28, 28), e.Name, UIStyle.Title);
            GUI.Label(R(x + 14, y + 8, w - 28, 28), $"{e.Size.x}x{e.Size.y}", new GUIStyle(UIStyle.Small) { alignment = TextAnchor.MiddleRight });
            float ty = y + 38;
            if (stats != null)
            {
                GUI.Label(R(x + 14, ty, w - 28, 22), stats, Colored(UIStyle.Small, UIStyle.Accent));
                ty += 22;
            }
            if (rules != null)
            {
                GUI.Label(R(x + 14, ty, w - 28, 20), rules, Colored(UIStyle.Small, new Color(0.6f, 0.8f, 1f)));
                ty += 20;
            }
            var wrap = new GUIStyle(UIStyle.Small) { wordWrap = true, alignment = TextAnchor.UpperLeft };
            GUI.Label(R(x + 14, ty + 2, w - 28, y + h - ty - 6), e.Description + "\nRight click to remove; hold and drag to clear belts and walls.", wrap);
        }

        static string Stats(BuildingCatalog.Entry e)
        {
            switch (e.Attack)
            {
                case AttackKind.Laser:
                case AttackKind.Flame: return $"{e.Damage:0} damage/s  ·  range {e.Range:0.#}";
                case AttackKind.Slow: return $"Slows enemies {e.Damage * 100f:0}%  ·  range {e.Range:0.#}";
                case AttackKind.Tesla: return $"{e.Damage:0} damage, chains to {e.Splash:0}  ·  {e.FireRate:0.#}/s  ·  range {e.Range:0.#}";
                case AttackKind.Mine: return $"{e.Damage:0} damage, blast {e.Splash:0.#}  ·  single use";
                case AttackKind.Missile:
                case AttackKind.Mortar: return $"{e.Damage:0} damage, blast {e.Splash:0.#}  ·  {e.FireRate:0.#}/s  ·  range {e.Range:0.#}";
                default: return $"{e.Damage:0} damage  ·  {e.FireRate:0.#}/s  ·  range {e.Range:0.#}";
            }
        }

        // ---------------------------------------------------------------- game over

        void DrawGameOver()
        {
            Time.timeScale = 0f;
            const float w = 460, h = 230;
            float x = (vw - w) * 0.5f, y = (vh - h) * 0.5f;
            Panel(x, y, w, h);
            GUI.Label(R(x, y + 18, w, 50), "THE CORE HAS FALLEN", new GUIStyle(UIStyle.Big) { alignment = TextAnchor.MiddleCenter, normal = { textColor = UIStyle.Danger } });
            if (waves != null)
                GUI.Label(R(x, y + 76, w, 26), $"Survived {Mathf.Max(0, waves.Wave - 1)} waves  ·  {waves.Kills} kills", UIStyle.CenterSmall);
            if (flow != null)
                GUI.Label(R(x, y + 102, w, 26), $"{flow.Delivered} crystals sold for {flow.GoldFromOre:N0} gold", UIStyle.CenterSmall);
            var button = R(x + w * 0.5f - 110, y + 150, 220, 52);
            if (GUI.Button(button, "RESTART", UIStyle.TabActive))
            {
                Time.timeScale = 1f;
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            }
        }

        static GUIStyle Colored(GUIStyle style, Color c) => new GUIStyle(style) { normal = { textColor = c } };
    }
}
