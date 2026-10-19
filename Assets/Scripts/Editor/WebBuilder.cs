using System.IO;
using System.IO.Compression;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace DungeonConveyor.EditorTools
{
    /// <summary>
    /// Web (WebGL) build for itch.io: MainMenu → GameScene, compression Disabled, then zips the
    /// build folder so index.html sits at the zip root.
    /// Menu: Dungeon > Build Web (itch.io), or headless:
    /// Unity -batchmode -projectPath P -executeMethod DungeonConveyor.EditorTools.WebBuilder.Build [-webBuildPath dir] -logFile log.txt
    /// </summary>
    public static class WebBuilder
    {
        const string DefaultOutput = "Builds/Web";

        [MenuItem("Dungeon/Build Web (itch.io)")]
        public static void BuildFromMenu() => BuildTo(DefaultOutput);

        public static void Build()
        {
            string output = DefaultOutput;
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-webBuildPath") output = args[i + 1];

            bool ok = BuildTo(output);
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        static bool BuildTo(string output)
        {
            AssetDatabase.Refresh();
            BuildingCatalogSync.Sync();
            EnemyCatalogSync.Sync();
            ArtLibrarySync.Sync();

            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.decompressionFallback = false;

            output = Path.GetFullPath(output);
            if (Directory.Exists(output)) Directory.Delete(output, true);

            var options = new BuildPlayerOptions
            {
                scenes = new[] { SceneSetup.MenuScene, SceneSetup.GameScene },
                locationPathName = output,
                target = BuildTarget.WebGL,
                targetGroup = BuildTargetGroup.WebGL,
                options = BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                Debug.LogError($"[WebBuilder] Build failed: {report.summary.result} ({report.summary.totalErrors} errors)");
                return false;
            }

            string zip = output.TrimEnd('/', '\\') + ".zip";
            if (File.Exists(zip)) File.Delete(zip);
            ZipFile.CreateFromDirectory(output, zip, System.IO.Compression.CompressionLevel.Optimal, false);
            Debug.Log($"[WebBuilder] Build succeeded ({report.summary.totalSize / (1024 * 1024)} MB) → {zip}");
            return true;
        }
    }
}
