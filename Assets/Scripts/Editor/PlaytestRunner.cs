using UnityEditor;
using UnityEditor.SceneManagement;

namespace DungeonConveyor.EditorTools
{
    /// <summary>
    /// Entry point for headless playtests:
    /// Unity -batchmode -projectPath P -executeMethod DungeonConveyor.EditorTools.PlaytestRunner.Run -playtest
    ///       [-playtestGames 3] [-playtestMinutes 10] [-playtestSpeed 8] [-playtestReport path] -logFile log.txt
    /// Syncs the catalogs, opens the game scene and enters Play mode; PlaytestBot plays and quits Unity when done.
    /// </summary>
    public static class PlaytestRunner
    {
        public static void Run()
        {
            AssetDatabase.Refresh();
            BuildingCatalogSync.Sync();
            EnemyCatalogSync.Sync();
            ArtLibrarySync.Sync();
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
            EditorApplication.EnterPlaymode();
        }
    }
}
