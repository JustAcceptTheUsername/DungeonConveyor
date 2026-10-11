using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>Runtime-generated materials and textures for the greybox art style.</summary>
    public static class Art
    {
        static Shader litShader, unlitShader, particleShader;
        static Texture2D voxelTex, beltTex, softDot;

        public static Material FloorA, FloorB, FloorC, RockSide, RockTop, RockDark, Lava, LavaCrack, Moss;
        public static Material MetalDark, Metal, MetalLight, Orange, OrangeDark, Glow, GlowHot, Black;
        public static Material CoreShell, CoreGlow, Rail, Spawn;
        public static Material[] BeltTop;
        public static Material[] Resource;     // item/ore materials, indexed by ResourceType
        public static Material EnemyBody, EnemyEye, RunnerBody, RunnerGlow, BruteBody, BruteGlow, Acid;
        public static Material Projectile, GhostOk, GhostBad, CellOk, CellBad, Additive, RangeRing;

        public static void Init()
        {
            litShader = Shader.Find("Universal Render Pipeline/Lit");
            unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
            particleShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");

            voxelTex = MakeVoxelTexture();
            beltTex = MakeBeltTexture();
            softDot = MakeSoftDot();

            FloorA = Lit(new Color(0.30f, 0.20f, 0.15f), 0.1f);
            FloorB = Lit(new Color(0.26f, 0.17f, 0.13f), 0.1f);
            FloorC = Lit(new Color(0.34f, 0.23f, 0.16f), 0.1f);
            RockSide = Lit(new Color(0.16f, 0.13f, 0.14f), 0.15f);
            RockTop = Lit(new Color(0.22f, 0.18f, 0.18f), 0.1f);
            RockDark = Lit(new Color(0.07f, 0.06f, 0.07f), 0.1f);
            Lava = Lit(new Color(1f, 0.35f, 0.05f), 0.6f, 0f, new Color(3.5f, 1.1f, 0.15f));
            LavaCrack = Lit(new Color(0.9f, 0.3f, 0.05f), 0.4f, 0f, new Color(1.8f, 0.5f, 0.05f));
            Moss = Lit(new Color(0.35f, 0.5f, 0.1f), 0.2f, 0f, new Color(0.25f, 0.45f, 0.02f));

            MetalDark = Lit(new Color(0.12f, 0.12f, 0.14f), 0.45f, 0.6f);
            Metal = Lit(new Color(0.24f, 0.24f, 0.27f), 0.5f, 0.6f);
            MetalLight = Lit(new Color(0.45f, 0.46f, 0.5f), 0.55f, 0.6f);
            Orange = Lit(new Color(0.85f, 0.45f, 0.08f), 0.4f, 0.3f);
            OrangeDark = Lit(new Color(0.5f, 0.25f, 0.05f), 0.35f, 0.3f);
            Glow = Lit(new Color(1f, 0.6f, 0.15f), 0.5f, 0f, new Color(2.2f, 1.0f, 0.2f));
            GlowHot = Lit(new Color(1f, 0.8f, 0.4f), 0.5f, 0f, new Color(5f, 2.6f, 0.6f));
            Black = Lit(new Color(0.03f, 0.03f, 0.03f), 0.2f);
            CoreShell = Lit(new Color(0.2f, 0.19f, 0.24f), 0.4f, 0.5f);
            CoreGlow = Lit(new Color(1f, 0.6f, 0.1f), 0.6f, 0f, new Color(6f, 2.8f, 0.4f));
            Rail = Lit(new Color(0.9f, 0.5f, 0.1f), 0.45f, 0.4f, new Color(0.25f, 0.1f, 0.0f));
            Spawn = Lit(new Color(0.4f, 0.1f, 0.5f), 0.3f, 0f, new Color(1.2f, 0.25f, 1.6f));

            BeltTop = new Material[3];
            for (int i = 0; i < BeltTop.Length; i++)
            {
                BeltTop[i] = Lit(Color.white, 0.3f, 0.2f, null, beltTex);
                BeltTop[i].SetTexture("_EmissionMap", beltTex);
                BeltTop[i].SetColor("_EmissionColor", new Color(0.6f, 0.35f, 0.05f));
                BeltTop[i].EnableKeyword("_EMISSION");
            }

            Resource = new[]
            {
                Lit(ResourceInfo.Colors[0], 0.8f, 0.8f, new Color(0.9f, 0.55f, 0.05f)),
                Lit(ResourceInfo.Colors[1], 0.9f, 0.1f, new Color(0.15f, 0.6f, 1.6f)),
                Lit(ResourceInfo.Colors[2], 0.9f, 0.1f, new Color(0.7f, 0.25f, 1.6f)),
                Lit(ResourceInfo.Colors[3], 0.6f, 0.9f),
            };

            EnemyBody = Lit(new Color(0.22f, 0.05f, 0.06f), 0.5f, 0.1f);
            EnemyEye = Lit(Color.red, 0.5f, 0f, new Color(4f, 0.2f, 0.1f));
            RunnerBody = Lit(new Color(0.35f, 0.32f, 0.08f), 0.5f, 0.1f);
            RunnerGlow = Lit(new Color(0.6f, 1f, 0.1f), 0.5f, 0f, new Color(1.6f, 3f, 0.2f));
            BruteBody = Lit(new Color(0.18f, 0.06f, 0.25f), 0.5f, 0.1f);
            BruteGlow = Lit(new Color(0.8f, 0.3f, 1f), 0.6f, 0f, new Color(3f, 0.8f, 4.5f));
            Acid = Lit(new Color(0.45f, 0.75f, 0.05f), 0.6f, 0f, new Color(0.5f, 1.1f, 0.05f));

            Projectile = Lit(new Color(1f, 0.8f, 0.3f), 0.5f, 0f, new Color(8f, 4f, 0.8f));
            GhostOk = Transparent(new Color(0.3f, 1f, 0.3f, 0.45f));
            GhostBad = Transparent(new Color(1f, 0.25f, 0.2f, 0.45f));
            CellOk = Transparent(new Color(0.5f, 1f, 0.2f, 0.35f));
            CellBad = Transparent(new Color(1f, 0.2f, 0.15f, 0.35f));
            RangeRing = Transparent(new Color(1f, 0.75f, 0.2f, 0.12f));
            Additive = MakeAdditive();
        }

        public static Material Lit(Color c, float smoothness = 0.3f, float metallic = 0f, Color? emission = null, Texture tex = null)
        {
            var m = new Material(litShader);
            m.SetColor("_BaseColor", c);
            m.SetTexture("_BaseMap", tex != null ? tex : voxelTex);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", metallic);
            if (emission.HasValue)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission.Value);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            m.enableInstancing = true;
            return m;
        }

        public static Material Transparent(Color c)
        {
            var m = new Material(unlitShader);
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return m;
        }

        static Material MakeAdditive()
        {
            var m = new Material(particleShader != null ? particleShader : unlitShader);
            m.SetTexture("_BaseMap", softDot);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 2f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return m;
        }

        /// <summary>16x16 noisy tile with darkened edges — gives every unit face a voxel look.</summary>
        static Texture2D MakeVoxelTexture()
        {
            const int n = 16;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat, name = "Voxel" };
            var rng = new System.Random(7);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float v = 0.82f + (float)rng.NextDouble() * 0.28f;
                    if (x == 0 || y == 0 || x == n - 1 || y == n - 1) v *= 0.7f;
                    t.SetPixel(x, y, new Color(v, v, v, 1));
                }
            t.Apply();
            return t;
        }

        /// <summary>Dark belt with orange chevrons pointing toward +V.</summary>
        static Texture2D MakeBeltTexture()
        {
            const int n = 32;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Repeat, name = "Belt" };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                    float f = Mathf.Repeat(v * 2f - Mathf.Abs(u - 0.5f) * 1.2f, 1f);
                    bool chevron = Mathf.Abs(u - 0.5f) < 0.28f && f > 0.5f && f < 0.68f;
                    bool groove = (y % 8) == 0;
                    Color c = chevron ? new Color(1f, 0.62f, 0.12f) : (groove ? new Color(0.07f, 0.07f, 0.08f) : new Color(0.13f, 0.12f, 0.13f));
                    t.SetPixel(x, y, c);
                }
            t.Apply();
            return t;
        }

        static Texture2D MakeSoftDot()
        {
            const int n = 32;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "SoftDot" };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                    float a = Mathf.Clamp01(1f - d);
                    a *= a;
                    t.SetPixel(x, y, new Color(1, 1, 1, a));
                }
            t.Apply();
            return t;
        }

        public static void ScrollBelts()
        {
            for (int i = 0; i < BeltTop.Length; i++)
            {
                var off = new Vector2(0, -Time.time * Conveyor.SpeedForLevel(i + 1));
                BeltTop[i].SetTextureOffset("_BaseMap", off);
            }
        }
    }
}
