using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>
    /// Placeholder art generated at runtime: stone column sprites (top face + cliff walls reaching down to the
    /// lowest level) in a few texture variants per elevation, and a diamond outline for the hover highlight.
    /// Sprites are neutral grey so SpriteRenderer.color can tint them (dirt, rock, base, spawn, ore).
    /// </summary>
    public static class BlockArt
    {
        public const int Variants = 4;
        const int W = 128, TopH = 64;                               // top face pixels; 128 px per world unit
        const int StepPx = (int)(IsoGrid.StepHeight * W);           // 32 px per elevation level
        const float TopValue = 0.9f, LeftValue = 0.58f, RightValue = 0.4f;

        static Sprite[,] columns;
        static Sprite highlight;
        static Sprite[] bridges;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            columns = null;
            highlight = null;
            bridges = null;
        }

        public static Sprite Column(int level, int variant)
        {
            if (columns == null) columns = new Sprite[IsoGrid.MaxLevel - IsoGrid.MinLevel + 1, Variants];
            int i = level - IsoGrid.MinLevel;
            variant = ((variant % Variants) + Variants) % Variants;
            return columns[i, variant] != null ? columns[i, variant] : columns[i, variant] = MakeColumn((i + 1) * StepPx, variant);
        }

        public static Sprite Highlight => highlight != null ? highlight : highlight = MakeHighlight();

        /// <summary>
        /// Wooden bridge deck. connections = sides that continue (1 +x, 2 -x, 4 +y, 8 -y); every other side gets
        /// a rail. alongX = planks lie across the grid x axis.
        /// </summary>
        public static Sprite Bridge(int connections, bool alongX)
        {
            if (bridges == null) bridges = new Sprite[32];
            int i = (connections & 15) * 2 + (alongX ? 0 : 1);
            return bridges[i] != null ? bridges[i] : bridges[i] = MakeBridge(connections, alongX);
        }

        /// <summary>Half the height of the top diamond at pixel column x (in pixels).</summary>
        static float HalfHeightAt(int x) => TopH * 0.5f * (1f - Mathf.Abs((x + 0.5f) / (W * 0.5f) - 1f));

        static Sprite MakeColumn(int wallPx, int variant)
        {
            int h = TopH + wallPx;
            var px = new Color[W * h];
            for (int row = 0; row < h; row++)           // row 0 = top of the image
                for (int x = 0; x < W; x++)
                {
                    float half = HalfHeightAt(x);
                    float fromMid = row + 0.5f - TopH * 0.5f; // + is downward
                    float v;
                    if (Mathf.Abs(fromMid) < half)
                        v = TopValue * TopTexture(x, row, variant);
                    else if (fromMid >= half && fromMid < half + wallPx)
                        v = WallValue(x, fromMid - half, variant);
                    else
                    {
                        px[(h - 1 - row) * W + x] = Color.clear;
                        continue;
                    }
                    px[(h - 1 - row) * W + x] = new Color(v, v, v, 1f);
                }
            // Pivot at the centre of the top face.
            return MakeSprite(px, W, h, new Vector2(0.5f, (h - TopH * 0.5f) / h), "Column");
        }

        /// <summary>
        /// Dirt texture in the cell's own (u,v) grid space. The noise wraps at the cell edges and its edge lattice
        /// values are shared by all variants, so neighbouring cells join without a visible seam.
        /// </summary>
        static float TopTexture(int x, int row, int variant)
        {
            CellUV(x, row, out float u, out float v);
            float n = 0.6f * PeriodicNoise(u, v, 4, variant, 11) + 0.4f * PeriodicNoise(u, v, 9, variant, 23);
            float value = 1f + (n - 0.5f) * 0.22f;
            float grain = Hash(x + variant * 997, row);
            value += (grain - 0.5f) * 0.06f;
            if (grain > 0.985f) value *= 0.72f;          // dark pebbles
            else if (grain < 0.012f) value *= 1.15f;     // light grit
            return value;
        }

        /// <summary>Top-face pixel -> position in the cell's grid space, u along grid x and v along grid y, 0..1.</summary>
        static void CellUV(int x, int row, out float u, out float v)
        {
            float dx = x + 0.5f - W * 0.5f, dy = TopH * 0.5f - (row + 0.5f);
            u = (dx / (W * 0.5f) + dy / (TopH * 0.5f)) * 0.5f + 0.5f;
            v = (dy / (TopH * 0.5f) - dx / (W * 0.5f)) * 0.5f + 0.5f;
        }

        static Sprite MakeBridge(int connections, bool alongX)
        {
            const int deckPx = 8, postPx = 56;
            int h = TopH + deckPx + postPx;
            var plank = new Color(0.5f, 0.33f, 0.19f);
            var rail = new Color(0.32f, 0.2f, 0.11f);
            var gap = new Color(0.1f, 0.07f, 0.05f);
            var px = new Color[W * h];
            for (int row = 0; row < h; row++)
                for (int x = 0; x < W; x++)
                {
                    float half = HalfHeightAt(x);
                    float fromMid = row + 0.5f - TopH * 0.5f;
                    Color c = Color.clear;
                    float alpha = 1f;
                    if (Mathf.Abs(fromMid) < half)
                    {
                        CellUV(x, row, out float u, out float v);
                        float along = alongX ? u : v;
                        int board = Mathf.FloorToInt(along * 5f);
                        const float r = 0.12f;
                        bool railed = (u > 1f - r && (connections & 1) == 0) || (u < r && (connections & 2) == 0)
                                   || (v > 1f - r && (connections & 4) == 0) || (v < r && (connections & 8) == 0);
                        if (railed) c = rail;
                        else if (along * 5f - board < 0.12f) c = gap;
                        else c = plank * (0.88f + Hash(board, alongX ? 3 : 5) * 0.24f);
                        c *= 0.94f + Hash(x, row) * 0.12f;
                    }
                    else if (fromMid >= half && fromMid < half + deckPx)
                        c = rail * (x < W / 2 ? 0.8f : 0.6f);
                    else if (fromMid >= half && (Mathf.Abs(x - W * 0.5f) < 3f || Mathf.Abs(x - W * 0.25f) < 2.5f || Mathf.Abs(x - W * 0.75f) < 2.5f))
                    {
                        // Support posts fading into the void below.
                        float t = Mathf.Clamp01((fromMid - half - deckPx) / postPx);
                        c = rail * (x < W / 2 ? 0.7f : 0.5f);
                        alpha = 1f - t;
                    }
                    else alpha = 0f;
                    px[(h - 1 - row) * W + x] = new Color(c.r, c.g, c.b, alpha);
                }
            return MakeSprite(px, W, h, new Vector2(0.5f, (h - TopH * 0.5f) / h), "Bridge");
        }

        /// <summary>Rough cliff face: bright lip at the top edge, darker with depth, faint per-level ledges.</summary>
        static float WallValue(int x, float below, int variant)
        {
            float v = x < W / 2 ? LeftValue : RightValue;
            if (below < 2f) v *= 1.35f;                                          // lit edge where wall meets top
            v *= Mathf.Lerp(1f, 0.55f, Mathf.Clamp01(below / (StepPx * 2.5f))); // ambient occlusion with depth
            float column = Hash(x * 7 + variant * 131, (int)(below / 9f));       // vertical rock streaks
            v *= 0.9f + column * 0.2f;
            if ((int)below % StepPx == StepPx - 1) v *= 0.85f;                   // ledge between levels
            v += (Hash(x + variant * 389, (int)below + 4096) - 0.5f) * 0.05f;
            return v;
        }

        /// <summary>Smooth value noise on an n x n lattice that wraps in u and v.</summary>
        static float PeriodicNoise(float u, float v, int n, int variant, int salt)
        {
            float fu = u * n, fv = v * n;
            int i0 = Mathf.FloorToInt(fu), j0 = Mathf.FloorToInt(fv);
            float tu = fu - i0, tv = fv - j0;
            tu = tu * tu * (3f - 2f * tu);
            tv = tv * tv * (3f - 2f * tv);
            float a = Lattice(i0, j0, n, variant, salt), b = Lattice(i0 + 1, j0, n, variant, salt);
            float c = Lattice(i0, j0 + 1, n, variant, salt), d = Lattice(i0 + 1, j0 + 1, n, variant, salt);
            return Mathf.Lerp(Mathf.Lerp(a, b, tu), Mathf.Lerp(c, d, tu), tv);
        }

        static float Lattice(int i, int j, int n, int variant, int salt)
        {
            i = ((i % n) + n) % n;
            j = ((j % n) + n) % n;
            if (i == 0 || j == 0) variant = 0; // edge values shared by every variant -> seamless between variants
            return Hash(i * 31 + salt, j * 17 + variant * 7919);
        }

        static Sprite MakeHighlight()
        {
            var px = new Color[W * TopH];
            for (int row = 0; row < TopH; row++)
                for (int x = 0; x < W; x++)
                {
                    float inside = HalfHeightAt(x) - Mathf.Abs(row + 0.5f - TopH * 0.5f);
                    px[row * W + x] = inside > 0f && inside < 3f ? Color.white : inside >= 3f ? new Color(1f, 1f, 1f, 0.12f) : Color.clear;
                }
            return MakeSprite(px, W, TopH, new Vector2(0.5f, 0.5f), "Highlight");
        }

        static Sprite MakeSprite(Color[] px, int w, int h, Vector2 pivot, string name)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = name,
            };
            tex.SetPixels(px);
            tex.Apply(false, true);
            var s = Sprite.Create(tex, new Rect(0, 0, w, h), pivot, W, 0, SpriteMeshType.FullRect);
            s.name = name;
            return s;
        }

        static float Hash(int x, int y)
        {
            uint h = (uint)(x * 374761393 + y * 668265263);
            h = (h ^ (h >> 13)) * 1274126177;
            return (h & 0xFFFF) / 65535f;
        }
    }
}
