using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>Runtime-generated IMGUI skin pieces: rounded panels, icons and text styles.</summary>
    public static class UIStyle
    {
        public static readonly Color PanelColor = new Color(0.055f, 0.05f, 0.06f, 0.92f);
        public static readonly Color BorderColor = new Color(0.28f, 0.25f, 0.24f, 1f);
        public static readonly Color Accent = new Color(1f, 0.62f, 0.15f);
        public static readonly Color Muted = new Color(0.7f, 0.68f, 0.66f);
        public static readonly Color Danger = new Color(1f, 0.25f, 0.2f);
        public static readonly Color Good = new Color(0.35f, 0.85f, 0.2f);

        public static Texture2D White, Panel, PanelHover, PanelAccent, Button, ButtonHover, Bar, AlertCircle, AlertTriangle;
        public static Texture2D[] ResourceIcons;

        static float builtScale = -1f;
        public static GUIStyle Text, Small, Title, Big, Huge, Center, CenterSmall, Right, ButtonStyle, Tab, TabActive, Card, CardActive;

        public static void Init()
        {
            White = Solid(Color.white);
            Panel = Rounded(PanelColor, BorderColor, 8);
            PanelHover = Rounded(new Color(0.11f, 0.1f, 0.1f, 0.95f), new Color(0.5f, 0.42f, 0.32f), 8);
            PanelAccent = Rounded(new Color(0.13f, 0.1f, 0.06f, 0.95f), Accent, 8);
            Button = Rounded(new Color(0.16f, 0.15f, 0.16f, 0.95f), new Color(0.38f, 0.35f, 0.33f), 6);
            ButtonHover = Rounded(new Color(0.24f, 0.21f, 0.18f, 0.95f), Accent, 6);
            Bar = Rounded(Color.white, Color.white, 3);
            AlertCircle = Circle(Danger);
            AlertTriangle = Triangle(Danger);
            ResourceIcons = new Texture2D[ResourceInfo.Count];
            for (int i = 0; i < ResourceInfo.Count; i++)
                ResourceIcons[i] = i == 0 || i == 3 ? IsoCube(ResourceInfo.Colors[i]) : Gem(ResourceInfo.Colors[i]);
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
            Huge = new GUIStyle(Text) { fontSize = F(52), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            Center = new GUIStyle(Text) { alignment = TextAnchor.MiddleCenter };
            CenterSmall = new GUIStyle(Small) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            Right = new GUIStyle(Text) { alignment = TextAnchor.MiddleRight };
            var border = new RectOffset(8, 8, 8, 8);
            ButtonStyle = new GUIStyle(Center)
            {
                fontStyle = FontStyle.Bold,
                border = new RectOffset(6, 6, 6, 6),
                normal = { background = Button, textColor = Color.white },
                hover = { background = ButtonHover, textColor = Color.white },
                active = { background = PanelAccent, textColor = Accent },
            };
            Tab = new GUIStyle(ButtonStyle) { fontSize = F(14), normal = { background = Panel, textColor = Muted }, hover = { background = PanelHover, textColor = Color.white }, border = border };
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

        public static void Fill(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, White);
            GUI.color = old;
        }

        public static void ProgressBar(Rect r, float t, Color fill)
        {
            var old = GUI.color;
            GUI.color = new Color(0.15f, 0.14f, 0.15f, 1f);
            GUI.DrawTexture(r, Bar);
            GUI.color = fill;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(t), r.height), Bar);
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

        static Texture2D Circle(Color c)
        {
            const int n = 32;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f));
                    float a = Mathf.Clamp01(n / 2f - d);
                    bool mark = Mathf.Abs(x - n / 2f + 0.5f) < 2 && (y > 13 && y < 26 || y > 7 && y < 11);
                    t.SetPixel(x, y, mark ? new Color(1, 1, 1, a) : new Color(c.r, c.g, c.b, a));
                }
            t.Apply();
            return t;
        }

        static Texture2D Triangle(Color c)
        {
            const int n = 32;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float half = (n - 2 - y) * 0.6f;               // apex at top
                    float dx = Mathf.Abs(x + 0.5f - n / 2f);
                    bool inside = y >= 2 && dx <= half;
                    bool mark = dx < 2 && (y > 9 && y < 21 || y > 4 && y < 8);
                    var col = mark ? new Color(0.1f, 0.05f, 0.05f, 1) : c;
                    t.SetPixel(x, n - 1 - y, inside ? col : Color.clear);
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

        static Texture2D Gem(Color c)
        {
            const int n = 32;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var light = Color.Lerp(c, Color.white, 0.45f);
            var dark = Color.Lerp(c, Color.black, 0.3f);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float px = (x + 0.5f - n / 2f) / (n / 2f);
                    float py = (y + 0.5f - n / 2f) / (n / 2f);
                    Color col = Color.clear;
                    if (Mathf.Abs(px) * 1.25f + Mathf.Abs(py) * 0.95f <= 1f)
                        col = py > 0.25f ? light : (px < 0 ? c : dark);
                    t.SetPixel(x, y, col);
                }
            t.Apply();
            return t;
        }
    }
}
