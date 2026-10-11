using System.Collections.Generic;
using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>
    /// Short-lived combat visuals built from one procedural soft-circle sprite: explosions, muzzle flashes, sparks,
    /// flame puffs, plus line renderers for beams and range rings. Ticked by Combat.
    /// </summary>
    public static class Fx
    {
        public const int Order = short.MaxValue - 20; // above the world, below the build ghost and hover

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
            sr.sortingOrder = Order;
            sr.color = color;
            sr.transform.position = pos;
            sr.transform.localScale = new Vector3(startScale, startScale * 0.6f, 1f);
            effects.Add(new Effect { Sr = sr, Velocity = velocity, Life = life, StartScale = startScale, EndScale = endScale, Color = color });
        }

        /// <summary>Explosion covering a grid radius (drawn as the matching isometric ellipse).</summary>
        public static void Explosion(Vector3 pos, float gridRadius, Color color)
        {
            Spawn(pos, color, 0.2f, gridRadius * 2f * 0.71f, 0.35f);
            Spawn(pos, new Color(1f, 0.95f, 0.8f, 0.9f), 0.15f, gridRadius * 0.8f, 0.18f);
        }

        public static void Burst(Vector3 pos, Color color, float size)
        {
            Spawn(pos, color, size * 0.3f, size, 0.3f);
            for (int i = 0; i < 4; i++)
                Spawn(pos, color, size * 0.25f, 0.05f, 0.4f, new Vector3(Random.Range(-1f, 1f), Random.Range(0.2f, 1.2f), 0f));
        }

        public static void Flash(Vector3 pos, Color color, float size = 0.35f) => Spawn(pos, color, size, size * 0.4f, 0.08f);

        public static void Tick(float dt)
        {
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
                float s = Mathf.Lerp(e.StartScale, e.EndScale, t);
                e.Sr.transform.localScale = new Vector3(s, s * 0.6f, 1f);
                e.Sr.transform.position += e.Velocity * dt;
                var c = e.Color;
                c.a *= 1f - t * t;
                e.Sr.color = c;
            }
        }

        // ---------------------------------------------------------------- lines

        public static LineRenderer NewLine(string name, Transform parent, Color color, float width, int order = Order)
        {
            var lr = new GameObject(name).AddComponent<LineRenderer>();
            lr.transform.SetParent(parent, false);
            if (material != null) lr.sharedMaterial = material;
            lr.useWorldSpace = true;
            lr.startWidth = lr.endWidth = width;
            lr.startColor = lr.endColor = color;
            lr.sortingOrder = order;
            lr.numCapVertices = 2;
            lr.enabled = false;
            return lr;
        }

        /// <summary>Draws a circle of the given grid radius around a grid point, at an elevation, as a loop.</summary>
        public static void Ring(LineRenderer lr, IsoGrid map, Vector2 gridCenter, float gridRadius, float level, int segments = 56)
        {
            lr.loop = true;
            lr.positionCount = segments;
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                var p = gridCenter + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * gridRadius;
                lr.SetPosition(i, map.GridToWorld(p, level));
            }
            lr.enabled = true;
        }
    }
}
