using System.Collections.Generic;
using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>
    /// An enemy's elemental variant (recolours its roles through EnemyPalette). None = the authored look. The others are
    /// the game's Element values in the same order (Element can't take a None: it indexes arrays), so
    /// EnemyPalette.ElementOf(Element) converts.
    /// </summary>
    public enum EnemyElement { None, Fire, Lightning, Frost, Poison }

    /// <summary>
    /// The colour roles of an enemy model: the exporter names every material after its role (DCX_Stone, DCX_StoneDark,
    /// DCX_Accent, DCX_Accent2, DCX_AccentDark, DCX_Metal, DCX_Plate, DCX_GlowCore, DCX_GlowHostile), so a role can be
    /// recoloured on any enemy at once. Glow roles are emission only (black base).
    /// </summary>
    public enum PaletteRole { Stone, StoneDark, Accent, Accent2, AccentDark, Metal, Plate, GlowCore, GlowHostile }

    /// <summary>One role's colour (authored sRGB, as picked; the bloom compensation is applied at runtime).</summary>
    [System.Serializable]
    public struct RoleColor
    {
        public PaletteRole Role;
        public Color Color;

        public RoleColor(PaletteRole role, Color color)
        {
            Role = role;
            Color = color;
        }

        public RoleColor(PaletteRole role, uint hex) : this(role, EnemyPalette.Hex(hex)) { }
    }

    /// <summary>The roles an element recolours (anything not listed keeps the enemy's own colour).</summary>
    [System.Serializable]
    public class ElementColors
    {
        public EnemyElement Element;
        public RoleColor[] Colors = new RoleColor[0];

        public ElementColors() { }

        public ElementColors(EnemyElement element, params RoleColor[] colors)
        {
            Element = element;
            Colors = colors;
        }

        public bool TryGet(PaletteRole role, out Color color)
        {
            if (Colors != null)
                for (int i = 0; i < Colors.Length; i++)
                    if (Colors[i].Role == role)
                    {
                        color = Colors[i].Color;
                        return true;
                    }
            color = default;
            return false;
        }
    }

    /// <summary>
    /// One enemy model's colours by role (Assets/Models/Enemies/&lt;Model&gt;_Palette.asset, made from the exporter's
    /// &lt;Id&gt;_palette.json sidecar by Dungeon/Import Enemy Palettes, linked as EnemyCatalog.Entry.Palette or
    /// BossPalette by the catalog sync). Colours are the authored sRGB ones; the game compensates them for the bloom at
    /// runtime (ExportColor, EnemyCatalog.Palette.Compensate) and paints them through MaterialPropertyBlocks
    /// (EnemyPaint). A colour still equal to Exported needs no paint (the glb carries it already).
    /// Element variants: Elements here win over the catalog's shared table (EnemyCatalog.Palette.Elements).
    /// </summary>
    [CreateAssetMenu(menuName = "Dungeon/Enemy Palette", fileName = "Enemy_Palette")]
    public class EnemyPalette : ScriptableObject
    {
        [System.Serializable]
        public class Slot
        {
            public PaletteRole Role;
            [Tooltip("The role's colour (authored sRGB). Change it to recolour this enemy.")]
            public Color Color = Color.white;
            [Tooltip("The authored colour the glb was exported with (from the sidecar; reference only).")]
            public Color Exported = Color.white;
            [Tooltip("Band lift baked into the mesh's vertex colours (plain faces carry 1/lift): the base is multiplied " +
                     "by it. Must match the export (sidecar band_lift); 1 for roles without bands.")]
            public float BandLift = 1f;
            [Tooltip("The glb carries the compensated colour (exported in compensated mode, the default).")]
            public bool ExportedCompensated = true;
        }

        public Slot[] Slots = new Slot[0];
        [Tooltip("This enemy's own element colours (win over EnemyCatalog.Palette.Elements, role by role).")]
        public ElementColors[] Elements = new ElementColors[0];

        public Slot Find(PaletteRole role)
        {
            if (Slots != null)
                for (int i = 0; i < Slots.Length; i++)
                    if (Slots[i] != null && Slots[i].Role == role) return Slots[i];
            return null;
        }

        /// <summary>The colour a role shows for an element: this palette's override, the shared table's, else its own.</summary>
        public Color Resolve(Slot slot, EnemyElement element, ElementColors[] shared)
        {
            if (element != EnemyElement.None)
            {
                if (TryElement(Elements, element, slot.Role, out var c)) return c;
                if (TryElement(shared, element, slot.Role, out c)) return c;
            }
            return slot.Color;
        }

        public static bool TryElement(ElementColors[] table, EnemyElement element, PaletteRole role, out Color color)
        {
            if (table != null)
                for (int i = 0; i < table.Length; i++)
                    if (table[i] != null && table[i].Element == element && table[i].TryGet(role, out color)) return true;
            color = default;
            return false;
        }

        public static EnemyElement ElementOf(Element e) => (EnemyElement)((int)e + 1);

        /// <summary>0xRRGGBB as an sRGB colour.</summary>
        public static Color Hex(uint rgb) =>
            new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);

        /// <summary>The sRGB colour as 0xRRGGBB (rounded, clamped).</summary>
        public static int HexOf(Color srgb) =>
            (Byte(srgb.r) << 16) | (Byte(srgb.g) << 8) | Byte(srgb.b);

        static int Byte(float v) => Mathf.RoundToInt(Mathf.Clamp01(v) * 255f);

        public static bool IsGlow(PaletteRole role) => role == PaletteRole.GlowCore || role == PaletteRole.GlowHostile;

        // ------------------------------------------------------------------ bloom compensation (export_color)
        // A port of export_color() in blender-work/_tools/dc_enemy_export.py (see notes/enemy-review/polish/
        // exporter_roles/ROLES.md): the post stack (bloom over the pale ground, warm WB, shadows grade) lifts and warms
        // small dark enemies, so the glb albedo is picked so that the lit TOP face lands on the authored colour in game.
        // Calibrated tables are exact (measured or art-directed); anything else uses a fitted model (mean error 5 sRGB
        // levels, p95 11). The generic glow rule is UNMEASURED: measure a new element glow in game and add it to Glows.
        // Once Dido raises the bloom threshold (~1.1), turn EnemyCatalog.Palette.Compensate off and re-export with
        // --raw-colors (or re-fit these constants).

        /// <summary>Authored hex -> glb albedo hex (lit roles). Exact, from the exporter's LIT_FIT.</summary>
        static readonly Dictionary<int, int> LitFit = new Dictionary<int, int>
        {
            { 0x4C525D, 0x002F3F }, { 0x2E323A, 0x00092C }, { 0x9A8F80, 0x5A6265 }, { 0xF5D76E, 0xA7843F },
            { 0x6FE3CF, 0x49C2BE }, { 0xFF8A2A, 0xE5694D }, { 0xE8B43C, 0xB38650 }, { 0x8FA8FF, 0x5679FF },
            { 0xC8D5FF, 0x8CAEFF }, { 0xD6E0FF, 0x9DBEFF }, { 0x6C84E6, 0x335AC9 }, { 0xC3B2FF, 0x5679FF },
        };
        /// <summary>Authored hex -> (emission hex, strength) (glow roles). Measured in game (polish/INTEGRATION.md).</summary>
        static readonly Dictionary<int, (int hex, float strength)> Glows = new Dictionary<int, (int, float)>
        {
            { 0xFF2E63, (0xFF0A2C, 0.63f) }, { 0xFF8A2A, (0xFF330B, 0.72f) },
        };
        // albedo = clamp01(LitM * target_linear ^ LitGamma + LitC)   (fit_comp.py)
        static readonly Vector3 LitM0 = new Vector3(0.57165f, 0.04544f, 0.0031f),
            LitM1 = new Vector3(0.01954f, 0.71847f, 0.00627f),
            LitM2 = new Vector3(0.02914f, 0.12202f, 0.7777f),
            LitC = new Vector3(-0.02128f, -0.00425f, 0.00427f);
        const float LitGamma = 1.31986f;
        // emission = GlowGain * (authored_linear / max channel) ^ GlowGamma
        const float GlowGain = 0.675f, GlowGamma = 1.8f;

        /// <summary>
        /// The material factors for an authored colour: baseLinear (glTF baseColorFactor, linear, band lift applied and
        /// clamped) and emissionLinear (glTF emissiveFactor). srgb: the authored colour as picked (sRGB). compensate:
        /// apply the bloom compensation (off: the authored colour as is, still times the band lift, which is baked into
        /// the mesh). Glow roles: black base, emission only.
        /// </summary>
        public static void ExportColor(Color srgb, PaletteRole role, float bandLift, bool compensate,
            out Color baseLinear, out Color emissionLinear)
        {
            srgb.r = Mathf.Clamp01(srgb.r);
            srgb.g = Mathf.Clamp01(srgb.g);
            srgb.b = Mathf.Clamp01(srgb.b);
            var lin = srgb.linear;
            int hex = HexOf(srgb);
            if (IsGlow(role))
            {
                Color e;
                if (!compensate) e = lin;
                else if (Glows.TryGetValue(hex, out var g)) e = Hex((uint)g.hex).linear * g.strength;
                else
                {
                    float m = Mathf.Max(Mathf.Max(lin.r, lin.g), Mathf.Max(lin.b, 1e-6f));
                    e = new Color(GlowGain * Mathf.Pow(lin.r / m, GlowGamma), GlowGain * Mathf.Pow(lin.g / m, GlowGamma),
                        GlowGain * Mathf.Pow(lin.b / m, GlowGamma), 1f);
                }
                e.a = 1f;
                baseLinear = new Color(0f, 0f, 0f, 1f);
                emissionLinear = e;
                return;
            }
            Color a;
            if (!compensate) a = lin;
            else if (LitFit.TryGetValue(hex, out var fit)) a = Hex((uint)fit).linear;
            else
            {
                var t = new Vector3(Mathf.Pow(lin.r, LitGamma), Mathf.Pow(lin.g, LitGamma), Mathf.Pow(lin.b, LitGamma));
                a = new Color(Vector3.Dot(LitM0, t) + LitC.x, Vector3.Dot(LitM1, t) + LitC.y, Vector3.Dot(LitM2, t) + LitC.z, 1f);
            }
            float lift = bandLift > 0f ? bandLift : 1f;
            baseLinear = new Color(Mathf.Clamp01(a.r * lift), Mathf.Clamp01(a.g * lift), Mathf.Clamp01(a.b * lift), 1f);
            emissionLinear = new Color(0f, 0f, 0f, 1f);
        }

        /// <summary>The shared element table's starting colours (placeholders for Dido: tune them in EnemyCatalog).</summary>
        public static ElementColors[] DefaultElements() => new[]
        {
            new ElementColors(EnemyElement.Fire,
                new RoleColor(PaletteRole.Accent, 0xFF5A1F), new RoleColor(PaletteRole.Accent2, 0xFFB04A),
                new RoleColor(PaletteRole.AccentDark, 0xB8300C), new RoleColor(PaletteRole.GlowCore, 0xFF7A1A)),
            new ElementColors(EnemyElement.Lightning,
                new RoleColor(PaletteRole.Accent, 0xFFF06A), new RoleColor(PaletteRole.Accent2, 0xFFFBD6),
                new RoleColor(PaletteRole.AccentDark, 0xD8B400), new RoleColor(PaletteRole.GlowCore, 0xFFF7A0)),
            new ElementColors(EnemyElement.Frost,
                new RoleColor(PaletteRole.Accent, 0x7FD8FF), new RoleColor(PaletteRole.Accent2, 0xD8F4FF),
                new RoleColor(PaletteRole.AccentDark, 0x3C8FD8), new RoleColor(PaletteRole.GlowCore, 0x8FE6FF)),
            new ElementColors(EnemyElement.Poison,
                new RoleColor(PaletteRole.Accent, 0x7CFF3A), new RoleColor(PaletteRole.Accent2, 0xC8FF8A),
                new RoleColor(PaletteRole.AccentDark, 0x3E9E1C), new RoleColor(PaletteRole.GlowCore, 0x8CFF2E)),
        };

        /// <summary>The role a material is named after (DCX_Stone, DCX_GlowHostile ... ; " (Instance)" ignored), or -1.</summary>
        public static int RoleOf(string materialName)
        {
            if (string.IsNullOrEmpty(materialName) || !materialName.StartsWith("DCX_", System.StringComparison.Ordinal)) return -1;
            int end = materialName.IndexOf(' ');
            int len = (end < 0 ? materialName.Length : end) - 4;
            for (int r = 0; r < RoleNames.Length; r++)
                if (len == RoleNames[r].Length && string.CompareOrdinal(materialName, 4, RoleNames[r], 0, len) == 0) return r;
            return -1;
        }

        static readonly string[] RoleNames = System.Enum.GetNames(typeof(PaletteRole));
    }
}
