using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>
    /// Shared IMGUI pieces for the stand-alone windows (SettingsMenu, Tutorial): a 1080p-tall virtual canvas, Arcane
    /// panels and buttons, shadowed text that shrinks to fit, and a dim layer that swallows clicks. Call Begin at the
    /// top of OnGUI.
    /// </summary>
    public static class MenuGui
    {
        /// <summary>Pixels per virtual unit, and the virtual canvas width.</summary>
        public static float S { get; private set; } = 1f;
        public static float VW { get; private set; } = 1920f;
        public static GUIStyle Title, Label, Body, Small, ButtonText;
        static float builtScale = -1f;

        public static void Begin()
        {
            UIStyle.Init();
            S = Screen.height / 1080f;
            VW = Screen.width / S;
            UIStyle.EnsureStyles(S);
            if (Mathf.Approximately(builtScale, S) && Title != null) return;
            builtScale = S;
            int F(float px) => Mathf.Max(8, Mathf.RoundToInt(px * S));
            Title = new GUIStyle { fontSize = F(44), alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            Label = new GUIStyle { fontSize = F(24), alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold };
            Body = new GUIStyle { fontSize = F(21), alignment = TextAnchor.UpperLeft, wordWrap = true, richText = true };
            Small = new GUIStyle { fontSize = F(17), alignment = TextAnchor.MiddleLeft, wordWrap = true };
            ButtonText = new GUIStyle { fontSize = F(26), alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            if (ArcaneSkin.Loaded)
            {
                Title.font = ButtonText.font = ArcaneSkin.Title;
                Label.font = ArcaneSkin.BodyBold;
                Body.font = Small.font = ArcaneSkin.Body;
            }
        }

        /// <summary>A rect in virtual units, in pixels.</summary>
        public static Rect R(float x, float y, float w, float h) => new Rect(x * S, y * S, w * S, h * S);

        public static void Dim(float alpha)
        {
            GUI.color = new Color(0f, 0f, 0f, alpha);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), UIStyle.White);
            GUI.color = Color.white;
        }

        public static void Panel(Rect r, float frameScale = 1f)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (ArcaneSkin.Loaded) UIStyle.Nine(r, ArcaneSkin.Panel, S * frameScale);
            else UIStyle.DrawPanel(r);
        }

        /// <summary>Text with a soft shadow, shrunk to fit its rect (wrapped styles shrink to fit the height).</summary>
        public static void Text(Rect r, string text, GUIStyle style, Color color)
        {
            if (Event.current.type != EventType.Repaint) return;
            int size = style.fontSize;
            style.fontSize = UIStyle.FitFontSize(style, text, r);
            style.normal.textColor = new Color(0f, 0f, 0f, 0.7f * color.a);
            GUI.Label(new Rect(r.x + 1.5f, r.y + 1.5f, r.width, r.height), text, style);
            style.normal.textColor = color;
            GUI.Label(r, text, style);
            style.fontSize = size;
        }

        public static bool Button(Rect r, string text, bool enabled = true, float textScale = 1f)
        {
            bool over = enabled && r.Contains(Event.current.mousePosition);
            bool clicked = enabled && GUI.Button(r, GUIContent.none, GUIStyle.none);
            if (Event.current.type == EventType.Repaint)
            {
                GUI.color = new Color(1f, 1f, 1f, enabled ? 1f : 0.45f);
                if (ArcaneSkin.Loaded) UIStyle.Nine(r, over ? ArcaneSkin.ButtonHover : ArcaneSkin.Button, S * Mathf.Min(1f, textScale));
                else UIStyle.DrawPanel(r, over ? UIStyle.PanelHover : UIStyle.Panel);
                GUI.color = Color.white;
            }
            int size = ButtonText.fontSize;
            ButtonText.fontSize = Mathf.RoundToInt(size * textScale);
            Text(r, text, ButtonText, enabled ? (over ? UIStyle.Accent : UIStyle.Parchment) : UIStyle.Muted);
            ButtonText.fontSize = size;
            if (clicked) Sfx.Click();
            return clicked;
        }

        /// <summary>Square check box with a label; true when clicked.</summary>
        public static bool Toggle(Rect box, string text, bool on, float labelWidth)
        {
            var hit = new Rect(box.x, box.y, box.width + labelWidth, box.height);
            bool clicked = GUI.Button(hit, GUIContent.none, GUIStyle.none);
            if (Event.current.type == EventType.Repaint)
            {
                if (ArcaneSkin.Loaded) UIStyle.Nine(box, on ? ArcaneSkin.SlotHover : ArcaneSkin.Slot, S * 0.7f);
                else UIStyle.DrawPanel(box);
                if (on)
                {
                    float pad = box.width * 0.28f;
                    GUI.color = UIStyle.Accent;
                    GUI.DrawTexture(new Rect(box.x + pad, box.y + pad, box.width - 2f * pad, box.height - 2f * pad), UIStyle.White);
                    GUI.color = Color.white;
                }
            }
            Text(new Rect(box.xMax + box.width * 0.4f, box.y, labelWidth - box.width * 0.4f, box.height), text, Small, UIStyle.Parchment);
            if (clicked) Sfx.Play(Sfx.Lib.Toggle);
            return clicked;
        }

        /// <summary>Clicks anywhere were meant for this window: nothing drawn earlier (below it) gets them.</summary>
        public static void SwallowClicks()
        {
            var t = Event.current.type;
            if (t == EventType.MouseDown || t == EventType.MouseUp || t == EventType.MouseDrag || t == EventType.ScrollWheel) Event.current.Use();
        }
    }
}
