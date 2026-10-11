using System.Collections.Generic;
using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>IMGUI heads-up display laid out on a 1080p-high virtual canvas.</summary>
    public class HUD : MonoBehaviour
    {
        static readonly List<Rect> blockers = new List<Rect>();

        float s;           // pixels per virtual unit
        float vw, vh;      // virtual canvas size
        BuildTab tab = BuildTab.Build;
        bool showMenu, showHelp = true;
        readonly List<(string, string)> lines = new List<(string, string)>();
        Minimap minimap;

        void Awake()
        {
            UIStyle.Init();
            minimap = new Minimap();
        }

        /// <summary>True when the screen position (Input System, origin bottom-left) is over a HUD panel.</summary>
        public static bool IsPointerOverUI(Vector2 screenPos)
        {
            var p = new Vector2(screenPos.x, Screen.height - screenPos.y);
            foreach (var r in blockers) if (r.Contains(p)) return true;
            return false;
        }

        Rect R(float x, float y, float w, float h) => new Rect(x * s, y * s, w * s, h * s);

        Rect Panel(float x, float y, float w, float h, Texture2D tex = null)
        {
            var r = R(x, y, w, h);
            UIStyle.DrawPanel(r, tex);
            if (Event.current.type == EventType.Repaint) blockers.Add(r);
            return r;
        }

        void Update() => minimap.Tick();

        void OnGUI()
        {
            if (Game.Bank == null) return;
            s = Screen.height / 1080f;
            vw = Screen.width / s;
            vh = 1080f;
            UIStyle.EnsureStyles(s);
            if (Event.current.type == EventType.Repaint) blockers.Clear();

            DrawFloatingText();
            DrawPlacementHint();
            DrawWavePanel();
            DrawObjectives();
            DrawLeftButtons();
            DrawResources();
            DrawTopRight();
            float y = DrawAlerts(112);
            DrawCoreHealth(y + 10);
            DrawBuildBar();
            DrawSelection();
            DrawMinimap();
            if (showHelp) DrawHelp();
            if (showMenu) DrawMenu();
            if (Game.Paused && !Game.IsGameOver) DrawCentered("PAUSED", "Space to resume");
            if (Game.IsGameOver) DrawGameOver();
        }

        // ---------------------------------------------------------------- left column

        void DrawWavePanel()
        {
            Panel(12, 12, 230, 150);
            var w = Game.Waves;
            GUI.Label(R(26, 20, 200, 36), "WAVE " + w.Wave, UIStyle.Big);
            GUI.Label(R(26, 56, 200, 26), "ENEMIES: " + w.Alive, UIStyle.Text);
            GUI.Label(R(26, 84, 200, 24), "NEXT WAVE IN:", UIStyle.Text);
            GUI.Label(R(26, 110, 200, 40), Fmt.Time(w.NextWaveIn), UIStyle.Big);
        }

        void DrawObjectives()
        {
            Panel(12, 172, 230, 132);
            GUI.Label(R(26, 180, 200, 28), "OBJECTIVES", UIStyle.Title);
            Bullet(26, 212, "Protect the Core");
            var g = Game.Objectives.Current;
            if (g != null)
            {
                int p = Mathf.Min(g.Progress(), g.Target);
                Bullet(26, 240, g.Text + " " + Fmt.Short(g.Target));
                GUI.Label(R(42, 264, 200, 24), $"{Fmt.Short(p)} / {Fmt.Short(g.Target)}", Colored(UIStyle.Small, UIStyle.Accent));
                UIStyle.ProgressBar(R(130, 274, 98, 6), p / (float)g.Target, UIStyle.Accent);
            }
            else Bullet(26, 240, "All objectives complete!");
        }

        void Bullet(float x, float y, string text)
        {
            UIStyle.Fill(R(x + 2, y + 10, 6, 6), UIStyle.Accent);
            GUI.Label(R(x + 16, y, 200, 26), text, UIStyle.Text);
        }

        void DrawLeftButtons()
        {
            Panel(12, 314, 62, 62);
            Panel(82, 314, 62, 62);
            if (GUI.Button(R(16, 318, 54, 54), Game.Paused ? ">" : "II", UIStyle.ButtonStyle)) Game.TogglePause();
            if (GUI.Button(R(86, 318, 54, 54), Game.Speed.ToString("0") + "x", UIStyle.ButtonStyle)) Game.CycleSpeed();
        }

        // ---------------------------------------------------------------- top bar

        void DrawResources()
        {
            const float w = 660, h = 74;
            float x = vw * 0.5f - w * 0.5f;
            Panel(x, 12, w, h);
            float cw = w / ResourceInfo.Count;
            for (int i = 0; i < ResourceInfo.Count; i++)
            {
                var t = (ResourceType)i;
                float cx = x + i * cw + 22;
                GUI.DrawTexture(R(cx, 28, 38, 38), UIStyle.ResourceIcons[i]);
                GUI.Label(R(cx + 46, 20, cw - 60, 32), Fmt.Short(Game.Bank[t]), UIStyle.Big);
                GUI.Label(R(cx + 47, 50, cw - 60, 22), "+" + Fmt.Short(Game.Bank.PerMinute(t)) + "/m", Colored(UIStyle.Small, ResourceInfo.Colors[i]));
            }
        }

        void DrawTopRight()
        {
            float x = vw - 12;
            Panel(x - 62, 12, 62, 62);
            if (GUI.Button(R(x - 58, 16, 54, 54), "", UIStyle.ButtonStyle)) showMenu = !showMenu;
            for (int i = 0; i < 3; i++) UIStyle.Fill(R(x - 44, 31 + i * 10, 26, 4), Color.white);
            x -= 70;
            Panel(x - 170, 12, 170, 62);
            if (GUI.Button(R(x - 166, 16, 162, 54), "CALL WAVE  [N]", UIStyle.ButtonStyle) && !Game.IsGameOver)
            {
                int bonus = Game.Waves.CallEarly();
                Game.Alerts.Push($"Called early: +{bonus} gold");
            }
            x -= 178;
            Panel(x - 62, 12, 62, 62);
            if (GUI.Button(R(x - 58, 16, 54, 54), "?", Bigger(UIStyle.ButtonStyle))) showHelp = !showHelp;
        }

        // ---------------------------------------------------------------- right column

        float DrawAlerts(float y)
        {
            var alerts = Game.Alerts.Current;
            const float w = 250;
            float x = vw - 12 - w;
            float h = 48 + Mathf.Max(1, alerts.Count) * 40;
            Panel(x, y, w, h);
            GUI.Label(R(x + 16, y + 10, w, 28), "ALERTS", Colored(UIStyle.Title, UIStyle.Danger));
            UIStyle.Fill(R(x + 10, y + 42, w - 20, 1), UIStyle.BorderColor);
            if (alerts.Count == 0)
                GUI.Label(R(x + 16, y + 48, w - 30, 34), "All systems nominal", UIStyle.Small);
            for (int i = 0; i < alerts.Count; i++)
            {
                var a = alerts[i];
                float ay = y + 48 + i * 40;
                if (a.Level != AlertLevel.Info)
                    GUI.DrawTexture(R(x + 16, ay + 7, 20, 20), a.Level == AlertLevel.Danger ? UIStyle.AlertCircle : UIStyle.AlertTriangle);
                else
                    UIStyle.Fill(R(x + 22, ay + 13, 8, 8), UIStyle.Accent);
                GUI.Label(R(x + 46, ay, w - 54, 34), a.Text, UIStyle.Text);
            }
            return y + h;
        }

        void DrawCoreHealth(float y)
        {
            var core = Game.Core;
            if (core == null) return;
            const float w = 250;
            float x = vw - 12 - w;
            Panel(x, y, w, 86);
            float t = Mathf.Max(0, core.Health) / core.MaxHealth;
            GUI.Label(R(x + 16, y + 8, w, 28), "CORE HEALTH", UIStyle.Title);
            GUI.Label(R(x + 16, y + 34, w, 28), Mathf.CeilToInt(t * 100) + "%", UIStyle.Big);
            UIStyle.ProgressBar(R(x + 16, y + 66, w - 32, 8), t, t > 0.5f ? UIStyle.Good : t > 0.25f ? UIStyle.Accent : UIStyle.Danger);
        }

        void DrawSelection()
        {
            var b = Game.Build.Selected;
            if (b == null || b.IsDestroyed) return;
            lines.Clear();
            b.Describe(lines);
            const float w = 320;
            float h = 150 + lines.Count * 30 + (b is CoreBuilding ? 0 : 0);
            float x = vw - 12 - w, y = vh - 12 - 230 - 12 - h;
            Panel(x, y, w, h);
            string level = b.Def.MaxLevel > 1 ? $"  Lv {b.Level}" : "";
            GUI.Label(R(x + 16, y + 8, w - 40, 30), "SELECTED: " + b.Def.Name.ToUpper() + level, Colored(UIStyle.Title, UIStyle.Good));
            UIStyle.Fill(R(x + 10, y + 40, w - 20, 1), UIStyle.BorderColor);

            float ly = y + 48;
            GUI.Label(R(x + 16, ly, 120, 26), "Health", UIStyle.Text);
            GUI.Label(R(x + 120, ly, w - 136, 26), $"{Mathf.CeilToInt(b.Health)} / {Mathf.CeilToInt(b.MaxHealth)}", UIStyle.Right);
            UIStyle.ProgressBar(R(x + 16, ly + 28, w - 32, 6), b.Health / b.MaxHealth, UIStyle.Good);
            ly += 42;
            foreach (var (label, value) in lines)
            {
                GUI.Label(R(x + 16, ly, 120, 26), label + ":", UIStyle.Text);
                GUI.Label(R(x + 130, ly, w - 146, 26), value, UIStyle.Text);
                ly += 30;
            }

            float by = y + h - 56;
            if (b.CanUpgrade)
            {
                var cost = b.UpgradeCost;
                bool afford = Game.Bank.CanAfford(cost);
                GUI.enabled = afford;
                if (GUI.Button(R(x + 12, by, 200, 44), "", UIStyle.ButtonStyle)) Game.Build.Upgrade(b);
                GUI.enabled = true;
                GUI.Label(R(x + 26, by, 90, 44), "UPGRADE", Colored(UIStyle.Text, afford ? Color.white : UIStyle.Muted));
                DrawCost(x + 112, by + 10, cost, 24);
            }
            else GUI.Label(R(x + 16, by, 200, 44), b.Def.MaxLevel > 1 ? "MAX LEVEL" : b.Def.Description, UIStyle.Small);

            if (!(b is CoreBuilding) && GUI.Button(R(x + w - 98, by, 86, 44), "SELL", UIStyle.ButtonStyle))
                Game.Build.Sell(b);
        }

        void DrawMinimap()
        {
            const float size = 230;
            float x = vw - 12 - size, y = vh - 12 - size;
            var r = Panel(x, y, size, size);
            var inner = new Rect(r.x + 8 * s, r.y + 8 * s, r.width - 16 * s, r.height - 16 * s);
            minimap.Draw(inner);

            // Zoom / recenter buttons
            float bx = x - 58;
            Panel(bx, y + 56, 50, 166);
            if (GUI.Button(R(bx + 4, y + 60, 42, 50), "", UIStyle.ButtonStyle) && Game.Core != null) Game.Cam.LookAt(Game.Core.Center);
            FrameIcon(bx + 25, y + 85, 14);
            if (GUI.Button(R(bx + 4, y + 114, 42, 50), "+", Bigger(UIStyle.ButtonStyle))) Game.Cam.Zoom(2);
            if (GUI.Button(R(bx + 4, y + 168, 42, 50), "-", Bigger(UIStyle.ButtonStyle))) Game.Cam.Zoom(-2);
        }

        /// <summary>Square-corners "recenter" glyph centred on (cx, cy).</summary>
        void FrameIcon(float cx, float cy, float half)
        {
            const float t = 2.5f, len = 6f;
            var c = Color.white;
            foreach (var (sx, sy) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
            {
                float x = cx + sx * half, y = cy + sy * half;
                UIStyle.Fill(R(sx < 0 ? x : x - len, sy < 0 ? y : y - t, len, t), c);
                UIStyle.Fill(R(sx < 0 ? x : x - t, sy < 0 ? y : y - len, t, len), c);
            }
            UIStyle.Fill(R(cx - 2.5f, cy - 2.5f, 5, 5), UIStyle.Accent);
        }

        // ---------------------------------------------------------------- build bar

        static readonly (BuildTab tab, string label)[] Tabs =
        {
            (BuildTab.Build, "BUILD"), (BuildTab.Machines, "MACHINES"), (BuildTab.Defenses, "DEFENSES"),
            (BuildTab.Utilities, "UTILITIES"), (BuildTab.Resources, "RESOURCES"),
        };

        void DrawBuildBar()
        {
            const float cardW = 124, cardH = 140, tabW = 128, tabH = 40;
            float y = vh - 12 - cardH - 16;
            float x = 12;

            for (int i = 0; i < Tabs.Length; i++)
            {
                var rect = R(x + i * (tabW + 4), y - tabH - 4, tabW, tabH);
                if (Event.current.type == EventType.Repaint) blockers.Add(rect);
                if (GUI.Button(rect, Tabs[i].label, tab == Tabs[i].tab ? UIStyle.TabActive : UIStyle.Tab)) tab = Tabs[i].tab;
            }

            if (tab == BuildTab.Resources) { DrawResourceStats(x, y); return; }

            var defs = new List<BuildingDef>();
            foreach (var d in Defs.Buildable) if (Defs.InTab(d, tab)) defs.Add(d);
            float w = 16 + defs.Count * (cardW + 6) + 4;
            Panel(x, y, w, cardH + 16);

            BuildingDef hovered = null;
            for (int i = 0; i < defs.Count; i++)
            {
                var d = defs[i];
                var rect = R(x + 10 + i * (cardW + 6), y + 8, cardW, cardH);
                bool active = Game.Build.Placing == d;
                bool afford = Game.Bank.CanAfford(d.Cost);
                if (GUI.Button(rect, GUIContent.none, active ? UIStyle.CardActive : UIStyle.Card))
                {
                    if (active) Game.Build.Cancel(); else Game.Build.BeginPlacing(d);
                }
                if (rect.Contains(Event.current.mousePosition)) hovered = d;

                float cx = x + 10 + i * (cardW + 6);
                var oldColor = GUI.color;
                if (!afford) GUI.color = new Color(1, 1, 1, 0.45f);
                if (d.Icon != null) GUI.DrawTexture(R(cx + 17, y + 14, 90, 76), d.Icon, ScaleMode.ScaleToFit);
                GUI.Label(R(cx, y + 88, cardW, 26), d.Name, UIStyle.Center);
                GUI.color = oldColor;
                DrawCost(cx + 10, y + 116, d.Cost, 22, centerWidth: cardW - 20);
                int hotkey = Defs.Buildable.IndexOf(d) + 1;
                GUI.Label(R(cx + 8, y + 10, 20, 20), hotkey.ToString(), UIStyle.Small);
            }

            if (hovered != null)
            {
                float tw = 380;
                Panel(x, y - 44 - 100, tw, 92);
                GUI.Label(R(x + 14, y - 136, tw - 28, 28), hovered.Name, UIStyle.Title);
                var wrap = new GUIStyle(UIStyle.Small) { wordWrap = true, alignment = TextAnchor.UpperLeft };
                GUI.Label(R(x + 14, y - 106, tw - 28, 56), hovered.Description, wrap);
            }
        }

        void DrawResourceStats(float x, float y)
        {
            const float w = 560, h = 156;
            Panel(x, y, w, h);
            GUI.Label(R(x + 16, y + 8, 200, 26), "RESOURCE", UIStyle.Small);
            GUI.Label(R(x + 200, y + 8, 100, 26), "STORED", UIStyle.Small);
            GUI.Label(R(x + 310, y + 8, 100, 26), "RATE", UIStyle.Small);
            GUI.Label(R(x + 420, y + 8, 120, 26), "COLLECTED", UIStyle.Small);
            for (int i = 0; i < ResourceInfo.Count; i++)
            {
                var t = (ResourceType)i;
                float ry = y + 36 + i * 28;
                GUI.DrawTexture(R(x + 16, ry + 3, 22, 22), UIStyle.ResourceIcons[i]);
                GUI.Label(R(x + 46, ry, 150, 28), ResourceInfo.Names[i], UIStyle.Text);
                GUI.Label(R(x + 200, ry, 100, 28), Fmt.Short(Game.Bank[t]), UIStyle.Text);
                GUI.Label(R(x + 310, ry, 100, 28), "+" + Game.Bank.PerMinute(t) + "/m", Colored(UIStyle.Text, ResourceInfo.Colors[i]));
                GUI.Label(R(x + 420, ry, 120, 28), Fmt.Short(Game.Bank.TotalCollected(t)), UIStyle.Text);
            }
        }

        void DrawCost(float x, float y, Cost c, float size, float centerWidth = 0)
        {
            var parts = new List<(int, int)>();
            for (int i = 0; i < ResourceInfo.Count; i++) if (c[i] > 0) parts.Add((i, c[i]));
            var style = UIStyle.Text;
            float total = 0;
            foreach (var (_, amount) in parts) total += size + 4 + style.CalcSize(new GUIContent(amount.ToString())).x / s + 8;
            float cx = centerWidth > 0 ? x + Mathf.Max(0, (centerWidth - total) * 0.5f) : x;
            foreach (var (i, amount) in parts)
            {
                bool enough = Game.Bank[(ResourceType)i] >= amount;
                GUI.DrawTexture(R(cx, y, size * 0.8f, size * 0.8f), UIStyle.ResourceIcons[i]);
                float tw = style.CalcSize(new GUIContent(amount.ToString())).x / s;
                GUI.Label(R(cx + size * 0.8f + 4, y - size * 0.1f, tw + 4, size), amount.ToString(), Colored(style, enough ? Color.white : UIStyle.Danger));
                cx += size * 0.8f + 4 + tw + 8;
            }
        }

        // ---------------------------------------------------------------- world-space overlays

        void DrawFloatingText()
        {
            var cam = Game.Cam != null ? Game.Cam.Cam : null;
            if (cam == null) return;
            var style = UIStyle.Big;
            for (int i = Effects.Texts.Count - 1; i >= 0; i--)
            {
                var t = Effects.Texts[i];
                float age = (Time.time - t.Born) / Effects.FloatTextLife;
                if (age >= 1f) { Effects.Texts.RemoveAt(i); continue; }
                var sp = cam.WorldToScreenPoint(t.Pos + Vector3.up * age * 0.8f);
                if (sp.z < 0) continue;
                var c = t.Color;
                c.a = 1f - age * age;
                UIStyle.Shadowed(new Rect(sp.x - 40 * s, Screen.height - sp.y - 16 * s, 80 * s, 32 * s), t.Text, Centered(style), c);
            }
        }

        void DrawPlacementHint()
        {
            var bc = Game.Build;
            if (bc.Placing == null || bc.PlaceError == null) return;
            var mp = Event.current.mousePosition;
            UIStyle.Shadowed(new Rect(mp.x + 18 * s, mp.y + 10 * s, 300 * s, 26 * s), bc.PlaceError, UIStyle.Text, UIStyle.Danger);
        }

        void DrawHelp()
        {
            const float w = 980;
            float x = vw * 0.5f - w * 0.5f + 60;
            float y = 96;
            Panel(x, y, w, 36);
            GUI.Label(R(x, y, w, 36),
                "LMB build/select · drag belts · R rotate · RMB cancel · X sell · U upgrade · WASD/MMB pan · Wheel zoom · Q/E turn · Space pause · F speed",
                UIStyle.CenterSmall);
        }

        void DrawMenu()
        {
            const float w = 240, h = 220;
            float x = vw - 12 - w, y = 84;
            Panel(x, y, w, h);
            GUI.Label(R(x + 16, y + 10, w, 28), "MENU", UIStyle.Title);
            if (GUI.Button(R(x + 16, y + 46, w - 32, 44), "Resume", UIStyle.ButtonStyle)) showMenu = false;
            if (GUI.Button(R(x + 16, y + 98, w - 32, 44), "Restart", UIStyle.ButtonStyle)) { showMenu = false; GameBootstrap.Restart(false); }
            if (GUI.Button(R(x + 16, y + 150, w - 32, 44), "New Map", UIStyle.ButtonStyle)) { showMenu = false; GameBootstrap.Restart(true); }
        }

        void DrawCentered(string title, string sub)
        {
            UIStyle.Shadowed(R(0, vh * 0.32f, vw, 70), title, UIStyle.Huge, Color.white);
            UIStyle.Shadowed(R(0, vh * 0.32f + 66, vw, 30), sub, UIStyle.Center, UIStyle.Muted);
        }

        void DrawGameOver()
        {
            UIStyle.Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0, 0, 0, 0.55f));
            UIStyle.Shadowed(R(0, vh * 0.3f, vw, 70), "CORE DESTROYED", UIStyle.Huge, UIStyle.Danger);
            UIStyle.Shadowed(R(0, vh * 0.3f + 70, vw, 30),
                $"You reached wave {Game.Waves.Wave} and defeated {Game.Waves.Kills} enemies.", UIStyle.Center, Color.white);
            float x = vw * 0.5f - 210;
            var r1 = R(x, vh * 0.3f + 130, 200, 54);
            var r2 = R(x + 220, vh * 0.3f + 130, 200, 54);
            if (Event.current.type == EventType.Repaint) { blockers.Add(r1); blockers.Add(r2); }
            if (GUI.Button(r1, "RESTART", UIStyle.ButtonStyle)) GameBootstrap.Restart(false);
            if (GUI.Button(r2, "NEW MAP", UIStyle.ButtonStyle)) GameBootstrap.Restart(true);
        }

        // ---------------------------------------------------------------- style helpers

        readonly Dictionary<(GUIStyle, Color), GUIStyle> colored = new Dictionary<(GUIStyle, Color), GUIStyle>();
        GUIStyle Colored(GUIStyle baseStyle, Color c)
        {
            if (!colored.TryGetValue((baseStyle, c), out var st))
            {
                st = new GUIStyle(baseStyle) { normal = { textColor = c } };
                colored[(baseStyle, c)] = st;
            }
            return st;
        }

        readonly Dictionary<GUIStyle, GUIStyle> bigger = new Dictionary<GUIStyle, GUIStyle>();
        GUIStyle Bigger(GUIStyle b)
        {
            if (!bigger.TryGetValue(b, out var st)) bigger[b] = st = new GUIStyle(b) { fontSize = Mathf.RoundToInt(b.fontSize * 1.6f) };
            return st;
        }

        readonly Dictionary<GUIStyle, GUIStyle> centered = new Dictionary<GUIStyle, GUIStyle>();
        GUIStyle Centered(GUIStyle b)
        {
            if (!centered.TryGetValue(b, out var st)) centered[b] = st = new GUIStyle(b) { alignment = TextAnchor.MiddleCenter };
            return st;
        }
    }
}
