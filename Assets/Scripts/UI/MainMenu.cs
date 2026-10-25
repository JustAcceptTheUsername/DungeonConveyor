using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace DungeonConveyor
{
    /// <summary>
    /// The title screen (Assets/Scenes/MainMenu.unity, made by Dungeon > Create Main Menu Scene): Play opens the new
    /// run screen (difficulty and starting wave, both remembered; see RunSettings.Difficulties) whose Start begins the
    /// run in the game scene, Upgrades spends Essence (earned every run) on permanent upgrades (Meta), Cards opens a gallery of every upgrade card (sortable by rarity), Quit closes the game (or
    /// leaves Play mode in the editor). Settings opens the SettingsMenu (audio volumes, reduced motion, tutorial). Drawn with IMGUI in the HUD's Arcane
    /// skin on a 1080p-tall virtual screen; it also draws in the editor's Game view outside Play mode (Preview picks
    /// the screen shown there).
    /// </summary>
    [ExecuteAlways]
    public class MainMenu : MonoBehaviour
    {
        public string GameScene = "GameScene";
        public string Title = "DUNGEON CONVEYOR";
        public string Subtitle = "Mine the deep. Hold the core.";
        [Tooltip("Seconds the menu takes to fade in.")]
        public float FadeIn = 0.8f;

        public enum Page { Main, NewRun, Cards, Upgrades }
        [Tooltip("The screen drawn in the editor's Game view outside Play mode.")]
        public Page Preview = Page.Main;

        enum Sort { RarityHighFirst, RarityLowFirst, Type }

        float s, vw, opened;
        bool leaving;
        Page page = Page.Main;
        Sort sort = Sort.RarityHighFirst;
        List<Run.Card> cards;
        Vector2 scroll;
        GUIStyle titleStyle, subStyle, buttonStyle, smallButtonStyle, textStyle, headStyle;
        float builtScale = -1f;

        void Start()
        {
            UIStyle.Init();
            opened = Time.unscaledTime;
            if (Application.isPlaying)
            {
                Time.timeScale = 1f; // in case the game was left paused or sped up
                Sfx.PlayMusic(Sfx.Lib.MenuMusic);
                Sfx.PlayAmbience(Sfx.Lib.MenuAmbience);
            }
        }

        void Update()
        {
            var k = Keyboard.current;
            if (Application.isPlaying && page != Page.Main && k != null && k.escapeKey.wasPressedThisFrame) page = Page.Main;
        }

        void OnGUI()
        {
            UIStyle.Init();
            s = Screen.height / 1080f;
            vw = Screen.width / s;
            UIStyle.EnsureStyles(s);
            BuildStyles();
            bool live = Application.isPlaying;
            var shown = live ? page : Preview;
            if (shown == Page.Cards)
            {
                if (cards == null) OpenGallery();
                DrawGallery();
                return;
            }

            float fade = live ? Mathf.Clamp01((Time.unscaledTime - opened) / Mathf.Max(0.01f, FadeIn)) : 1f;
            float ease = HudFx.OutCubic(fade);

            // Title, with a warm glow behind it.
            float ty = 120f - 30f * (1f - ease);
            GUI.color = new Color(1f, 0.55f, 0.15f, 0.22f * ease);
            UIStyle.GlowRing(R(vw * 0.5f - 420f, ty + 40f, 840f, 60f), s, 60f);
            GUI.color = Color.white;
            Label(R(0f, ty, vw, 140f), Title, titleStyle, With(UIStyle.Accent, ease));
            Label(R(0f, ty + 124f, vw, 44f), Subtitle, subStyle, With(UIStyle.Muted, ease));

            GUI.color = new Color(1f, 1f, 1f, ease);
            if (shown == Page.NewRun) DrawNewRun(ease);
            else if (shown == Page.Upgrades) DrawUpgrades(ease);
            else DrawMain(ease);
            GUI.color = Color.white;
        }

        void DrawMain(float ease)
        {
            const float bw = 380f, bh = 76f, gap = 18f;
            float bx = (vw - bw) * 0.5f, by = 380f + 40f * (1f - ease);
            if (Button(bx, by, bw, bh, "Play", true)) page = Page.NewRun;
            if (Button(bx, by + (bh + gap), bw, bh, "Upgrades", true)) page = Page.Upgrades;
            if (Button(bx, by + 2f * (bh + gap), bw, bh, "Cards", true)) { OpenGallery(); page = Page.Cards; }
            if (Button(bx, by + 3f * (bh + gap), bw, bh, "Settings", true)) SettingsMenu.Open();
            if (Button(bx, by + 4f * (bh + gap), bw, bh, "Quit", true)) Quit();
            Label(R(0f, by + 5f * (bh + gap) + 4f, vw, 30f), $"{Meta.Essence} essence", headStyle, With(EssenceColor, ease));
        }

        static readonly Color EssenceColor = new Color(0.75f, 0.6f, 1f);

        /// <summary>Permanent upgrades bought with Essence, and the Core type to bring.</summary>
        void DrawUpgrades(float ease)
        {
            Label(R(0f, 300f, vw, 40f), $"ESSENCE  {Meta.Essence}", smallButtonStyle, With(EssenceColor, ease));
            Label(R(0f, 336f, vw, 26f), "Earned every run: 1 per wave cleared, 8 per boss, 15 per map. Upgrades last forever.", textStyle, With(UIStyle.Muted, ease));

            const float cw = 420f, ch = 112f, gap = 16f;
            int cols = Mathf.Clamp(Mathf.FloorToInt((vw - 80f + gap) / (cw + gap)), 1, 3);
            float x0 = (vw - (cols * cw + (cols - 1) * gap)) * 0.5f, y0 = 380f;
            for (int i = 0; i < Meta.Upgrades.Length; i++)
            {
                var u = Meta.Upgrades[i];
                float x = x0 + (i % cols) * (cw + gap), y = y0 + (i / cols) * (ch + gap);
                if (ArcaneSkin.Loaded) UIStyle.Nine(R(x, y, cw, ch), ArcaneSkin.Panel, s * 0.6f);
                else UIStyle.DrawPanel(R(x, y, cw, ch));
                int level = Meta.Level(u.Id);
                string pips = u.MaxLevel > 1 ? $"  {level}/{u.MaxLevel}" : level > 0 ? "  OWNED" : "";
                Label(R(x + 20f, y + 10f, cw - 170f, 30f), u.Name + pips, new GUIStyle(headStyle) { alignment = TextAnchor.MiddleLeft }, With(level > 0 ? UIStyle.Accent : UIStyle.Parchment, ease));
                Label(R(x + 20f, y + 42f, cw - 170f, 60f), u.Text, new GUIStyle(textStyle) { alignment = TextAnchor.UpperLeft }, With(UIStyle.Muted, ease));
                bool maxed = Meta.Maxed(u);
                int cost = Meta.NextCost(u);
                if (Button(x + cw - 140f, y + 30f, 120f, 52f, maxed ? "MAX" : cost.ToString(), !maxed && Meta.Essence >= cost, small: true))
                    if (Meta.TryBuy(u)) Sfx.Play(Sfx.Lib.UpgradeBought);
            }

            // Core type.
            int rows = (Meta.Upgrades.Length + cols - 1) / cols;
            float cy = y0 + rows * (ch + gap) + 6f;
            Label(R(0f, cy, vw, 28f), "CORE", headStyle, With(UIStyle.Muted, ease));
            const float tw = 300f;
            float tx = (vw - 3f * tw - 2f * gap) * 0.5f;
            for (int i = 0; i < 3; i++)
            {
                var type = (Meta.CoreType)i;
                bool owned = Meta.CoreOwned(type), active = Meta.ActiveCore == type;
                if (Button(tx + i * (tw + gap), cy + 30f, tw, 56f, Meta.CoreNames[i], owned, owned ? null : "Buy it above", small: true, active: active))
                    { Meta.ActiveCore = type; Sfx.Play(Sfx.Lib.Toggle); }
            }
            Label(R(0f, cy + 92f, vw, 26f), Meta.CoreTexts[(int)Meta.ActiveCore], textStyle, With(UIStyle.Parchment, ease));

            if (Button((vw - 300f) * 0.5f, Mathf.Min(1080f - 96f, cy + 130f), 300f, 70f, "Back", true)) page = Page.Main;
        }

        /// <summary>The new run screen: difficulty, starting wave and what skipping ahead grants, then Start / Back.</summary>
        void DrawNewRun(float ease)
        {
            float y = 300f;
            DrawDifficulty(y, ease);
            DrawStartWave(y + 270f, ease);

            const float bw = 300f, bh = 76f, gap = 30f;
            float bx = vw * 0.5f - bw - gap * 0.5f, by = 880f + 40f * (1f - ease);
            if (Button(bx, by, bw, bh, "Back", true)) page = Page.Main;
            if (Button(bx + bw + gap, by, bw, bh, "Start", true)) Play();
        }

        /// <summary>"&lt; NAME &gt;" picker with the difficulty's description under it.</summary>
        void DrawDifficulty(float y, float alpha)
        {
            var list = RunSettings.Instance.Difficulties;
            if (list.Count == 0) return;
            int i = Run.DifficultyIndex;
            var d = list[i];
            Label(R(0f, y, vw, 30f), "DIFFICULTY", headStyle, With(UIStyle.Muted, alpha));
            // Harder is redder.
            float t = list.Count > 1 ? i / (float)(list.Count - 1) : 0f;
            var col = Color.Lerp(new Color(0.55f, 0.9f, 0.5f), UIStyle.Danger, t);
            int pick = Picker(y + 34f, (d.Name ?? "").ToUpperInvariant(), col, alpha, i > 0, i < list.Count - 1);
            if (pick != 0) SetDifficulty(i + pick);
            Label(R(vw * 0.5f - 380f, y + 108f, 760f, 120f), d.Description ?? "", textStyle, With(UIStyle.Parchment, alpha));
        }

        /// <summary>Starting wave picker (the first wave of each map), with what the skipped maps hand out.</summary>
        void DrawStartWave(float y, float alpha)
        {
            int stage = Run.StartStage;
            Label(R(0f, y, vw, 30f), "STARTING WAVE", headStyle, With(UIStyle.Muted, alpha));
            string name = $"WAVE {Run.FirstWaveOf(stage)}  ·  MAP {stage + 1}";
            int pick = Picker(y + 34f, name, UIStyle.Accent, alpha, stage > 0, stage < Run.MaxStartStage);
            if (pick != 0)
            {
                Run.StartStage = stage + pick;
                PlayerPrefs.Save();
            }

            var settings = RunSettings.Instance;
            int cards = (Run.FirstWaveOf(stage) - 1) / Mathf.Max(1, settings.WavesPerCard);
            string text = stage == 0
                ? "From the very beginning."
                : $"Starts with {cards} random upgrade cards, +{settings.GoldPerSkippedMap * stage} gold, {settings.CrystalsPerSkippedMap * stage} crystals " +
                  $"and the buildings the skipped maps' shops would have offered.";
            if (stage == Run.DefaultStartStage) text += "  (Default for this difficulty.)";
            Label(R(vw * 0.5f - 380f, y + 108f, 760f, 90f), text, textStyle, With(UIStyle.Parchment, alpha));
        }

        /// <summary>A "&lt; value &gt;" banner; returns -1 / +1 when an arrow is clicked.</summary>
        int Picker(float y, string value, Color color, float alpha, bool canDown, bool canUp)
        {
            const float aw = 64f, nw = 400f, h = 64f;
            float x = (vw - nw) * 0.5f;
            int pick = 0;
            if (Button(x - aw - 12f, y, aw, h, "<", canDown, small: true)) pick = -1;
            if (Button(x + nw + 12f, y, aw, h, ">", canUp, small: true)) pick = 1;
            if (ArcaneSkin.Loaded) { var old = GUI.color; GUI.color = new Color(1f, 1f, 1f, alpha); UIStyle.Nine(R(x, y, nw, h), ArcaneSkin.Banner, s); GUI.color = old; }
            else UIStyle.DrawPanel(R(x, y, nw, h));
            Label(R(x, y, nw, h), value, smallButtonStyle, With(color, alpha));
            return pick;
        }

        void SetDifficulty(int i)
        {
            Run.DifficultyIndex = i;
            Run.StartStage = -1; // follow the new difficulty's default starting wave
            PlayerPrefs.Save();
        }

        void Play()
        {
            if (leaving || !Application.isPlaying) return;
            leaving = true;
            Run.Begin(); // a fresh run with the chosen difficulty and starting wave
            SceneManager.LoadScene(GameScene);
        }

        void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ---------------------------------------------------------------- card gallery

        void OpenGallery()
        {
            scroll = Vector2.zero;
            cards = Run.AllCards();
            SortCards();
        }

        void SortCards()
        {
            int Order(Run.Card c) => (int)c.Kind * 2 + (c.PerTower ? 1 : 0);
            cards.Sort((a, b) =>
            {
                int r = sort == Sort.RarityLowFirst ? a.Rarity.CompareTo(b.Rarity) : sort == Sort.RarityHighFirst ? b.Rarity.CompareTo(a.Rarity) : 0;
                if (r != 0) return r;
                int k = Order(a).CompareTo(Order(b));
                return k != 0 ? k : a.Rarity.CompareTo(b.Rarity);
            });
        }

        /// <summary>Every card in a scrolling grid, with sort buttons and the rarity legend.</summary>
        void DrawGallery()
        {
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), UIStyle.White);
            GUI.color = Color.white;

            float px = 50f, py = 40f, pw = vw - 100f, ph = 1000f;
            if (ArcaneSkin.Loaded) UIStyle.Nine(R(px, py, pw, ph), ArcaneSkin.Panel, s);
            else UIStyle.DrawPanel(R(px, py, pw, ph));
            Label(R(px + 50f, py + 30f, 500f, 50f), "ALL CARDS", new GUIStyle(subStyle) { alignment = TextAnchor.MiddleLeft, fontSize = Mathf.RoundToInt(40 * s), fontStyle = FontStyle.Bold, font = titleStyle.font }, UIStyle.Accent);

            // Sort buttons and close.
            (Sort mode, string label)[] modes = { (Sort.RarityHighFirst, "Rarity: high first"), (Sort.RarityLowFirst, "Rarity: low first"), (Sort.Type, "By type") };
            // The sort row sits beside the title when there's room, else on its own row below it.
            const float sw = 250f, sh = 56f, titleEnd = 470f;
            if (Button(px + pw - 60f - 64f, py + 34f, 64f, sh, "X", true, small: true)) page = Page.Main;
            float sx = px + pw - 60f - 3 * (sw + 12f) - 76f, sy = py + 34f, below = 0f;
            if (sx - 110f < px + titleEnd)
            {
                sx = px + 160f;
                sy = py + 100f;
                below = sh + 10f;
            }
            Label(R(sx - 110f, sy, 100f, sh), "SORT", headStyle, UIStyle.Muted);
            for (int i = 0; i < modes.Length; i++)
                if (Button(sx + i * (sw + 12f), sy, sw, sh, modes[i].label, true, small: true, active: sort == modes[i].mode))
                {
                    sort = modes[i].mode;
                    SortCards();
                }
            py += below;
            ph -= below;

            // Legend: how many of each rarity, in its colour.
            var counts = new int[4];
            foreach (var c in cards) counts[(int)c.Rarity]++;
            float lx = px + 54f;
            for (int i = 0; i < 4; i++)
            {
                Label(R(lx, py + 96f, 220f, 30f), $"{Run.RarityNames[i]}  {counts[i]}", new GUIStyle(headStyle) { alignment = TextAnchor.MiddleLeft }, Run.RarityColors[i]);
                lx += 220f;
            }
            Label(R(px + 54f, py + 126f, pw - 108f, 28f), "Per-tower cards name one of your unlocked towers when they are dealt. Esc to close.",
                new GUIStyle(textStyle) { alignment = TextAnchor.MiddleLeft }, UIStyle.Muted);

            // Grid.
            const float cw = 200f, gap = 26f;
            float ch = cw * CardArt.CardH / CardArt.CardW;
            var view = R(px + 40f, py + 170f, pw - 80f, ph - 200f);
            int cols = Mathf.Max(1, Mathf.FloorToInt((view.width / s + gap - 24f) / (cw + gap)));
            int rows = (cards.Count + cols - 1) / cols;
            float gridW = cols * cw + (cols - 1) * gap;
            float x0 = (view.width / s - 24f - gridW) * 0.5f;
            var content = new Rect(0f, 0f, view.width - 24f * s, (rows * (ch + gap) + 20f) * s);
            scroll = GUI.BeginScrollView(view, scroll, content, false, true);
            for (int i = 0; i < cards.Count; i++)
            {
                var r = R(x0 + (i % cols) * (cw + gap), 14f + (i / cols) * (ch + gap), cw, ch);
                if (r.yMax < scroll.y || r.y > scroll.y + view.height) continue; // off screen
                var c = cards[i];
                if (!ArcaneSkin.Loaded) { CardArt.Plain(c, r, 1f); continue; }
                CardArt.Glow(c, r, 1f, false);
                CardArt.Frame(c, r, 1f);
                CardArt.Face(c, r, 1f);
            }
            GUI.EndScrollView();
        }

        // ---------------------------------------------------------------- drawing

        Rect R(float x, float y, float w, float h) => new Rect(x * s, y * s, w * s, h * s);

        static Color With(Color c, float a) => new Color(c.r, c.g, c.b, c.a * a);

        void BuildStyles()
        {
            if (Mathf.Approximately(builtScale, s) && titleStyle != null) return;
            builtScale = s;
            int F(float px) => Mathf.Max(8, Mathf.RoundToInt(px * s));
            titleStyle = new GUIStyle { fontSize = F(92), alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            subStyle = new GUIStyle { fontSize = F(26), alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Italic };
            buttonStyle = new GUIStyle { fontSize = F(34), alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            smallButtonStyle = new GUIStyle(buttonStyle) { fontSize = F(24) };
            headStyle = new GUIStyle { fontSize = F(18), alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            textStyle = new GUIStyle { fontSize = F(20), alignment = TextAnchor.UpperCenter, wordWrap = true };
            if (ArcaneSkin.Loaded)
            {
                titleStyle.font = ArcaneSkin.Title;
                subStyle.font = ArcaneSkin.Body;
                buttonStyle.font = smallButtonStyle.font = ArcaneSkin.Title;
                headStyle.font = ArcaneSkin.BodyBold;
                textStyle.font = ArcaneSkin.Body;
            }
        }

        static void Label(Rect r, string text, GUIStyle style, Color color)
        {
            if (Event.current.type != EventType.Repaint) return;
            float k = Screen.height / 1080f;
            var tint = GUI.color;
            GUI.color = Color.white; // the colour carries the alpha
            int size = style.fontSize;
            style.fontSize = UIStyle.FitFontSize(style, text, r); // shrink to stay inside its rect
            style.normal.textColor = new Color(0f, 0f, 0f, color.a * 0.8f);
            GUI.Label(new Rect(r.x + 2f * k, r.y + 2f * k, r.width, r.height), text, style);
            style.normal.textColor = color;
            GUI.Label(r, text, style);
            style.fontSize = size;
            GUI.color = tint;
        }

        /// <summary>
        /// A kit button (button / hover / pressed art); a disabled one is dimmed and shows its note on hover. active
        /// keeps it lit (the chosen sort).
        /// </summary>
        bool Button(float x, float y, float w, float h, string label, bool enabled, string note = null, bool small = false, bool active = false)
        {
            var r = R(x, y, w, h);
            bool over = r.Contains(Event.current.mousePosition);
            bool down = enabled && over && Mouse.current != null && Mouse.current.leftButton.isPressed;
            bool clicked = enabled && GUI.Button(r, GUIContent.none, GUIStyle.none);
            if (clicked) Sfx.Click();
            float alpha = GUI.color.a;

            if (Event.current.type == EventType.Repaint)
            {
                var tint = GUI.color;
                bool lit = (over && enabled) || active;
                GUI.color = new Color(1f, 1f, 1f, alpha * (enabled ? 1f : 0.45f));
                if (ArcaneSkin.Loaded)
                {
                    var tex = down ? ArcaneSkin.ButtonPressed : lit ? ArcaneSkin.ButtonHover : ArcaneSkin.Button;
                    float grow = lit && !small ? 6f : lit ? 3f : 0f;
                    UIStyle.Nine(R(x - grow, y - grow, w + 2f * grow, h + 2f * grow), tex, s * (small ? 0.75f : 1f));
                }
                else UIStyle.DrawPanel(r, lit ? UIStyle.PanelHover : UIStyle.Panel);
                GUI.color = tint;

                var text = enabled ? (lit ? UIStyle.Accent : UIStyle.Parchment) : UIStyle.Muted;
                text.a = alpha * (enabled ? 1f : 0.6f);
                var lr = R(x, y + (down ? 2f : 0f), w, h);
                Label(lr, over && !enabled && note != null ? note : label, small ? smallButtonStyle : buttonStyle, text);
            }
            return clicked;
        }
    }
}
