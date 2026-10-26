using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace DungeonConveyor.EditorTools
{
    /// <summary>
    /// Makes the EnemyPalette assets from the exporter's &lt;Id&gt;_palette.json sidecars (written next to each staged
    /// glb by blender-work/_tools/dc_enemy_export.py): Assets/Models/Enemies/&lt;Id&gt;_Palette.asset, one slot per role
    /// with the authored colour, the band lift baked into the mesh and the export mode. Re-importing updates an
    /// asset in place (its GUID and catalog links stay) and keeps any colour edited since (a slot whose Color is no
    /// longer its old Exported colour). EnemyCatalogSync then links &lt;Model&gt;_Palette to Entry.Palette or BossPalette.
    /// </summary>
    public static class EnemyPaletteSync
    {
        const string ModelsFolder = "Assets/Models/Enemies";

        [MenuItem("Dungeon/Import Enemy Palettes...")]
        static void ImportMenu()
        {
            var dir = EditorUtility.OpenFolderPanel("Folder with <Id>_palette.json sidecars", "", "");
            if (string.IsNullOrEmpty(dir)) return;
            Debug.Log(ImportFolder(dir));
        }

        /// <summary>Imports every *_palette.json in dir (for models present in Assets/Models/Enemies). Returns a report.</summary>
        public static string ImportFolder(string dir)
        {
            var report = new System.Text.StringBuilder();
            foreach (var file in Directory.GetFiles(dir, "*_palette.json"))
            {
                var id = Path.GetFileName(file);
                id = id.Substring(0, id.Length - "_palette.json".Length);
                if (AssetDatabase.LoadAssetAtPath<GameObject>(ModelsFolder + "/" + id + ".glb") == null)
                {
                    report.AppendLine(id + ": no " + id + ".glb in " + ModelsFolder + ", skipped");
                    continue;
                }
                report.AppendLine(Import(File.ReadAllText(file), id));
            }
            AssetDatabase.SaveAssets();
            EnemyCatalogSync.Sync();
            return report.ToString();
        }

        /// <summary>Creates or updates &lt;id&gt;_Palette.asset from one sidecar's text.</summary>
        public static string Import(string json, string id)
        {
            string path = ModelsFolder + "/" + id + "_Palette.asset";
            var palette = AssetDatabase.LoadAssetAtPath<EnemyPalette>(path);
            bool created = palette == null;
            if (created) palette = ScriptableObject.CreateInstance<EnemyPalette>();
            bool compensated = !Regex.IsMatch(json, "\"mode\"\\s*:\\s*\"raw\"");
            var slots = new System.Collections.Generic.List<EnemyPalette.Slot>();
            var log = new System.Text.StringBuilder(id + ":");
            // "Stone": { "role": "stone", "material": "DCX_Stone", "hex": "#4C525D", "kind": "lit", "band_lift": 3.0, ...
            foreach (Match m in Regex.Matches(json,
                         "\"(\\w+)\"\\s*:\\s*\\{[^{}]*?\"hex\"\\s*:\\s*\"#([0-9A-Fa-f]{6})\"[^{}]*?\"band_lift\"\\s*:\\s*([0-9.]+|null)"))
            {
                if (!System.Enum.TryParse(m.Groups[1].Value, out PaletteRole role))
                {
                    log.Append(" WARN unknown role " + m.Groups[1].Value + " (a second colour on one role? not recolourable);");
                    continue;
                }
                var exported = EnemyPalette.Hex(System.Convert.ToUInt32(m.Groups[2].Value, 16));
                float lift = m.Groups[3].Value == "null" ? 1f : float.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
                var old = palette.Find(role);
                // An unedited slot follows the export; an edited one keeps its colour.
                var color = old != null && EnemyPalette.HexOf(old.Color) != EnemyPalette.HexOf(old.Exported) ? old.Color : exported;
                slots.Add(new EnemyPalette.Slot { Role = role, Color = color, Exported = exported, BandLift = lift, ExportedCompensated = compensated });
                log.Append(" " + role + " #" + m.Groups[2].Value.ToUpperInvariant() + (lift != 1f ? " x" + lift : "") + ";");
            }
            slots.Sort((a, b) => a.Role.CompareTo(b.Role));
            palette.Slots = slots.ToArray();
            if (created) AssetDatabase.CreateAsset(palette, path);
            else EditorUtility.SetDirty(palette);
            return (created ? "created " : "updated ") + path + " " + log;
        }
    }
}
