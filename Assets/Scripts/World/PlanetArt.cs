using System.Collections.Generic;
using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>
    /// Helpers for drawing on the planet: laying sprites flat on the ground (their +x along grid x, +y along grid y,
    /// so top-down art lines up with the cells), turning sprites to face the camera, the game's own shaders
    /// (Resources/Shaders), see-through ghost copies of models, and per-planet colour palettes.
    /// </summary>
    public static class PlanetArt
    {
        static readonly Dictionary<string, Shader> shaders = new Dictionary<string, Shader>();
        static readonly Dictionary<Color, Material> ghostMaterials = new Dictionary<Color, Material>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            shaders.Clear();
            ghostMaterials.Clear();
        }

        /// <summary>A new material with one of the game's shaders (Resources/Shaders/name).</summary>
        public static Material NewMaterial(string name)
        {
            if (!shaders.TryGetValue(name, out var shader) || shader == null)
            {
                shader = shaders[name] = Resources.Load<Shader>("Shaders/" + name);
                if (shader == null) Debug.LogWarning($"PlanetArt: shader Resources/Shaders/{name} not found.");
            }
            return new Material(shader != null ? shader : Shader.Find("Hidden/InternalErrorShader")) { name = name };
        }

        /// <summary>
        /// Lays a sprite (or any quad in its local XY plane) flat on a cell, lift above the ground: sprite +x along the
        /// cell's right, +y along its forward (side 0), so art drawn in the cell frame (see Dir) lines up.
        /// </summary>
        public static void LayFlat(Transform t, IsoGrid map, Vector2Int cell, float level, float lift = 0.02f)
        {
            var frame = map.Frame(cell);
            var up = frame * Vector3.up;
            t.SetPositionAndRotation(map.CellPoint(cell, Vector2.zero, level) + up * lift, Quaternion.LookRotation(-up, frame * Vector3.forward));
        }

        /// <summary>Turns a sprite to face the camera.</summary>
        public static void Billboard(Transform t)
        {
            var cam = Camera.main;
            if (cam != null) t.rotation = cam.transform.rotation;
        }

        /// <summary>Shared see-through material of one colour (placement previews).</summary>
        public static Material Ghost(Color color)
        {
            if (!ghostMaterials.TryGetValue(color, out var m) || m == null)
            {
                m = ghostMaterials[color] = NewMaterial("Ghost");
                m.SetColor("_Color", color);
            }
            return m;
        }

        /// <summary>Paints every renderer under root with one material (keeping their sub-mesh count).</summary>
        public static void Paint(GameObject root, Material material)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer) { r.enabled = false; continue; }
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = material;
                r.sharedMaterials = mats;
            }
        }

        /// <summary>Removes colliders from a decorative model instance so it never gets in the way of clicks.</summary>
        public static void StripColliders(GameObject root)
        {
            foreach (var c in root.GetComponentsInChildren<Collider>(true)) Object.Destroy(c);
        }

        // ---------------------------------------------------------------- palettes

        /// <summary>The look of one planet; the run cycles through them map by map.</summary>
        public struct Palette
        {
            public string Name;
            public Color Low, High, Atmosphere, Space;
        }

        public static readonly Palette[] Palettes =
        {
            new Palette { Name = "Verdant", Low = new Color(0.36f, 0.48f, 0.24f), High = new Color(0.52f, 0.47f, 0.4f), Atmosphere = new Color(0.35f, 0.62f, 1f), Space = new Color(0.02f, 0.025f, 0.05f) },
            new Palette { Name = "Rust", Low = new Color(0.55f, 0.3f, 0.18f), High = new Color(0.4f, 0.3f, 0.28f), Atmosphere = new Color(1f, 0.55f, 0.3f), Space = new Color(0.045f, 0.02f, 0.02f) },
            new Palette { Name = "Frost", Low = new Color(0.62f, 0.7f, 0.78f), High = new Color(0.9f, 0.93f, 0.97f), Atmosphere = new Color(0.55f, 0.85f, 1f), Space = new Color(0.015f, 0.03f, 0.05f) },
            new Palette { Name = "Ash", Low = new Color(0.3f, 0.27f, 0.3f), High = new Color(0.2f, 0.17f, 0.2f), Atmosphere = new Color(0.85f, 0.35f, 1f), Space = new Color(0.03f, 0.015f, 0.045f) },
        };

        public static Palette Current => Palettes[Mathf.Abs(Run.Stage) % Palettes.Length];
    }
}
