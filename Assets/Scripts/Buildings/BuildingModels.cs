using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DungeonConveyor
{
    /// <summary>
    /// Live 3D building models standing in the 2D isometric world (BuildingCatalog.LiveModels).
    /// - Each model is turned by the inverse of ModelArt's view angle and scaled to world units, so the main camera
    ///   (looking straight down +Z) sees it exactly as ModelArt's sprite would look, pivot on the same point.
    /// - Sorting: its materials move to the transparent queue and its renderers take the building's sortingOrder, so
    ///   it is painted in the same back-to-front order as the sprites. Its depth sits behind the sprite plane (z = 0),
    ///   so sprites painted after it (terrain and enemies in front) still cover it; between models the depth is their
    ///   true 3D depth, so they also hide their own back faces and each other correctly.
    /// - Lighting: once every sprite is baked, the scene's directional lights are turned with the models and a key
    ///   light like ModelArt's is added, so models are lit from the same directions as the baked sprites.
    /// </summary>
    public static class BuildingModels
    {
        const float DepthBase = 50f; // keeps every model behind the sprites at z = 0

        static readonly Dictionary<Material, Material> sorted = new Dictionary<Material, Material>();
        static Light keyLight;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            sorted.Clear();
            keyLight = null;
        }

        public static bool Enabled => BuildingCatalog.Instance != null && BuildingCatalog.Instance.LiveModels;

        static Quaternion ToWorld => Quaternion.Inverse(ModelArt.ViewRotation);

        /// <summary>
        /// Instantiates the model with its origin on grid point `grid` at elevation `level`, under parent. scale and
        /// yaw are in the model's own axes, as in ModelArt.Render. Returns the model instance.
        /// </summary>
        public static GameObject Spawn(IsoGrid map, GameObject prefab, Vector2 grid, float level, Vector3 scale, float yaw, int order, Transform parent)
        {
            var root = new GameObject(prefab.name + " (model)").transform;
            root.SetParent(parent, false);
            root.SetPositionAndRotation(Position(map, grid, level), ToWorld);
            root.localScale = Vector3.one * ModelArt.WorldPerModelUnit;

            var model = Object.Instantiate(prefab, root, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            model.transform.localScale = scale;
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                r.sortingOrder = order;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) if (mats[i] != null) mats[i] = Sorted(mats[i]);
                r.sharedMaterials = mats;
            }
            return model;
        }

        /// <summary>
        /// World position of a grid point: x and y where the sprites put it; z its depth along the view, so models
        /// further back are further away.
        /// </summary>
        public static Vector3 Position(IsoGrid map, Vector2 grid, float level)
        {
            var p = map.GridToWorld(grid, level);
            var q = ToWorld;
            float s = ModelArt.WorldPerModelUnit;
            float height = level * IsoGrid.StepHeight / (s * (q * Vector3.up).y); // in model units
            p.z = DepthBase + s * (q * new Vector3(grid.x, height, grid.y)).z;
            return p;
        }

        /// <summary>A copy of the material drawn in the transparent queue (keeping its own depth writing).</summary>
        static Material Sorted(Material src)
        {
            if (src.renderQueue >= (int)RenderQueue.Transparent) return src;
            if (!sorted.TryGetValue(src, out var m))
                sorted[src] = m = new Material(src) { name = src.name + " (sorted)", renderQueue = (int)RenderQueue.Transparent };
            return m;
        }

        /// <summary>
        /// Call once every sprite has been baked (after GameArt.EnsureRendered): turns the scene's directional lights
        /// along with the models and adds ModelArt's key light, turned the same way. Once per scene.
        /// </summary>
        public static void MatchBakeLighting()
        {
            if (keyLight != null) return;
            var q = ToWorld;
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) l.transform.rotation = q * l.transform.rotation;

            keyLight = new GameObject("ModelKeyLight").AddComponent<Light>();
            keyLight.type = LightType.Directional;
            keyLight.transform.rotation = q * ModelArt.KeyLightRotation;
            keyLight.intensity = ModelArt.KeyLightIntensity;
            keyLight.color = ModelArt.KeyLightColor;
            keyLight.shadows = LightShadows.None;
        }

        public static Transform FindChild(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t)
            {
                var found = FindChild(c, name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
