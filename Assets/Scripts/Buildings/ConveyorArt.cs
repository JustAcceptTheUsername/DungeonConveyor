using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>
    /// Procedural conveyor belt sprites, drawn top-down to lie flat on a hex cell (see PlanetArt.LayFlat), one per
    /// (input side, output side, animation frame). Opposite sides give a straight belt; other pairs an arc that meets
    /// both edges square on (a wide bend for sides two apart, a tight one for neighbouring sides). Chevrons point and
    /// scroll in the direction of flow. A belt ramping up to a higher neighbour is the same sprite tilted (see
    /// BuildController.LayBelt). Sides are indexed like <see cref="Dir"/>.
    /// </summary>
    public static class ConveyorArt
    {
        public const int Frames = 8;  // with ridges every 1/6 cell, each frame moves them 3/8 of a period: reads as forward motion
        const int W = 72;
        const float HalfWidth = 0.23f, RailWidth = 0.055f;

        static readonly Color Belt = new Color(0.15f, 0.14f, 0.15f), Ridge = new Color(0.1f, 0.09f, 0.1f);
        static readonly Color Rail = new Color(0.45f, 0.42f, 0.44f), Chevron = new Color(1f, 0.64f, 0.12f);

        static Sprite[,,] sprites;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => sprites = null;

        /// <summary>Height of the belt surface above the ground, in world units.</summary>
        public const float SurfaceHeight = 0.05f;

        /// <summary>The belt sprite; the lifts don't change the art (the sprite is tilted instead) and are ignored.</summary>
        public static Sprite Get(int inSide, int outSide, int inLift, int outLift, int frame)
        {
            if (sprites == null) sprites = new Sprite[Dir.Count, Dir.Count, Frames];
            inSide = Dir.Wrap(inSide);
            outSide = Dir.Wrap(outSide);
            if (inSide == outSide) outSide = Dir.Opposite(inSide);
            frame = ((frame % Frames) + Frames) % Frames;
            if (sprites[inSide, outSide, frame] == null) MakeAllFrames(inSide, outSide);
            return sprites[inSide, outSide, frame];
        }

        /// <summary>Geometry is the same for every frame, so it's solved once and only the colours change.</summary>
        static void MakeAllFrames(int inSide, int outSide)
        {
            var on = new bool[W * W];
            var s = new float[W * W];
            var t = new float[W * W];
            for (int y = 0; y < W; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x;
                    var p = new Vector2(((x + 0.5f) / W - 0.5f) * Dir.SpriteSpan, ((y + 0.5f) / W - 0.5f) * Dir.SpriteSpan);
                    BeltCoords(p, inSide, outSide, out s[i], out t[i]);
                    on[i] = Dir.InsideHex(p) && Mathf.Abs(t[i]) <= HalfWidth + RailWidth && s[i] > -0.02f && s[i] < 1.02f;
                }

            var px = new Color[W * W];
            for (int frame = 0; frame < Frames; frame++)
            {
                float phase = frame / (float)Frames;
                for (int i = 0; i < px.Length; i++)
                {
                    if (!on[i]) { px[i] = Color.clear; continue; }
                    var c = SurfaceColor(s[i], t[i], phase);
                    c.a = 1f;
                    px[i] = c;
                }
                var tex = new Texture2D(W, W, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "Conveyor" };
                tex.SetPixels(px);
                tex.Apply(false, true);
                sprites[inSide, outSide, frame] = Sprite.Create(tex, new Rect(0, 0, W, W), new Vector2(0.5f, 0.5f), W / Dir.SpriteSpan, 0, SpriteMeshType.FullRect);
            }
        }

        static Color SurfaceColor(float s, float t, float phase)
        {
            float at = Mathf.Abs(t);
            if (at > HalfWidth) return Rail * (at > HalfWidth + RailWidth * 0.6f ? 0.7f : 1f);
            float flow = s - phase * 0.5f;                       // chevrons repeat every half cell
            if (at < 0.15f && Mathf.Repeat(flow * 2f + at * 1.6f, 1f) < 0.16f) return Chevron;
            return Mathf.Repeat(flow * 6f, 1f) < 0.18f ? Ridge : Belt;
        }

        /// <summary>
        /// The arc for a bend: its centre is where the two sides' edge lines cross, so it leaves both edges square on.
        /// sweep is signed (radians from the input midpoint to the output midpoint around the centre).
        /// </summary>
        static void Arc(int inSide, int outSide, out Vector2 centre, out float radius, out float sweep)
        {
            Vector2 a = Dir.Normal(inSide), b = Dir.Normal(outSide);
            // Solve p·a = 0.5, p·b = 0.5.
            float det = a.x * b.y - a.y * b.x;
            centre = new Vector2((0.5f * b.y - 0.5f * a.y) / det, (a.x * 0.5f - b.x * 0.5f) / det);
            var fromIn = Dir.Mid(inSide) - centre;
            var toOut = Dir.Mid(outSide) - centre;
            radius = fromIn.magnitude;
            sweep = Vector2.SignedAngle(fromIn, toOut) * Mathf.Deg2Rad;
        }

        /// <summary>Point on the belt's centre line at s (0 = input side, 1 = output side), in the cell's frame (centre = 0).</summary>
        public static Vector2 CenterLine(int inSide, int outSide, float s)
        {
            if (Dir.Opposite(inSide) == outSide || inSide == outSide) return Vector2.Lerp(Dir.Mid(inSide), Dir.Mid(Dir.Opposite(inSide)), s);
            Arc(inSide, outSide, out var centre, out var radius, out var sweep);
            var from = Dir.Mid(inSide) - centre;
            float a = Mathf.Atan2(from.y, from.x) + sweep * s;
            return centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
        }

        /// <summary>s = 0..1 along the flow from the input side to the output side; t = signed offset from the centre line.</summary>
        static void BeltCoords(Vector2 p, int inSide, int outSide, out float s, out float t)
        {
            if (Dir.Opposite(inSide) == outSide)
            {
                Vector2 a = Dir.Mid(inSide), b = Dir.Mid(outSide);
                var along = b - a;
                s = Vector2.Dot(p - a, along) / along.sqrMagnitude;
                var across = new Vector2(-along.y, along.x).normalized;
                t = Vector2.Dot(p - a, across);
                return;
            }
            Arc(inSide, outSide, out var centre, out var radius, out var sweep);
            var w = p - centre;
            var from = Dir.Mid(inSide) - centre;
            float angle = Vector2.SignedAngle(from, w) * Mathf.Deg2Rad;
            s = angle / sweep;
            t = w.magnitude - radius;
        }
    }

    /// <summary>
    /// Hex sides of a cell, in the cell's own frame (x = right, y = forward): side d faces the math angle 90° + 60°·d,
    /// so 0 = forward and they run counter-clockwise seen from above. A pentagon has sides 0..4 (its side 5 leads back to
    /// itself). To step to a neighbour use IsoGrid.Neighbour / DirTo.
    /// </summary>
    public static class Dir
    {
        public const int Count = 6;
        /// <summary>Width of the square sprites drawn on a cell, in cells (a hexagon with centres 1 apart is 1.155 across its corners).</summary>
        public const float SpriteSpan = 1.2f;

        public static int Wrap(int d) => ((d % Count) + Count) % Count;
        public static int Opposite(int d) => Wrap(d + 3);
        /// <summary>Unit vector from the centre toward side d.</summary>
        public static Vector2 Normal(int d)
        {
            float a = (90f + 60f * Wrap(d)) * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        }
        /// <summary>Middle of side d's edge (half a cell out).</summary>
        public static Vector2 Mid(int d) => Normal(d) * 0.5f;
        /// <summary>Yaw (degrees, Unity's: positive turns +X toward -Z) that points a model's +X axis at side d.</summary>
        public static float Yaw(int d) => -(90f + 60f * Wrap(d));
        /// <summary>Is a point (in cells, cell frame) inside the cell's hexagon?</summary>
        public static bool InsideHex(Vector2 p)
        {
            for (int d = 0; d < 3; d++)
                if (Mathf.Abs(Vector2.Dot(p, Normal(d))) > 0.5f) return false;
            return true;
        }
    }
}
