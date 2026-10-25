using System.Collections.Generic;
using System.IO;
using System.Xml;
using UnityEditor;
using UnityEngine;

namespace DungeonConveyor.EditorTools
{
    /// <summary>
    /// Creates Resources/SoundLibrary.asset if it is missing and adds an entry for every building and enemy type that
    /// lacks one, after scripts compile. Dungeon > Select Sound Library selects it for editing.
    /// </summary>
    [InitializeOnLoad]
    public static class SoundLibrarySync
    {
        const string Path = "Assets/Resources/SoundLibrary.asset";

        static SoundLibrarySync() => EditorApplication.delayCall += () => Ensure();

        static SoundLibrary Ensure()
        {
            var lib = AssetDatabase.LoadAssetAtPath<SoundLibrary>(Path);
            bool created = lib == null;
            if (created)
            {
                if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
                lib = ScriptableObject.CreateInstance<SoundLibrary>();
                AssetDatabase.CreateAsset(lib, Path);
            }
            var buildings = AssetDatabase.LoadAssetAtPath<BuildingCatalog>("Assets/Resources/BuildingCatalog.asset");
            var enemies = AssetDatabase.LoadAssetAtPath<EnemyCatalog>("Assets/Resources/EnemyCatalog.asset");
            if (lib.SyncWithCatalogs(buildings, enemies) || created)
            {
                EditorUtility.SetDirty(lib);
                AssetDatabase.SaveAssets();
                if (created) Debug.Log($"Sound library created ({Path}): pick an FMOD event for every sound slot in the Inspector.");
            }
            return lib;
        }

        [MenuItem("Dungeon/Select Sound Library")]
        static void Select()
        {
            Selection.activeObject = Ensure();
            EditorGUIUtility.PingObject(Selection.activeObject);
        }
    }

    /// <summary>
    /// Inspector for a Sound slot: a dropdown of every event in the FMOD Studio project (read straight from its
    /// metadata, so it works whether or not the banks are built), with the slot's volume, channel and limits under a
    /// foldout.
    /// </summary>
    [CustomPropertyDrawer(typeof(Sound))]
    public class SoundDrawer : PropertyDrawer
    {
        const string ProjectFolder = "Assets/FMOD/FmodProject";
        static string[] events;
        static double readAt;

        /// <summary>Event paths from the Studio project's Metadata folder ("event:/Folder/Name"), re-read every 10 s.</summary>
        static string[] Events()
        {
            if (events != null && EditorApplication.timeSinceStartup - readAt < 10.0) return events;
            readAt = EditorApplication.timeSinceStartup;
            var list = new List<string>();
            try
            {
                var folders = new Dictionary<string, (string name, string parent)>();
                string folderDir = System.IO.Path.Combine(ProjectFolder, "Metadata", "EventFolder");
                if (Directory.Exists(folderDir))
                    foreach (var file in Directory.GetFiles(folderDir, "*.xml"))
                    {
                        var doc = new XmlDocument();
                        doc.Load(file);
                        var obj = doc.SelectSingleNode("//object");
                        if (obj == null) continue;
                        string id = obj.Attributes["id"]?.Value;
                        string cls = obj.Attributes["class"]?.Value;
                        string name = cls == "MasterEventFolder" ? null : doc.SelectSingleNode("//object/property[@name='name']/value")?.InnerText;
                        string parent = doc.SelectSingleNode("//object/relationship[@name='folder']/destination")?.InnerText;
                        if (id != null) folders[id] = (name, parent);
                    }
                string eventDir = System.IO.Path.Combine(ProjectFolder, "Metadata", "Event");
                if (Directory.Exists(eventDir))
                    foreach (var file in Directory.GetFiles(eventDir, "*.xml"))
                    {
                        var doc = new XmlDocument();
                        doc.Load(file);
                        string name = doc.SelectSingleNode("//object[@class='Event']/property[@name='name']/value")?.InnerText;
                        if (string.IsNullOrEmpty(name)) continue;
                        string path = name;
                        string folder = doc.SelectSingleNode("//object[@class='Event']/relationship[@name='folder']/destination")?.InnerText;
                        for (int guard = 0; folder != null && folders.TryGetValue(folder, out var f) && guard < 20; guard++)
                        {
                            if (f.name != null) path = f.name + "/" + path;
                            folder = f.parent;
                        }
                        list.Add("event:/" + path);
                    }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("SoundDrawer: could not read the FMOD project's events: " + e.Message);
            }
            list.Sort(System.StringComparer.OrdinalIgnoreCase);
            list.Insert(0, "(none)");
            return events = list.ToArray();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float line = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            return property.isExpanded ? line * 7 : line;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            float h = EditorGUIUtility.singleLineHeight, step = h + EditorGUIUtility.standardVerticalSpacing;
            var row = new Rect(position.x, position.y, position.width, h);

            // Foldout + event dropdown on one line.
            var labelRect = new Rect(row.x, row.y, EditorGUIUtility.labelWidth, h);
            property.isExpanded = EditorGUI.Foldout(labelRect, property.isExpanded, label, true);
            var evt = property.FindPropertyRelative("Event");
            var all = Events();
            int index = string.IsNullOrEmpty(evt.stringValue) ? 0 : System.Array.IndexOf(all, evt.stringValue);
            var options = all;
            if (index < 0)
            {
                // An event that isn't in the project (renamed or typed by hand): keep it and flag it.
                options = new string[all.Length + 1];
                all.CopyTo(options, 0);
                options[all.Length] = evt.stringValue + "  (missing)";
                index = all.Length;
            }
            var field = new Rect(row.x + EditorGUIUtility.labelWidth, row.y, row.width - EditorGUIUtility.labelWidth, h);
            int picked = EditorGUI.Popup(field, index, options);
            if (picked != index) evt.stringValue = picked == 0 ? "" : picked < all.Length ? all[picked] : evt.stringValue;

            if (property.isExpanded)
            {
                EditorGUI.indentLevel++;
                foreach (var name in new[] { "Event", "Volume", "Channel", "MinGap", "MaxVoices", "Positional" })
                {
                    row.y += step;
                    EditorGUI.PropertyField(row, property.FindPropertyRelative(name));
                }
                EditorGUI.indentLevel--;
            }
            EditorGUI.EndProperty();
        }
    }
}
