using System.Collections.Generic;
using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>
    /// The HUD's small tween toolbox. Everything here is advanced by <see cref="Update"/> with unscaled delta time, so
    /// effects keep playing while Time.timeScale is 0 (card picks, end screens). Pieces: <see cref="Timer"/> (flash,
    /// bump, shake, glint), <see cref="Counter"/> (a number that counts toward a target), fly-ins (a screen-space
    /// label that travels from A to B) and the easing helpers. All durations are chosen by the caller; the HUD keeps
    /// them at 0.6 s or less except for the boss warning pulse.
    /// </summary>
    public class HudFx
    {
        /// <summary>PlayerPrefs key of the reduced-motion option (no shake, no fly-ins; numbers still count).</summary>
        public const string ReducedMotionKey = "dc.reducedMotion";

        /// <summary>A one-shot 0..1 clock: <see cref="Start"/> it, read <see cref="N"/>, tick it every frame.</summary>
        public struct Timer
        {
            float t, dur;
            public void Start(float seconds) { t = 0f; dur = Mathf.Max(0.0001f, seconds); }
            public void Tick(float dt) { if (t < dur) t += dt; }
            public bool Active => dur > 0f && t < dur;
            /// <summary>Progress 0..1 (1 when idle).</summary>
            public float N => dur <= 0f ? 1f : Mathf.Clamp01(t / dur);
            /// <summary>1 at the start falling to 0 (0 when idle): a decaying pulse.</summary>
            public float Fade => Active ? 1f - N : 0f;
        }

        /// <summary>A number that counts toward <see cref="Target"/>: fast for big gaps, never slower than 25/s.</summary>
        public class Counter
        {
            public float Shown;
            public int Target;
            public Timer Bump;
            public void Reset(int value) { Shown = Target = value; }
            public void Tick(float dt)
            {
                Bump.Tick(dt);
                float diff = Target - Shown;
                if (Mathf.Abs(diff) < 0.5f) { Shown = Target; return; }
                float speed = Mathf.Max(Mathf.Abs(diff) / 0.35f, 25f);
                Shown = Mathf.MoveTowards(Shown, Target, speed * dt);
            }
            public int Value => Mathf.RoundToInt(Shown);
        }

        /// <summary>A label travelling over the screen (IMGUI pixels, origin top-left) from <c>From</c> to <c>To</c>.</summary>
        public class Fly
        {
            public Vector2 From, To;
            public float T, Dur = 0.55f, Size = 22f;
            public string Text;
            public Color Color = Color.white;
            public bool Coin;
            public System.Action OnArrive;
            public float N => Mathf.Clamp01(T / Dur);
        }

        public readonly List<Fly> Flies = new List<Fly>();

        public static bool ReducedMotion
        {
            get => PlayerPrefs.GetInt(ReducedMotionKey, 0) == 1;
            set { PlayerPrefs.SetInt(ReducedMotionKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>Starts a fly-in; with reduced motion it lands at once.</summary>
        public void Add(Fly f)
        {
            if (ReducedMotion || Flies.Count > 40) { f.OnArrive?.Invoke(); return; }
            Flies.Add(f);
        }

        /// <summary>Advances every fly-in and runs the arrival callbacks.</summary>
        public void Update(float dt)
        {
            for (int i = Flies.Count - 1; i >= 0; i--)
            {
                var f = Flies[i];
                f.T += dt;
                if (f.T < f.Dur) continue;
                Flies.RemoveAt(i);
                f.OnArrive?.Invoke();
            }
        }

        /// <summary>Position of a fly-in: eased straight line plus a small arc.</summary>
        public static Vector2 Pos(Fly f, float arc)
        {
            float e = InCubic(f.N);
            var p = Vector2.Lerp(f.From, f.To, e);
            p.y -= Mathf.Sin(Mathf.PI * f.N) * arc;
            return p;
        }

        /// <summary>Damped random-ish shake offset in px: amplitude <paramref name="amp"/> fading with <paramref name="fade"/>.</summary>
        public static Vector2 Shake(float fade, float amp, float time)
        {
            if (ReducedMotion || fade <= 0f) return Vector2.zero;
            return new Vector2(Mathf.Sin(time * 91f), Mathf.Sin(time * 77f + 1.3f)) * (amp * fade * fade);
        }

        /// <summary>Integer counting from 0 to <paramref name="target"/> between <paramref name="delay"/> and delay + dur.</summary>
        public static int CountUp(int target, float age, float delay, float dur = 0.6f)
            => Mathf.RoundToInt(target * OutCubic(Mathf.Clamp01((age - delay) / dur)));

        public static float OutCubic(float t) { t = 1f - Mathf.Clamp01(t); return 1f - t * t * t; }
        public static float InCubic(float t) { t = Mathf.Clamp01(t); return t * t * t; }
        public static float InOutCubic(float t) { t = Mathf.Clamp01(t); return t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f; }
        public static float OutBack(float t)
        {
            t = Mathf.Clamp01(t) - 1f;
            return 1f + 2.70158f * t * t * t + 1.70158f * t * t;
        }
    }
}
