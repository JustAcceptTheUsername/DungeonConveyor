using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DungeonConveyor
{
    /// <summary>
    /// Builds the whole game from code when Play is pressed — no scene setup needed.
    /// Everything spawned lives under the "Game" root so restarting is just destroy + rebuild.
    /// </summary>
    public class GameBootstrap : MonoBehaviour
    {
        public static int Seed = 1337;
        static bool artReady;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Game.Reset();
            artReady = false;
            Seed = 1337;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoStart()
        {
            if (FindAnyObjectByType<GameBootstrap>() == null)
                new GameObject("Game").AddComponent<GameBootstrap>();
        }

        public static void Restart(bool newMap)
        {
            if (newMap) Seed = Random.Range(1, 100000);
            var old = FindAnyObjectByType<GameBootstrap>();
            if (old != null)
            {
                old.gameObject.SetActive(false);
                Destroy(old.gameObject);
            }
            Game.Reset();
            new GameObject("Game").AddComponent<GameBootstrap>();
        }

        void Awake()
        {
            if (!artReady)
            {
                Art.Init();
                Defs.Init();
                artReady = true;
            }

            var root = transform;
            SetupLighting();
            var cam = SetupCamera();

            BuildingVisuals.Init(root);
            Effects.Init(root);
            ItemPool.Init(root);
            Projectile.Init(root);
            Enemy.Init(root);

            var map = new GameObject("Map").AddComponent<GridMap>();
            map.transform.SetParent(root, false);
            map.Generate(Seed);
            map.BuildVisuals();

            Game.Map = map;
            Game.Flow = new FlowField(map);
            Game.Bank = new ResourceBank(new Cost(800, 0, 0, 60));
            Game.Cam = cam;
            Game.Build = gameObject.AddComponent<BuildController>();
            Game.Waves = gameObject.AddComponent<WaveManager>();
            Game.Alerts = gameObject.AddComponent<AlertSystem>();
            Game.Objectives = gameObject.AddComponent<Objectives>();
            gameObject.AddComponent<HUD>();

            Game.Build.Place(Defs.Core, map.CoreOrigin, Dir.N, free: true);
            cam.LookAt(Game.Core.Center + new Vector3(-4, 0, -2));

            if (Defs.Buildable[0].Icon == null) BuildingIcons.RenderAll(root);
            Game.Apply();
        }

        void Update() => Art.ScrollBelts();

        CameraController SetupCamera()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                cam = new GameObject("Main Camera").AddComponent<Camera>();
                cam.tag = "MainCamera";
            }
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.015f, 0.02f);
            cam.fieldOfView = 30f;
            cam.nearClipPlane = 0.5f;
            cam.farClipPlane = 250f;
            cam.cullingMask &= ~(1 << 31); // icon layer
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

            var controller = cam.GetComponent<CameraController>();
            if (controller == null) controller = cam.gameObject.AddComponent<CameraController>();
            return controller;
        }

        void SetupLighting()
        {
            Light sun = null;
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) { sun = l; break; }
            if (sun == null)
            {
                sun = new GameObject("Sun").AddComponent<Light>();
                sun.type = LightType.Directional;
            }
            sun.transform.rotation = Quaternion.Euler(58f, -35f, 0f);
            sun.color = new Color(1f, 0.82f, 0.7f);
            sun.intensity = 0.75f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.85f;

            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.3f, 0.24f, 0.32f);
            RenderSettings.ambientEquatorColor = new Color(0.2f, 0.14f, 0.14f);
            RenderSettings.ambientGroundColor = new Color(0.07f, 0.05f, 0.05f);
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = null;
            RenderSettings.fog = false;
            DynamicGI.UpdateEnvironment();

            var volumeGo = new GameObject("PostFX");
            volumeGo.transform.SetParent(transform, false);
            var volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 100;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.value = 1.1f;
            bloom.threshold.value = 0.9f;
            bloom.scatter.value = 0.72f;
            var tone = profile.Add<Tonemapping>(true);
            tone.mode.value = TonemappingMode.ACES;
            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.value = 0.35f;
            color.contrast.value = 12f;
            color.saturation.value = 10f;
            color.colorFilter.value = Color.white;
            color.hueShift.value = 0f;
            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.value = 0.32f;
            vignette.smoothness.value = 0.45f;
            vignette.color.value = Color.black;
            volume.profile = profile;
        }
    }
}
