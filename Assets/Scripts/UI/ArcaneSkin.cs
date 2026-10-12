using System.Collections.Generic;
using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>The Arcane HUD art kit (carved stone, gold trim) and its fonts, loaded from Resources/UI.
    /// <see cref="Loaded"/> is false when any texture is missing; the HUD then keeps its old procedural look.</summary>
    public static class ArcaneSkin
    {
        const string Dir = "UI/Arcane/";

        /// <summary>Frames, buttons and gems.</summary>
        public static Texture2D Panel, HeaderPanel, Banner, BarFrame, Slot, SlotHover,
                                Tab, TabActive, Button, ButtonHover, ButtonPressed, GemCommon, GemRare, Coin;
        /// <summary>Spell-card frames per element or stat. Draw them whole (not 9-sliced): the arch and plates have fixed proportions.</summary>
        public static Texture2D CardFire, CardLightning, CardFrost, CardPoison, CardStat, CardGold;
        /// <summary>Emblems for cards, the upgrade row and gold.</summary>
        public static Texture2D IconFire, IconLightning, IconFrost, IconPoison, IconDamage, IconSpeed, IconRange, IconGold;
        /// <summary>Cinzel for titles and big numbers, Alegreya Sans for body text.</summary>
        public static Font Title, Body, BodyBold;

        static bool loaded;
        static readonly Dictionary<Texture2D, RectOffset> borders = new Dictionary<Texture2D, RectOffset>();

        /// <summary>True when every texture loaded.</summary>
        public static bool Loaded => loaded;

        /// <summary>Loads the kit from Resources. Safe to call repeatedly.</summary>
        public static void Load()
        {
            if (loaded) return;
            Panel = T("panel"); HeaderPanel = T("header_panel"); Banner = T("banner"); BarFrame = T("bar_frame");
            Slot = T("slot"); SlotHover = T("slot_hover"); Tab = T("tab"); TabActive = T("tab_active");
            Button = T("button"); ButtonHover = T("button_hover"); ButtonPressed = T("button_pressed");
            GemCommon = T("gem_common"); GemRare = T("gem_rare"); Coin = T("coin");
            CardFire = T("card_fire"); CardLightning = T("card_lightning"); CardFrost = T("card_frost");
            CardPoison = T("card_poison"); CardStat = T("card_stat"); CardGold = T("card_gold");
            IconFire = T("icon_fire"); IconLightning = T("icon_lightning"); IconFrost = T("icon_frost");
            IconPoison = T("icon_poison"); IconDamage = T("icon_damage"); IconSpeed = T("icon_speed");
            IconRange = T("icon_range"); IconGold = T("icon_gold");
            Title = Resources.Load<Font>("UI/Fonts/Cinzel");
            Body = Resources.Load<Font>("UI/Fonts/AlegreyaSans-Regular");
            BodyBold = Resources.Load<Font>("UI/Fonts/AlegreyaSans-Bold");

            borders.Clear();
            // (left, top, right, bottom) in source pixels, measured per texture. IMGUI does not scale borders,
            // so the textures are authored at HUD size (about 30 px of trim on the big panels).
            B(Panel, 30, 30, 30, 30); B(HeaderPanel, 26, 45, 26, 26); B(Banner, 75, 20, 75, 20); B(BarFrame, 45, 25, 45, 25);
            B(Slot, 18, 18, 18, 18); B(SlotHover, 20, 20, 20, 20);
            B(Tab, 30, 12, 30, 11); B(TabActive, 30, 14, 30, 13);
            B(Button, 35, 15, 35, 15); B(ButtonHover, 38, 20, 38, 20); B(ButtonPressed, 35, 16, 35, 16);
            foreach (var c in new[] { CardFire, CardLightning, CardFrost, CardPoison, CardStat, CardGold }) B(c, 30, 90, 30, 75);

            loaded = Panel && HeaderPanel && Banner && BarFrame && Slot && SlotHover && Tab && TabActive
                && Button && ButtonHover && ButtonPressed && GemCommon && GemRare && Coin
                && CardFire && CardLightning && CardFrost && CardPoison && CardStat && CardGold
                && IconFire && IconLightning && IconFrost && IconPoison && IconDamage && IconSpeed && IconRange && IconGold;
            if (!loaded) Debug.LogWarning("ArcaneSkin: kit missing in Resources/UI/Arcane, HUD keeps the old look.");
        }

        /// <summary>9-slice border in source pixels for a frame texture (zero for non-frame textures).</summary>
        public static RectOffset Border(Texture2D t)
        {
            return t != null && borders.TryGetValue(t, out var b) ? new RectOffset(b.left, b.right, b.top, b.bottom) : new RectOffset();
        }

        static Texture2D T(string name) => Resources.Load<Texture2D>(Dir + name);

        static void B(Texture2D t, int l, int top, int r, int bottom)
        {
            if (t != null) borders[t] = new RectOffset(l, r, top, bottom);
        }
    }
}
