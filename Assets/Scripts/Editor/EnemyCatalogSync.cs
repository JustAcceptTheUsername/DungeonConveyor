using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace DungeonConveyor.EditorTools
{
    /// <summary>
    /// Keeps Resources/EnemyCatalog.asset in sync with Assets/Models/Enemies: new models get an entry with default
    /// stats, removed ones are dropped, edited stats are left alone.
    /// </summary>
    [InitializeOnLoad]
    public class EnemyCatalogSync : AssetPostprocessor
    {
        const string ModelsFolder = "Assets/Models/Enemies";
        const string CatalogPath = "Assets/Resources/EnemyCatalog.asset";

        // id -> (health, speed, core damage, bounty, first wave, weight, hover)
        static readonly Dictionary<string, (float, float, float, int, int, float, float)> Defaults = new Dictionary<string, (float, float, float, int, int, float, float)>
        {
            { "Crawler", (40f, 1.5f, 10f, 3, 1, 4f, 0f) },
            { "CrystalSpider", (75f, 1.25f, 15f, 5, 2, 3f, 0f) },
            { "Wasp", (32f, 2.3f, 8f, 4, 4, 2f, 0.35f) },
            { "AcidSpitter", (150f, 0.95f, 30f, 9, 6, 1.5f, 0f) },
            { "Brute", (480f, 0.7f, 90f, 25, 9, 0.6f, 0f) },
        };

        static EnemyCatalogSync() => EditorApplication.delayCall += Sync;

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

        [MenuItem("Dungeon/Sync Enemy Catalog")]
        public static void Sync()
        {
            if (!AssetDatabase.IsValidFolder(ModelsFolder)) return;
            var catalog = AssetDatabase.LoadAssetAtPath<EnemyCatalog>(CatalogPath);
            if (catalog == null)
            {
                if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
                catalog = ScriptableObject.CreateInstance<EnemyCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            var models = new Dictionary<string, GameObject>();
            foreach (var guid in AssetDatabase.FindAssets("", new[] { ModelsFolder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext != ".glb" && ext != ".gltf" && ext != ".obj" && ext != ".fbx") continue;
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;
                var id = Path.GetFileNameWithoutExtension(path);
                if (!models.ContainsKey(id) || ext == ".glb" || ext == ".gltf") models[id] = go;
            }

            bool changed = catalog.Entries.RemoveAll(e => e == null || !models.ContainsKey(e.Id)) > 0;
            foreach (var pair in models)
            {
                var entry = catalog.Entries.Find(e => e.Id == pair.Key);
                if (entry != null)
                {
                    if (entry.Model == null) { entry.Model = pair.Value; changed = true; }
                    continue;
                }
                var (hp, speed, damage, bounty, wave, weight, hover) = Defaults.TryGetValue(pair.Key, out var d) ? d : (60f, 1.2f, 12f, 4, 3, 1f, 0f);
                catalog.Entries.Add(new EnemyCatalog.Entry
                {
                    Id = pair.Key,
                    Name = Regex.Replace(pair.Key, "(?<=[a-z])(?=[A-Z])", " "),
                    Model = pair.Value,
                    Scale = pair.Key == "Brute" ? 0.8f : 0.6f,
                    Health = hp,
                    Speed = speed,
                    CoreDamage = damage,
                    Bounty = bounty,
                    FirstWave = wave,
                    Weight = weight,
                    Hover = hover,
                });
                changed = true;
            }

            if (!changed) return;
            catalog.Entries.Sort((a, b) => a.FirstWave.CompareTo(b.FirstWave));
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log($"Enemy catalog synced: {catalog.Entries.Count} enemies ({CatalogPath}).");
        }
    }
}
