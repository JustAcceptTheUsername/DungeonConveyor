using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace DungeonConveyor
{
    /// <summary>
    /// Renders 3D models into transparent sprites seen from the same 2:1 isometric angle as the tiles, so they sort
    /// with the 2D terrain. Scale: 1 model unit = 1 cell. Model +X lies along grid +x (up-right), model +Z along grid
    /// +y (up-left). The sprite pivot is the model's origin. Use as: using (var r = new ModelArt(parent)) r.Render(...).
    /// </summary>
    public sealed class ModelArt : IDisposable
    {
        const int Layer = 31;
        const float PixelsPerCameraUnit = 160f;
        static readonly Quaternion ViewRotation = Quaternion.Euler(30f, 45f, 0f);
        // A ground square 1 unit wide projects to a diamond √2 camera units wide; on the grid that diamond is 1 world unit.
        static readonly float PixelsPerWorldUnit = PixelsPerCameraUnit * Mathf.Sqrt(2f);

        readonly Transform stage;
        readonly Camera cam;

        public ModelArt(Transform parent)
        {
            stage = new GameObject("ModelArtStage").transform;
            stage.SetParent(parent, false);
            stage.position = new Vector3(5000f, 5000f, 0f); // far from the map so the main camera never sees it

            cam = new GameObject("Camera").AddComponent<Camera>();
            cam.transform.SetParent(stage, false);
            cam.enabled = false;
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.clear;
            cam.cullingMask = 1 << Layer;
            cam.allowHDR = false;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 100f;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.renderShadows = false;

            // Key light from the upper left so left faces read brighter, matching the terrain shading.
            var light = new GameObject("Light").AddComponent<Light>();
            light.transform.SetParent(stage, false);
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50f, 10f, 0f);
            light.intensity = 1.3f;
            light.color = new Color(1f, 0.93f, 0.85f);
            light.cullingMask = 1 << Layer;
        }

        public void Dispose() => Object.Destroy(stage.gameObject);

        /// <summary>Renders the model; returns null (with a warning) if anything goes wrong.</summary>
        public Sprite Render(GameObject prefab, float scale = 1f, float yaw = 0f, string name = null) =>
            Render(prefab, Vector3.one * scale, yaw, name);

        /// <summary>Renders the model with a per-axis scale (applied in the model's own axes, before the yaw).</summary>
        public Sprite Render(GameObject prefab, Vector3 scale, float yaw, string name = null)
        {
            if (prefab == null) return null;
            try { return RenderInternal(prefab, scale, yaw, name ?? prefab.name); }
            catch (Exception e)
            {
                Debug.LogWarning($"Model render failed for {prefab.name}: {e.Message}");
                return null;
            }
        }

        /// <summary>Size of the model's renderer bounds at scale 1.</summary>
        public Vector3 MeasureSize(GameObject prefab)
        {
            if (prefab == null) return Vector3.one;
            var model = Object.Instantiate(prefab, stage);
            model.transform.localPosition = Vector3.zero;
            var bounds = new Bounds(model.transform.position, Vector3.zero);
            foreach (var r in model.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
            Object.DestroyImmediate(model);
            return bounds.size;
        }

        Sprite RenderInternal(GameObject prefab, Vector3 scale, float yaw, string name)
        {
            var model = Object.Instantiate(prefab, stage);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            model.transform.localScale = scale;
            foreach (var t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;

            var bounds = new Bounds(model.transform.position, Vector3.zero);
            foreach (var r in model.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);

            // Tight rectangle of the bounds as seen by the camera.
            var inv = Quaternion.Inverse(ViewRotation);
            Vector2 min = Vector2.positiveInfinity, max = Vector2.negativeInfinity;
            for (int i = 0; i < 8; i++)
            {
                var corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3(i & 1, (i >> 1) & 1, (i >> 2) & 1) * 2f - Vector3.one);
                Vector2 p = inv * (corner - bounds.center);
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }
            const float pad = 0.04f;
            min -= Vector2.one * pad;
            max += Vector2.one * pad;
            int w = Mathf.Max(4, Mathf.CeilToInt((max.x - min.x) * PixelsPerCameraUnit));
            int h = Mathf.Max(4, Mathf.CeilToInt((max.y - min.y) * PixelsPerCameraUnit));
            var mid = (min + max) * 0.5f;

            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default, 4);
            cam.targetTexture = rt;
            cam.aspect = (float)w / h;
            cam.orthographicSize = h / PixelsPerCameraUnit * 0.5f;
            cam.transform.rotation = ViewRotation;
            cam.transform.position = bounds.center + ViewRotation * new Vector3(mid.x, mid.y, -50f);
            cam.Render();

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = name, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply(false, false); // stays readable for pixel-accurate clicking (right-click delete)
            RenderTexture.active = prev;

            // Pivot = the model's origin, i.e. the centre of its footprint on the ground.
            Vector2 pivot = cam.WorldToViewportPoint(model.transform.position);
            cam.targetTexture = null;
            RenderTexture.ReleaseTemporary(rt);
            Object.DestroyImmediate(model);

            var sprite = Sprite.Create(tex, new Rect(0, 0, w, h), pivot, PixelsPerWorldUnit, 0, SpriteMeshType.FullRect);
            sprite.name = name;
            return sprite;
        }
    }
}
