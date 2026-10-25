using System.Collections.Generic;
using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>
    /// Points out the meteor landing sites: a red arrow bobbing above each one, pointing down at it, and, while a
    /// site is off screen or round the back of the planet, an arrow at the screen edge pointing the way to it.
    /// </summary>
    [RequireComponent(typeof(IsoGrid))]
    public class SpawnIndicators : MonoBehaviour
    {
        public Color Color = new Color(1f, 0.3f, 0.18f, 0.95f);
        [Tooltip("Height of the arrow above the landing site, in cells.")]
        public float Height = 2.2f;
        [Tooltip("Size of the screen-edge arrows, in pixels.")]
        public float EdgeSize = 46f;
        [Tooltip("Gap between the screen-edge arrows and the screen edge, in pixels.")]
        public float EdgeMargin = 40f;

        IsoGrid map;
        BuildController build;
        readonly List<SpriteRenderer> markers = new List<SpriteRenderer>();
        Texture2D edgeArrow;
        Sprite sprite;
        Material material;

        void Start()
        {
            map = GetComponent<IsoGrid>();
            build = GetComponent<BuildController>();
            material = GetComponent<MapView>().SpriteMaterial;
            sprite = MakeMarker();
            edgeArrow = MakeEdgeArrow();
        }

        /// <summary>One marker per landing site (sites can be added mid-game).</summary>
        void EnsureMarkers()
        {
            while (markers.Count < map.Spawns.Count)
            {
                var sr = new GameObject("SpawnMarker").AddComponent<SpriteRenderer>();
                sr.transform.SetParent(transform, false);
                if (material != null) sr.sharedMaterial = material;
                sr.sprite = sprite;
                sr.color = Color;
                markers.Add(sr);
            }
        }

        bool Hidden => build != null && build.Core != null && build.Core.Destroyed;

        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null) return;
            EnsureMarkers();
            float bob = Mathf.Sin(Time.time * 3f) * 0.25f;
            for (int i = 0; i < markers.Count; i++)
            {
                var site = map.Spawns[i].Landing;
                var up = map.Up(site);
                var t = markers[i].transform;
                markers[i].enabled = !Hidden;
                t.position = map.SurfaceCenter(site) + up * (Height + bob);
                // Stand upright on the planet (+y away from it) and face the camera as far as that allows.
                var facing = Vector3.ProjectOnPlane(cam.transform.forward, up);
                if (facing.sqrMagnitude < 1e-4f) facing = Vector3.ProjectOnPlane(cam.transform.up, up);
                t.rotation = Quaternion.LookRotation(facing, up);
                t.localScale = Vector3.one * Mathf.Lerp(1f, 2.5f, Mathf.InverseLerp(map.Radius * 1.5f, map.Radius * 6f, cam.transform.position.magnitude));
            }
        }

        void OnGUI()
        {
            var cam = Camera.main;
            if (cam == null || Hidden || Event.current.type != EventType.Repaint) return;
            var center = new Vector2(Screen.width, Screen.height) * 0.5f;
            foreach (var site in map.Spawns)
            {
                var p = map.SurfaceCenter(site.Landing);
                var vp = cam.WorldToViewportPoint(p);
                bool facing = Vector3.Dot(map.Up(site.Landing), cam.transform.position - p) > 0f;
                bool onScreen = vp.z > 0f && vp.x > 0.03f && vp.x < 0.97f && vp.y > 0.03f && vp.y < 0.97f;
                if (facing && onScreen) continue;

                // Which way to turn: the site's position across the view, even when it is behind the planet.
                var local = cam.transform.InverseTransformPoint(p);
                var dir = new Vector2(local.x, -local.y); // GUI y runs down
                if (dir.sqrMagnitude < 1e-6f) dir = Vector2.down;
                dir.Normalize();
                float sx = (center.x - EdgeMargin) / Mathf.Max(1e-4f, Mathf.Abs(dir.x));
                float sy = (center.y - EdgeMargin) / Mathf.Max(1e-4f, Mathf.Abs(dir.y));
                var at = center + dir * Mathf.Min(sx, sy);

                float pulse = 1f + 0.12f * Mathf.Sin(Time.unscaledTime * 5f);
                float size = EdgeSize * pulse;
                var matrix = GUI.matrix;
                GUIUtility.RotateAroundPivot(Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg, at);
                var old = GUI.color;
                GUI.color = Color;
                GUI.DrawTexture(new Rect(at.x - size * 0.5f, at.y - size * 0.5f, size, size), edgeArrow);
                GUI.color = old;
                GUI.matrix = matrix;
            }
        }

        /// <summary>A downward arrow (shaft and head), white so it can be tinted, pivot at its tip.</summary>
        static Sprite MakeMarker()
        {
            const int w = 48, h = 72;
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float cx = Mathf.Abs(x + 0.5f - w * 0.5f);
                    bool head = y < 30 && cx < (y + 0.5f) * 0.8f;        // triangle, tip at the bottom
                    bool shaft = y >= 28 && cx < 8f;
                    float edge = head ? (y + 0.5f) * 0.8f - cx : shaft ? 8f - cx : 0f;
                    px[y * w + x] = head || shaft ? new Color(1f, 1f, 1f, Mathf.Clamp01(edge)) : Color.clear;
                }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0f), 48f);
        }

        /// <summary>A right-pointing triangle for the screen edge.</summary>
        static Texture2D MakeEdgeArrow()
        {
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float fx = (x + 0.5f) / n, fy = Mathf.Abs((y + 0.5f) / n - 0.5f) * 2f;
                    float inside = (1f - fx) - fy * 0.9f;              // tip at the right
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(inside * n * 0.5f) * (fx > 0.08f ? 1f : 0f)));
                }
            tex.Apply();
            return tex;
        }
    }
}
