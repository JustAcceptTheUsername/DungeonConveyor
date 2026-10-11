using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>Runtime-generated IMGUI skin pieces: rounded panels, a gold icon and text styles.</summary>
    public static class UIStyle
    {
        public static readonly Color PanelColor = new Color(0.055f, 0.05f, 0.06f, 0.92f);
        public static readonly Color BorderColor = new Color(0.28f, 0.25f, 0.24f, 1f);
        public static readonly Color Accent = new Color(1f, 0.62f, 0.15f);
        public static readonly Color Muted = new Color(0.7f, 0.68f, 0.66f);
        public static readonly Color Danger = new Color(1f, 0.3f, 0.25f);
        public static readonly Color GoldColor = new Color(1f, 0.72f, 0.15f);

        public static Texture2D White, Panel, PanelHover, PanelAccent, GoldIcon;

        static float builtScale = -1f;
        public static GUIStyle Text, Small, Title, Big, Center, CenterSmall, Tab, TabActive, Card, CardActive;

        public static void Init()
        {
            if (White != null) return;
            White = Solid(Color.white);
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

            Text = new GUIStyle { fontSize = F(17), normal = { textColor = Color.white }, alignment = TextAnchor.MiddleLeft, wordWrap = false };
            Small = new GUIStyle(Text) { fontSize = F(13), normal = { textColor = Muted } };
            Title = new GUIStyle(Text) { fontSize = F(17), fontStyle = FontStyle.Bold, normal = { textColor = Accent } };
            Big = new GUIStyle(Text) { fontSize = F(24), fontStyle = FontStyle.Bold };
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
