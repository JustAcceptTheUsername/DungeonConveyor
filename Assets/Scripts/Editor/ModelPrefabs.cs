using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DungeonConveyor.EditorTools
{
    /// <summary>
    /// Mirrors every model in Assets/Models as an editable prefab variant in Assets/Prefabs/Models (same sub-folders,
    /// one per base name, .glb preferred over .obj), so extra nodes like effect origins can be added without touching
    /// the imported files. Existing prefabs are never overwritten. The building/enemy catalogs and the art library are
    /// then repointed from the raw models to these prefabs, and the sync tools prefer a prefab when one exists.
    /// </summary>
    public static class ModelPrefabs
    {
        public const string ModelsRoot = "Assets/Models";
        public const string PrefabsRoot = "Assets/Prefabs/Models";

        static readonly string[] ModelExtensions = { ".glb", ".gltf", ".obj", ".fbx" };

        static readonly string[] Users =
        {
            "Assets/Resources/BuildingCatalog.asset",
            "Assets/Resources/EnemyCatalog.asset",
            "Assets/Resources/ArtLibrary.asset",
        };

        [MenuItem("Dungeon/Create Model Prefabs")]
        public static void CreateAll()
        {
            var sources = new Dictionary<string, GameObject>(); // prefab path -> preferred model
            foreach (var guid in AssetDatabase.FindAssets("", new[] { ModelsRoot }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var ext = Path.GetExtension(path).ToLowerInvariant();
                if (System.Array.IndexOf(ModelExtensions, ext) < 0) continue;
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;
                var target = PrefabPathFor(path);
                if (!sources.ContainsKey(target) || ext == ".glb" || ext == ".gltf") sources[target] = go;
            }

            int created = 0;
            var remap = new Dictionary<Object, Object>();
            foreach (var pair in sources)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(pair.Key);
                if (prefab == null)
                {
                    EnsureFolder(Path.GetDirectoryName(pair.Key).Replace('\\', '/'));
                    var instance = PrefabUtility.InstantiatePrefab(pair.Value) as GameObject ?? Object.Instantiate(pair.Value);
                    instance.name = pair.Value.name;
                    prefab = PrefabUtility.SaveAsPrefabAsset(instance, pair.Key); // a variant, so model re-imports still flow through
                    Object.DestroyImmediate(instance);
                    if (prefab == null) continue;
                    created++;
                }
                // Both the .glb and the .obj of a model map to its prefab.
                foreach (var ext in ModelExtensions)
                {
                    var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPathFor(pair.Key, ext));
                    if (model != null) remap[model] = prefab;
                }
            }

            int repointed = 0;
            foreach (var userPath in Users)
            {
                var asset = AssetDatabase.LoadAssetAtPath<Object>(userPath);
                if (asset == null) continue;
                var so = new SerializedObject(asset);
                var it = so.GetIterator();
                while (it.Next(true))
                    if (it.propertyType == SerializedPropertyType.ObjectReference && it.objectReferenceValue != null
                        && remap.TryGetValue(it.objectReferenceValue, out var prefab))
                    {
                        it.objectReferenceValue = prefab;
                        repointed++;
                    }
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"Model prefabs: {created} created, {sources.Count - created} already existed, {repointed} catalog references repointed ({PrefabsRoot}).");
        }

        /// <summary>The prefab made for a model (by path, extension ignored), or the model itself if there is none.</summary>
        public static GameObject Prefer(string modelPath, GameObject model)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPathFor(modelPath));
            return prefab != null ? prefab : model;
        }

        static string PrefabPathFor(string modelPath)
        {
            var rel = modelPath.Substring(ModelsRoot.Length);
            return PrefabsRoot + Path.ChangeExtension(rel, ".prefab").Replace('\\', '/');
        }

        static string ModelPathFor(string prefabPath, string ext) =>
            ModelsRoot + Path.ChangeExtension(prefabPath.Substring(PrefabsRoot.Length), ext).Replace('\\', '/');

        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            var parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
