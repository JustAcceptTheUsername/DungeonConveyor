using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace DungeonConveyor.EditorTools
{
    /// <summary>
    /// Keeps Resources/BuildingCatalog.asset in sync with the models in Assets/Models/Buildings: new models get an
    /// entry with sensible defaults (category, cost, footprint measured from the mesh), removed ones are dropped, and
    /// anything already edited in the Inspector is left alone. The Core is set up once to fill the 4x4 base and is
    /// not purchasable. Runs after scripts compile and whenever a model changes.
    /// </summary>
    [InitializeOnLoad]
    public class BuildingCatalogSync : AssetPostprocessor
    {
        const string ModelsFolder = "Assets/Models/Buildings";
        const string CatalogPath = "Assets/Resources/BuildingCatalog.asset";

        // Models in the folder that are not part of the game.
        static readonly HashSet<string> Excluded = new HashSet<string> { "Lift" };

        // id -> (category, cost, description) for the models we know about.
        static readonly Dictionary<string, (BuildCategory, int, string)> Defaults = new Dictionary<string, (BuildCategory, int, string)>
        {
            { "Conveyor", (BuildCategory.Logistics, 10, "Moves ore along a belt.") },
            { "Splitter", (BuildCategory.Logistics, 60, "Takes items from belts feeding into it and shares them between the belts leading out.") },
            { "Collector", (BuildCategory.Logistics, 250, RefinerDescription) },
            { "Miner", (BuildCategory.Mining, 120, "Drills ore. Must be placed on an ore deposit.") },
            { "MineShaft", (BuildCategory.Mining, 300, "Deep mine with high output. Must be placed on an ore deposit.") },
            { "Wall", (BuildCategory.Defense, 25, "Cheap barrier. Enemies must walk around it; use walls to lengthen their path.") },
            { "LandMine", (BuildCategory.Defense, 40, "Explodes when an enemy steps on it. Can be placed anywhere enemies walk.") },
            { "Turret", (BuildCategory.Defense, 150, "Reliable rapid-fire gun.") },
            { "Flamethrower", (BuildCategory.Defense, 250, "Burns every enemy inside its short-range field.") },
            { "Mortar", (BuildCategory.Defense, 280, "Long-range shells with a big blast. Can't hit enemies up close.") },
            { "LaserTower", (BuildCategory.Defense, 300, "Continuous beam that melts a single target.") },
            { "MissileLauncher", (BuildCategory.Defense, 350, "Homing missiles that explode on impact.") },
            { "TeslaCoil", (BuildCategory.Defense, 400, "Lightning that chains between nearby enemies.") },
            { "ShieldGenerator", (BuildCategory.Defense, 450, ShieldDescription) },
        };

        const string RefinerDescription = "Refines raw ore passing through it, multiplying its value. Feed it with a belt and lead a belt out.";
        const string ShieldDescription = "Projects a field that slows every enemy inside it.";

        // id -> (attack, range, damage, fire rate, splash). Damage is per second for Laser/Flame; for Slow it's the slow fraction.
        static readonly Dictionary<string, (AttackKind, float, float, float, float)> Combat = new Dictionary<string, (AttackKind, float, float, float, float)>
        {
            { "Turret", (AttackKind.Bullet, 4.5f, 10f, 3f, 0f) },
            { "Flamethrower", (AttackKind.Flame, 2.5f, 35f, 1f, 0f) },
            { "LaserTower", (AttackKind.Laser, 5f, 30f, 1f, 0f) },
            { "MissileLauncher", (AttackKind.Missile, 7f, 55f, 0.6f, 1.2f) },
            { "Mortar", (AttackKind.Mortar, 8.5f, 70f, 0.4f, 1.6f) },
            { "TeslaCoil", (AttackKind.Tesla, 4f, 28f, 1f, 4f) },
            { "LandMine", (AttackKind.Mine, 0.6f, 150f, 1f, 1.6f) },
            { "ShieldGenerator", (AttackKind.Slow, 3.5f, 0.5f, 1f, 0f) },
        };

        static BuildingCatalogSync() => EditorApplication.delayCall += Sync;

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (var list in new[] { imported, deleted, moved, movedFrom })
                foreach (var p in list)
                    if (p.StartsWith(ModelsFolder + "/"))
                    {
                        EditorApplication.delayCall -= Sync;
                        EditorApplication.delayCall += Sync;
                        return;
                    }
        }

        [MenuItem("Dungeon/Sync Building Catalog")]
        public static void Sync()
        {
            if (!AssetDatabase.IsValidFolder(ModelsFolder)) return;

            var catalog = AssetDatabase.LoadAssetAtPath<BuildingCatalog>(CatalogPath);
            if (catalog == null)
            {
                if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
                catalog = ScriptableObject.CreateInstance<BuildingCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            var models = FindModels();
            bool changed = catalog.Entries.RemoveAll(e => e == null || !models.ContainsKey(e.Id)) > 0;
            foreach (var pair in models)
            {
                var entry = catalog.Entries.Find(e => e.Id == pair.Key);
                if (entry == null)
                {
                    catalog.Entries.Add(CreateEntry(pair.Key, pair.Value));
                    changed = true;
                }
                else if (entry.Model == null)
                {
                    entry.Model = pair.Value;
                    changed = true;
                }
            }

            // One-time Core setup (entries created before the Purchasable flag existed still read as purchasable).
            var core = catalog.Find(BuildingCatalog.CoreId);
            if (core != null && core.Purchasable && core.Model != null)
            {
                var extents = MeasureBody(core.Model);
                core.Purchasable = false;
                core.Cost = 0;
                core.Size = Vector2Int.one * IsoGrid.BaseSize;
                core.Scale = extents.x > 0f ? IsoGrid.BaseSize / Mathf.Max(extents.x, extents.y) : 1f;
                core.Description = "Your base. If it falls, the dungeon is lost.";
                changed = true;
            }

            // One-time migrations for entries made by earlier versions.
            var refiner = catalog.Find(BuildingCatalog.RefinerId);
            if (refiner != null && refiner.Name == "Collector")
            {
                refiner.Name = "Refiner";
                refiner.Cost = 250;
                refiner.Category = BuildCategory.Logistics;
                refiner.Description = RefinerDescription;
                changed = true;
            }
            foreach (var e in catalog.Entries)
            {
                if (e.Attack != AttackKind.None || !Combat.TryGetValue(e.Id, out var stats)) continue;
                (e.Attack, e.Range, e.Damage, e.FireRate, e.Splash) = stats;
                if (Defaults.TryGetValue(e.Id, out var d)) e.Description = d.Item3;
                changed = true;
            }
            // Walls are for steering enemies tile by tile: 1x1, with the 2-unit-long model scaled to fit.
            var wall = catalog.Find("Wall");
            if (wall != null && wall.Size != Vector2Int.one && Mathf.Approximately(wall.Scale, 1f))
            {
                wall.Size = Vector2Int.one;
                wall.Scale = 0.5f;
                changed = true;
            }
            var splitter = catalog.Find(BuildingCatalog.SplitterId);
            if (splitter != null && splitter.Description == "Splits one belt into several.")
            {
                splitter.Description = Defaults["Splitter"].Item3;
                changed = true;
            }

            if (!changed) return;
            catalog.Entries.Sort((a, b) => a.Cost != b.Cost ? a.Cost.CompareTo(b.Cost) : string.CompareOrdinal(a.Id, b.Id));
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log($"Building catalog synced: {catalog.Entries.Count} buildings ({CatalogPath}).");
        }

        /// <summary>One model per base name; .glb (glTFast, keeps emissive materials) is preferred over .obj.</summary>
        static Dictionary<string, GameObject> FindModels()
        {
            var result = new Dictionary<string, GameObject>();
            foreach (var guid in AssetDatabase.FindAssets("", new[] { ModelsFolder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext != ".glb" && ext != ".gltf" && ext != ".obj" && ext != ".fbx") continue;
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;
                var id = Path.GetFileNameWithoutExtension(path);
                if (Excluded.Contains(id)) continue;
                if (!result.ContainsKey(id) || ext == ".glb" || ext == ".gltf") result[id] = go;
            }
            return result;
        }

        static BuildingCatalog.Entry CreateEntry(string id, GameObject model)
        {
            var (category, cost, description) = Defaults.TryGetValue(id, out var d) ? d : (BuildCategory.Defense, 100, "");
            return new BuildingCatalog.Entry
            {
                Id = id,
                Name = Regex.Replace(id, "(?<=[a-z])(?=[A-Z])", " "),
                Model = model,
                Category = category,
                Cost = cost,
                Size = Footprint(MeasureBody(model)),
                Description = description,
            };
        }

        /// <summary>Footprint in cells from the body size, 1 model unit = 1 cell.</summary>
        static Vector2Int Footprint(Vector2 body) =>
            new Vector2Int(Mathf.Clamp(Mathf.RoundToInt(body.x), 1, 4), Mathf.Clamp(Mathf.RoundToInt(body.y), 1, 4));

        /// <summary>
        /// Body width along model X and Z, using the extent that is symmetric about the origin so overhangs like gun
        /// barrels don't count. Model X maps to grid x and model Z to grid y (see BuildingArt).
        /// </summary>
        static Vector2 MeasureBody(GameObject model)
        {
            var root = model.transform.worldToLocalMatrix;
            bool any = false;
            var b = new Bounds();
            foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var m = root * mf.transform.localToWorldMatrix;
                var mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = mb.center + Vector3.Scale(mb.extents, new Vector3(i & 1, (i >> 1) & 1, (i >> 2) & 1) * 2f - Vector3.one);
                    var p = m.MultiplyPoint3x4(corner);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                    else b.Encapsulate(p);
                }
            }
            if (!any) return Vector2.one;
            return new Vector2(2f * Mathf.Min(-b.min.x, b.max.x), 2f * Mathf.Min(-b.min.z, b.max.z));
        }
    }
}
