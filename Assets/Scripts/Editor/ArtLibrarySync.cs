using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DungeonConveyor.EditorTools
{
    /// <summary>
    /// Creates Resources/ArtLibrary.asset and fills any empty slot from Assets/Models (Crystals, Terrain), including
    /// the five ore types. Slots already set, by hand or earlier, are left alone. Runs after scripts compile and when
    /// those folders change.
    /// </summary>
    [InitializeOnLoad]
    public class ArtLibrarySync : AssetPostprocessor
    {
        const string LibraryPath = "Assets/Resources/ArtLibrary.asset";
        const string Crystals = "Assets/Models/Crystals/";
        const string Terrain = "Assets/Models/Terrain/";

        // Crystal colour suffix -> ore defaults (name, tile colour, value, weight, min distance from base, amount per tile).
        static readonly (string suffix, string name, Color color, int value, float weight, float minDistance, Vector2Int amount)[] OreDefaults =
        {
            ("Blue", "Sapphire", new Color(0.22f, 0.5f, 1f), 5, 5f, 0f, new Vector2Int(25, 45)),
            ("Green", "Emerald", new Color(0.25f, 0.85f, 0.4f), 8, 3f, 6f, new Vector2Int(20, 40)),
            ("Purple", "Amethyst", new Color(0.7f, 0.35f, 1f), 12, 2f, 10f, new Vector2Int(15, 35)),
            ("Gold", "Gold", new Color(1f, 0.75f, 0.2f), 20, 1.2f, 13f, new Vector2Int(12, 28)),
            ("Red", "Ruby", new Color(1f, 0.25f, 0.25f), 32, 0.7f, 16f, new Vector2Int(8, 20)),
        };

        static ArtLibrarySync() => EditorApplication.delayCall += Sync;

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (var p in imported)
                if (p.StartsWith(Crystals) || p.StartsWith(Terrain))
                {
                    EditorApplication.delayCall -= Sync;
                    EditorApplication.delayCall += Sync;
                    return;
                }
        }

        [MenuItem("Dungeon/Sync Art Library")]
        public static void Sync()
        {
            var lib = AssetDatabase.LoadAssetAtPath<ArtLibrary>(LibraryPath);
            if (lib == null)
            {
                if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
                lib = ScriptableObject.CreateInstance<ArtLibrary>();
                AssetDatabase.CreateAsset(lib, LibraryPath);
            }

            bool changed = false;
            if (lib.Ores.Count == 0)
            {
                foreach (var d in OreDefaults)
                    lib.Ores.Add(new OreType
                    {
                        Name = d.name,
                        Color = d.color,
                        Value = d.value,
                        Weight = d.weight,
                        MinBaseDistance = d.minDistance,
                        AmountPerTile = d.amount,
                        Deposits = Models($"Crystal_OreNode_{d.suffix}", $"Crystal_ClusterLarge_{d.suffix}", $"Crystal_ClusterSmall_{d.suffix}"),
                        Item = Model(Crystals + $"Crystal_Single_{d.suffix}"),
                        RefinedItem = Model(Crystals + $"Crystal_ClusterSmall_{d.suffix}"),
                    });
                changed = true;
            }
            if (lib.CliffCorner == null)
                changed |= Set(ref lib.CliffCorner, Model(Terrain + "Models/CliffCorner"));
            changed |= Fill(lib.CliffEdges, Terrain + "Models/CliffEdge_A", Terrain + "Models/CliffEdge_B", Terrain + "Models/CliffEdge_C");
            changed |= Fill(lib.RockWalls, Terrain + "Rocks/RockWall_Low", Terrain + "Rocks/RockWall_Mid", Terrain + "Rocks/RockWall_Square",
                Terrain + "Rocks/RockWall_MidTall", Terrain + "Rocks/RockWall_Tall", Terrain + "Rocks/RockWall_VeryTall");
            changed |= Fill(lib.RockAccents, Terrain + "Rocks/RockWall_Tall_Crystal");

            if (!changed) return;
            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();
            Debug.Log($"Art library synced ({LibraryPath}).");
        }

        static List<GameObject> Models(params string[] names)
        {
            var list = new List<GameObject>();
            foreach (var n in names)
            {
                var m = Model(Crystals + n);
                if (m != null) list.Add(m);
            }
            return list;
        }

        static bool Set<T>(ref T slot, T value) where T : Object
        {
            if (value == null) return false;
            slot = value;
            return true;
        }

        /// <summary>Only fills an empty list, so hand-picked lists survive.</summary>
        static bool Fill(List<GameObject> list, params string[] paths)
        {
            list.RemoveAll(g => g == null);
            if (list.Count > 0) return false;
            foreach (var p in paths)
            {
                var m = Model(p);
                if (m != null) list.Add(m);
            }
            return list.Count > 0;
        }

        /// <summary>The model's prefab (Dungeon > Create Model Prefabs), else its .glb (keeps emissive materials), falling back to .obj.</summary>
        static GameObject Model(string pathWithoutExtension)
        {
            foreach (var ext in new[] { ".glb", ".gltf", ".obj", ".fbx" })
            {
                var path = pathWithoutExtension + ext;
                if (!File.Exists(path)) continue;
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go != null) return ModelPrefabs.Prefer(path, go);
            }
            return null;
        }
    }
}
