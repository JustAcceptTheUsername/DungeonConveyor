using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>Runtime-generated IMGUI skin pieces: rounded panels, a gold icon and text styles.</summary>
    public static class UIStyle
    {
        public static readonly Color PanelColor = new Color(0.055f, 0.05f, 0.06f, 0.92f);
        public static readonly Color BorderColor = new Color(0.28f, 0.25f, 0.24f, 1f);
        /// <summary>Palette: gold accents, danger red, parchment body text (the Arcane skin's values once loaded).</summary>
        public static Color Accent = new Color(1f, 0.62f, 0.15f);
        public static Color Muted = new Color(0.7f, 0.68f, 0.66f);
        public static Color Danger = new Color(1f, 0.3f, 0.25f);
        public static Color GoldColor = new Color(1f, 0.72f, 0.15f);
        /// <summary>Warm parchment white for body text.</summary>
        public static Color Parchment = Color.white;

        public static Texture2D White, Panel, PanelHover, PanelAccent, GoldIcon, Glow;

        static float builtScale = -1f;
        public static GUIStyle Text, Small, Title, Big, Center, CenterSmall, Tab, TabActive, Card, CardActive;

        public static void Init()
        {
            if (White != null) return;
            ArcaneSkin.Load();
            if (ArcaneSkin.Loaded)
            {
                Accent = new Color(0.949f, 0.663f, 0.231f);   // #F2A93B
                Danger = new Color(1f, 0.353f, 0.29f);        // #FF5A4A
                Muted = new Color(0.78f, 0.72f, 0.62f);
                Parchment = new Color(0.949f, 0.902f, 0.812f); // #F2E6CF
            }
            White = Solid(Color.white);
            Glow = MakeGlow();
            Panel = Rounded(PanelColor, BorderColor, 8);
            PanelHover = Rounded(new Color(0.11f, 0.1f, 0.1f, 0.95f), new Color(0.5f, 0.42f, 0.32f), 8);
            PanelAccent = Rounded(new Color(0.13f, 0.1f, 0.06f, 0.95f), Accent, 8);
            GoldIcon = IsoCube(GoldColor);
            builtScale = -1f;
        }

        public static void EnsureStyles(float s)
        {
            if (Mathf.Approximately(s, builtScale) && Text != null) return;
            builtScale = s;
            int F(float px) => Mathf.Max(8, Mathf.RoundToInt(px * s));

            Text = new GUIStyle { fontSize = F(17), normal = { textColor = ArcaneSkin.Loaded ? Parchment : Color.white }, alignment = TextAnchor.MiddleLeft, wordWrap = false };
            if (ArcaneSkin.Loaded) Text.font = ArcaneSkin.Body;
            Small = new GUIStyle(Text) { fontSize = F(13), normal = { textColor = Muted } };
            Title = new GUIStyle(Text) { fontSize = F(17), fontStyle = FontStyle.Bold, normal = { textColor = Accent } };
            Big = new GUIStyle(Text) { fontSize = F(24), fontStyle = FontStyle.Bold };
            if (ArcaneSkin.Loaded)
            {
                Title.font = ArcaneSkin.Title;
                Big.font = ArcaneSkin.Title;
            }
            Center = new GUIStyle(Text) { alignment = TextAnchor.MiddleCenter };
            CenterSmall = new GUIStyle(Small) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            var border = new RectOffset(8, 8, 8, 8);
            Tab = new GUIStyle(Center)
            {
                fontSize = F(14),
                fontStyle = FontStyle.Bold,
                border = border,
                normal = { background = Panel, textColor = Muted },
                hover = { background = PanelHover, textColor = Color.white },
                active = { background = PanelAccent, textColor = Accent },
            };
            TabActive = new GUIStyle(Tab) { normal = { background = PanelAccent, textColor = Color.white } };
            Card = new GUIStyle { border = border, normal = { background = Panel }, hover = { background = PanelHover } };
            CardActive = new GUIStyle(Card) { normal = { background = PanelAccent }, hover = { background = PanelAccent } };
        }

        /// <summary>
        /// Draws a 9-sliced frame whose corners are scaled by <paramref name="k"/> (IMGUI's own border does not scale, so the
        /// HUD's 1080p-authored frames would otherwise keep full-size trim at 720p).
        /// </summary>
        public static void Nine(Rect r, Texture2D t, float k)
        {
            if (t == null || Event.current.type != EventType.Repaint) return;
            var b = ArcaneSkin.Border(t);
            float l = b.left * k, rt = b.right * k, tp = b.top * k, bt = b.bottom * k;
            float fx = Mathf.Min(1f, r.width / (l + rt + 1f)), fy = Mathf.Min(1f, r.height / (tp + bt + 1f));
            l *= fx; rt *= fx; tp *= fy; bt *= fy;
            float tw = t.width, th = t.height;
            float[] us = { 0f, b.left / tw, 1f - b.right / tw, 1f };
            float[] vs = { 1f, 1f - b.top / th, b.bottom / th, 0f };   // rows top to bottom, v is bottom-up
            float[] xs = { Mathf.Round(r.x), Mathf.Round(r.x + l), Mathf.Round(r.xMax - rt), Mathf.Round(r.xMax) };
            float[] ys = { Mathf.Round(r.y), Mathf.Round(r.y + tp), Mathf.Round(r.yMax - bt), Mathf.Round(r.yMax) };
            for (int row = 0; row < 3; row++)
                for (int col = 0; col < 3; col++)
                {
                    var dst = new Rect(xs[col], ys[row], xs[col + 1] - xs[col], ys[row + 1] - ys[row]);
                    var src = new Rect(us[col], vs[row + 1], us[col + 1] - us[col], vs[row] - vs[row + 1]);
                    GUI.DrawTextureWithTexCoords(dst, t, src);
                }
        }

        /// <summary>Soft ring of light around <paramref name="r"/> (peaks on the rect's edge), tinted by GUI.color.</summary>
        public static void GlowRing(Rect r, float k, float reach = 12f)
        {
            if (Event.current.type != EventType.Repaint) return;
            float e = reach * k;
            var big = new Rect(r.x - e, r.y - e, r.width + 2 * e, r.height + 2 * e);
            float c = Mathf.Min(24f * k * (reach / 12f), Mathf.Min(big.width, big.height) * 0.5f - 1f);
            float tw = Glow.width;
            float u = 24f / tw;
            float[] xs = { big.x, big.x + c, big.xMax - c, big.xMax };
            float[] ys = { big.y, big.y + c, big.yMax - c, big.yMax };
            float[] us = { 0f, u, 1f - u, 1f };
            float[] vs = { 1f, 1f - u, u, 0f };
            for (int row = 0; row < 3; row++)
                for (int col = 0; col < 3; col++)
                    GUI.DrawTextureWithTexCoords(new Rect(xs[col], ys[row], xs[col + 1] - xs[col], ys[row + 1] - ys[row]), Glow,
                        new Rect(us[col], vs[row + 1], us[col + 1] - us[col], vs[row] - vs[row + 1]));
        }

        public static void DrawPanel(Rect r, Texture2D tex = null)
        {
            if (Event.current.type != EventType.Repaint) return;
            var style = new GUIStyle { border = new RectOffset(8, 8, 8, 8), normal = { background = tex != null ? tex : Panel } };
            style.Draw(r, false, false, false, false);
        }

        public static void DrawBar(Rect r, float fraction, Color fill)
        {
            var old = GUI.color;
            GUI.color = new Color(0.15f, 0.14f, 0.15f, 1f);
            GUI.DrawTexture(r, White);
            GUI.color = fill;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(fraction), r.height), White);
            GUI.color = old;
        }

        public static void Shadowed(Rect r, string text, GUIStyle style, Color color)
        {
            var old = style.normal.textColor;
            style.normal.textColor = new Color(0, 0, 0, color.a * 0.8f);
            GUI.Label(new Rect(r.x + 1.5f, r.y + 1.5f, r.width, r.height), text, style);
            style.normal.textColor = color;
            GUI.Label(r, text, style);
            style.normal.textColor = old;
        }

        // ---------------------------------------------------------------- textures

        /// <summary>48x48 white ring texture (alpha peaks 12 px from the inner box, fades to 0 at 24 px); 9-slice border 24.</summary>
        static Texture2D MakeGlow()
        {
            const int n = 49;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "Glow" };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = Mathf.Max(0f, Mathf.Max(24f - x, x - 24f)), dy = Mathf.Max(0f, Mathf.Max(24f - y, y - 24f));
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = d >= 24f ? 0f : Mathf.Sin(Mathf.PI * d / 24f);
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                }
            t.Apply();
            return t;
        }

        static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1) { name = "White" };
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        static Texture2D Rounded(Color fill, Color border, int radius)
        {
            int n = radius * 2 + 4;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float cx = Mathf.Clamp(x + 0.5f, radius, n - radius), cy = Mathf.Clamp(y + 0.5f, radius, n - radius);
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                    float a = Mathf.Clamp01(radius - d + 0.5f);
                    bool edge = d > radius - 1.5f || x == 0 || y == 0 || x == n - 1 || y == n - 1;
                    var c = edge ? border : fill;
                    t.SetPixel(x, y, new Color(c.r, c.g, c.b, c.a * a));
                }
            t.Apply();
            return t;
        }

        static Texture2D IsoCube(Color c)
        {
            const int n = 32;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var top = Color.Lerp(c, Color.white, 0.35f);
            var left = c;
            var right = Color.Lerp(c, Color.black, 0.35f);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float px = (x + 0.5f - n / 2f) / (n / 2f);
                    float py = (y + 0.5f - n / 2f) / (n / 2f);   // +y up
                    Color col = Color.clear;
                    // Hexagon silhouette of an isometric cube.
                    if (Mathf.Abs(px) <= 0.85f && Mathf.Abs(py) + Mathf.Abs(px) * 0.5f <= 0.9f)
                    {
                        if (py > Mathf.Abs(px) * 0.5f - 0.02f) col = top;
                        else col = px < 0 ? left : right;
                    }
                    t.SetPixel(x, y, col);
                }
            t.Apply();
            return t;
        }
    }
}
