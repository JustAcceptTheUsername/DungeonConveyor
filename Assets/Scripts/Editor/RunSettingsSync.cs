using UnityEditor;
using UnityEngine;

namespace DungeonConveyor.EditorTools
{
    /// <summary>
    /// Creates Resources/RunSettings.asset (card rarity odds, difficulties, shop) with its defaults if it is missing,
    /// after scripts compile. Dungeon > Select Run Settings selects it for editing.
    /// </summary>
    [InitializeOnLoad]
    public static class RunSettingsSync
    {
        const string Path = "Assets/Resources/RunSettings.asset";

        static RunSettingsSync() => EditorApplication.delayCall += () => Ensure();

        static RunSettings Ensure()
        {
            var settings = AssetDatabase.LoadAssetAtPath<RunSettings>(Path);
            if (settings != null)
            {
                // Grammar fixes for descriptions created by an earlier version (edited ones are left alone).
                bool changed = false;
                foreach (var d in settings.Difficulties)
                {
                    if (d.Description == "For seasoned keepers. Enemies have 35% more health, tougher creatures arrive 3 waves early and make up more of each wave.")
                    { d.Description = RunSettings.OverseerText; changed = true; }
                    if (d.Description == "The deep wakes. Enemies have 80% more health, the worst creatures crawl out 6 waves early and swarm every wave.")
                    { d.Description = RunSettings.AbyssalText; changed = true; }
                }
                if (changed) { EditorUtility.SetDirty(settings); AssetDatabase.SaveAssets(); }
                return settings;
            }
            if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
            settings = ScriptableObject.CreateInstance<RunSettings>();
            AssetDatabase.CreateAsset(settings, Path);
            AssetDatabase.SaveAssets();
            Debug.Log($"Run settings created ({Path}): card rarity odds, difficulties and the building shop.");
            return settings;
        }

        [MenuItem("Dungeon/Select Run Settings")]
        static void Select()
        {
            Selection.activeObject = Ensure();
            EditorGUIUtility.PingObject(Selection.activeObject);
        }
    }
}
