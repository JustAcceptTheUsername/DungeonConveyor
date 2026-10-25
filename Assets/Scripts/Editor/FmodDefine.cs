using System.Linq;
using UnityEditor;
using UnityEditor.Build;

namespace DungeonConveyor.EditorTools
{
    /// <summary>
    /// Adds the FMOD_PRESENT scripting define (for every build target the game uses) as soon as the FMOD for Unity
    /// plugin is in the project, and removes it if the plugin is taken out, so Sfx switches between FMOD and silence
    /// without anyone editing Player Settings.
    /// </summary>
    [InitializeOnLoad]
    static class FmodDefine
    {
        const string Define = "FMOD_PRESENT";
        static readonly NamedBuildTarget[] Targets = { NamedBuildTarget.Standalone, NamedBuildTarget.WebGL };

        static FmodDefine()
        {
            bool present = System.AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetType("FMODUnity.RuntimeManager") != null);
            foreach (var target in Targets)
            {
                PlayerSettings.GetScriptingDefineSymbols(target, out var defines);
                bool has = defines.Contains(Define);
                if (has == present) continue;
                var list = defines.ToList();
                if (present) list.Add(Define); else list.Remove(Define);
                PlayerSettings.SetScriptingDefineSymbols(target, list.ToArray());
            }
        }
    }
}
