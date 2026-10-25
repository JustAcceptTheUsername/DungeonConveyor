using System.Collections.Generic;
using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>
    /// The readability and feedback layer over one enemy's model, built for any model (whole-model level, every mesh
    /// under it): a dark ink outline, a hit flash, status tints and glowing cracks drawn on the body itself
    /// (Resources/Shaders/EnemyLook, on a copy of each mesh so the model's own materials stay as imported), a soft
    /// contact shadow on the ground, and rock chips when hit. Owned and ticked by Enemy; off when
    /// EnemyCatalog.EnemyFeedback is.
    /// </summary>
    public class EnemyLook
    {
        /// <summary>Glow of hurt and hostile enemies (cracks, low-health pulse); the future enemy glow colour.</summary>
        public static readonly Color HostileGlow = new Color32(0xFF, 0x2E, 0x63, 0xFF);
        static readonly Color ShadowColor = new Color(0.07f, 0.04f, 0.09f, 1f);

        static readonly int InkColorId = Shader.PropertyToID("_InkColor"), InkWidthId = Shader.PropertyToID("_InkWidth"),
            InkMinPixelsId = Shader.PropertyToID("_InkMinPixels"), UseSmoothId = Shader.PropertyToID("_UseSmooth"),
            FlashId = Shader.PropertyToID("_Flash"), TintId = Shader.PropertyToID("_Tint"), CrackId = Shader.PropertyToID("_Crack"),
            CrackColorId = Shader.PropertyToID("_CrackColor"), CrackScaleId = Shader.PropertyToID("_CrackScale"),
            GlowId = Shader.PropertyToID("_Glow"), FadeId = Shader.PropertyToID("_Fade"),
            SunDirId = Shader.PropertyToID("_PlanetSunDir"); // set by CameraController
        const string OverlayPass = "UniversalForward";

        // Per source mesh: a single-submesh copy with normals averaged by position in UV3 (the models' verts are split,
        // so a hull pushed along their own normals would crack open). Null if the mesh can't be read.
        static readonly Dictionary<Mesh, Mesh> hulls = new Dictionary<Mesh, Mesh>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => hulls.Clear();

        static Sprite blob;
        /// <summary>Soft dark disc for contact shadows: solid in the middle, fading out over the outer half.</summary>
        static Sprite Blob
        {
            get
            {
                if (blob != null) return blob;
                const int n = 64;
                var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                        t.SetPixel(x, y, new Color(1f, 1f, 1f, 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1f, d))));
                    }
                t.Apply();
                return blob = Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), n);
            }
        }

        public static EnemyCatalog.FeedbackSettings Settings =>
            EnemyCatalog.Instance != null ? EnemyCatalog.Instance.Feedback : DefaultSettings;
        static readonly EnemyCatalog.FeedbackSettings DefaultSettings = new EnemyCatalog.FeedbackSettings();

        /// <summary>Feedback switched on (EnemyCatalog.EnemyFeedback).</summary>
        public static bool On => EnemyCatalog.Instance == null || EnemyCatalog.Instance.EnemyFeedback;

        readonly EnemyCatalog.FeedbackSettings cfg;
        readonly Material material;
        readonly SpriteRenderer shadow;
        readonly Vector2 footprint;
        float flashUntil = -1f, chipsReady;
        // Last values sent to the material, so unchanged ones aren't set again.
        float shownFlash = -1f, shownCrack = -1f, shownGlow = -1f, shownFade = -1f;
        Color shownTint = new Color(-1f, 0f, 0f, 0f);
        bool overlayOn = true;

        public float DeathSeconds => Mathf.Max(0.05f, cfg.DeathSeconds);

        /// <summary>Adds the layer to every mesh under model; the shadow goes under root. footprint: model size across x/z.</summary>
        public EnemyLook(GameObject model, Transform root, Material spriteMaterial, Vector2 footprint)
        {
            cfg = Settings;
            this.footprint = footprint;
            material = PlanetArt.NewMaterial("EnemyLook");
            material.SetColor(InkColorId, cfg.InkColor);
            material.SetFloat(InkWidthId, cfg.InkWidth);
            material.SetFloat(InkMinPixelsId, cfg.InkMinPixels);
            material.SetColor(CrackColorId, HostileGlow);
            material.SetFloat(CrackScaleId, cfg.CrackScale);

            bool smooth = true;
            foreach (var mf in model.GetComponentsInChildren<MeshFilter>())
            {
                var source = mf.sharedMesh;
                var r = mf.GetComponent<MeshRenderer>();
                if (source == null || r == null) continue;
                var hull = Hull(source);
                var go = new GameObject("Look");
                go.transform.SetParent(mf.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = hull != null ? hull : source;
                var lr = go.AddComponent<MeshRenderer>();
                var mats = new Material[hull != null ? 1 : source.subMeshCount];
                for (int i = 0; i < mats.Length; i++) mats[i] = material;
                lr.sharedMaterials = mats;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                lr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                if (hull == null) smooth = false;
            }
            material.SetFloat(UseSmoothId, smooth ? 1f : 0f);

            shadow = new GameObject("Shadow").AddComponent<SpriteRenderer>();
            shadow.transform.SetParent(root, false);
            if (spriteMaterial != null) shadow.sharedMaterial = spriteMaterial;
            shadow.sprite = Blob;
            shadow.enabled = false;
            SetOverlay(0f, Color.clear, 0f, 0f, 1f);
        }

        public void Dispose()
        {
            if (material != null) Object.Destroy(material);
        }

        static Mesh Hull(Mesh source)
        {
            if (hulls.TryGetValue(source, out var hull)) return hull;
            if (!source.isReadable) return hulls[source] = null;

            var verts = source.vertices;
            var normals = source.normals;
            if (normals.Length != verts.Length)
            {
                var copy = Object.Instantiate(source);
                copy.RecalculateNormals();
                normals = copy.normals;
                Object.Destroy(copy);
            }
            var sums = new Dictionary<Vector3Int, Vector3>(verts.Length);
            var keys = new Vector3Int[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                var k = keys[i] = Vector3Int.RoundToInt(verts[i] * 10000f);
                sums.TryGetValue(k, out var s);
                sums[k] = s + normals[i];
            }
            var smooth = new Vector3[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                var s = sums[keys[i]];
                smooth[i] = s.sqrMagnitude > 1e-8f ? s.normalized : normals[i];
            }
            var tris = new List<int>();
            for (int s = 0; s < source.subMeshCount; s++)
                if (source.GetTopology(s) == MeshTopology.Triangles) tris.AddRange(source.GetTriangles(s));

            hull = new Mesh
            {
                name = source.name + " (look)",
                indexFormat = verts.Length > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16,
            };
            hull.vertices = verts;
            hull.normals = normals;
            hull.SetUVs(3, smooth);
            hull.SetTriangles(tris, 0);
            var b = source.bounds;
            b.Expand(0.1f); // room for the outline
            hull.bounds = b;
            hull.UploadMeshData(true);
            return hulls[source] = hull;
        }

        /// <summary>A hit: a short white flash and a few rock chips (both throttled, so damage every frame strobes).</summary>
        public void Hit(Vector3 at, float size)
        {
            float now = Time.time;
            if (now > flashUntil + cfg.FlashSeconds) flashUntil = now + cfg.FlashSeconds;
            if (now >= chipsReady && cfg.ChipsPerHit > 0)
            {
                chipsReady = now + 0.12f;
                Chips(at, cfg.ChipsPerHit, size);
            }
        }

        /// <summary>Small grey stone chips flying off a point on the planet.</summary>
        public static void Chips(Vector3 at, int count, float size, float speed = 1f)
        {
            var up = at.normalized;
            for (int i = 0; i < count; i++)
            {
                float g = Random.Range(0.32f, 0.62f);
                var velocity = (Vector3.ProjectOnPlane(Random.insideUnitSphere, up) * 1.6f + up * Random.Range(0.4f, 1.5f)) * speed;
                Fx.Spawn(at + Random.insideUnitSphere * 0.08f * size, new Color(g, g * 0.95f, g * 0.9f, 1f), 0.16f * size, 0.06f * size,
                    Random.Range(0.25f, 0.45f), velocity);
            }
        }

        /// <summary>
        /// Every frame while it walks: flash, status tint, cracks by health, and the shadow under it.
        /// ground: its point on the ground; air: how high its body floats (bigger, fainter shadow); size: body scale.
        /// </summary>
        public void Tick(float health01, Color tint, Vector3 ground, Vector3 up, Vector3 facing, float size, float air)
        {
            float now = Time.time;
            float flash = cfg.FlashSeconds > 0f ? Mathf.Clamp01((flashUntil - now) / cfg.FlashSeconds) * 0.9f : 0f;
            float crack = 0f, glow = 0f;
            if (health01 < cfg.CrackAt.y)
            {
                crack = 0.85f;
                glow = 0.45f + 0.25f * Mathf.Sin(now * 9f);
            }
            else if (health01 < cfg.CrackAt.x)
            {
                crack = 0.45f;
                glow = 0.15f;
            }
            // The status colours are mixed down from Color.clear (darker as they fade); on the body only the hue and
            // the strength count.
            float bright = Mathf.Max(tint.r, Mathf.Max(tint.g, tint.b));
            if (bright > 0.001f) { tint.r /= bright; tint.g /= bright; tint.b /= bright; }
            tint.a = Mathf.Clamp01(tint.a * cfg.StatusStrength);
            SetOverlay(flash, tint, crack, glow, 1f);
            PlaceShadow(ground, up, facing, size, air, 1f);
        }

        /// <summary>The crumbling death, k from 0 to 1: cracks flare and fade, the shadow goes.</summary>
        public void Crumble(float k, Vector3 ground, Vector3 up, Vector3 facing, float size)
        {
            SetOverlay(0f, Color.clear, 1f, 1f - k, 1f - k * k);
            PlaceShadow(ground, up, facing, size, 0f, 1f - k);
        }

        void PlaceShadow(Vector3 ground, Vector3 up, Vector3 facing, float size, float air, float fade)
        {
            bool flier = air > 0.05f;
            float grow = cfg.ShadowSize * size * (flier ? 1.35f : 1f);
            // Seen from above the body hides what is right under it, so the shadow leans away from the sun a little
            // (further for fliers) and peeks out on one side.
            var away = -Vector3.ProjectOnPlane(Shader.GetGlobalVector(SunDirId), up);
            var offset = away * (cfg.ShadowOffset * size + air * 0.6f);
            shadow.transform.SetPositionAndRotation(ground + offset + up * (ConveyorArt.SurfaceHeight + 0.02f),
                Quaternion.LookRotation(-up, facing.sqrMagnitude > 1e-6f ? facing : Vector3.ProjectOnPlane(Vector3.forward, up)));
            shadow.transform.localScale = new Vector3(footprint.x * grow, footprint.y * grow, 1f);
            var c = ShadowColor;
            c.a = cfg.ShadowAlpha * (flier ? 0.55f : 1f) * fade;
            shadow.color = c;
            shadow.enabled = c.a > 0.005f;
        }

        void SetOverlay(float flash, Color tint, float crack, float glow, float fade)
        {
            if (flash != shownFlash) material.SetFloat(FlashId, shownFlash = flash);
            if (tint != shownTint) material.SetColor(TintId, shownTint = tint);
            if (crack != shownCrack) material.SetFloat(CrackId, shownCrack = crack);
            if (glow != shownGlow) material.SetFloat(GlowId, shownGlow = glow);
            if (fade != shownFade) material.SetFloat(FadeId, shownFade = fade);
            // Nothing to show: skip the overlay draw (the ink still draws).
            bool on = flash > 0f || tint.a > 0f || crack > 0f || glow > 0f;
            if (on != overlayOn) material.SetShaderPassEnabled(OverlayPass, overlayOn = on);
        }
    }
}
