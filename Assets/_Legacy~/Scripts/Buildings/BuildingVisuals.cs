using System.Collections.Generic;
using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>
    /// Procedural greybox models for buildings. To use real art, drop a prefab at
    /// Assets/Resources/Models/&lt;Id&gt;.prefab (e.g. Models/Turret) — pivot at footprint centre on the ground,
    /// facing +Z. Optional child names "Head", "Spin", "Muzzle" are animated if present.
    /// </summary>
    public static class BuildingVisuals
    {
        static readonly Dictionary<string, GameObject> templates = new Dictionary<string, GameObject>();
        static Transform templateRoot;

        public static void Init(Transform parent)
        {
            templates.Clear();
            beltMeshes.Clear();
            templateRoot = new GameObject("Templates").transform;
            templateRoot.SetParent(parent, false);
            templateRoot.gameObject.SetActive(false);
        }

        public static Transform Create(BuildingDef def, Transform parent)
        {
            if (!templates.TryGetValue(def.Id, out var template))
            {
                var prefab = Resources.Load<GameObject>("Models/" + def.Id);
                template = prefab != null ? Object.Instantiate(prefab) : Build(def);
                template.name = def.Id + "_Visual";
                template.transform.SetParent(templateRoot, false);
                templates[def.Id] = template;
            }
            var v = Object.Instantiate(template, parent).transform;
            v.name = "Visual";
            v.localPosition = Vector3.zero;
            return v;
        }

        static GameObject Build(BuildingDef def)
        {
            switch (def.Id)
            {
                case "Conveyor": return new GameObject("Conveyor"); // mesh is assigned by Conveyor itself
                case "Splitter": return Splitter();
                case "Lift": return Lift();
                case "Miner": return Miner();
                case "Turret": return Turret();
                case "Flamethrower": return Flamethrower();
                case "Wall": return Wall();
                case "Collector": return Collector();
                case "Core": return Core();
            }
            return new VoxelModel(def.Id).Box(new Vector3(0, 0.5f, 0), Vector3.one * 0.9f, Art.Orange).Finish();
        }

        static VoxelModel Base(string name, float size, float height = 0.3f)
        {
            var m = new VoxelModel(name);
            float s = size - 0.08f;
            m.Box(new Vector3(0, height * 0.5f, 0), new Vector3(s, height, s), Art.MetalDark);
            m.Box(new Vector3(0, height + 0.03f, 0), new Vector3(s - 0.1f, 0.06f, s - 0.1f), Art.Metal);
            // Corner bolts
            float c = s * 0.5f - 0.08f;
            foreach (var p in new[] { new Vector3(c, 0, c), new Vector3(-c, 0, c), new Vector3(c, 0, -c), new Vector3(-c, 0, -c) })
                m.Box(p + Vector3.up * (height + 0.02f), new Vector3(0.1f, 0.1f, 0.1f), Art.Orange);
            return m;
        }

        static GameObject Splitter()
        {
            var m = Base("Splitter", 1f, 0.22f);
            m.Box(new Vector3(0, 0.38f, 0), new Vector3(0.5f, 0.3f, 0.5f), Art.Orange, "", 45f);
            m.Box(new Vector3(0, 0.56f, 0), new Vector3(0.3f, 0.08f, 0.3f), Art.Glow, "", 45f);
            for (int i = 0; i < 4; i++)
            {
                var d = ((Dir)i).World();
                m.Box(d * 0.36f + Vector3.up * 0.3f, new Vector3(0.18f, 0.08f, 0.18f), Art.Rail, "", 45f);
            }
            return m.Finish();
        }

        static GameObject Lift()
        {
            var m = Base("Lift", 1f, 0.25f);
            m.Box(new Vector3(0, 0.25f + 0.6f, 0), new Vector3(0.42f, 1.2f, 0.42f), Art.OrangeDark);
            m.Box(new Vector3(0.24f, 0.8f, 0), new Vector3(0.06f, 1.1f, 0.06f), Art.Metal);
            m.Box(new Vector3(-0.24f, 0.8f, 0), new Vector3(0.06f, 1.1f, 0.06f), Art.Metal);
            m.Box(new Vector3(0, 1.5f, 0), new Vector3(0.56f, 0.14f, 0.56f), Art.Metal);
            m.Box(new Vector3(0, 1.62f, 0), new Vector3(0.3f, 0.12f, 0.3f), Art.Resource[(int)ResourceType.Crystal]);
            m.Box(new Vector3(0, 1.5f, 0.32f), new Vector3(0.2f, 0.08f, 0.14f), Art.Glow); // launch direction
            return m.Finish();
        }

        static GameObject Miner()
        {
            var m = Base("Miner", 2f, 0.35f);
            m.Box(new Vector3(0, 0.75f, -0.1f), new Vector3(1.3f, 0.8f, 1.2f), Art.Orange);
            m.Box(new Vector3(0, 1.2f, -0.1f), new Vector3(1.4f, 0.12f, 1.3f), Art.OrangeDark);
            m.Box(new Vector3(-0.45f, 1.5f, -0.45f), new Vector3(0.22f, 0.6f, 0.22f), Art.MetalDark);
            m.Box(new Vector3(-0.45f, 1.83f, -0.45f), new Vector3(0.26f, 0.08f, 0.26f), Art.Glow);
            m.Box(new Vector3(0.35f, 0.75f, 0.52f), new Vector3(0.35f, 0.35f, 0.05f), Art.Glow);   // window
            m.Box(new Vector3(0, 0.45f, 0.8f), new Vector3(0.5f, 0.2f, 0.35f), Art.Metal);       // output chute
            m.Box(new Vector3(0, 0.56f, 0.88f), new Vector3(0.3f, 0.04f, 0.2f), Art.Glow);
            m.Part("Spin", new Vector3(0.2f, 1.3f, -0.2f));
            m.Box(new Vector3(0, 0.12f, 0), new Vector3(0.3f, 0.24f, 0.3f), Art.Metal, "Spin");
            for (int i = 0; i < 4; i++)
                m.Box(Quaternion.Euler(0, i * 90, 0) * new Vector3(0, 0.2f, 0.25f), new Vector3(0.1f, 0.08f, 0.3f), Art.MetalLight, "Spin", i * 90f);
            return m.Finish();
        }

        static GameObject Turret()
        {
            var m = Base("Turret", 1f, 0.3f);
            m.Box(new Vector3(0, 0.4f, 0), new Vector3(0.6f, 0.14f, 0.6f), Art.Orange, "", 45f);
            m.Part("Head", new Vector3(0, 0.5f, 0));
            m.Box(new Vector3(0, 0.18f, -0.02f), new Vector3(0.46f, 0.32f, 0.5f), Art.Metal, "Head");
            m.Box(new Vector3(0, 0.36f, -0.05f), new Vector3(0.36f, 0.06f, 0.36f), Art.Orange, "Head");
            m.Box(new Vector3(0.09f, 0.2f, 0.42f), new Vector3(0.08f, 0.08f, 0.45f), Art.MetalDark, "Head");
            m.Box(new Vector3(-0.09f, 0.2f, 0.42f), new Vector3(0.08f, 0.08f, 0.45f), Art.MetalDark, "Head");
            m.Box(new Vector3(0, 0.22f, 0.24f), new Vector3(0.12f, 0.06f, 0.04f), Art.Glow, "Head");
            m.Part("Muzzle", new Vector3(0, 0.2f, 0.68f), "Head");
            return m.Finish();
        }

        static GameObject Flamethrower()
        {
            var m = Base("Flamethrower", 1f, 0.3f);
            m.Box(new Vector3(0, 0.4f, 0), new Vector3(0.62f, 0.14f, 0.62f), Art.MetalDark);
            m.Part("Head", new Vector3(0, 0.5f, 0));
            m.Box(new Vector3(0, 0.2f, -0.05f), new Vector3(0.5f, 0.36f, 0.5f), Art.OrangeDark, "Head");
            m.Box(new Vector3(-0.18f, 0.48f, -0.15f), new Vector3(0.16f, 0.3f, 0.16f), Art.Orange, "Head");   // fuel tanks
            m.Box(new Vector3(0.18f, 0.48f, -0.15f), new Vector3(0.16f, 0.3f, 0.16f), Art.Orange, "Head");
            m.Box(new Vector3(0, 0.22f, 0.36f), new Vector3(0.16f, 0.16f, 0.4f), Art.Metal, "Head");
            m.Box(new Vector3(0, 0.22f, 0.58f), new Vector3(0.2f, 0.2f, 0.08f), Art.GlowHot, "Head");
            m.Part("Muzzle", new Vector3(0, 0.22f, 0.64f), "Head");
            return m.Finish();
        }

        static GameObject Wall()
        {
            var stone = Art.RockTop;
            var m = new VoxelModel("Wall");
            m.Box(new Vector3(0, 0.45f, 0), new Vector3(0.96f, 0.9f, 0.96f), Art.Metal, "", 0f, stone);
            m.Box(new Vector3(0, 0.3f, 0), new Vector3(1.0f, 0.1f, 1.0f), Art.OrangeDark);
            for (int x = -1; x <= 1; x += 2)
                for (int z = -1; z <= 1; z += 2)
                    m.Box(new Vector3(x * 0.3f, 1.0f, z * 0.3f), new Vector3(0.28f, 0.2f, 0.28f), Art.Metal, "", 0f, stone);
            return m.Finish();
        }

        static GameObject Collector()
        {
            var m = Base("Collector", 1f, 0.3f);
            m.Box(new Vector3(0, 0.55f, 0), new Vector3(0.6f, 0.5f, 0.6f), Art.Orange);
            m.Box(new Vector3(0, 0.85f, 0), new Vector3(0.75f, 0.1f, 0.75f), Art.OrangeDark);
            m.Box(new Vector3(0, 0.88f, 0), new Vector3(0.5f, 0.06f, 0.5f), Art.Black);
            m.Box(new Vector3(0, 1.05f, 0), new Vector3(0.12f, 0.35f, 0.12f), Art.Metal);
            m.Box(new Vector3(0, 1.25f, 0), new Vector3(0.18f, 0.1f, 0.18f), Art.Glow);
            return m.Finish();
        }

        static GameObject Core()
        {
            var m = new VoxelModel("Core");
            m.Box(new Vector3(0, 0.2f, 0), new Vector3(2.95f, 0.4f, 2.95f), Art.MetalDark);
            m.Box(new Vector3(0, 0.2f, 0), new Vector3(2.4f, 0.42f, 2.4f), Art.CoreShell, "", 45f);
            m.Box(new Vector3(0, 0.7f, 0), new Vector3(2.3f, 0.6f, 2.3f), Art.CoreShell);
            m.Box(new Vector3(0, 1.05f, 0), new Vector3(2.4f, 0.1f, 2.4f), Art.Orange);
            m.Box(new Vector3(0, 1.4f, 0), new Vector3(1.7f, 0.6f, 1.7f), Art.CoreShell, "", 45f);
            m.Box(new Vector3(0, 1.75f, 0), new Vector3(1.3f, 0.12f, 1.3f), Art.Glow, "", 45f);
            // Glowing vents
            for (int i = 0; i < 4; i++)
            {
                var d = ((Dir)i).World();
                m.Box(d * 1.16f + Vector3.up * 0.7f, new Vector3(0.5f, 0.3f, 0.5f) - new Vector3(Mathf.Abs(d.x), 0, Mathf.Abs(d.z)) * 0.44f, Art.GlowHot);
            }
            // Corner pylons
            for (int x = -1; x <= 1; x += 2)
                for (int z = -1; z <= 1; z += 2)
                {
                    var p = new Vector3(x * 1.15f, 0, z * 1.15f);
                    m.Box(p + Vector3.up * 0.8f, new Vector3(0.45f, 1.6f, 0.45f), Art.CoreShell);
                    m.Box(p + Vector3.up * 1.65f, new Vector3(0.3f, 0.12f, 0.3f), Art.Glow);
                }
            m.Part("Spin", new Vector3(0, 2.2f, 0));
            m.Box(Vector3.zero, new Vector3(0.7f, 0.9f, 0.7f), Art.CoreGlow, Quaternion.Euler(45, 0, 45), "Spin");
            m.Box(new Vector3(0, 0.55f, 0), new Vector3(0.35f, 0.5f, 0.35f), Art.CoreGlow, Quaternion.Euler(0, 45, 0), "Spin");
            m.Part("Muzzle", new Vector3(0, 2.2f, 0));
            var go = m.Finish();

            var light = new GameObject("Light").AddComponent<Light>();
            light.transform.SetParent(go.transform, false);
            light.transform.localPosition = new Vector3(0, 3f, 0);
            light.type = LightType.Point;
            light.color = new Color(1f, 0.55f, 0.15f);
            light.range = 10f;
            light.intensity = 4f;
            return go;
        }

        // ---------------------------------------------------------------- level pips

        public static void SetLevelPips(Transform building, int level, int size)
        {
            var old = building.Find("Pips");
            if (old != null) Object.Destroy(old.gameObject);
            if (level <= 1) return;
            var m = new VoxelModel("Pips");
            float half = size * 0.5f - 0.12f;
            for (int i = 0; i < level - 1; i++)
                m.Box(new Vector3(-half + i * 0.14f, 0.36f, -half), new Vector3(0.1f, 0.1f, 0.1f), Art.GlowHot);
            var go = m.Finish(false);
            go.transform.SetParent(building, false);
        }

        // ---------------------------------------------------------------- conveyor meshes

        public enum BeltShape { Straight, FromLeft, FromRight }
        static readonly Dictionary<(BeltShape, int), (Mesh mesh, Material[] mats)> beltMeshes = new Dictionary<(BeltShape, int), (Mesh, Material[])>();

        public static (Mesh mesh, Material[] mats) BeltMesh(BeltShape shape, int level)
        {
            if (beltMeshes.TryGetValue((shape, level), out var cached) && cached.mesh != null) return cached;
            var mb = new MeshBuilder();
            var top = Art.BeltTop[Mathf.Clamp(level - 1, 0, Art.BeltTop.Length - 1)];
            const float h = 0.16f, w = 0.84f, beltW = 0.66f;

            // Exit half always runs from the centre toward +Z.
            mb.Box(new Vector3(0, h * 0.5f, 0.25f - (w - 0.5f) * 0.25f), new Vector3(w, h, 0.5f + (w - 0.5f) * 0.5f), Art.MetalDark);
            mb.Face(top, new Vector3(0, h + 0.005f, 0.25f), Vector3.up, Vector3.right * beltW * 0.5f, Vector3.forward * 0.25f);

            if (shape == BeltShape.Straight)
            {
                mb.Box(new Vector3(0, h * 0.5f, -0.25f), new Vector3(w, h, 0.5f), Art.MetalDark);
                mb.Face(top, new Vector3(0, h + 0.005f, -0.25f), Vector3.up, Vector3.right * beltW * 0.5f, Vector3.forward * 0.25f);
                Rails(mb, new Vector3(0.44f, 0, 0), 1f, true);
                Rails(mb, new Vector3(-0.44f, 0, 0), 1f, true);
            }
            else
            {
                float s = shape == BeltShape.FromLeft ? -1f : 1f; // entry side on X
                mb.Box(new Vector3(s * 0.04f, h * 0.5f, 0), new Vector3(0.92f, h, w), Art.MetalDark);
                // Entry half: flow runs from the side edge toward the centre.
                var flow = new Vector3(-s, 0, 0);
                mb.Face(top, new Vector3(s * 0.25f, h + 0.004f, 0), Vector3.up, Vector3.Cross(flow, Vector3.down) * beltW * 0.5f, flow * 0.25f);
                Rails(mb, new Vector3(0, 0, -0.44f), 1f, false);       // outer back rail
                Rails(mb, new Vector3(-s * 0.44f, 0, 0.0f), 1f, true); // outer side rail
            }
            var mesh = mb.Build("Belt_" + shape + level);
            var result = (mesh, mb.Materials.ToArray());
            beltMeshes[(shape, level)] = result;
            return result;
        }

        static void Rails(MeshBuilder mb, Vector3 center, float length, bool alongZ)
        {
            var size = alongZ ? new Vector3(0.07f, 0.24f, length) : new Vector3(length, 0.24f, 0.07f);
            mb.Box(center + Vector3.up * 0.12f, size, Art.Rail);
        }
    }
}
