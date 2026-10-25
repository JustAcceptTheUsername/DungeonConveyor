using System.Collections.Generic;
using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>
    /// Short-lived combat visuals built from one procedural soft-circle sprite, always turned to face the camera:
    /// explosions, muzzle flashes, sparks, flame puffs, plus line renderers for beams and range rings on the planet.
    /// Ticked by Combat.
    /// </summary>
    public static class Fx
    {
        class Effect
        {
            public SpriteRenderer Sr;
            public Vector3 Velocity;
            public float Age, Life, StartScale, EndScale;
            public Color Color;
        }

        static readonly List<Effect> effects = new List<Effect>();
        static Transform root;
        static Material material;
        static Sprite soft;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            effects.Clear();
            root = null;
            material = null;
        }

        public static void Init(Transform parent, Material spriteMaterial)
        {
            root = new GameObject("Fx").transform;
            root.SetParent(parent, false);
            material = spriteMaterial;
        }

        public static Material Material => material;

        /// <summary>White radial glow, 64 px, one world unit across.</summary>
        public static Sprite Soft
        {
            get
            {
                if (soft != null) return soft;
                const int n = 64;
                var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                        float a = Mathf.Clamp01(1f - d);
                        t.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                    }
                t.Apply();
                return soft = Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
            }
        }

        public static void Spawn(Vector3 pos, Color color, float startScale, float endScale, float life, Vector3 velocity = default)
        {
            if (root == null) return;
            var sr = new GameObject("Fx").AddComponent<SpriteRenderer>();
            sr.transform.SetParent(root, false);
            if (material != null) sr.sharedMaterial = material;
            sr.sprite = Soft;
            sr.color = color;
            sr.transform.position = pos;
            sr.transform.localScale = Vector3.one * startScale;
            PlanetArt.Billboard(sr.transform);
            effects.Add(new Effect { Sr = sr, Velocity = velocity, Life = life, StartScale = startScale, EndScale = endScale, Color = color });
        }

        /// <summary>Explosion covering a radius in cells.</summary>
        public static void Explosion(Vector3 pos, float radius, Color color)
        {
            Spawn(pos, color, 0.2f, radius * 2f, 0.35f);
            Spawn(pos, new Color(1f, 0.95f, 0.8f, 0.9f), 0.15f, radius * 0.8f, 0.18f);
        }

        /// <summary>A puff with a few sparks flying up off the planet.</summary>
        public static void Burst(Vector3 pos, Color color, float size)
        {
            Spawn(pos, color, size * 0.3f, size, 0.3f);
            var up = pos.normalized;
            for (int i = 0; i < 4; i++)
                Spawn(pos, color, size * 0.25f, 0.05f, 0.4f, Vector3.ProjectOnPlane(Random.insideUnitSphere, up) + up * Random.Range(0.2f, 1.2f));
        }

        public static void Flash(Vector3 pos, Color color, float size = 0.35f) => Spawn(pos, color, size, size * 0.4f, 0.08f);

        /// <summary>Quick jagged spark trail between two points (lightning arcs).</summary>
        public static void Bolt(Vector3 from, Vector3 to, Color color)
        {
            const int steps = 6;
            for (int i = 0; i <= steps; i++)
            {
                var p = Vector3.Lerp(from, to, i / (float)steps);
                if (i > 0 && i < steps) p += Random.insideUnitSphere * 0.08f;
                Spawn(p, color, 0.12f, 0.04f, 0.14f);
            }
        }

        public static void Tick(float dt)
        {
            var cam = Camera.main;
            var facing = cam != null ? cam.transform.rotation : Quaternion.identity;
            for (int i = effects.Count - 1; i >= 0; i--)
            {
                var e = effects[i];
                e.Age += dt;
                if (e.Sr == null || e.Age >= e.Life)
                {
                    if (e.Sr != null) Object.Destroy(e.Sr.gameObject);
                    effects.RemoveAt(i);
                    continue;
                }
                float t = e.Age / e.Life;
                e.Sr.transform.localScale = Vector3.one * Mathf.Lerp(e.StartScale, e.EndScale, t);
                e.Sr.transform.SetPositionAndRotation(e.Sr.transform.position + e.Velocity * dt, facing);
                var c = e.Color;
                c.a *= 1f - t * t;
                e.Sr.color = c;
            }
        }

        // ---------------------------------------------------------------- lines

        public static LineRenderer NewLine(string name, Transform parent, Color color, float width)
        {
            var lr = new GameObject(name).AddComponent<LineRenderer>();
            lr.transform.SetParent(parent, false);
            if (material != null) lr.sharedMaterial = material;
            lr.useWorldSpace = true;
            lr.startWidth = lr.endWidth = width;
            lr.startColor = lr.endColor = color;
            lr.numCapVertices = 2;
            lr.enabled = false;
            return lr;
        }

        /// <summary>
        /// Draws a circle of the given radius (in cells) on the planet around a point on its surface, as a loop just
        /// above the ground at that point's height.
        /// </summary>
        public static void Ring(LineRenderer lr, Vector3 center, float radius, int segments = 64)
        {
            var up = center.normalized;
            float height = center.magnitude + 0.06f;
            var a = Vector3.Cross(up, Mathf.Abs(up.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            var b = Vector3.Cross(up, a);
            float angle = radius / Mathf.Max(0.01f, height); // radians around the planet centre
            lr.loop = true;
            lr.positionCount = segments;
            for (int i = 0; i < segments; i++)
            {
                float t = i * Mathf.PI * 2f / segments;
                var dir = up * Mathf.Cos(angle) + (a * Mathf.Cos(t) + b * Mathf.Sin(t)) * Mathf.Sin(angle);
                lr.SetPosition(i, dir * height);
            }
            lr.enabled = true;
        }
    }
}
