using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>
    /// Placeholder art generated at runtime, drawn top-down to lie flat on a planet cell (see PlanetArt.LayFlat):
    /// a hexagon outline for the hover highlight and footprint marks, and path chevrons. Dir.SpriteSpan cells across,
    /// in the cell frame (see Dir); white, so SpriteRenderer.color tints them.
    /// </summary>
    public static class BlockArt
    {
        const int W = 64;

        static Sprite highlight, hexFill;
        static Sprite[] arrows;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            highlight = hexFill = null;
            arrows = null;
        }

        public static Sprite Highlight => highlight != null ? highlight : highlight = MakeHighlight();

        /// <summary>A solid hexagon a little smaller than a cell, with soft edges (the enemy path's trail).</summary>
        public static Sprite HexFill => hexFill != null ? hexFill : hexFill = MakeHexFill();

        /// <summary>A chevron lying flat on a cell, pointing toward side dir (see Dir).</summary>
        public static Sprite PathArrow(int dir)
        {
            dir = Dir.Wrap(dir);
            if (arrows == null) arrows = new Sprite[Dir.Count];
            return arrows[dir] != null ? arrows[dir] : arrows[dir] = MakeArrow(dir);
        }

        /// <summary>Point in the cell's frame (in cells) at the middle of pixel (x, y).</summary>
        static Vector2 At(int x, int y) => new Vector2((x + 0.5f) / W - 0.5f, (y + 0.5f) / W - 0.5f) * Dir.SpriteSpan;

        /// <summary>A hexagon outline with a faint fill.</summary>
        static Sprite MakeHighlight()
        {
            var px = new Color[W * W];
            float pixel = Dir.SpriteSpan / W;
            for (int y = 0; y < W; y++)
                for (int x = 0; x < W; x++)
                {
                    var p = At(x, y);
                    float edge = 0f; // largest distance toward any side, 0.5 on the outline
                    for (int d = 0; d < 3; d++) edge = Mathf.Max(edge, Mathf.Abs(Vector2.Dot(p, Dir.Normal(d))));
                    px[y * W + x] = edge > 0.5f ? Color.clear : edge > 0.5f - 3f * pixel ? Color.white : new Color(1f, 1f, 1f, 0.14f);
                }
            return MakeSprite(px, "Highlight");
        }

        static Sprite MakeHexFill()
        {
            var px = new Color[W * W];
            float pixel = Dir.SpriteSpan / W;
            for (int y = 0; y < W; y++)
                for (int x = 0; x < W; x++)
                {
                    var p = At(x, y);
                    float edge = 0f;
                    for (int d = 0; d < 3; d++) edge = Mathf.Max(edge, Mathf.Abs(Vector2.Dot(p, Dir.Normal(d))));
                    float a = Mathf.Clamp01((0.47f - edge) / (3f * pixel));
                    px[y * W + x] = new Color(1f, 1f, 1f, a);
                }
            return MakeSprite(px, "HexFill");
        }

        static Sprite MakeArrow(int dir)
        {
            const float tip = 0.22f, thickness = 0.16f, span = 0.3f;
            var forward = Dir.Normal(dir);
            var side = new Vector2(-forward.y, forward.x);
            var px = new Color[W * W];
            for (int y = 0; y < W; y++)
                for (int x = 0; x < W; x++)
                {
                    // Into the arrow's frame: `along` points where it points, `across` is sideways.
                    var p = At(x, y);
                    float along = Vector2.Dot(p, forward);
                    float across = Vector2.Dot(p, side);
                    float s = along + Mathf.Abs(across);
                    bool on = s < tip && s > tip - thickness && Mathf.Abs(across) < span;
                    px[y * W + x] = on ? Color.white : Color.clear;
                }
            return MakeSprite(px, "PathArrow");
        }

        static Sprite MakeSprite(Color[] px, string name)
        {
            var tex = new Texture2D(W, W, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = name,
            };
            tex.SetPixels(px);
            tex.Apply(false, true);
            var s = Sprite.Create(tex, new Rect(0, 0, W, W), new Vector2(0.5f, 0.5f), W / Dir.SpriteSpan, 0, SpriteMeshType.FullRect);
            s.name = name;
            return s;
        }
    }
}
