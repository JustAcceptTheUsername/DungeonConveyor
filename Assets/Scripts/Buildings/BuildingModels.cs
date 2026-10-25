using UnityEngine;
using UnityEngine.Rendering;

namespace DungeonConveyor
{
    /// <summary>
    /// 3D models standing on the planet. A model sits with its origin on a point of a cell, upright on the ground
    /// there, its +X along the cell's right and +Z along its forward (side 0) (1 model unit = 1 cell).
    /// </summary>
    public static class BuildingModels
    {
        /// <summary>
        /// Instantiates the model at `offset` from a cell's centre (in its frame) at elevation `level`, under parent.
        /// scale and yaw are in the model's own axes. Returns the model instance.
        /// </summary>
        public static GameObject Spawn(IsoGrid map, GameObject prefab, Vector2Int cell, Vector2 offset, float level, Vector3 scale, float yaw, Transform parent) =>
            Spawn(prefab, map.CellPoint(cell, offset, level), map.CellFrame(cell, offset), scale, yaw, parent);

        /// <summary>Instantiates the model at a world position and ground frame (+Y up), under parent.</summary>
        public static GameObject Spawn(GameObject prefab, Vector3 position, Quaternion frame, Vector3 scale, float yaw, Transform parent)
        {
            var root = new GameObject(prefab.name + " (model)").transform;
            root.SetParent(parent, false);
            root.SetPositionAndRotation(position, frame);

            var model = Object.Instantiate(prefab, root, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            model.transform.localScale = scale;
            PlanetArt.StripColliders(model);
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            return model;
        }

        /// <summary>A box collider on `owner` around everything the model draws, so the building can be clicked.</summary>
        public static void AddPickCollider(GameObject owner, GameObject model)
        {
            var t = owner.transform;
            var bounds = new Bounds();
            bool any = false;
            foreach (var r in model.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer) continue;
                var b = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = t.InverseTransformPoint(b.center + Vector3.Scale(b.extents, new Vector3(i & 1, (i >> 1) & 1, (i >> 2) & 1) * 2f - Vector3.one));
                    if (!any) { bounds = new Bounds(corner, Vector3.zero); any = true; }
                    else bounds.Encapsulate(corner);
                }
            }
            if (!any) return;
            var box = owner.GetComponent<BoxCollider>();
            if (box == null) box = owner.AddComponent<BoxCollider>();
            box.center = bounds.center;
            box.size = Vector3.Max(bounds.size, Vector3.one * 0.3f);
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
