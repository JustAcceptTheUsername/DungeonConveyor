using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEngine;

namespace DungeonConveyor.EditorTools
{
    /// <summary>
    /// Dungeon > Create Building Effects: builds the particle effect prefabs (Assets/Prefabs/Effects) and gives each
    /// working building's model prefab a BuildingFx with its effect points: one muzzle per gun barrel (Turret 2,
    /// Missile Launcher 4), emitter tips for the laser and flamethrower, the coil top, the drill, and so on.
    /// Never overwrites: effect prefabs that exist are kept, and model prefabs that already have a BuildingFx are left
    /// alone, so hand-tuned effects and points survive re-running it. Delete one to have it rebuilt.
    /// All sizes and speeds are in model units (1 = one cell).
    /// </summary>
    public static class BuildingEffects
    {
        const string EffectsFolder = "Assets/Prefabs/Effects";
        const string BuildingsFolder = ModelPrefabs.PrefabsRoot + "/Buildings";

        // Model axes after import: guns point along -X; Euler(0,-90,0) turns a point's +Z (its emit direction) that way.
        static readonly Vector3 Forward = new Vector3(0f, -90f, 0f);
        static readonly Vector3 Up = new Vector3(-90f, 0f, 0f);

        struct Point
        {
            public string Name;
            public bool OnHead; // position is relative to the Head node, and turns with it
            public Vector3 Pos, Euler;
            public Point(string name, bool onHead, Vector3 pos, Vector3 euler) { Name = name; OnHead = onHead; Pos = pos; Euler = euler; }
        }

        class Setup
        {
            public string Model;
            public Point[] Muzzles = new Point[0];
            public string Fire;
            public bool Detach;
            public Point? LoopAt;  // null: the loop runs at the first muzzle
            public string Loop;
            public bool Always;
        }

        // Points measured from the models' meshes (barrel tips, emitter tips, tops).
        static readonly Setup[] Setups =
        {
            new Setup
            {
                Model = "Turret", Fire = "TurretMuzzleFlash",
                Muzzles = new[]
                {
                    new Point("Muzzle_L", true, new Vector3(-1.03f, 0.13f, 0.12f), Forward),
                    new Point("Muzzle_R", true, new Vector3(-1.03f, 0.13f, -0.12f), Forward),
                },
            },
            new Setup
            {
                // Fired diagonally across the 2x2 rack.
                Model = "MissileLauncher", Fire = "MissileLaunch",
                Muzzles = new[]
                {
                    new Point("Muzzle_TopL", true, new Vector3(-0.35f, 0.41f, 0.085f), Forward),
                    new Point("Muzzle_BottomR", true, new Vector3(-0.39f, 0.28f, -0.085f), Forward),
                    new Point("Muzzle_TopR", true, new Vector3(-0.35f, 0.41f, -0.085f), Forward),
                    new Point("Muzzle_BottomL", true, new Vector3(-0.39f, 0.28f, 0.085f), Forward),
                },
            },
            new Setup
            {
                Model = "Mortar", Fire = "MortarBlast",
                Muzzles = new[] { new Point("Muzzle", true, new Vector3(-0.2f, 0.77f, 0f), new Vector3(-55f, -90f, 0f)) },
            },
            new Setup
            {
                Model = "LaserTower", Loop = "LaserEmitter",
                Muzzles = new[] { new Point("Muzzle", true, new Vector3(-0.69f, 0.14f, 0f), Forward) },
            },
            new Setup
            {
                Model = "Flamethrower", Loop = "FlameJet",
                Muzzles = new[] { new Point("Muzzle", true, new Vector3(-0.84f, 0.16f, 0f), Forward) },
            },
            new Setup
            {
                Model = "TeslaCoil", Fire = "TeslaDischarge", Loop = "TeslaCrackle", Always = true,
                Muzzles = new[] { new Point("CoilTop", false, new Vector3(0f, 1.5f, 0f), Up) },
            },
            new Setup
            {
                Model = "ShieldGenerator", Loop = "ShieldAura", Always = true,
                LoopAt = new Point("FieldEmitter", false, new Vector3(0f, 1.34f, 0f), Up),
            },
            new Setup
            {
                Model = "LandMine", Fire = "MineExplosion", Detach = true,
                Muzzles = new[] { new Point("BlastPoint", false, new Vector3(0f, 0.2f, 0f), Up) },
            },
            new Setup
            {
                Model = "Miner", Loop = "MinerDust",
                LoopAt = new Point("DrillPoint", false, new Vector3(0f, 0.05f, 0f), Up),
            },
            new Setup
            {
                Model = "MineShaft", Loop = "MineShaftSmoke",
                LoopAt = new Point("Chimney", false, new Vector3(-0.1f, 1.45f, -0.3f), Up),
            },
            new Setup
            {
                Model = "Collector", Loop = "RefinerSparkle", // the Refiner
                LoopAt = new Point("RefinerTop", false, new Vector3(0f, 1.78f, 0f), Up),
            },
            new Setup
            {
                Model = "Splitter", Fire = "SplitterPuff",
                Muzzles = new[] { new Point("Hub", false, new Vector3(0f, 0.7f, 0f), Up) },
            },
            new Setup
            {
                Model = "Core", Fire = "CoreDelivery",
                Muzzles = new[] { new Point("DeliveryPoint", false, new Vector3(0f, 2.5f, 0f), Up) },
            },
        };

        static Material additive, alpha;

        /// <summary>The shared glow material (additive, soft dot), created if missing.</summary>
        public static Material AdditiveMaterial()
        {
            EnsureFolder(EffectsFolder + "/Materials");
            var texture = SoftTexture(EffectsFolder + "/Materials/FxSoft.png");
            return ParticleMaterial(EffectsFolder + "/Materials/FxAdditive.mat", texture, true);
        }

        [MenuItem("Dungeon/Create Building Effects")]
        public static void CreateAll()
        {
            ModelPrefabs.CreateAll(); // model prefabs must exist (and the catalog point at them)
            additive = AdditiveMaterial();
            alpha = ParticleMaterial(EffectsFolder + "/Materials/FxSmoke.mat", SoftTexture(EffectsFolder + "/Materials/FxSoft.png"), false);

            var builders = new Dictionary<string, System.Action<ParticleSystem>>
            {
                { "TurretMuzzleFlash", TurretMuzzleFlash },
                { "MissileLaunch", MissileLaunch },
                { "MortarBlast", MortarBlast },
                { "LaserEmitter", LaserEmitter },
                { "FlameJet", FlameJet },
                { "TeslaDischarge", TeslaDischarge },
                { "TeslaCrackle", TeslaCrackle },
                { "ShieldAura", ShieldAura },
                { "MineExplosion", MineExplosion },
                { "MinerDust", MinerDust },
                { "MineShaftSmoke", MineShaftSmoke },
                { "RefinerSparkle", RefinerSparkle },
                { "SplitterPuff", SplitterPuff },
                { "CoreDelivery", CoreDelivery },
            };
            var effects = new Dictionary<string, ParticleSystem>();
            int made = 0;
            foreach (var pair in builders)
            {
                var path = $"{EffectsFolder}/{pair.Key}.prefab";
                var existing = AssetDatabase.LoadAssetAtPath<ParticleSystem>(path);
                if (existing == null)
                {
                    var root = new GameObject(pair.Key);
                    var ps = root.AddComponent<ParticleSystem>();
                    pair.Value(ps);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    Object.DestroyImmediate(root);
                    existing = AssetDatabase.LoadAssetAtPath<ParticleSystem>(path);
                    made++;
                }
                effects[pair.Key] = existing;
            }

            int rigged = 0;
            foreach (var s in Setups)
                if (Rig(s, effects)) rigged++;

            AssetDatabase.SaveAssets();
            Debug.Log($"Building effects: {made} effect prefabs created ({EffectsFolder}), {rigged} building prefabs given effect points.");
        }

        /// <summary>Adds the effect points and a BuildingFx to a building's model prefab, unless it has one already.</summary>
        static bool Rig(Setup s, Dictionary<string, ParticleSystem> effects)
        {
            var path = $"{BuildingsFolder}/{s.Model}.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
            {
                Debug.LogWarning($"Building effects: no model prefab at {path}.");
                return false;
            }
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (root.GetComponent<BuildingFx>() != null) return false;
                var head = BuildingModels.FindChild(root.transform, GameArt.HeadNode);

                Transform Add(Point p)
                {
                    var parent = p.OnHead && head != null ? head : root.transform;
                    var t = new GameObject(p.Name).transform;
                    t.SetParent(parent, false);
                    t.localPosition = p.Pos;
                    t.localRotation = Quaternion.Euler(p.Euler);
                    return t;
                }

                var fx = root.AddComponent<BuildingFx>();
                fx.Muzzles = new Transform[s.Muzzles.Length];
                for (int i = 0; i < s.Muzzles.Length; i++) fx.Muzzles[i] = Add(s.Muzzles[i]);
                if (s.Fire != null) fx.FireEffect = effects[s.Fire];
                fx.DetachFireEffect = s.Detach;
                if (s.Loop != null)
                {
                    fx.LoopEffect = effects[s.Loop];
                    fx.LoopPoint = s.LoopAt.HasValue ? Add(s.LoopAt.Value) : fx.Muzzles[0];
                }
                fx.LoopAlways = s.Always;
                PrefabUtility.SaveAsPrefabAsset(root, path);
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ---------------------------------------------------------------- the effects

        static readonly Color Gold = new Color(1f, 0.82f, 0.35f), Orange = new Color(1f, 0.55f, 0.15f);
        static readonly Color Smoke = new Color(0.35f, 0.32f, 0.3f, 0.45f), Electric = new Color(0.55f, 0.85f, 1f);

        static void TurretMuzzleFlash(ParticleSystem ps)
        {
            Flash(ps, Gold, 0.35f, 0.06f, 2);
            var sparks = Child(ps, "Sparks", additive);
            Burst(sparks, 0.25f, 5, (0.08f, 0.16f), (3f, 5f), (0.04f, 0.06f), Gold, true);
            Cone(sparks, 10f, 0.02f);
            Stretch(sparks, 0.04f);
            var smoke = Child(ps, "Smoke", alpha);
            Burst(smoke, 0.6f, 2, (0.3f, 0.45f), (0.3f, 0.6f), (0.12f, 0.16f), Smoke, true);
            Cone(smoke, 20f, 0.02f);
            Grow(smoke, 1f, 2.5f);
        }

        static void MissileLaunch(ParticleSystem ps)
        {
            Flash(ps, Orange, 0.3f, 0.08f, 1);
            var smoke = Child(ps, "Exhaust", alpha);
            Burst(smoke, 1f, 6, (0.5f, 0.9f), (0.3f, 1.2f), (0.12f, 0.18f), Smoke, true);
            Cone(smoke, 30f, 0.03f);
            Grow(smoke, 1f, 3f);
            var embers = Child(ps, "Embers", additive);
            Burst(embers, 0.4f, 4, (0.15f, 0.3f), (1f, 2.5f), (0.03f, 0.05f), Orange, true);
            Cone(embers, 25f, 0.02f);
        }

        static void MortarBlast(ParticleSystem ps)
        {
            Flash(ps, Gold, 0.5f, 0.1f, 2);
            var smoke = Child(ps, "Smoke", alpha);
            Burst(smoke, 1.4f, 8, (0.7f, 1.2f), (0.4f, 1.2f), (0.2f, 0.3f), Smoke, true);
            Cone(smoke, 35f, 0.05f);
            Grow(smoke, 1f, 3f);
            var sparks = Child(ps, "Sparks", additive);
            Burst(sparks, 0.5f, 8, (0.2f, 0.4f), (2f, 4f), (0.03f, 0.05f), Orange, true);
            Cone(sparks, 25f, 0.03f);
            Stretch(sparks, 0.05f);
            var main = sparks.main;
            main.gravityModifier = 0.6f;
        }

        static void LaserEmitter(ParticleSystem ps)
        {
            var red = new Color(1f, 0.3f, 0.35f);
            Loop(ps, 25f, (0.12f, 0.2f), (0f, 0f), (0.18f, 0.28f), red, false);
            NoShape(ps);
            var sparks = Child(ps, "Sparks", additive);
            Loop(sparks, 18f, (0.1f, 0.2f), (1f, 2f), (0.025f, 0.04f), new Color(1f, 0.6f, 0.6f), false);
            Cone(sparks, 45f, 0.02f);
            Stretch(sparks, 0.04f);
        }

        static void FlameJet(ParticleSystem ps)
        {
            // Reaches about 2.5 cells, the flamethrower's range.
            Loop(ps, 70f, (0.35f, 0.5f), (4.5f, 5.5f), (0.1f, 0.16f), Color.white, true);
            Cone(ps, 10f, 0.03f);
            Grow(ps, 1f, 5f);
            Colors(ps, new[] { (new Color(1f, 0.95f, 0.6f), 0f), (new Color(1f, 0.55f, 0.12f), 0.35f), (new Color(0.6f, 0.12f, 0.05f), 1f) },
                new[] { (1f, 0f), (0.9f, 0.5f), (0f, 1f) });
            var smoke = Child(ps, "Smoke", alpha);
            Loop(smoke, 10f, (0.6f, 0.9f), (2.5f, 3.5f), (0.3f, 0.4f), Smoke, true);
            Cone(smoke, 14f, 0.05f);
            Grow(smoke, 1f, 2.5f);
            FadeInOut(smoke);
        }

        static void TeslaDischarge(ParticleSystem ps)
        {
            Flash(ps, Electric, 0.6f, 0.1f, 1);
            var sparks = Child(ps, "Sparks", additive);
            Burst(sparks, 0.4f, 12, (0.12f, 0.28f), (2f, 4f), (0.03f, 0.05f), Electric, true);
            Sphere(sparks, 0.1f);
            Stretch(sparks, 0.05f);
        }

        static void TeslaCrackle(ParticleSystem ps)
        {
            Loop(ps, 7f, (0.08f, 0.18f), (0.5f, 1.5f), (0.05f, 0.12f), Electric, false);
            Sphere(ps, 0.15f);
            var glow = Child(ps, "Glow", additive);
            Loop(glow, 3f, (0.6f, 0.9f), (0f, 0f), (0.35f, 0.45f), new Color(0.4f, 0.7f, 1f, 0.35f), false);
            NoShape(glow);
            FadeInOut(glow);
        }

        static void ShieldAura(ParticleSystem ps)
        {
            var cyan = new Color(0.45f, 0.85f, 1f);
            Loop(ps, 8f, (1.5f, 2.2f), (0.1f, 0.3f), (0.06f, 0.12f), cyan, false);
            Sphere(ps, 0.35f);
            FadeInOut(ps);
            var glow = Child(ps, "Glow", additive);
            Loop(glow, 2f, (1.2f, 1.6f), (0f, 0f), (0.6f, 0.8f), new Color(0.4f, 0.8f, 1f, 0.25f), false);
            NoShape(glow);
            Grow(glow, 0.8f, 1.2f);
            FadeInOut(glow);
        }

        static void MineExplosion(ParticleSystem ps)
        {
            Flash(ps, new Color(1f, 0.9f, 0.6f), 1.6f, 0.12f, 1);
            var fire = Child(ps, "Fire", additive);
            Burst(fire, 0.6f, 14, (0.3f, 0.5f), (1f, 2.5f), (0.3f, 0.4f), Color.white, true);
            Hemisphere(fire, 0.2f);
            Grow(fire, 1f, 2.2f);
            Colors(fire, new[] { (new Color(1f, 0.9f, 0.5f), 0f), (new Color(1f, 0.45f, 0.1f), 0.4f), (new Color(0.5f, 0.1f, 0.05f), 1f) },
                new[] { (1f, 0f), (0.8f, 0.5f), (0f, 1f) });
            var smoke = Child(ps, "Smoke", alpha);
            Burst(smoke, 1.6f, 10, (0.8f, 1.3f), (0.5f, 1.2f), (0.4f, 0.5f), new Color(0.2f, 0.18f, 0.17f, 0.55f), true);
            Hemisphere(smoke, 0.3f);
            Grow(smoke, 1f, 2.5f);
            var debris = Child(ps, "Debris", alpha);
            Burst(debris, 1f, 10, (0.5f, 0.8f), (2f, 4f), (0.05f, 0.08f), new Color(0.3f, 0.22f, 0.15f), false);
            Hemisphere(debris, 0.1f);
            var main = debris.main;
            main.gravityModifier = 1f;
        }

        static void MinerDust(ParticleSystem ps)
        {
            ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = alpha;
            Loop(ps, 6f, (0.8f, 1.2f), (0.2f, 0.5f), (0.15f, 0.22f), new Color(0.55f, 0.43f, 0.32f, 0.5f), true);
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.6f;
            Grow(ps, 1f, 2.5f);
            FadeInOut(ps);
            var chips = Child(ps, "Chips", alpha);
            Loop(chips, 4f, (0.3f, 0.5f), (0.8f, 1.5f), (0.03f, 0.05f), new Color(0.35f, 0.27f, 0.2f), true);
            Cone(chips, 40f, 0.5f);
            var main = chips.main;
            main.gravityModifier = 0.8f;
        }

        static void MineShaftSmoke(ParticleSystem ps)
        {
            ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = alpha;
            Loop(ps, 4f, (1.5f, 2.2f), (0.4f, 0.7f), (0.15f, 0.2f), new Color(0.4f, 0.38f, 0.36f, 0.45f), true);
            Cone(ps, 15f, 0.05f);
            Grow(ps, 1f, 4f);
            FadeInOut(ps);
        }

        static void RefinerSparkle(ParticleSystem ps)
        {
            Loop(ps, 10f, (0.6f, 0.9f), (0.4f, 0.9f), (0.04f, 0.08f), new Color(0.6f, 0.9f, 1f), false);
            Cone(ps, 25f, 0.1f);
            FadeInOut(ps);
            var glow = Child(ps, "Glow", additive);
            Loop(glow, 3f, (0.6f, 0.9f), (0f, 0f), (0.3f, 0.4f), new Color(0.5f, 0.8f, 1f, 0.35f), false);
            NoShape(glow);
            FadeInOut(glow);
        }

        static void SplitterPuff(ParticleSystem ps)
        {
            Burst(ps, 0.5f, 4, (0.25f, 0.4f), (0.3f, 0.7f), (0.04f, 0.06f), new Color(1f, 0.85f, 0.5f), false);
            Cone(ps, 50f, 0.15f);
        }

        static void CoreDelivery(ParticleSystem ps)
        {
            Burst(ps, 0.9f, 6, (0.5f, 0.8f), (0.6f, 1.2f), (0.06f, 0.1f), Gold, false);
            Cone(ps, 35f, 0.15f);
            FadeInOut(ps);
        }

        // ---------------------------------------------------------------- particle helpers

        static ParticleSystem Child(ParticleSystem parent, string name, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
            return ps;
        }

        static void Common(ParticleSystem ps, bool loop, float duration, (float, float) life, (float, float) speed, (float, float) size, Color color, bool world)
        {
            var main = ps.main;
            main.loop = loop;
            main.duration = duration;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life.Item1, life.Item2);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed.Item1, speed.Item2);
            main.startSize = new ParticleSystem.MinMaxCurve(size.Item1, size.Item2);
            main.startColor = color;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, 2f * Mathf.PI);
            main.simulationSpace = world ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = 200;
            var r = ps.GetComponent<ParticleSystemRenderer>();
            if (r.sharedMaterial != alpha) r.sharedMaterial = additive; // smoke layers set FxSmoke first
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            Fade(ps);
        }

        static void Burst(ParticleSystem ps, float duration, int count, (float, float) life, (float, float) speed, (float, float) size, Color color, bool world)
        {
            Common(ps, false, duration, life, speed, size, color, world);
            var em = ps.emission;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
        }

        static void Loop(ParticleSystem ps, float rate, (float, float) life, (float, float) speed, (float, float) size, Color color, bool world)
        {
            Common(ps, true, 1f, life, speed, size, color, world);
            var em = ps.emission;
            em.rateOverTime = rate;
            em.SetBursts(new ParticleSystem.Burst[0]);
        }

        /// <summary>A short bright blob at the point (muzzle flashes).</summary>
        static void Flash(ParticleSystem ps, Color color, float size, float life, int count)
        {
            Burst(ps, 0.2f, count, (life, life), (0f, 0f), (size * 0.8f, size), color, false);
            NoShape(ps);
            Grow(ps, 1f, 1.4f);
        }

        static void NoShape(ParticleSystem ps)
        {
            var shape = ps.shape;
            shape.enabled = false;
        }

        static void Cone(ParticleSystem ps, float angle, float radius)
        {
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = angle;
            shape.radius = radius;
        }

        static void Sphere(ParticleSystem ps, float radius)
        {
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = radius;
        }

        static void Hemisphere(ParticleSystem ps, float radius)
        {
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = radius;
        }

        static void Stretch(ParticleSystem ps, float velocityScale)
        {
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = velocityScale;
            r.lengthScale = 1f;
        }

        static void Grow(ParticleSystem ps, float from, float to)
        {
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            float max = Mathf.Max(from, to);
            sol.size = new ParticleSystem.MinMaxCurve(max, AnimationCurve.Linear(0f, from / max, 1f, to / max));
        }

        static void Fade(ParticleSystem ps) => Colors(ps, new[] { (Color.white, 0f), (Color.white, 1f) }, new[] { (1f, 0f), (0f, 1f) });

        static void FadeInOut(ParticleSystem ps) =>
            Colors(ps, new[] { (Color.white, 0f), (Color.white, 1f) }, new[] { (0f, 0f), (1f, 0.2f), (0f, 1f) });

        static void Colors(ParticleSystem ps, (Color c, float t)[] colors, (float a, float t)[] alphas)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            var ck = new GradientColorKey[colors.Length];
            for (int i = 0; i < colors.Length; i++) ck[i] = new GradientColorKey(colors[i].c, colors[i].t);
            var ak = new GradientAlphaKey[alphas.Length];
            for (int i = 0; i < alphas.Length; i++) ak[i] = new GradientAlphaKey(alphas[i].a, alphas[i].t);
            g.SetKeys(ck, ak);
            col.color = g;
        }

        // ---------------------------------------------------------------- assets

        /// <summary>A white dot with a soft round falloff in its alpha.</summary>
        static Texture2D SoftTexture(string path)
        {
            if (!File.Exists(path))
            {
                const int n = 64;
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), Vector2.one * (n * 0.5f)) / (n * 0.5f);
                        float a = Mathf.Clamp01(1f - d);
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a * (3f - 2f * a)));
                    }
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>URP Particles/Unlit, transparent, additive (glows) or alpha-blended (smoke).</summary>
        static Material ParticleMaterial(string path, Texture2D texture, bool add)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            mat.SetTexture("_BaseMap", texture);
            mat.SetFloat("_Surface", (float)BaseShaderGUI.SurfaceType.Transparent);
            mat.SetFloat("_Blend", (float)(add ? BaseShaderGUI.BlendMode.Additive : BaseShaderGUI.BlendMode.Alpha));
            BaseShaderGUI.SetMaterialKeywords(mat, null, ParticleGUI.SetMaterialKeywords);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            var parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
