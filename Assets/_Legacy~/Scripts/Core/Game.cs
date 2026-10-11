using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>Service locator for the running game. Populated by GameBootstrap.</summary>
    public static class Game
    {
        public static GridMap Map;
        public static FlowField Flow;
        public static ResourceBank Bank;
        public static WaveManager Waves;
        public static BuildController Build;
        public static CameraController Cam;
        public static AlertSystem Alerts;
        public static Objectives Objectives;
        public static CoreBuilding Core;

        public static bool IsGameOver;
        public static bool Paused { get; private set; }
        public static float Speed { get; private set; } = 1f;

        public static void Reset()
        {
            Map = null; Flow = null; Bank = null; Waves = null; Build = null; Cam = null;
            Alerts = null; Objectives = null; Core = null;
            IsGameOver = false;
            Paused = false;
            Speed = 1f;
            Time.timeScale = 1f;
        }

        public static void TogglePause()
        {
            Paused = !Paused;
            Apply();
        }

        public static void CycleSpeed()
        {
            Speed = Speed >= 3f ? 1f : Speed + 1f;
            Paused = false;
            Apply();
        }

        public static void Apply() => Time.timeScale = Paused || IsGameOver ? 0f : Speed;

        public static void GameOver()
        {
            IsGameOver = true;
            Apply();
        }
    }
}
