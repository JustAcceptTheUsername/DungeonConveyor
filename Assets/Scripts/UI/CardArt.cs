using System.Collections.Generic;
using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>
    /// Draws upgrade cards in the Arcane kit (shared by the HUD's card pick and the main menu's card gallery): the
    /// element/stat frame, a glow and gem in the card's rarity colour (common gray, rare green, epic purple,
    /// legendary gold), the emblem, title, tag, text and the rarity name. Rects are in screen pixels; everything inside
    /// scales with the card's width (authored at CardW).
    /// </summary>
    public static class CardArt
    {
        public const float CardW = 280f, CardH = 397f;

        static readonly Dictionary<(int, bool, bool, bool, TextAnchor), GUIStyle> styles = new Dictionary<(int, bool, bool, bool, TextAnchor), GUIStyle>();

        public static Texture2D IconFor(Run.CardIcon i)
        {
            switch (i)
            {
                case Run.CardIcon.Fire: return ArcaneSkin.IconFire;
                case Run.CardIcon.Lightning: return ArcaneSkin.IconLightning;
                case Run.CardIcon.Frost: return ArcaneSkin.IconFrost;
                case Run.CardIcon.Poison: return ArcaneSkin.IconPoison;
                case Run.CardIcon.Damage: return ArcaneSkin.IconDamage;
                case Run.CardIcon.Speed: return ArcaneSkin.IconSpeed;
                case Run.CardIcon.Range: return ArcaneSkin.IconRange;
                default: return ArcaneSkin.IconGold;
            }
        }

        public static Texture2D FrameFor(Run.CardIcon i)
        {
            switch (i)
            {
                case Run.CardIcon.Fire: return ArcaneSkin.CardFire;
                case Run.CardIcon.Lightning: return ArcaneSkin.CardLightning;
                case Run.CardIcon.Frost: return ArcaneSkin.CardFrost;
                case Run.CardIcon.Poison: return ArcaneSkin.CardPoison;
                case Run.CardIcon.Gold: return ArcaneSkin.CardGold;
                default: return ArcaneSkin.CardStat;
            }
        }

        /// <summary>The rarity glow around a card (stronger the rarer; legendary pulses). hot = under the cursor.</summary>
        public static void Glow(Run.Card c, Rect px, float alpha, bool hot)
        {
            if (Event.current.type != EventType.Repaint) return;
            float k = px.width / CardW * 1.4f;
            float strength = c.Rarity == Rarity.Common ? 0f : c.Rarity == Rarity.Rare ? 0.5f : c.Rarity == Rarity.Epic ? 0.7f : 0.85f;
            if (c.Rarity == Rarity.Legendary) strength *= 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 4f);
            if (hot) strength = Mathf.Max(strength, 0.9f);
            if (strength <= 0f) return;
            var col = c.Rarity == Rarity.Common ? new Color(1f, 0.95f, 0.8f) : Run.ColorOf(c.Rarity);
            var old = GUI.color;
            GUI.color = new Color(col.r, col.g, col.b, strength * alpha);
            UIStyle.GlowRing(px, k, hot || c.Rarity >= Rarity.Epic ? 16f : 12f);
            GUI.color = old;
        }

        /// <summary>The card frame, tinted (the face-down back is dim).</summary>
        public static void Frame(Run.Card c, Rect px, float alpha, bool back = false)
        {
            if (Event.current.type != EventType.Repaint) return;
            var old = GUI.color;
            GUI.color = back ? new Color(0.28f, 0.26f, 0.3f, alpha) : new Color(1f, 1f, 1f, alpha);
            GUI.DrawTexture(px, FrameFor(c.Icon), ScaleMode.StretchToFill);
            GUI.color = old;
        }

        /// <summary>Everything on the face: gem, emblem, title, tag, text, rarity name and (index >= 0) the hotkey number.</summary>
        public static void Face(Run.Card c, Rect px, float alpha, int index = -1)
        {
            float k = px.width / CardW; // pixels per card unit
            float x = px.x, y = px.y, w = px.width, h = px.height;
            var rarity = Run.ColorOf(c.Rarity);
            var parchment = UIStyle.Parchment;

            Icon(c.Rarity >= Rarity.Epic ? ArcaneSkin.GemRare : ArcaneSkin.GemCommon, new Rect(x + w * 0.5f - 15 * k, y + 3 * k, 30 * k, 30 * k),
                new Color(Mathf.Lerp(1f, rarity.r, 0.75f), Mathf.Lerp(1f, rarity.g, 0.75f), Mathf.Lerp(1f, rarity.b, 0.75f), alpha));
            float es = 118 * k;
            Icon(IconFor(c.Icon), new Rect(x + (w - es) * 0.5f, y + h * 0.17f, es, es), new Color(1f, 1f, 1f, alpha));
            float ts = c.Title.Length > 15 ? 17 : c.Title.Length > 11 ? 20 : 23;
            Text(new Rect(x + w * 0.1f, y + h * 0.5f, w * 0.8f, h * 0.115f), c.Title, ts * k, With(parchment, alpha), TextAnchor.MiddleCenter, true);
            Text(new Rect(x + w * 0.08f, y + h * 0.635f, w * 0.84f, h * 0.05f), c.Tag, 14 * k, With(c.Color, alpha), TextAnchor.MiddleCenter, false, true);
            Text(new Rect(x + w * 0.12f, y + h * 0.69f, w * 0.76f, h * 0.2f), c.Text, 17 * k, With(parchment, alpha), TextAnchor.UpperCenter, false, false, true);
            Text(new Rect(x + w * 0.2f, y + h * 0.885f, w * 0.6f, h * 0.06f), Run.RarityNames[(int)c.Rarity], 14 * k, With(rarity, alpha), TextAnchor.MiddleCenter, true);
            if (index >= 0)
                Text(new Rect(x + w * 0.03f, y + h * 0.03f, 26 * k, 22 * k), (index + 1).ToString(), 15 * k, With(UIStyle.Muted, alpha * 0.8f), TextAnchor.MiddleCenter, true);
        }

        /// <summary>Plain card for when the Arcane kit is missing.</summary>
        public static void Plain(Run.Card c, Rect px, float alpha)
        {
            float k = px.width / CardW;
            UIStyle.DrawPanel(px);
            Text(new Rect(px.x, px.y + 10 * k, px.width, 40 * k), c.Title, 22 * k, With(c.Color, alpha), TextAnchor.MiddleCenter, true);
            Text(new Rect(px.x, px.y + 50 * k, px.width, 30 * k), Run.RarityNames[(int)c.Rarity], 15 * k, With(Run.ColorOf(c.Rarity), alpha), TextAnchor.MiddleCenter, true);
            Text(new Rect(px.x + 16 * k, px.y + 100 * k, px.width - 32 * k, px.height - 120 * k), c.Text, 17 * k, With(UIStyle.Parchment, alpha), TextAnchor.UpperCenter, false, false, true);
        }

        static Color With(Color c, float a) => new Color(c.r, c.g, c.b, c.a * a);

        static void Icon(Texture2D t, Rect r, Color tint)
        {
            if (t == null || Event.current.type != EventType.Repaint) return;
            var old = GUI.color;
            GUI.color = tint;
            GUI.DrawTexture(r, t, ScaleMode.ScaleToFit);
            GUI.color = old;
        }

        /// <summary>Text with a soft drop shadow; size in pixels. Shrinks to fit its rect so it stays on the card.</summary>
        static void Text(Rect r, string text, float size, Color c, TextAnchor a, bool title = false, bool bold = false, bool wrap = false)
        {
            if (Event.current.type != EventType.Repaint) return;
            var g = Style(Mathf.Max(6, Mathf.RoundToInt(size)), title, bold, wrap, a);
            int fit = UIStyle.FitFontSize(g, text, r, 0.45f);
            if (fit < g.fontSize) g = Style(fit, title, bold, wrap, a);
            float off = Mathf.Max(1f, g.fontSize * 0.07f);
            var old = GUI.contentColor;
            GUI.contentColor = new Color(0f, 0f, 0f, c.a * 0.75f);
            GUI.Label(new Rect(r.x + off, r.y + off, r.width, r.height), text, g);
            GUI.contentColor = c;
            GUI.Label(r, text, g);
            GUI.contentColor = old;
        }

        static GUIStyle Style(int px, bool title, bool bold, bool wrap, TextAnchor a)
        {
            var key = (px, title, bold, wrap, a);
            if (styles.TryGetValue(key, out var g)) return g;
            g = new GUIStyle { fontSize = px, alignment = a, wordWrap = wrap, normal = { textColor = Color.white } };
            if (ArcaneSkin.Loaded) g.font = title ? ArcaneSkin.Title : bold ? ArcaneSkin.BodyBold : ArcaneSkin.Body;
            else g.fontStyle = title || bold ? FontStyle.Bold : FontStyle.Normal;
            return styles[key] = g;
        }
    }
}
