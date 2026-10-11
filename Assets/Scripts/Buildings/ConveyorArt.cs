using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>
    /// Procedural conveyor belt sprites, one per (input side, output side, input lift, output lift, animation frame).
    /// Opposite sides give a straight belt, adjacent sides a quarter-circle curve. A lift of 1 raises that end of the
    /// belt by one elevation level, so a belt climbing to a higher neighbour ramps up across the lower tile; side
    /// walls drop to the ground underneath. Chevrons point and scroll in the direction of flow.
    /// Sides are indexed like <see cref="Dir"/>: 0 = +x, 1 = -x, 2 = +y, 3 = -y.
    /// </summary>
    public static class ConveyorArt
    {
        public const int Frames = 8;  // with ridges every 1/6 cell, each frame moves them 3/8 of a period: reads as forward motion
        const int W = 128, TopH = 64, Lift = 6;             // belt surface sits 6 px above the ground
        const int StepPx = (int)(IsoGrid.StepHeight * W);   // one elevation level = 32 px
        const int Margin = StepPx;                          // headroom above the cell for a raised end
        const float HalfWidth = 0.34f, RailWidth = 0.08f;

        static readonly Color Belt = new Color(0.15f, 0.14f, 0.15f), Ridge = new Color(0.1f, 0.09f, 0.1f);
        static readonly Color Rail = new Color(0.45f, 0.42f, 0.44f), Chevron = new Color(1f, 0.64f, 0.12f);
        static readonly Color SideLeft = new Color(0.27f, 0.25f, 0.26f), SideRight = new Color(0.19f, 0.17f, 0.18f);

        static Sprite[,,,,] sprites;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => sprites = null;

        public static Sprite Get(int inSide, int outSide, int inLift, int outLift, int frame)
        {
            if (sprites == null) sprites = new Sprite[4, 4, 2, 2, Frames];
            inLift = Mathf.Clamp(inLift, 0, 1);
            outLift = Mathf.Clamp(outLift, 0, 1);
            frame = ((frame % Frames) + Frames) % Frames;
            if (sprites[inSide, outSide, inLift, outLift, frame] == null)
                MakeAllFrames(inSide, outSide, inLift, outLift);
            return sprites[inSide, outSide, inLift, outLift, frame];
        }

        /// <summary>Geometry is the same for every frame, so it's solved once and only the colours change.</summary>
        static void MakeAllFrames(int inSide, int outSide, int inLift, int outLift)
        {
            int h = Margin + TopH + Lift + 2;
            var kind = new byte[W * h];       // 0 empty, 1 belt top, 2 side wall
            var s = new float[W * h];
            var t = new float[W * h];
            var shade = new float[W * h];
            var lift = new float[W * h];

            for (int row = 0; row < h; row++)  // row 0 = top of the image
                for (int x = 0; x < W; x++)
                {
                    int i = row * W + x;
                    if (TopAt(x, row, inSide, outSide, inLift, outLift, out s[i], out t[i], out lift[i])) kind[i] = 1;
                }

            // Side wall: the nearest belt pixel above is close enough that its wall reaches down to here.
            for (int row = 0; row < h; row++)
                for (int x = 0; x < W; x++)
                {
                    int i = row * W + x;
                    if (kind[i] == 1) continue;
                    for (int k = 1; k <= Lift + StepPx && row - k >= 0; k++)
                    {
                        int above = (row - k) * W + x;
                        if (kind[above] != 1) continue;
                        if (k <= Lift + lift[above])
                        {
                            kind[i] = 2;
                            shade[i] = 1f - Mathf.Min(k, 10) * 0.035f;
                        }
                        break;
                    }
                }

            // Pivot = ground centre of the cell: Lift px below the unraised belt surface's centre.
            var pivot = new Vector2(0.5f, (h - Margin - TopH * 0.5f - Lift) / h);
            var px = new Color[W * h];
            for (int frame = 0; frame < Frames; frame++)
            {
                float phase = frame / (float)Frames;
                for (int row = 0; row < h; row++)
                    for (int x = 0; x < W; x++)
                    {
                        int i = row * W + x;
                        Color c = kind[i] == 1 ? SurfaceColor(s[i], t[i], phase)
                            : kind[i] == 2 ? (x < W / 2 ? SideLeft : SideRight) * shade[i]
                            : Color.clear;
                        if (kind[i] != 0) c.a = 1f;
                        px[(h - 1 - row) * W + x] = c;
                    }
                var tex = new Texture2D(W, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Conveyor" };
                tex.SetPixels(px);
                tex.Apply(false, true);
                sprites[inSide, outSide, inLift, outLift, frame] = Sprite.Create(tex, new Rect(0, 0, W, h), pivot, W, 0, SpriteMeshType.FullRect);
            }
        }

        /// <summary>
        /// Is this pixel on the belt's top surface? Inverts the projection: the column fixes u - v, and the row fixes
        /// u + v once the lift at that spot is known, so the lift is found by a few fixed-point iterations.
        /// </summary>
        static bool TopAt(int x, int row, int inSide, int outSide, int inLift, int outLift, out float s, out float t, out float liftPx)
        {
            s = t = liftPx = 0f;
            float diff = (x + 0.5f - W * 0.5f) / (W * 0.5f);                  // u - v
            float u = 0f, v = 0f;
            int iterations = inLift == 0 && outLift == 0 ? 1 : 6;
            for (int it = 0; it < iterations; it++)
            {
                float sum = 1f + (Margin + TopH * 0.5f - (row + 0.5f) - liftPx) / (TopH * 0.5f); // u + v
                u = (sum + diff) * 0.5f;
                v = (sum - diff) * 0.5f;
                BeltCoords(Mathf.Clamp01(u), Mathf.Clamp01(v), inSide, outSide, out s, out t);
                liftPx = Mathf.Lerp(inLift, outLift, s) * StepPx;
            }
            if (u < 0f || u > 1f || v < 0f || v > 1f) return false;
            BeltCoords(u, v, inSide, outSide, out s, out t);
            return Mathf.Abs(t) <= HalfWidth + RailWidth;
        }

        static Color SurfaceColor(float s, float t, float phase)
        {
            float at = Mathf.Abs(t);
            if (at > HalfWidth) return Rail * (at > HalfWidth + RailWidth * 0.6f ? 0.7f : 1f);
            float flow = s - phase * 0.5f;                       // chevrons repeat every half cell
            if (at < 0.22f && Mathf.Repeat(flow * 2f + at * 1.3f, 1f) < 0.16f) return Chevron;
            return Mathf.Repeat(flow * 6f, 1f) < 0.18f ? Ridge : Belt;
        }

        /// <summary>Height of the belt surface above the ground, in world units.</summary>
        public const float SurfaceHeight = (float)Lift / W;

        /// <summary>Point on the belt's centre line at s (0 = input side, 1 = output side), in the cell's (u, v) space.</summary>
        public static Vector2 CenterLine(int inSide, int outSide, float s)
        {
            if (Dir.Opposite(inSide) == outSide) return Vector2.Lerp(SideMid(inSide), SideMid(outSide), s);
            var corner = new Vector2(SideU(inSide, outSide), SideV(inSide, outSide));
            var toIn = (SideMid(inSide) - corner).normalized;
            var toOut = (SideMid(outSide) - corner).normalized;
            float a = s * Mathf.PI * 0.5f;
            return corner + (toIn * Mathf.Cos(a) + toOut * Mathf.Sin(a)) * 0.5f;
        }

        /// <summary>s = 0..1 along the flow from the input side to the output side; t = signed offset from the centre line.</summary>
        static void BeltCoords(float u, float v, int inSide, int outSide, out float s, out float t)
        {
            if (Dir.Opposite(inSide) == outSide)
            {
                bool alongX = outSide < 2;
                float along = alongX ? u : v;
                s = outSide == 0 || outSide == 2 ? along : 1f - along;
                t = (alongX ? v : u) - 0.5f;
                return;
            }

            // Curve: quarter circle of radius 0.5 around the cell corner shared by the two sides.
            var corner = new Vector2(SideU(inSide, outSide), SideV(inSide, outSide));
            var w = new Vector2(u, v) - corner;
            var toIn = SideMid(inSide) - corner;
            var toOut = SideMid(outSide) - corner;
            float a = Mathf.Atan2(Vector2.Dot(w, toOut.normalized), Vector2.Dot(w, toIn.normalized));
            s = Mathf.Clamp01(a / (Mathf.PI * 0.5f));
            t = w.magnitude - 0.5f;
        }

        static Vector2 SideMid(int side) => side switch
        {
            0 => new Vector2(1f, 0.5f),
            1 => new Vector2(0f, 0.5f),
            2 => new Vector2(0.5f, 1f),
            _ => new Vector2(0.5f, 0f),
        };

        // The x-side of the pair fixes the corner's u, the y-side fixes its v.
        static float SideU(int a, int b) => (a < 2 ? a : b) == 0 ? 1f : 0f;
        static float SideV(int a, int b) => (a >= 2 ? a : b) == 2 ? 1f : 0f;
    }

    /// <summary>Grid directions as side indices: 0 = +x, 1 = -x, 2 = +y, 3 = -y.</summary>
    public static class Dir
    {
        public static readonly Vector2Int[] Offsets = { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
        public static int Opposite(int d) => d ^ 1;
        public static int Of(Vector2Int delta) => delta.x > 0 ? 0 : delta.x < 0 ? 1 : delta.y > 0 ? 2 : 3;
    }
}
