using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace DungeonConveyor
{
    /// <summary>
    /// IMGUI heads-up display on a 1080p-high virtual canvas, in the Arcane look (carved stone, gold trim; see
    /// <see cref="ArcaneSkin"/>): the wave panel and upgrade row at the top left, gold at the top centre, Core bar at the
    /// top right, build menu with category tabs at the bottom (tooltips show combat stats with the run's upgrades), ore
    /// info when hovering a deposit, a placement hint while placing, the upgrade-card pick after each wave (with the
    /// wave's tally), the map-cleared screen and the game-over screen. Reward feedback (gold fly-ins, Core hit, boss
    /// warning and bar, card deal and pick) runs through <see cref="HudFx"/> on unscaled time. Owns Time.timeScale:
    /// the chosen game speed, or 0 while a card pick or end screen is up. Falls back to plain panels if the kit is missing.
    /// </summary>
    [RequireComponent(typeof(BuildController))]
    public class HUD : MonoBehaviour
    {
        static readonly List<Rect> blockers = new List<Rect>();
        static readonly (BuildCategory tab, string label)[] Tabs =
        {
            (BuildCategory.Defense, "DEFENSES"), (BuildCategory.Logistics, "LOGISTICS"), (BuildCategory.Mining, "MINING"),
        };

        enum Screen_ { None, Cards, Cleared, Over }

        float s;           // pixels per virtual unit
        float vw, vh;      // virtual canvas size
        float lastScale = -1f;
        BuildCategory tab = BuildCategory.Defense;
        BuildController build;
        WaveManager waves;
        ItemFlow flow;
        TileHover hover;
        IsoGrid map;
        Camera cam;
        readonly List<BuildingCatalog.Entry> shown = new List<BuildingCatalog.Entry>();
        readonly Dictionary<(int, bool, bool, bool, TextAnchor), GUIStyle> styles = new Dictionary<(int, bool, bool, bool, TextAnchor), GUIStyle>();
        static bool Skin => ArcaneSkin.Loaded;

        // Effects state.
        readonly HudFx fx = new HudFx();
        readonly HudFx.Counter gold = new HudFx.Counter();
        HudFx.Timer goldRed, goldFlash, coreFlash, coreShake;
        float coreShakeAmp, coreGhost, coreGhostHold, bossGhost, bossGhostHold, lastBossFrac = 1f;
        Vector2 goldPx;
        CoreBuilding hookedCore;
        bool goldHooked;
        Enemy lastBoss;
        float lastGoldChange = -10f;
        int warnedFor = -1;
        float bossBannerT0 = -10f;
        readonly Dictionary<BuildingCatalog.Entry, bool> wasAfford = new Dictionary<BuildingCatalog.Entry, bool>();
        readonly Dictionary<BuildingCatalog.Entry, float> glint = new Dictionary<BuildingCatalog.Entry, float>();

        // Screens.
        Screen_ screen;
        float screenT;
        List<Run.Card> curOffer;
        float offerT0;
        int tallyWave = -1;
        bool freshTally;
        int takenBefore;
        float flyingUntil;

        class PickAnim
        {
            public List<Run.Card> Cards;
            public Rect[] Rects;
            public int Chosen;
            public float T0;
            public Vector2 Target;      // virtual centre of the upgrade slot the card flies to
            public const float Dur = 0.6f;
        }
        PickAnim pickAnim;

        // Pending tooltip (drawn last, on top).
        string tipTitle, tipTag, tipBody;
        Rect tipAnchor;
        bool tipRight;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => blockers.Clear();

        /// <summary>True when the screen position (Input System, origin bottom-left) is over a HUD panel.</summary>
        public static bool IsPointerOverUI(Vector2 screenPos)
        {
            var p = new Vector2(screenPos.x, UnityEngine.Screen.height - screenPos.y);
            foreach (var r in blockers) if (r.Contains(p)) return true;
            return false;
        }

        void Awake()
        {
            UIStyle.Init();
            build = GetComponent<BuildController>();
        }

        Rect R(float x, float y, float w, float h) => new Rect(x * s, y * s, w * s, h * s);
        Rect R(Rect v) => R(v.x, v.y, v.width, v.height);

        bool CoreLost => build.Core != null && build.Core.Destroyed;
        bool Picking => waves != null && waves.Offer != null && !waves.AutoPickCards;
        bool Cleared => waves != null && waves.StageCleared && !CoreLost;
        bool PickAnimating => pickAnim != null && Time.unscaledTime - pickAnim.T0 < PickAnim.Dur;

        // ---------------------------------------------------------------- update: events, effects, keys

        void Update()
        {
            if (waves == null) waves = GetComponent<WaveManager>();
            if (cam == null) cam = Camera.main;
            float dt = Time.unscaledDeltaTime;
            Hook();
            fx.Update(dt);
            gold.Tick(dt);
            goldRed.Tick(dt); goldFlash.Tick(dt); coreFlash.Tick(dt); coreShake.Tick(dt);
            if (fx.Flies.Count == 0 && gold.Target != build.Gold) gold.Target = build.Gold;
            TickBars(dt);
            TickGlints(dt);
            TickBoss();

            var kind = CoreLost ? Screen_.Over : Picking ? Screen_.Cards : Cleared ? Screen_.Cleared : Screen_.None;
            if (kind != screen) { screen = kind; screenT = 0f; }
            screenT += dt;
            TrackOffer();
            ReadKeys();

            GameSpeed.Apply(CoreLost || Picking || Cleared);
        }

        /// <summary>Subscribes to the gold and Core events once their sources exist.</summary>
        void Hook()
        {
            if (!goldHooked)
            {
                goldHooked = true;
                gold.Reset(build.Gold);
                build.GoldChanged += OnGold;
            }
            if (build.Core != null && hookedCore != build.Core)
            {
                hookedCore = build.Core;
                coreGhost = hookedCore.Health / hookedCore.MaxHealth;
                hookedCore.Damaged += OnCoreDamaged;
            }
        }

        void OnDestroy()
        {
            if (build != null) build.GoldChanged -= OnGold;
            if (hookedCore != null) hookedCore.Damaged -= OnCoreDamaged;
        }

        void OnGold(int delta, Vector3? from)
        {
            lastGoldChange = Time.unscaledTime;
            if (delta == 0) return;
            if (delta < 0)
            {
                gold.Target += delta;
                goldRed.Start(0.45f);
                return;
            }
            bool bossKill = lastBoss != null && lastBoss.Health <= 0f && from.HasValue;
            if (bossKill) lastBoss = null;
            void Land()
            {
                gold.Target += delta;
                gold.Bump.Start(bossKill ? 0.4f : 0.25f);
                if (bossKill) goldFlash.Start(0.55f);
            }
            if (from.HasValue && cam != null && !HudFx.ReducedMotion)
            {
                var sp = cam.WorldToScreenPoint(from.Value);
                if (sp.z >= 0f)
                {
                    float size = Mathf.Lerp(20f, 40f, Mathf.Clamp01(Mathf.Log10(Mathf.Max(1, delta)) / 2.7f)) * (bossKill ? 1.3f : 1f);
                    fx.Add(new HudFx.Fly
                    {
                        From = new Vector2(Mathf.Clamp(sp.x, 24f, UnityEngine.Screen.width - 24f), Mathf.Clamp(UnityEngine.Screen.height - sp.y, 24f, UnityEngine.Screen.height - 24f)),
                        To = goldPx,
                        Dur = bossKill ? 0.6f : 0.5f,
                        Size = size,
                        Text = "+" + delta,
                        Color = UIStyle.GoldColor,
                        Coin = true,
                        OnArrive = Land,
                    });
                    return;
                }
            }
            Land();
        }

        void OnCoreDamaged(float amount, bool fromBoss)
        {
            var core = build.Core;
            float before = Mathf.Clamp01((core.Health + amount) / core.MaxHealth);
            coreGhost = Mathf.Max(coreGhost, before);
            coreGhostHold = 0.4f;
            coreFlash.Start(fromBoss ? 0.5f : 0.35f);
            coreShake.Start(fromBoss ? 0.45f : 0.3f);
            coreShakeAmp = fromBoss ? 14f : 5f;
        }

        void TickBars(float dt)
        {
            if (build.Core != null)
            {
                float f = Mathf.Clamp01(build.Core.Health / build.Core.MaxHealth);
                coreGhostHold -= dt;
                if (coreGhostHold <= 0f && coreGhost > f) coreGhost = Mathf.MoveTowards(coreGhost, f, dt * Mathf.Max(0.4f, (coreGhost - f) * 2f));
                if (coreGhost < f) coreGhost = f;
            }
            var boss = waves != null ? waves.Boss : null;
            if (boss != null)
            {
                float f = Mathf.Clamp01(boss.Health / boss.MaxHealth);
                if (f < lastBossFrac) { bossGhost = Mathf.Max(bossGhost, lastBossFrac); bossGhostHold = 0.35f; }
                lastBossFrac = f;
                bossGhostHold -= dt;
                if (bossGhostHold <= 0f && bossGhost > f) bossGhost = Mathf.MoveTowards(bossGhost, f, dt * Mathf.Max(0.4f, (bossGhost - f) * 2f));
                if (bossGhost < f) bossGhost = f;
            }
            else { lastBossFrac = 1f; bossGhost = 1f; }
        }

        void TickGlints(float dt)
        {
            if (build.Catalog == null) return;
            foreach (var e in build.Catalog.Entries)
            {
                if (!e.Purchasable) continue;
                bool a = build.CanAfford(e);
                if (wasAfford.TryGetValue(e, out bool prev) && !prev && a && Time.unscaledTime - lastGoldChange < 0.5f) glint[e] = 0f;
                wasAfford[e] = a;
            }
            var keys = new List<BuildingCatalog.Entry>(glint.Keys);
            foreach (var k in keys)
            {
                glint[k] += dt;
                if (glint[k] > 0.6f) glint.Remove(k);
            }
        }

        void TickBoss()
        {
            if (waves == null) return;
            var boss = waves.Boss;
            if (boss != null) lastBoss = boss;
            if (waves.BossNext && waves.NextWaveIn <= 10f && warnedFor != waves.Wave + 1)
            {
                warnedFor = waves.Wave + 1;
                bossBannerT0 = Time.unscaledTime;
            }
        }

        bool WarningPulse => waves != null && waves.BossNext && waves.NextWaveIn <= 10f && !CoreLost && screen == Screen_.None;

        /// <summary>Detects a freshly dealt offer (and whether its wave still needs its tally counted up).</summary>
        void TrackOffer()
        {
            if (!Picking) { curOffer = null; return; }
            if (PickAnimating) { offerT0 = Time.unscaledTime; return; }   // the next card pick waits for the last pick to land
            if (waves.Offer == curOffer) return;
            curOffer = waves.Offer;
            offerT0 = Time.unscaledTime;
            int w = waves.LastCleared.Wave;
            freshTally = w != tallyWave;
            tallyWave = w;
        }

        void ReadKeys()
        {
            var k = Keyboard.current;
            if (k == null) return;
            if (screen == Screen_.Cards && curOffer != null && !PickAnimating && Time.unscaledTime - offerT0 > DealEnd(curOffer.Count))
            {
                int i = k.digit1Key.wasPressedThisFrame || k.numpad1Key.wasPressedThisFrame ? 0
                    : k.digit2Key.wasPressedThisFrame || k.numpad2Key.wasPressedThisFrame ? 1
                    : k.digit3Key.wasPressedThisFrame || k.numpad3Key.wasPressedThisFrame ? 2 : -1;
                if (i >= 0 && i < curOffer.Count) PickCard(i);
            }
            else if (screen == Screen_.Cleared && screenT > 0.35f
                && (k.spaceKey.wasPressedThisFrame || k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame))
                NextMap();
            else if (screen == Screen_.Over && screenT > 0.35f && k.rKey.wasPressedThisFrame)
                NewRun();
        }

        void NextMap()
        {
            Run.Advance(build.Gold);
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        void NewRun()
        {
            Run.Reset();
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        // ---------------------------------------------------------------- OnGUI

        void OnGUI()
        {
            if (waves == null) waves = GetComponent<WaveManager>();
            if (flow == null) flow = GetComponent<ItemFlow>();
            if (hover == null) hover = GetComponent<TileHover>();
            if (map == null) map = GetComponent<IsoGrid>();

            s = UnityEngine.Screen.height / 1080f;
            vw = UnityEngine.Screen.width / s;
            vh = 1080f;
            UIStyle.EnsureStyles(s);
            if (!Mathf.Approximately(s, lastScale)) { styles.Clear(); lastScale = s; }
            if (Event.current.type == EventType.Repaint) blockers.Clear();
            tipTitle = null;

            DrawTopBar();
            DrawBossBar();
            if (waves != null) { DrawWavePanel(); DrawUpgradeRow(); }
            DrawPlacementHint();
            DrawHoverInfo();
            if (build.Catalog != null) DrawBuildBar();
            DrawBossBanner();
            if (CoreLost) DrawGameOver();
            else if (Picking && !PickAnimating) DrawCards();
            else if (Cleared) DrawStageCleared();
            if (PickAnimating) DrawPickAnim();
            DrawFlies();
            DrawTip();
        }

        // ---------------------------------------------------------------- primitives

        static GUIStyle None => GUIStyle.none;

        GUIStyle St(float size, bool title, bool bold, bool wrap, TextAnchor a)
        {
            int px = Mathf.Max(8, Mathf.RoundToInt(size * s));
            var key = (px, title, bold, wrap, a);
            if (styles.TryGetValue(key, out var g)) return g;
            g = new GUIStyle { fontSize = px, alignment = a, wordWrap = wrap, normal = { textColor = Color.white } };
            if (Skin) g.font = title ? ArcaneSkin.Title : bold ? ArcaneSkin.BodyBold : ArcaneSkin.Body;
            else g.fontStyle = title || bold ? FontStyle.Bold : FontStyle.Normal;
            styles[key] = g;
            return g;
        }

        /// <summary>
        /// Text in virtual units with a soft drop shadow; <paramref name="title"/> picks the display font. Shrinks to fit
        /// its rect (width, or wrapped height) so it never spills out of its panel.
        /// </summary>
        void Txt(float x, float y, float w, float h, string text, float size, Color c, TextAnchor a = TextAnchor.MiddleLeft,
            bool title = false, bool bold = false, bool wrap = false, bool shadow = true)
        {
            if (Event.current.type != EventType.Repaint) return;
            var g = St(size, title, bold, wrap, a);
            var r = R(x, y, w, h);
            int fit = UIStyle.FitFontSize(g, text, r);
            if (fit < g.fontSize) g = St(size * fit / g.fontSize, title, bold, wrap, a);
            if (shadow)
            {
                GUI.contentColor = new Color(0f, 0f, 0f, c.a * 0.75f);
                GUI.Label(new Rect(r.x + 1.5f * s, r.y + 1.5f * s, r.width, r.height), text, g);
            }
            GUI.contentColor = c;
            GUI.Label(r, text, g);
            GUI.contentColor = Color.white;
        }

        void TxtC(float x, float y, float w, float h, string text, float size, Color c, bool title = false, bool bold = false, bool wrap = false)
            => Txt(x, y, w, h, text, size, c, TextAnchor.MiddleCenter, title, bold, wrap);

        void Tint(float a) => GUI.color = new Color(1f, 1f, 1f, a);
        void Untint() => GUI.color = Color.white;

        /// <summary>Draws a kit frame (or the old procedural panel when the kit is missing) and registers it as a click blocker.</summary>
        Rect Frame(float x, float y, float w, float h, Texture2D tex, bool block = true)
        {
            var r = R(x, y, w, h);
            if (Skin) UIStyle.Nine(r, tex, s);
            else UIStyle.DrawPanel(r);
            if (block && Event.current.type == EventType.Repaint) blockers.Add(r);
            return r;
        }

        Rect Panel(float x, float y, float w, float h) => Frame(x, y, w, h, ArcaneSkin.Panel);

        void Block(Rect px)
        {
            if (Event.current.type == EventType.Repaint) blockers.Add(px);
        }

        void Icon(Texture2D t, float x, float y, float w, float h, float alpha = 1f)
        {
            if (t == null || Event.current.type != EventType.Repaint) return;
            Tint(alpha);
            GUI.DrawTexture(R(x, y, w, h), t, ScaleMode.ScaleToFit);
            Untint();
        }

        void Dim(float alpha = 0.55f)
        {
            var all = new Rect(0, 0, UnityEngine.Screen.width, UnityEngine.Screen.height);
            GUI.color = new Color(0f, 0f, 0f, alpha);
            GUI.DrawTexture(all, UIStyle.White);
            Untint();
            Block(all);
        }

        /// <summary>Gem button (button / hover / pressed art, hover and pressed drawn a little larger for their glow).</summary>
        bool GemButton(float x, float y, float w, float h, string label, float size, bool enabled = true, bool accent = false)
        {
            var r = R(x, y, w, h);
            Block(r);
            bool clicked = false;
            bool over = enabled && r.Contains(Event.current.mousePosition);
            bool down = over && Mouse.current != null && Mouse.current.leftButton.isPressed;
            if (enabled && GUI.Button(r, GUIContent.none, None)) clicked = true;
            if (Event.current.type == EventType.Repaint)
            {
                if (!Skin)
                {
                    GUI.Box(r, GUIContent.none, over ? UIStyle.TabActive : UIStyle.Tab);
                }
                else
                {
                    var tex = down ? ArcaneSkin.ButtonPressed : over ? ArcaneSkin.ButtonHover : ArcaneSkin.Button;
                    float grow = over ? 6f : 0f;
                    GUI.color = new Color(1f, 1f, 1f, enabled ? 1f : 0.45f);
                    UIStyle.Nine(R(x - grow, y - grow, w + 2 * grow, h + 2 * grow + (down ? 0f : 0f)), tex, s);
                    Untint();
                }
            }
            Txt(x, y + (down ? 2f : 0f), w, h, label, size, enabled ? (accent ? UIStyle.Accent : UIStyle.Parchment) : UIStyle.Muted, TextAnchor.MiddleCenter, true);
            return clicked;
        }

        // ---------------------------------------------------------------- top

        void DrawTopBar()
        {
            // Gold: banner plate, coin and the counter (bumps, ticks red when spending, flashes on a boss bounty).
            // Gold and crystals are centred as one group, so it stays clear of the wave panel and the Core bar.
            const float bw = 320, bh = 84;
            float x = (vw - (bw + 6 + CrystalsW)) * 0.5f, y = 6;
            Frame(x, y, bw, bh, ArcaneSkin.Banner);
            string text = gold.Value.ToString("N0");
            float size = 36f * (1f + 0.18f * Mathf.Sin(gold.Bump.N * Mathf.PI) * (gold.Bump.Active ? 1f : 0f));
            float tw = St(size, true, false, false, TextAnchor.MiddleLeft).CalcSize(new GUIContent(text)).x / s;
            float total = 44 + 10 + tw, cx = x + (bw - total) * 0.5f;
            if (Skin)
            {
                Icon(ArcaneSkin.Coin, cx, y + bh * 0.5f - 22, 44, 44);
                if (goldFlash.Active)
                {
                    GUI.color = new Color(1f, 0.85f, 0.4f, 0.8f * goldFlash.Fade);
                    UIStyle.GlowRing(R(x + 40, y + 12, bw - 80, bh - 24), s, 16f);
                    Untint();
                }
            }
            else GUI.DrawTexture(R(cx, y + bh * 0.5f - 16, 32, 32), UIStyle.GoldIcon);
            var col = Color.Lerp(UIStyle.Parchment, UIStyle.Danger, goldRed.Fade);
            col = Color.Lerp(col, new Color(1f, 0.9f, 0.5f), goldFlash.Fade);
            Txt(cx + 54, y, tw + 20, bh, text, size, col, TextAnchor.MiddleLeft, true);
            goldPx = new Vector2((cx + 26) * s, (y + bh * 0.5f) * s);
            DrawCrystals(x + bw + 6, y, bh);

            DrawCoreBar();
        }

        /// <summary>Crystals banked this run (spent in the shop between maps), right of the gold.</summary>
        const float CrystalsW = 230;

        void DrawCrystals(float x, float y, float h)
        {
            const float w = CrystalsW;
            var r = Frame(x, y, w, h, ArcaneSkin.Banner);
            string text = Run.Crystals.ToString("N0");
            float tw = St(28, true, false, false, TextAnchor.MiddleLeft).CalcSize(new GUIContent(text)).x / s;
            float total = 34 + 8 + tw, cx = x + (w - total) * 0.5f;
            if (Skin) Icon(ArcaneSkin.GemRare, cx, y + h * 0.5f - 17, 34, 34);
            Txt(cx + 42, y, tw + 20, h, text, 28, new Color(0.6f, 0.85f, 1f), TextAnchor.MiddleLeft, true);
            if (r.Contains(Event.current.mousePosition))
                SetTip("Crystals", "BANKED THIS RUN", "Every crystal delivered to the Core is sold and also banked here. Spend crystals in the shop after each map to unlock new buildings.",
                    new Rect(x, y, w, h), false);
        }

        void DrawCoreBar()
        {
            var core = build.Core;
            if (core == null) return;
            const float w = 380, h = 78;
            float x = vw - w - 12, y = 6;
            var sh = HudFx.Shake(coreShake.Fade, coreShakeAmp * s, Time.unscaledTime);
            float ox = sh.x / s, oy = sh.y / s;
            float f = Mathf.Clamp01(core.Health / core.MaxHealth);
            var frame = Frame(x + ox, y + oy, w, h, ArcaneSkin.BarFrame);
            var inner = BarInner(x + ox, y + oy, w, h);
            DrawFill(inner, f, coreGhost, Color.Lerp(UIStyle.Danger, new Color(0.35f, 0.85f, 0.25f), f));
            if (coreFlash.Active)
            {
                GUI.color = new Color(1f, 0.25f, 0.2f, 0.55f * coreFlash.Fade);
                GUI.DrawTexture(inner, UIStyle.White);
                GUI.color = new Color(1f, 0.3f, 0.25f, 0.9f * coreFlash.Fade);
                UIStyle.GlowRing(frame, s, 14f);
                Untint();
            }
            Txt(x + ox, y + oy, w, h, $"CORE  {Mathf.CeilToInt(core.Health)} / {core.MaxHealth:0}", 21, UIStyle.Parchment, TextAnchor.MiddleCenter, false, true);
        }

        /// <summary>The dark channel inside a bar frame, in pixels.</summary>
        Rect BarInner(float x, float y, float w, float h) => Skin ? R(x + 36, y + h * 0.5f - 13, w - 72, 26) : R(x + 6, y + 6, w - 12, h - 12);

        /// <summary>Fill plus a lighter ghost segment showing what was just lost.</summary>
        void DrawFill(Rect inner, float f, float ghost, Color fill)
        {
            if (Event.current.type != EventType.Repaint) return;
            GUI.color = new Color(0.1f, 0.09f, 0.1f, 1f);
            GUI.DrawTexture(inner, UIStyle.White);
            if (ghost > f)
            {
                GUI.color = new Color(1f, 0.82f, 0.7f, 0.9f);
                GUI.DrawTexture(new Rect(inner.x + inner.width * f, inner.y, inner.width * (ghost - f), inner.height), UIStyle.White);
            }
            GUI.color = fill;
            GUI.DrawTexture(new Rect(inner.x, inner.y, inner.width * f, inner.height), UIStyle.White);
            GUI.color = new Color(1f, 1f, 1f, 0.18f);
            GUI.DrawTexture(new Rect(inner.x, inner.y, inner.width * f, inner.height * 0.4f), UIStyle.White);
            Untint();
        }

        void DrawBossBar()
        {
            var boss = waves != null ? waves.Boss : null;
            if (boss == null) return;
            const float w = 640, h = 78;
            float x = (vw - w) * 0.5f, y = 88;
            float f = Mathf.Clamp01(boss.Health / boss.MaxHealth);
            Frame(x, y, w, h, ArcaneSkin.BarFrame);
            DrawFill(BarInner(x, y, w, h), f, bossGhost, new Color(0.85f, 0.2f, 0.18f));
            string name = boss.Def != null && !string.IsNullOrEmpty(boss.Def.Name) ? boss.Def.Name.ToUpperInvariant() : "BOSS";
            Txt(x, y, w, h, $"BOSS  ·  {name}  ·  {Pct(f)}%", 21, UIStyle.Parchment, TextAnchor.MiddleCenter, false, true);
        }

        /// <summary>Whole percent of a 0..1 fraction; never shows 0% while something is left.</summary>
        static int Pct(float f) => Mathf.Max(f > 0f ? 1 : 0, Mathf.RoundToInt(f * 100f));

        /// <summary>"BOSS APPROACHING" slams in once per boss wave, holds a moment, then fades.</summary>
        void DrawBossBanner()
        {
            float age = Time.unscaledTime - bossBannerT0;
            if (age < 0f || age > 2.2f || screen != Screen_.None) return;
            float sc = Mathf.Lerp(1.7f, 1f, HudFx.OutBack(age / 0.25f));
            float alpha = Mathf.Clamp01(age / 0.1f) * (1f - Mathf.Clamp01((age - 1.7f) / 0.5f));
            float bw = 620, bh = 92, x = (vw - bw) * 0.5f, y = 250;
            var old = GUI.matrix;
            GUIUtility.ScaleAroundPivot(Vector2.one * sc, new Vector2(vw * 0.5f * s, (y + bh * 0.5f) * s));
            GUI.color = new Color(1f, 1f, 1f, alpha);
            if (Skin) UIStyle.Nine(R(x, y, bw, bh), ArcaneSkin.Banner, s);
            Txt(x, y, bw, bh, "BOSS APPROACHING", 38, new Color(UIStyle.Danger.r, UIStyle.Danger.g, UIStyle.Danger.b, alpha), TextAnchor.MiddleCenter, true);
            Untint();
            GUI.matrix = old;
        }

        void DrawWavePanel()
        {
            const float x = 12, y = 12, w = 280, h = 312;
            Frame(x, y, w, h, ArcaneSkin.HeaderPanel);
            if (WarningPulse && Skin && Event.current.type == EventType.Repaint)
            {
                float p = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f);
                GUI.color = new Color(1f, 0.2f, 0.15f, 0.25f + 0.55f * p);
                UIStyle.GlowRing(R(x, y + 14, w, h - 14), s, 14f);
                Untint();
            }
            int inMap = waves.Wave - waves.FirstWave + 1, total = Run.Current.Waves;
            bool boss = Run.IsBossWave(waves.Wave) && waves.Alive > 0;
            float cx = x + 30, cw = w - 60;
            int cardAt = waves.NextCardWave - waves.FirstWave + 1;
            string mapLine = cardAt <= total ? $"MAP {Run.Stage + 1}  ·  CARD AT WAVE {cardAt}" : $"MAP {Run.Stage + 1}";
            TxtC(cx, y + 50, cw, 22, mapLine, 17, UIStyle.Muted, true);
            TxtC(cx, y + 70, cw, 36, inMap <= 0 ? "BUILD PHASE" : boss ? "BOSS WAVE" : $"WAVE {inMap} / {total}", inMap <= 0 ? 24 : 28,
                boss ? UIStyle.Danger : UIStyle.Parchment, true);

            // Enemies / kills block.
            if (Event.current.type == EventType.Repaint)
            {
                GUI.color = new Color(0f, 0f, 0f, 0.28f);
                GUI.DrawTexture(R(cx, y + 106, cw, 50), UIStyle.White);
                Untint();
            }
            TxtC(cx, y + 107, cw * 0.5f, 20, "Enemies", 16, UIStyle.Muted, false, true);
            TxtC(cx + cw * 0.5f, y + 107, cw * 0.5f, 20, "Kills", 16, UIStyle.Muted, false, true);
            TxtC(cx, y + 125, cw * 0.5f, 30, waves.Alive.ToString(), 26, UIStyle.Parchment, true);
            TxtC(cx + cw * 0.5f, y + 125, cw * 0.5f, 30, waves.Kills.ToString(), 26, UIStyle.Parchment, true);

            string next = waves.AllWavesSent ? "Final wave: clear the map"
                : $"{(waves.BossNext ? "BOSS" : "Next wave")} in {Mathf.CeilToInt(Mathf.Max(0f, waves.NextWaveIn))}s";
            TxtC(cx, y + 160, cw, 26, next, 20, waves.BossNext ? UIStyle.Danger : UIStyle.Parchment, false, true);

            if (GemButton(x + 22, y + 192, w - 44, 50, $"SEND NOW  +{waves.EarlyBonus}", 20, !waves.AllWavesSent)) waves.CallEarly();

            // Game speed gems, then the reduced-motion toggle at the row's end.
            const float sb = 40, gap = 6;
            float rowW = (GameSpeed.Choices.Length + 1) * sb + GameSpeed.Choices.Length * gap;
            float sx = x + (w - rowW) * 0.5f, sy = y + 254;
            for (int i = 0; i < GameSpeed.Choices.Length; i++)
            {
                float v = GameSpeed.Choices[i];
                bool on = Mathf.Approximately(GameSpeed.Speed, v);
                if (SquareButton(sx + i * (sb + gap), sy, sb, on, v < 1f ? ".5x" : $"{v:0}x")) GameSpeed.Speed = v;
            }
            float rx = sx + GameSpeed.Choices.Length * (sb + gap);
            bool reduced = HudFx.ReducedMotion;
            var rr = R(rx, sy, sb, sb);
            if (SquareButton(rx, sy, sb, false, "FX", reduced)) HudFx.ReducedMotion = !reduced;
            if (rr.Contains(Event.current.mousePosition))
                SetTip("Reduced motion", reduced ? "ON" : "OFF", reduced ? "Screen shake and fly-ins are off. Click to turn them back on." : "Click to turn off screen shake and fly-ins.", new Rect(rx, sy, sb, sb), true);
        }

        bool SquareButton(float x, float y, float size, bool active, string label, bool struck = false)
        {
            var r = R(x, y, size, size);
            Block(r);
            bool over = r.Contains(Event.current.mousePosition);
            bool clicked = GUI.Button(r, GUIContent.none, None);
            if (Event.current.type == EventType.Repaint)
            {
                if (Skin)
                {
                    float g = active || over ? 3f : 0f;
                    UIStyle.Nine(R(x - g, y - g, size + 2 * g, size + 2 * g), active || over ? ArcaneSkin.SlotHover : ArcaneSkin.Slot, s);
                }
                else GUI.Box(r, GUIContent.none, active ? UIStyle.TabActive : UIStyle.Tab);
            }
            Txt(x, y, size, size, label, 19, struck ? UIStyle.Muted : active ? UIStyle.Accent : UIStyle.Parchment, TextAnchor.MiddleCenter, false, true);
            if (struck && Event.current.type == EventType.Repaint)
            {
                GUI.color = UIStyle.Danger;
                var save = GUI.matrix;
                GUIUtility.RotateAroundPivot(-35f, r.center);
                GUI.DrawTexture(new Rect(r.center.x - r.width * 0.36f, r.center.y - 1.5f * s, r.width * 0.72f, 3f * s), UIStyle.White);
                GUI.matrix = save;
                Untint();
            }
            return clicked;
        }

        // ---------------------------------------------------------------- upgrade row

        const float UpX = 16, UpY = 334, UpCell = 50, UpStride = 56;
        const int UpCols = 5, UpRows = 3;

        Rect UpgradeRect(int i) => new Rect(UpX + (i % UpCols) * UpStride, UpY + (i / UpCols) * UpStride, UpCell, UpCell);

        static Texture2D IconFor(Run.CardIcon i) => CardArt.IconFor(i);

        /// <summary>One small emblem per card taken this run; hover shows what it does.</summary>
        void DrawUpgradeRow()
        {
            if (!Skin) return;
            int n = Run.Taken.Count - (flyingUntil > Time.unscaledTime ? 1 : 0);
            int cap = UpCols * UpRows;
            for (int i = 0; i < Mathf.Min(n, cap); i++)
            {
                var c = Run.Taken[i];
                var v = UpgradeRect(i);
                var r = R(v);
                Block(r);
                bool over = r.Contains(Event.current.mousePosition);
                if (Event.current.type == EventType.Repaint)
                {
                    float g = over ? 2f : 0f;
                    if (c.Rarity != Rarity.Common)
                    {
                        var rc = Run.ColorOf(c.Rarity);
                        GUI.color = new Color(rc.r, rc.g, rc.b, 0.75f);
                        UIStyle.GlowRing(r, s, 6f);
                        Untint();
                    }
                    UIStyle.Nine(R(v.x - g, v.y - g, v.width + 2 * g, v.height + 2 * g), over ? ArcaneSkin.SlotHover : ArcaneSkin.Slot, s);
                }
                Icon(IconFor(c.Icon), v.x + 7, v.y + 7, v.width - 14, v.height - 14);
                if (over) SetTip(c.Title, Run.RarityNames[(int)c.Rarity] + "  ·  " + c.Tag, c.Text, v, true);
            }
            if (n > cap) Txt(UpX, UpY + UpRows * UpStride, 200, 24, $"+{n - cap} more", 16, UIStyle.Muted);
        }

        // ---------------------------------------------------------------- run screens

        const float CardW = 280, CardH = 397, CardGap = 44, CardTop = 330;

        Rect CardRect(int i, int n)
        {
            float total = n * CardW + (n - 1) * CardGap;
            return new Rect((vw - total) * 0.5f + i * (CardW + CardGap), CardTop, CardW, CardH);
        }

        float DealStart(int i) => freshTally ? 0.95f + 0.18f * i : 0.1f + 0.15f * i;
        float DealEnd(int n) => DealStart(n - 1) + 0.5f;

        void DrawCards()
        {
            Dim(0.68f);
            var offer = waves.Offer;
            if (offer == null) return;
            float age = Time.unscaledTime - offerT0;
            bool boss = Run.IsBossWave(waves.PickingFor);
            int cleared = waves.PickingFor - waves.FirstWave + 1;

            // Banner slams in.
            float bt = HudFx.OutBack(age / 0.28f);
            float bsc = Mathf.Lerp(1.5f, 1f, bt), ba = Mathf.Clamp01(age / 0.12f);
            var old = GUI.matrix;
            GUIUtility.ScaleAroundPivot(Vector2.one * bsc, new Vector2(vw * 0.5f * s, 160 * s));
            GUI.color = new Color(1f, 1f, 1f, ba);
            if (Skin) UIStyle.Nine(R((vw - 620) * 0.5f, 112, 620, 96), ArcaneSkin.Banner, s);
            Txt((vw - 620) * 0.5f, 112, 620, 96, boss ? "BOSS WAVE CLEARED" : $"WAVE {cleared} CLEARED", 40, new Color(UIStyle.Parchment.r, UIStyle.Parchment.g, UIStyle.Parchment.b, ba), TextAnchor.MiddleCenter, true);
            Untint();
            GUI.matrix = old;
            if (age > 0.2f)
            {
                int together = waves.ClearedTogether;
                if (together > 1)
                    TxtC(0, 206, vw, 28, $"{together} waves cleared together  ·  rarity boosted!", 22, Run.ColorOf(Rarity.Epic), false, true);
                else TxtC(0, 206, vw, 28, "Choose an upgrade", 22, UIStyle.Muted);
            }

            DrawTally(age);

            for (int i = 0; i < offer.Count; i++)
            {
                var c = offer[i];
                var rect = CardRect(i, offer.Count);
                float u = Mathf.Clamp01((age - DealStart(i)) / 0.45f);
                if (u <= 0f) continue;
                bool dealt = u >= 1f && !PickAnimating;
                var pr = R(rect);
                bool hot = dealt && pr.Contains(Event.current.mousePosition);
                if (dealt && GUI.Button(pr, GUIContent.none, None)) { PickCard(i); return; }

                float lift = hot ? 14f : 0f;
                DrawCard(c, new Rect(rect.x, rect.y - lift - (1f - HudFx.OutCubic(u)) * 70f, rect.width, rect.height), hot ? 1.04f : 1f, 1f, u, hot, i);
            }

            float bottom = CardTop + CardH + 22;
            if (age > DealEnd(offer.Count) - 0.2f)
                TxtC(0, bottom, vw, 26, "Press 1, 2 or 3, or click a card", 18, UIStyle.Muted);
            if (waves.CardsOwed > 1)
                TxtC(0, bottom + 26, vw, 26, $"{waves.CardsOwed - 1} more pick{(waves.CardsOwed > 2 ? "s" : "")} after this one", 16, UIStyle.Muted);
            DrawRarityOdds(bottom + 58, waves.PickLuck);
        }

        /// <summary>The chance of each rarity for these picks, each in its colour.</summary>
        void DrawRarityOdds(float y, float luck)
        {
            var p = Run.RarityChances(luck);
            const float cw = 190;
            float x0 = (vw - 4 * cw) * 0.5f;
            for (int i = 0; i < 4; i++)
            {
                float pct = p[i] * 100f;
                string v = pct >= 9.95f ? pct.ToString("0") : pct.ToString("0.#");
                TxtC(x0 + i * cw, y, cw, 24, $"{Run.RarityNames[i]} {v}%", 16, Run.RarityColors[i], false, true);
            }
        }

        /// <summary>Wave tally: kills, gold earned, leaked (counting up), plus a FLAWLESS stamp when nothing got through.</summary>
        void DrawTally(float age)
        {
            var st = waves.LastCleared;
            if (!freshTally) age += 5f;
            float y = 244, cw = 200;
            float x0 = (vw - 3 * cw) * 0.5f;
            if (age > 0.2f)
            {
                string[] labels = { "KILLS", "GOLD EARNED", "LEAKED" };
                int[] vals = { HudFx.CountUp(st.Kills, age, 0.25f), HudFx.CountUp(st.Gold, age, 0.4f), HudFx.CountUp(st.Leaked, age, 0.55f) };
                for (int i = 0; i < 3; i++)
                {
                    TxtC(x0 + i * cw, y, cw, 20, labels[i], 15, UIStyle.Muted, false, true);
                    var col = i == 1 ? UIStyle.GoldColor : i == 2 && st.Leaked > 0 ? UIStyle.Danger : UIStyle.Parchment;
                    TxtC(x0 + i * cw, y + 18, cw, 36, vals[i].ToString("N0"), 30, col, true);
                }
            }
            if (st.Leaked == 0 && age > 1.0f)
            {
                float t = HudFx.OutBack((age - 1.0f) / 0.3f);
                var old = GUI.matrix;
                var piv = new Vector2((vw * 0.5f + 360) * s, (y + 26) * s);
                GUIUtility.RotateAroundPivot(-8f, piv);
                GUIUtility.ScaleAroundPivot(Vector2.one * Mathf.Lerp(1.8f, 1f, t), piv);
                float a = Mathf.Clamp01((age - 1.0f) / 0.1f);
                TxtC(vw * 0.5f + 250, y, 220, 52, "FLAWLESS", 30, new Color(UIStyle.GoldColor.r, UIStyle.GoldColor.g, UIStyle.GoldColor.b, a), true);
                GUI.matrix = old;
            }
        }

        /// <summary>
        /// Draws one spell card whole (never 9-sliced): emblem in the arch, title on the plate, tag and text in the lower
        /// panel, rarity gem in the top diamond. <paramref name="deal"/> 0..1 plays the drop-in flip.
        /// </summary>
        void DrawCard(Run.Card c, Rect v, float scale, float alpha, float deal, bool hot, int index, bool bare = false)
        {
            var centre = v.center;
            float w = v.width * scale, h = v.height * scale;
            var rr = new Rect(centre.x - w * 0.5f, centre.y - h * 0.5f, w, h);
            float a = alpha * Mathf.Clamp01(deal * 3f);
            if (!Skin)
            {
                CardArt.Plain(c, R(rr), a);
                return;
            }

            // Flip: back (dim frame) shrinks to an edge, face grows out of it.
            bool back = deal < 0.5f;
            float fw = back ? w * (1f - deal * 2f) : w * (deal * 2f - 1f);
            if (deal >= 1f) fw = w;
            var fr = new Rect(centre.x - fw * 0.5f, rr.y, Mathf.Max(1f, fw), rr.height);
            if (deal >= 1f) CardArt.Glow(c, R(fr), a, hot);
            CardArt.Frame(c, R(fr), a, back);
            if (deal < 1f) return;

            if (bare)
            {
                // Shrinking into the upgrade row: just the emblem on the frame, no text.
                Icon(IconFor(c.Icon), rr.x + w * 0.2f, rr.y + h * 0.2f, w * 0.6f, w * 0.6f, alpha);
                return;
            }
            CardArt.Face(c, R(rr), alpha, index);
        }

        /// <summary>Picks card <paramref name="i"/>: starts the pop / fade / fly animation and hands the card to the wave manager.</summary>
        void PickCard(int i)
        {
            var offer = waves.Offer;
            if (offer == null || i >= offer.Count) return;
            var card = offer[i];
            var rects = new Rect[offer.Count];
            for (int k = 0; k < rects.Length; k++) rects[k] = CardRect(k, offer.Count);
            pickAnim = new PickAnim { Cards = new List<Run.Card>(offer), Rects = rects, Chosen = i, T0 = Time.unscaledTime };
            waves.Pick(card);
            int slot = Mathf.Min(Run.Taken.Count - 1, UpCols * UpRows - 1);
            var ur = UpgradeRect(Mathf.Max(0, slot));
            pickAnim.Target = ur.center;
            flyingUntil = Time.unscaledTime + PickAnim.Dur;
            if (HudFx.ReducedMotion) { pickAnim.T0 = Time.unscaledTime - PickAnim.Dur + 0.15f; flyingUntil = pickAnim.T0 + PickAnim.Dur; }
        }

        void DrawPickAnim()
        {
            float age = Time.unscaledTime - pickAnim.T0;
            float pop = 0.18f;
            for (int i = 0; i < pickAnim.Cards.Count; i++)
            {
                var r = pickAnim.Rects[i];
                var c = pickAnim.Cards[i];
                if (i != pickAnim.Chosen)
                {
                    float a = 1f - Mathf.Clamp01(age / 0.25f);
                    if (a > 0f) DrawCard(c, r, 1f, a, 1f, false, i);
                    continue;
                }
                if (age < pop)
                    DrawCard(c, r, Mathf.Lerp(1f, 1.12f, HudFx.OutCubic(age / pop)), 1f, 1f, true, i);
                else
                {
                    float f = HudFx.InOutCubic((age - pop) / (PickAnim.Dur - pop));
                    var centre = Vector2.Lerp(r.center, pickAnim.Target, f);
                    float sc = Mathf.Lerp(1.12f, UpCell / CardW * 0.9f, f);
                    DrawCard(c, new Rect(centre.x - r.width * 0.5f, centre.y - r.height * 0.5f, r.width, r.height), sc, 1f - 0.2f * f, 1f, false, i, f > 0.3f);
                }
            }
        }

        /// <summary>
        /// Map cleared: the map's tally, then the shop, where crystals banked this run unlock new buildings (prices and
        /// availability in RunSettings), then on to the next map. Gold, upgrades and unlocks carry over.
        /// </summary>
        void DrawStageCleared()
        {
            Dim();
            var items = ShopItems();
            const float cellW = 200, cellH = 188;
            int cols = Mathf.Min(5, UIStyle.CellsThatFit(vw - 160, cellW, 0f)); // as many as the screen holds, up to 5
            int rows = Mathf.Max(1, (items.Count + cols - 1) / cols);
            float w = Mathf.Min(vw - 40, Mathf.Max(760, cols * cellW + 120)), h = 262 + rows * cellH + 112;
            float x = (vw - w) * 0.5f, y = Mathf.Max(60, (vh - h) * 0.5f + 20);
            float age = screenT;
            float sc = Mathf.Lerp(0.9f, 1f, HudFx.OutBack(age / 0.3f)), al = Mathf.Clamp01(age / 0.15f);
            var old = GUI.matrix;
            GUIUtility.ScaleAroundPivot(Vector2.one * sc, new Vector2(vw * 0.5f * s, (y + h * 0.5f) * s));
            GUI.color = new Color(1f, 1f, 1f, al);
            Panel(x, y, w, h);
            if (Skin) UIStyle.Nine(R(x + w * 0.5f - 310, y - 34, 620, 90), ArcaneSkin.Banner, s);
            Untint();
            Txt(x + w * 0.5f - 310, y - 34, 620, 90, $"MAP {Run.Stage + 1} CLEARED", 38, UIStyle.Accent, TextAnchor.MiddleCenter, true);

            string[] labels = { "KILLS", "GOLD", "UPGRADES", "CRYSTALS" };
            int[] vals =
            {
                HudFx.CountUp(waves.Kills, age, 0.3f, 0.7f), HudFx.CountUp(build.Gold, age, 0.45f, 0.7f),
                HudFx.CountUp(Run.Taken.Count, age, 0.6f, 0.5f), HudFx.CountUp(Run.Crystals, age, 0.75f, 0.7f),
            };
            Color[] colors = { UIStyle.Parchment, UIStyle.GoldColor, UIStyle.Parchment, new Color(0.6f, 0.85f, 1f) };
            // Stats share the row with the stamp on the right, so they never run under it.
            var statsRow = new Rect(x + 40, y + 70, w - 300, 70);
            for (int i = 0; i < 4; i++)
            {
                var col = UIStyle.Column(statsRow, i, 4, 8);
                TxtC(col.x, col.y, col.width, 22, labels[i], 16, UIStyle.Muted, false, true);
                TxtC(col.x, col.y + 22, col.width, 44, vals[i].ToString("N0"), 34, colors[i], true);
            }
            TxtC(x + 40, y + 142, w - 80, 30, "Your gold, upgrades and unlocked buildings carry over to the next map.", 18, UIStyle.Parchment, false, false, true);

            if (age > 1.1f)
            {
                float t = HudFx.OutBack((age - 1.1f) / 0.3f);
                var piv = new Vector2((x + w - 140) * s, (y + 100) * s);
                var m2 = GUI.matrix;
                GUIUtility.RotateAroundPivot(-9f, piv);
                GUIUtility.ScaleAroundPivot(Vector2.one * Mathf.Lerp(1.8f, 1f, t), piv);
                string stamp = waves.Leaked == 0 ? "FLAWLESS" : "CLEARED";
                TxtC(x + w - 250, y + 76, 220, 48, stamp, 30, UIStyle.GoldColor, true);
                GUI.matrix = m2;
            }

            // Shop.
            // Header row: title left, balance right, the explanation in what's left between them.
            float shopY = y + 188;
            var header = new Rect(x + 60, shopY, w - 120, 40);
            Txt(header.x, header.y, 100, header.height, "SHOP", 30, UIStyle.Accent, TextAnchor.MiddleLeft, true);
            Txt(header.xMax - 240, header.y, 240, header.height, $"{Run.Crystals:N0} crystals", 24, new Color(0.6f, 0.85f, 1f), TextAnchor.MiddleRight, true);
            Txt(header.x + 110, header.y + 4, header.width - 360, 36, "Spend crystals to unlock buildings for the rest of the run.", 17, UIStyle.Muted, TextAnchor.MiddleLeft, false, true);
            if (items.Count == 0)
                TxtC(x, shopY + 60, w, 40, "Every building is unlocked.", 20, UIStyle.Parchment);

            float gx = x + (w - cols * cellW) * 0.5f, gy = shopY + 56;
            BuildingCatalog.Entry hovered = null;
            Rect hoveredCell = default;
            for (int i = 0; i < items.Count; i++)
            {
                var (e, item) = items[i];
                float cx = gx + (i % cols) * cellW, cy = gy + (i / cols) * cellH;
                if (ShopCell(e, item, cx, cy, cellW, cellH)) { hovered = e; hoveredCell = new Rect(cx, cy, cellW, cellH); }
            }

            float by = y + h - 96;
            if (GemButton(x + w * 0.5f - 130, by, 260, 56, "NEXT MAP", 24, true, true)) NextMap();
            TxtC(x, by + 60, w, 24, "or press Space / Enter", 16, UIStyle.Muted);
            GUI.matrix = old;
            if (hovered != null) DrawTooltip(hovered, hoveredCell.x, hoveredCell.y - 6);
        }

        /// <summary>Every buyable building outside the starting set, cheapest first, with its shop entry.</summary>
        List<(BuildingCatalog.Entry, RunSettings.ShopItem)> ShopItems()
        {
            var list = new List<(BuildingCatalog.Entry, RunSettings.ShopItem)>();
            if (build.Catalog == null) return list;
            var settings = RunSettings.Instance;
            foreach (var e in build.Catalog.Entries)
                if (e.Purchasable && !settings.StartingBuildings.Contains(e.Id)) list.Add((e, settings.ShopEntry(e.Id)));
            list.Sort((a, b) => a.Item2.AvailableAfterMap != b.Item2.AvailableAfterMap
                ? a.Item2.AvailableAfterMap.CompareTo(b.Item2.AvailableAfterMap) : a.Item2.Crystals.CompareTo(b.Item2.Crystals));
            return list;
        }

        /// <summary>One shop slot: icon, name, price and its unlock button (or owned / not yet available). True if hovered.</summary>
        bool ShopCell(BuildingCatalog.Entry e, RunSettings.ShopItem item, float x, float y, float w, float h)
        {
            const float slot = 92;
            bool owned = Run.IsUnlocked(e.Id);
            bool available = Run.Stage + 1 >= item.AvailableAfterMap;
            bool afford = Run.Crystals >= item.Crystals;
            float sx = x + (w - slot) * 0.5f, sy = y + 4;
            var slotRect = R(sx, sy, slot, slot);
            bool over = R(x, y, w, h).Contains(Event.current.mousePosition);
            if (Event.current.type == EventType.Repaint)
            {
                if (Skin) UIStyle.Nine(slotRect, owned || over ? ArcaneSkin.SlotHover : ArcaneSkin.Slot, s);
                else GUI.Box(slotRect, GUIContent.none, owned ? UIStyle.CardActive : UIStyle.Card);
                if (owned)
                {
                    GUI.color = new Color(0.35f, 0.9f, 0.4f, 0.6f);
                    UIStyle.GlowRing(slotRect, s, 10f);
                    Untint();
                }
            }
            Tint(owned || (available && afford) ? 1f : 0.45f);
            if (e.Sprite != null && Event.current.type == EventType.Repaint)
                GUI.DrawTexture(R(sx + 10, sy + 8, slot - 20, slot - 20), e.Sprite.texture, ScaleMode.ScaleToFit);
            Untint();
            TxtC(x, sy + slot + 2, w, 24, e.Name, 18, owned ? UIStyle.Parchment : UIStyle.Muted, false, true);

            float ry = sy + slot + 30;
            if (owned) TxtC(x, ry, w, 46, "UNLOCKED", 18, new Color(0.4f, 0.9f, 0.45f), true);
            else if (!available) TxtC(x, ry, w, 46, $"After map {item.AvailableAfterMap}", 17, UIStyle.Muted, false, true);
            else if (GemButton(x + 22, ry, w - 44, 46, $"{item.Crystals:N0}", 20, afford, afford) && Run.TryUnlock(e.Id))
                glint[e] = 0f;
            if (!owned && available && Skin) Icon(ArcaneSkin.GemRare, x + 32, ry + 11, 24, 24, afford ? 1f : 0.45f);
            return over;
        }

        void DrawPlacementHint()
        {
            var e = build.Placing;
            if (e == null) return;
            const float w = 760, h = 76;
            float x = (vw - w) * 0.5f, y = waves != null && waves.Boss != null ? 172 : 94;
            Frame(x, y, w, h, ArcaneSkin.Banner);
            TxtC(x + 80, y + 8, w - 160, 30, "Placing: " + e.Name, 21, UIStyle.Accent, true);
            string hint = build.BlockedReason != null ? build.BlockedReason
                : !build.CapturesLeftDrag ? "Left-click to build  ·  Right-click or Esc to cancel"
                : e.Id == BuildingCatalog.WallId ? "Drag to build a wall line  ·  Hold right-click to clear  ·  Esc to cancel"
                : "Drag to lay a belt (end on a belt to join it)  ·  Hold right-click to clear  ·  Esc to cancel";
            TxtC(x + 80, y + 38, w - 160, 26, hint, 16, build.BlockedReason != null ? UIStyle.Danger : UIStyle.Parchment, false, true);
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
                body = left > 0 ? $"{left} {(left == 1 ? "crystal" : "crystals")} left to dig" : "Ore used up";
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

            float w = Mathf.Min(Mathf.Clamp(St(15, false, true, false, TextAnchor.MiddleLeft).CalcSize(new GUIContent(body)).x / s + 190, 420, 760), vw - 24), h = 76;
            float x = vw - w - 12, y = 92;
            Frame(x, y, w, h, ArcaneSkin.Banner);
            TxtC(x + 80, y + 8, w - 160, 28, title, 20, UIStyle.Accent, true);
            TxtC(x + 80, y + 38, w - 160, 26, body, 16, UIStyle.Parchment, false, true);
        }

        // ---------------------------------------------------------------- build menu

        void DrawBuildBar()
        {
            const float cell = 112, slot = 92, tabW = 156, tabH = 48, panelH = 200, pad = 28;
            float x = 12, y = vh - 12 - panelH;

            shown.Clear();
            foreach (var e in build.Catalog.Entries)
                if (e.Purchasable && e.Category == tab && Run.IsUnlocked(e.Id)) shown.Add(e);
            float w = pad * 2 + Mathf.Max(3, shown.Count) * cell;
            Panel(x, y, w, panelH);
            if (shown.Count == 0)
                Txt(x + pad + 8, y + 40, cell * 3, 80, "Nothing here yet.\nUnlock buildings in the shop after each map.", 17, UIStyle.Muted, TextAnchor.MiddleLeft, false, false, true);

            // Tabs sit on the panel's top edge.
            for (int i = 0; i < Tabs.Length; i++)
            {
                bool act = tab == Tabs[i].tab;
                float tx = x + 18 + i * (tabW + 2), ty = y - tabH + 6;
                var rect = R(tx, ty, tabW, tabH);
                Block(rect);
                bool over = rect.Contains(Event.current.mousePosition);
                if (GUI.Button(rect, GUIContent.none, None)) tab = Tabs[i].tab;
                if (Skin)
                {
                    UIStyle.Nine(rect, act ? ArcaneSkin.TabActive : ArcaneSkin.Tab, s);
                    if (over && !act) { GUI.color = new Color(1f, 1f, 1f, 0.4f); UIStyle.Nine(rect, ArcaneSkin.TabActive, s); Untint(); }
                }
                else GUI.Box(rect, GUIContent.none, act ? UIStyle.TabActive : UIStyle.Tab);
                Txt(tx, ty - 2, tabW, tabH - 8, Tabs[i].label, 18, act ? UIStyle.Parchment : UIStyle.Accent, TextAnchor.MiddleCenter, true);
            }

            BuildingCatalog.Entry hovered = null;
            for (int i = 0; i < shown.Count; i++)
            {
                var e = shown[i];
                float cx = x + pad + i * cell;
                float sx = cx + (cell - slot) * 0.5f, sy = y + 24;
                var rect = R(cx, sy - 4, cell, panelH - 40);
                bool active = build.Placing == e;
                bool afford = build.CanAfford(e);
                bool over = rect.Contains(Event.current.mousePosition);
                if (GUI.Button(rect, GUIContent.none, None)) build.Select(e);
                if (over) hovered = e;

                if (Event.current.type == EventType.Repaint)
                {
                    bool lit = active || over;
                    if (Skin)
                    {
                        float g = lit ? 3f : 0f;
                        UIStyle.Nine(R(sx - g, sy - g, slot + 2 * g, slot + 2 * g), lit ? ArcaneSkin.SlotHover : ArcaneSkin.Slot, s);
                    }
                    else GUI.Box(R(sx, sy, slot, slot), GUIContent.none, lit ? UIStyle.CardActive : UIStyle.Card);
                    if (glint.TryGetValue(e, out float ga))
                    {
                        float k = 1f - ga / 0.6f;
                        GUI.color = new Color(1f, 0.85f, 0.4f, k);
                        UIStyle.GlowRing(R(sx, sy, slot, slot), s, 14f);
                        Untint();
                    }
                }
                Tint(afford ? 1f : 0.45f);
                if (e.Sprite != null && Event.current.type == EventType.Repaint)
                    GUI.DrawTexture(R(sx + 10, sy + 8, slot - 20, slot - 20), e.Sprite.texture, ScaleMode.ScaleToFit);
                Untint();
                TxtC(cx - 4, sy + slot + 4, cell + 8, 22, e.Name, 17, afford ? UIStyle.Parchment : UIStyle.Muted, false, true);
                DrawCost(cx, sy + slot + 26, cell, e.Cost, afford);
            }

            if (hovered != null) DrawTooltip(hovered, x, y - tabH - 6);
        }

        void DrawCost(float x, float y, float width, int cost, bool afford)
        {
            var text = cost.ToString("N0");
            float tw = St(17, false, true, false, TextAnchor.MiddleLeft).CalcSize(new GUIContent(text)).x / s;
            float total = 22 + 5 + tw;
            float cx = x + (width - total) * 0.5f;
            if (Skin) Icon(ArcaneSkin.Coin, cx, y + 1, 22, 22);
            else GUI.DrawTexture(R(cx, y + 2, 20, 20), UIStyle.GoldIcon);
            Txt(cx + 27, y, tw + 4, 24, text, 17, afford ? UIStyle.GoldColor : UIStyle.Danger, TextAnchor.MiddleLeft, false, true);
        }

        void DrawTooltip(BuildingCatalog.Entry e, float x, float bottom)
        {
            string stats = e.Attack != AttackKind.None ? Stats(e) : null;
            string rules = e.NeedsOre ? "Must be placed on ore." : null;
            string desc = e.Description + "\nRight-click to remove it; hold and drag to clear belts and walls.";
            const float w = 480, pad = 34;
            float inner = w - 2 * pad;
            // Each block is measured at the panel's inner width, so the panel grows to hold all of it.
            float H(string text, bool bold) => text == null ? 0f : St(16, false, bold, true, TextAnchor.UpperLeft).CalcHeight(new GUIContent(text), inner * s) / s + 4;
            float sh = H(stats, true), rh = H(rules, true), dh = H(desc, false);
            float h = pad + 32 + sh + rh + dh + pad - 4;
            var box = UIStyle.ClampInside(new Rect(x, bottom - h, w, h), new Rect(8, 8, vw - 16, vh - 16));
            Panel(box.x, box.y, box.width, box.height);
            var area = UIStyle.Inset(box, pad);
            area.y -= 6;
            var head = UIStyle.TakeTop(ref area, 30, 2);
            Txt(head.x, head.y, head.width - 50, head.height, e.Name, 22, UIStyle.Accent, TextAnchor.MiddleLeft, true);
            Txt(head.x, head.y, head.width, head.height, $"{e.Size.x}x{e.Size.y}", 16, UIStyle.Muted, TextAnchor.MiddleRight);
            if (stats != null) { var r = UIStyle.TakeTop(ref area, sh); Txt(r.x, r.y, r.width, r.height, stats, 16, UIStyle.Accent, TextAnchor.UpperLeft, false, true, true); }
            if (rules != null) { var r = UIStyle.TakeTop(ref area, rh); Txt(r.x, r.y, r.width, r.height, rules, 16, new Color(0.6f, 0.8f, 1f), TextAnchor.UpperLeft, false, true, true); }
            var d = UIStyle.TakeTop(ref area, dh + 6);
            Txt(d.x, d.y, d.width, d.height, desc, 16, UIStyle.Parchment, TextAnchor.UpperLeft, false, false, true);
        }

        /// <summary>Combat stats including the run's upgrades.</summary>
        static string Stats(BuildingCatalog.Entry e)
        {
            var m = Run.For(e.Id);
            float dmg = e.Damage * (1f + m.Damage), rate = e.FireRate * (1f + m.FireRate), range = Combat.RangeOf(e);
            string text;
            switch (e.Attack)
            {
                case AttackKind.Laser:
                case AttackKind.Flame: text = $"{dmg * (1f + m.FireRate):0} damage/s  ·  range {range:0.#}"; break;
                case AttackKind.Slow: return $"Slows enemies {e.Damage * 100f:0}%  ·  range {range:0.#}";
                case AttackKind.Tesla: text = $"{dmg:0} damage, chains to {e.Splash:0}  ·  {rate:0.#}/s  ·  range {range:0.#}"; break;
                case AttackKind.Mine: text = $"{dmg:0} damage, blast {e.Splash:0.#}  ·  single use"; break;
                case AttackKind.Missile:
                case AttackKind.Mortar: text = $"{dmg:0} damage, blast {e.Splash:0.#}  ·  {rate:0.#}/s  ·  range {range:0.#}"; break;
                default: text = $"{dmg:0} damage  ·  {rate:0.#}/s  ·  range {range:0.#}"; break;
            }
            string[] names = { "fire", "lightning", "frost", "poison" };
            for (int i = 0; i < names.Length; i++)
                if (m.Elements[i] > 0f) text += $"  ·  +{m.Elements[i]:0} {names[i]}";
            return text;
        }

        // ---------------------------------------------------------------- game over

        void DrawGameOver()
        {
            Dim(0.45f);
            bool boss = waves != null && waves.BossHealthAtLoss >= 0f;
            float w = 640, h = boss ? 470 : 430;
            float x = (vw - w) * 0.5f, y = (vh - h) * 0.5f + 20;
            float age = screenT;
            float sc = Mathf.Lerp(0.92f, 1f, HudFx.OutBack(age / 0.3f)), al = Mathf.Clamp01(age / 0.2f);
            var old = GUI.matrix;
            GUIUtility.ScaleAroundPivot(Vector2.one * sc, new Vector2(vw * 0.5f * s, (y + h * 0.5f) * s));
            GUI.color = new Color(1f, 1f, 1f, al);
            Panel(x, y, w, h);
            if (Skin) UIStyle.Nine(R(x + 20, y - 34, w - 40, 90), ArcaneSkin.Banner, s);
            Untint();
            Txt(x + 20, y - 34, w - 40, 90, "THE CORE HAS FALLEN", 34, UIStyle.Danger, TextAnchor.MiddleCenter, true);

            int reached = waves != null ? Mathf.Max(0, waves.Wave - waves.FirstWave + 1) : 0;
            TxtC(x + 40, y + 70, w - 80, 28, $"Map {Run.Stage + 1}  ·  reached wave {reached} of {Run.Current.Waves}", 21, UIStyle.Parchment, false, true);
            string[] labels = { "KILLS", "UPGRADES", "CRYSTALS SOLD" };
            int kills = waves != null ? waves.Kills : 0;
            int[] vals = { HudFx.CountUp(kills, age, 0.3f, 0.7f), HudFx.CountUp(Run.Taken.Count, age, 0.45f, 0.5f), HudFx.CountUp(flow != null ? flow.Delivered : 0, age, 0.6f, 0.7f) };
            float cw = (w - 100) / 3f;
            for (int i = 0; i < 3; i++)
            {
                TxtC(x + 50 + i * cw, y + 112, cw, 22, labels[i], 15, UIStyle.Muted, false, true);
                TxtC(x + 50 + i * cw, y + 134, cw, 44, vals[i].ToString("N0"), 34, UIStyle.Parchment, true);
            }
            float by = y + 196;
            if (flow != null) { TxtC(x + 40, by, w - 80, 26, $"{flow.GoldFromOre:N0} gold earned from crystals", 18, UIStyle.Muted); by += 30; }
            if (boss)
            {
                int pct = Pct(waves.BossHealthAtLoss);
                TxtC(x + 40, by + 4, w - 80, 34, $"The boss had {pct}% of its health left", 24, UIStyle.Accent, true);
                by += 44;
            }
            if (GemButton(x + w * 0.5f - 130, y + h - 130, 260, 58, "NEW RUN", 26, true, true)) NewRun();
            TxtC(x, y + h - 62, w, 24, "or press R", 17, UIStyle.Muted);
            GUI.matrix = old;
        }

        // ---------------------------------------------------------------- floating pieces

        void DrawFlies()
        {
            if (Event.current.type != EventType.Repaint) return;
            foreach (var f in fx.Flies)
            {
                var p = HudFx.Pos(f, 36f * s);
                float a = 1f - Mathf.Clamp01((f.N - 0.8f) / 0.2f) * 0.6f;
                float size = f.Size * (1f + 0.15f * Mathf.Sin(f.N * Mathf.PI));
                var col = new Color(f.Color.r, f.Color.g, f.Color.b, a);
                var g = St(size, true, false, false, TextAnchor.MiddleCenter);
                float cw = 200f * s, ch = size * 1.6f * s;
                float tw = g.CalcSize(new GUIContent(f.Text)).x;
                var r = new Rect(p.x - cw * 0.5f + (f.Coin ? 10f * s : 0f), p.y - ch * 0.5f, cw, ch);
                GUI.contentColor = new Color(0f, 0f, 0f, a * 0.8f);
                GUI.Label(new Rect(r.x + 1.5f * s, r.y + 1.5f * s, r.width, r.height), f.Text, g);
                GUI.contentColor = col;
                GUI.Label(r, f.Text, g);
                GUI.contentColor = Color.white;
                if (f.Coin && Skin)
                {
                    float cs = size * 0.75f * s;
                    GUI.color = new Color(1f, 1f, 1f, a);
                    GUI.DrawTexture(new Rect(p.x - tw * 0.5f - cs + 6f * s, p.y - cs * 0.5f, cs, cs), ArcaneSkin.Coin, ScaleMode.ScaleToFit);
                    Untint();
                }
            }
        }

        void SetTip(string title, string tag, string body, Rect anchorV, bool right)
        {
            tipTitle = title; tipTag = tag; tipBody = body; tipAnchor = anchorV; tipRight = right;
        }

        void DrawTip()
        {
            if (tipTitle == null) return;
            const float w = 320, pad = 30;
            float dh = St(16, false, false, true, TextAnchor.UpperLeft).CalcHeight(new GUIContent(tipBody), (w - 2 * pad) * s) / s;
            float h = pad + 26 + 22 + dh + pad - 4;
            float x = tipAnchor.xMax + 12;
            if (x + w > vw - 8) x = tipAnchor.x - w - 12;
            var box = UIStyle.ClampInside(new Rect(x, tipAnchor.y, w, h), new Rect(8, 8, vw - 16, vh - 16));
            x = box.x;
            float y = box.y;
            Panel(x, y, w, h);
            Txt(x + pad, y + pad - 8, w - 2 * pad, 26, tipTitle, 19, UIStyle.Accent, TextAnchor.MiddleLeft, true);
            if (!string.IsNullOrEmpty(tipTag)) Txt(x + pad, y + pad + 16, w - 2 * pad, 20, tipTag, 14, UIStyle.Muted, TextAnchor.MiddleLeft, false, true);
            Txt(x + pad, y + pad + 40, w - 2 * pad, dh + 4, tipBody, 16, UIStyle.Parchment, TextAnchor.UpperLeft, false, false, true);
        }
    }
}
