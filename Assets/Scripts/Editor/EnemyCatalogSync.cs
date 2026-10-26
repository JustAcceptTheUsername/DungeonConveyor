using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace DungeonConveyor.EditorTools
{
    /// <summary>
    /// Keeps Resources/EnemyCatalog.asset in sync with Assets/Models/Enemies: new models get an entry with default
    /// stats, removed ones are dropped, edited stats are left alone. A reworked model can take over an old Id
    /// (Replaces), a boss model goes on its kind's entry instead of becoming an enemy (BossModels), and clip assets
    /// named &lt;Model&gt;_&lt;Action&gt;.asset (EnemyGait) next to a model are linked to its entry: Walk or Fly as the Gait
    /// if it has none, the rest added to Clips (the boss model's go to BossGait and BossClips). A palette
    /// &lt;Model&gt;_Palette.asset (EnemyPalette, from EnemyPaletteSync) goes to Palette (BossPalette for the boss model).
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

        // New model -> the existing catalog Id it replaces (the Id stays, so waves, saves and stats keep working).
        // Only once the new model's file is in the folder: the replaced model's own file is then ignored, so it can stay
        // there for rollback without being re-linked. Until then the old model stays linked.
        static readonly Dictionary<string, string> Replaces = new Dictionary<string, string>
        {
            { "PebbleSkitterer", "Crawler" },
            { "OreEater", "CrystalSpider" },
            { "RockBat", "Wasp" },
            { "MagmaSpitter", "AcidSpitter" },
            { "OreGolem", "Brute" },
        };

        // Boss model -> the catalog Id it is the boss of (Entry.BossModel); never an enemy of its own.
        static readonly Dictionary<string, string> BossModels = new Dictionary<string, string>
        {
            { "OreGolem_Boss", "Brute" },
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

            // Model files and clip assets directly in the folder (sub-folders are free for non-enemy models).
            var files = new List<(string name, string path, string ext, GameObject go)>();
            var present = new HashSet<string>();
            var gaits = new List<EnemyGait>();
            foreach (var guid in AssetDatabase.FindAssets("", new[] { ModelsFolder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetDirectoryName(path).Replace('\\', '/') != ModelsFolder) continue;
                var ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext == ".asset")
                {
                    var gait = AssetDatabase.LoadAssetAtPath<EnemyGait>(path);
                    if (gait != null) gaits.Add(gait);
                    continue;
                }
                if (ext != ".glb" && ext != ".gltf" && ext != ".obj" && ext != ".fbx") continue;
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;
                var name = Path.GetFileNameWithoutExtension(path);
                files.Add((name, path, ext, go));
                present.Add(name);
            }

            var models = new Dictionary<string, GameObject>();
            var modelFile = new Dictionary<string, string>(); // catalog Id -> its model's file name (for the clips)
            var bosses = new Dictionary<string, (GameObject model, string file)>();
            foreach (var (name, path, ext, go) in files)
            {
                bool better = ext == ".glb" || ext == ".gltf";
                if (BossModels.TryGetValue(name, out var bossOf))
                {
                    if (!bosses.ContainsKey(bossOf) || better) bosses[bossOf] = (ModelPrefabs.Prefer(path, go), name);
                    continue;
                }
                if (IsReplaced(name, present)) continue;
                var id = Replaces.TryGetValue(name, out var replaced) ? replaced : name;
                if (models.ContainsKey(id) && !better) continue;
                models[id] = ModelPrefabs.Prefer(path, go);
                modelFile[id] = name;
            }

            bool changed = catalog.Entries.RemoveAll(e => e == null || !models.ContainsKey(e.Id)) > 0;
            foreach (var pair in models)
            {
                var entry = catalog.Entries.Find(e => e.Id == pair.Key);
                if (entry != null)
                {
                    // A replacement model always wins over whatever the entry pointed at before.
                    if (entry.Model == null || (IsReplaced(entry.Id, present) && entry.Model != pair.Value))
                    {
                        entry.Model = pair.Value;
                        changed = true;
                    }
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

            foreach (var entry in catalog.Entries)
            {
                if (modelFile.TryGetValue(entry.Id, out var file))
                {
                    changed |= LinkClips(file, gaits, ref entry.Gait, ref entry.Clips);
                    changed |= LinkPalette(file, ref entry.Palette);
                }
                if (!bosses.TryGetValue(entry.Id, out var boss)) continue;
                if (entry.BossModel != boss.model)
                {
                    entry.BossModel = boss.model;
                    changed = true;
                }
                changed |= LinkClips(boss.file, gaits, ref entry.BossGait, ref entry.BossClips);
                changed |= LinkPalette(boss.file, ref entry.BossPalette);
            }

            if (!changed) return;
            catalog.Entries.Sort((a, b) => a.FirstWave.CompareTo(b.FirstWave));
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log($"Enemy catalog synced: {catalog.Entries.Count} enemies ({CatalogPath}).");
        }

        /// <summary>Links &lt;file&gt;_Palette.asset (EnemyPaletteSync) if nothing is set. Returns whether it changed.</summary>
        static bool LinkPalette(string file, ref EnemyPalette palette)
        {
            if (palette != null) return false;
            palette = AssetDatabase.LoadAssetAtPath<EnemyPalette>(ModelsFolder + "/" + file + "_Palette.asset");
            return palette != null;
        }

        /// <summary>id was taken over by a new model whose file is in the folder.</summary>
        static bool IsReplaced(string id, HashSet<string> present)
        {
            foreach (var pair in Replaces)
                if (pair.Value == id && present.Contains(pair.Key)) return true;
            return false;
        }

        /// <summary>
        /// Links the clip assets &lt;file&gt;_&lt;Action&gt; (an Action without '_'): Walk or Fly becomes the gait if there is
        /// none, the others are added to clips (a Fly too, so it is the loop on a flier). Never unlinks or replaces what is
        /// set. Returns whether anything changed.
        /// </summary>
        static bool LinkClips(string file, List<EnemyGait> gaits, ref EnemyGait gait, ref EnemyGait[] clips)
        {
            bool changed = false;
            var list = new List<EnemyGait>(clips ?? new EnemyGait[0]);
            foreach (var g in gaits)
            {
                if (!g.name.StartsWith(file + "_")) continue;
                var action = g.name.Substring(file.Length + 1);
                if (action.Length == 0 || action.Contains("_")) continue; // OreGolem_Boss_Slam is not OreGolem's
                if ((action == "Walk" || action == "Fly") && gait == null)
                {
                    gait = g;
                    changed = true;
                }
                if (g == gait || list.Contains(g)) continue;
                list.Add(g);
                changed = true;
            }
            if (changed) clips = list.ToArray();
            return changed;
        }
    }
}
