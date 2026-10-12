using System;
using System.Collections.Generic;
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
        readonly Light light;

        /// <summary>
        /// Temporary (style test D): render with the DitherBake shader, smooth-shaded normals and point-filtered
        /// sprites. See BuildingCatalog.DitherLook.
        /// </summary>
        public bool Dither;
        readonly Dictionary<Material, Material> ditherMats = new Dictionary<Material, Material>();
        readonly Dictionary<Mesh, Mesh> smoothMeshes = new Dictionary<Mesh, Mesh>();
        Shader ditherShader;

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
            light = new GameObject("Light").AddComponent<Light>();
            light.transform.SetParent(stage, false);
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50f, 10f, 0f);
            light.intensity = 1.3f;
            light.color = new Color(1f, 0.93f, 0.85f);
            light.cullingMask = 1 << Layer;
        }

        public void Dispose()
        {
            Object.Destroy(stage.gameObject);
            foreach (var m in ditherMats.Values) Object.Destroy(m);
            foreach (var kv in smoothMeshes) if (kv.Value != kv.Key) Object.Destroy(kv.Value);
        }

        /// <summary>Renders the model; returns null (with a warning) if anything goes wrong.</summary>
        public Sprite Render(GameObject prefab, float scale = 1f, float yaw = 0f, string name = null) =>
            Render(prefab, Vector3.one * scale, yaw, name);

        /// <summary>
        /// Renders the model with a per-axis scale (applied in the model's own axes, before the yaw). prepare, if
        /// given, gets the instance before rendering (e.g. to hide or turn parts); the pivot stays the model's origin.
        /// </summary>
        public Sprite Render(GameObject prefab, Vector3 scale, float yaw, string name = null, Action<GameObject> prepare = null)
        {
            if (prefab == null) return null;
            try { return RenderInternal(prefab, scale, yaw, name ?? prefab.name, prepare); }
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

        Sprite RenderInternal(GameObject prefab, Vector3 scale, float yaw, string name, Action<GameObject> prepare)
        {
            var model = Object.Instantiate(prefab, stage);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            model.transform.localScale = scale;
            foreach (var t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
            prepare?.Invoke(model);
            if (Dither) ApplyDither(model, scale.y);

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
            // The full-screen dither (style test D2) must not end up baked into sprites.
            float screenDither = Shader.GetGlobalFloat("_DitherScreenOn");
            Shader.SetGlobalFloat("_DitherScreenOn", 0f);
            cam.Render();
            Shader.SetGlobalFloat("_DitherScreenOn", screenDither);

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = name, filterMode = Dither ? FilterMode.Point : FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
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

        // ---------------------------------------------------------------- style test D (dither look)

        // DCX material name -> (palette colour, brightness, specular, glow). Mirrors dc_dither.py DMAP; the dark
        // variants are the base hue dithered toward ink.
        static readonly Dictionary<string, (string hex, float bright, float spec, float glow)> DitherPalette =
            new Dictionary<string, (string, float, float, float)>
            {
                ["SlateDark"] = ("#1B262E", 0, 0, 0), ["Slate"] = ("#2F3E49", 0, 0, 0),
                ["SlateMid"] = ("#566B79", 0, 0, 0), ["SlateLight"] = ("#8EA3B1", 0, 0, 0),
                ["Teal"] = ("#00A9B8", 0, 1, 0), ["TealDark"] = ("#00A9B8", -4, 0, 0), ["Orange"] = ("#FF8A1F", 0, 1, 0),
                ["MinP"] = ("#F2A93B", 0, 1, 0), ["MinPD"] = ("#F2A93B", -4, 0, 0), ["MinA"] = ("#4B5BD6", 0, 1, 0),
                ["LogP"] = ("#A3D93B", 0, 1, 0), ["LogPD"] = ("#A3D93B", -4, 0, 0), ["LogA"] = ("#9255E0", 0, 1, 0),
                ["Glow_Hot"] = ("#FF8A1F", 3, 0, 1), ["Glow_Red"] = ("#FF2A12", 0, 0, 1),
                ["Glow_Purple"] = ("#9255E0", 3, 0, 1), ["Glow_Blue"] = ("#49D8FF", 0, 0, 1),
                ["Glow_Gold"] = ("#F2A93B", 3, 0, 1), ["Outline"] = ("#2A1838", 0, 0, 1),
            };

        void ApplyDither(GameObject model, float scale)
        {
            if (ditherShader == null) ditherShader = Shader.Find("DungeonConveyor/DitherBake");
            if (ditherShader == null) throw new Exception("DitherBake shader not found");
            Shader.SetGlobalVector("_DitherLightDir", -light.transform.forward);
            Shader.SetGlobalVector("_DitherViewDir", -(ViewRotation * Vector3.forward));
            Shader.SetGlobalFloat("_DitherGroundY", model.transform.position.y);
            Shader.SetGlobalFloat("_DitherScale", scale);
            foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null) mf.sharedMesh = Smooth(mf.sharedMesh);
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) if (mats[i] != null) mats[i] = DitherMaterial(mats[i]);
                r.sharedMaterials = mats;
            }
        }

        Material DitherMaterial(Material src)
        {
            if (ditherMats.TryGetValue(src, out var m)) return m;
            string key = src.name.Replace(" (Instance)", "");
            if (key.StartsWith("DCX_")) key = key.Substring(4);
            m = new Material(ditherShader) { name = "Dither_" + key };
            if (DitherPalette.TryGetValue(key, out var p) && ColorUtility.TryParseHtmlString(p.hex, out var c))
            {
                m.SetColor("_Color", c); m.SetFloat("_Bright", p.bright); m.SetFloat("_Spec", p.spec); m.SetFloat("_Glow", p.glow);
            }
            else // models not reworked yet: keep their base colour, no specular
                m.SetColor("_Color", src.HasProperty("baseColorFactor") ? src.GetColor("baseColorFactor")
                                    : src.HasProperty("_BaseColor") ? src.GetColor("_BaseColor") : src.color);
            ditherMats[src] = m;
            return m;
        }

        /// <summary>
        /// Copy of a (flat-shaded) mesh whose normals are averaged across edges sharper than 40 degrees, so light rolls
        /// off round parts (barrels, cones) while box edges stay hard. Same as smooth_normals() in dc_dither.py.
        /// </summary>
        Mesh Smooth(Mesh src)
        {
            if (smoothMeshes.TryGetValue(src, out var dst)) return dst;
            if (!src.isReadable) return smoothMeshes[src] = src; // can't read it: keep the imported normals
            dst = Object.Instantiate(src);
            var verts = src.vertices; var normals = src.normals; var tris = src.triangles;
            var faceSum = new Dictionary<Vector3Int, List<Vector3>>();
            Vector3Int Key(Vector3 v) => Vector3Int.RoundToInt(v * 10000f);
            for (int t = 0; t < tris.Length; t += 3)
            {
                Vector3 a = verts[tris[t]], b = verts[tris[t + 1]], c = verts[tris[t + 2]];
                var fn = Vector3.Cross(b - a, c - a); // length = 2 * area, so bigger faces weigh more
                for (int k = 0; k < 3; k++)
                {
                    var key = Key(verts[tris[t + k]]);
                    if (!faceSum.TryGetValue(key, out var list)) faceSum[key] = list = new List<Vector3>();
                    list.Add(fn);
                }
            }
            float cos = Mathf.Cos(40f * Mathf.Deg2Rad);
            var result = new Vector3[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                var own = normals.Length == verts.Length ? normals[i] : Vector3.up;
                var sum = Vector3.zero;
                if (faceSum.TryGetValue(Key(verts[i]), out var faces))
                    foreach (var fn in faces)
                        if (Vector3.Dot(fn.normalized, own) >= cos) sum += fn;
                result[i] = sum.sqrMagnitude > 1e-12f ? sum.normalized : own;
            }
            dst.normals = result;
            smoothMeshes[src] = dst;
            return dst;
        }
    }
}
