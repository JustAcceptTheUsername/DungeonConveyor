using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace DungeonConveyor.EditorTools
{
    /// <summary>
    /// Dungeon > Setup Post Processing: the URP volume profile for the game's look (warm glowing highlights, bloom on
    /// fire and crystals, cool purple shadows, dark vignette, like the reference mockup), applied to the game and menu
    /// scenes' global volume and cameras.
    /// Dungeon > Create Main Menu Scene: Assets/Scenes/MainMenu.unity (title, Play / Settings / Quit, drifting embers),
    /// first in the build list so the game starts there.
    /// </summary>
    public static class SceneSetup
    {
        public const string GameScene = "Assets/Scenes/GameScene.unity";
        public const string MenuScene = "Assets/Scenes/MainMenu.unity";
        const string ProfilePath = "Assets/Settings/DungeonPostFX.asset";

        static readonly Color Background = new Color(0.035f, 0.022f, 0.03f);

        [MenuItem("Dungeon/Setup Post Processing")]
        public static void SetupPostProcessing()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var profile = Profile();
            foreach (var path in new[] { GameScene, MenuScene })
                if (System.IO.File.Exists(path)) EditScene(path, scene => ApplyPost(scene, profile));
            Debug.Log($"Post processing: {ProfilePath} applied to the game and menu scenes.");
        }

        [MenuItem("Dungeon/Create Main Menu Scene")]
        public static void CreateMainMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (System.IO.File.Exists(MenuScene) &&
                !EditorUtility.DisplayDialog("Main menu", $"{MenuScene} exists. Rebuild it from scratch?", "Rebuild", "Cancel"))
                return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var objects = new List<GameObject>();

            var cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
            cam.transform.position = new Vector3(0f, 0f, -10f);
            cam.orthographic = true;
            cam.orthographicSize = 5f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Background;
            cam.gameObject.AddComponent<AudioListener>();
            objects.Add(cam.gameObject);

            objects.Add(new GameObject("Global Volume", typeof(Volume)));
            objects.Add(new GameObject("Main Menu", typeof(MainMenu)));
            objects.Add(Embers());
            objects.Add(GroundGlow());
            foreach (var go in objects) SceneManager.MoveGameObjectToScene(go, scene);

            ApplyPost(scene, Profile());
            EditorSceneManager.SaveScene(scene, MenuScene);
            EditorSceneManager.CloseScene(scene, true);

            // Menu first, then the game, then anything else already listed.
            var list = EditorBuildSettings.scenes.Where(s => s.path != MenuScene && s.path != GameScene).ToList();
            list.Insert(0, new EditorBuildSettingsScene(GameScene, true));
            list.Insert(0, new EditorBuildSettingsScene(MenuScene, true));
            EditorBuildSettings.scenes = list.ToArray();
            Debug.Log($"Main menu: {MenuScene} created and set as the first scene in the build.");
        }

        // ---------------------------------------------------------------- post processing

        /// <summary>The profile, created on first use. Tune it in the Inspector afterwards; it is never overwritten.</summary>
        static VolumeProfile Profile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile != null) return profile;
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);

            // Fire, lava and crystals glow; the bloom is tinted warm like the mockup's light.
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.value = 0.85f;
            bloom.intensity.value = 1.1f;
            bloom.scatter.value = 0.7f;
            bloom.tint.value = new Color(1f, 0.86f, 0.72f);
            bloom.highQualityFiltering.value = true;

            var tone = profile.Add<Tonemapping>(true);
            tone.mode.value = TonemappingMode.Neutral;

            // Rich, slightly warm colour with punchy contrast.
            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.value = 0.1f;
            color.contrast.value = 15f;
            color.saturation.value = 15f;
            color.colorFilter.value = new Color(1f, 0.96f, 0.92f);
            color.hueShift.overrideState = false;

            var white = profile.Add<WhiteBalance>(true);
            white.temperature.value = 8f;
            white.tint.value = 0f;

            // Purple-blue in the shadows, orange in the highlights.
            var smh = profile.Add<ShadowsMidtonesHighlights>(true);
            smh.shadows.value = new Vector4(0.98f, 0.92f, 1.12f, -0.04f);
            smh.midtones.value = new Vector4(1f, 1f, 1f, 0f);
            smh.highlights.value = new Vector4(1.08f, 1f, 0.9f, 0f);

            // Dark cave edges.
            var vignette = profile.Add<Vignette>(true);
            vignette.color.value = new Color(0.05f, 0.02f, 0.04f);
            vignette.center.value = new Vector2(0.5f, 0.5f);
            vignette.intensity.value = 0.38f;
            vignette.smoothness.value = 0.45f;
            vignette.rounded.value = false;

            foreach (var c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        /// <summary>
        /// Points the scene's global volume (made if missing) at the profile and turns on HDR and post processing on its
        /// cameras. Post Processing v2 components (that package doesn't run under URP) are switched off.
        /// </summary>
        static void ApplyPost(Scene scene, VolumeProfile profile)
        {
            var roots = scene.GetRootGameObjects();
            var volumes = roots.SelectMany(r => r.GetComponentsInChildren<Volume>(true)).ToList();
            if (volumes.Count == 0)
            {
                var go = new GameObject("Global Volume", typeof(Volume));
                SceneManager.MoveGameObjectToScene(go, scene);
                volumes.Add(go.GetComponent<Volume>());
            }
            foreach (var v in volumes)
            {
                if (!v.isGlobal) continue;
                v.sharedProfile = profile;
                v.weight = 1f;
                EditorUtility.SetDirty(v);
            }
            if (!volumes.Any(v => v.isGlobal))
            {
                volumes[0].isGlobal = true;
                volumes[0].sharedProfile = profile;
            }

            foreach (var cam in roots.SelectMany(r => r.GetComponentsInChildren<Camera>(true)))
            {
                cam.allowHDR = true;
                var data = cam.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = true;
                EditorUtility.SetDirty(cam);
                EditorUtility.SetDirty(data);
            }

            foreach (var mb in roots.SelectMany(r => r.GetComponentsInChildren<MonoBehaviour>(true)))
                if (mb != null && mb.GetType().Namespace == "UnityEngine.Rendering.PostProcessing" && mb.enabled)
                {
                    mb.enabled = false;
                    EditorUtility.SetDirty(mb);
                }
        }

        static void EditScene(string path, Action<Scene> edit)
        {
            var scene = SceneManager.GetSceneByPath(path);
            bool wasOpen = scene.isLoaded;
            if (!wasOpen) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            edit(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            if (!wasOpen) EditorSceneManager.CloseScene(scene, true);
        }

        // ---------------------------------------------------------------- menu backdrop

        /// <summary>Glowing embers drifting up from below the screen.</summary>
        static GameObject Embers()
        {
            var go = new GameObject("Embers");
            go.transform.SetPositionAndRotation(new Vector3(0f, -6f, 0f), Quaternion.Euler(-90f, 0f, 0f)); // emits up
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.prewarm = true;
            main.duration = 5f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 10f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.1f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.13f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(2.2f, 0.9f, 0.25f), new Color(2.4f, 0.45f, 0.1f));
            main.maxParticles = 400;
            var em = ps.emission;
            em.rateOverTime = 22f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(22f, 0.1f, 0.5f);
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.35f;
            noise.frequency = 0.35f;
            noise.scrollSpeed = 0.2f;
            FadeInOut(ps);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = BuildingEffects.AdditiveMaterial();
            return go;
        }

        /// <summary>A dim warm glow along the bottom of the screen, as if from lava below.</summary>
        static GameObject GroundGlow()
        {
            var go = new GameObject("Ground Glow");
            go.transform.position = new Vector3(0f, -6.5f, 1f);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.prewarm = true;
            main.duration = 4f;
            main.startLifetime = 6f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(7f, 10f);
            main.startColor = new Color(1f, 0.35f, 0.08f, 0.16f);
            var em = ps.emission;
            em.rateOverTime = 1.2f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(18f, 0.5f, 0.1f);
            FadeInOut(ps);
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = BuildingEffects.AdditiveMaterial();
            return go;
        }

        static void FadeInOut(ParticleSystem ps)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
        }
    }
}
