using System.Collections.Generic;
using UnityEngine;

namespace DungeonConveyor
{
    public class WaveManager : MonoBehaviour
    {
        public const float FirstWaveDelay = 120f;
        const float SpawnInterval = 0.22f;

        public int Wave { get; private set; }
        public float NextWaveIn { get; private set; } = FirstWaveDelay;
        public int Kills { get; private set; }
        public int Alive => Enemy.All.Count + pending.Count;

        readonly Queue<(EnemyKind kind, int spawn, float hp)> pending = new Queue<(EnemyKind, int, float)>();
        float spawnTimer;

        public static float WaveGap(int wave) => Mathf.Max(30f, 50f - wave);

        void Update()
        {
            if (Game.IsGameOver) return;

            NextWaveIn -= Time.deltaTime;
            if (NextWaveIn <= 0f) StartWave();

            spawnTimer -= Time.deltaTime;
            while (pending.Count > 0 && spawnTimer <= 0f)
            {
                spawnTimer += SpawnInterval;
                var (kind, spawn, hp) = pending.Dequeue();
                var cell = Game.Map.SpawnCells[spawn % Game.Map.SpawnCells.Count];
                var pos = GridMap.CellCenter(cell) + new Vector3(Random.Range(-0.4f, 0.4f), 0, Random.Range(-0.4f, 0.4f));
                Enemy.Spawn(kind, pos, hp);
            }
            if (pending.Count == 0) spawnTimer = Mathf.Max(spawnTimer, 0f);
        }

        /// <summary>Start the next wave now. Calling early pays a gold bonus for the skipped time.</summary>
        public int CallEarly()
        {
            int bonus = Mathf.FloorToInt(NextWaveIn * 2f);
            Game.Bank.Grant(ResourceType.Gold, bonus);
            StartWave();
            return bonus;
        }

        void StartWave()
        {
            Wave++;
            NextWaveIn = WaveGap(Wave);
            float hp = 1f + 0.14f * (Wave - 1) + 0.006f * Wave * Wave;

            int spawns = Mathf.Min(Game.Map.SpawnCells.Count, 1 + Wave / 3);
            int spiders = 3 + Wave * 3;
            int runners = Wave >= 3 ? Wave * 2 : 0;
            int brutes = Wave >= 5 ? (Wave - 3) / 2 : 0;
            if (Wave % 10 == 0) brutes += Wave / 5;

            var list = new List<EnemyKind>();
            for (int i = 0; i < spiders; i++) list.Add(EnemyKind.Spider);
            for (int i = 0; i < runners; i++) list.Add(EnemyKind.Runner);
            for (int i = 0; i < brutes; i++) list.Add(EnemyKind.Brute);
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
            for (int i = 0; i < list.Count; i++) pending.Enqueue((list[i], i % spawns, hp));

            Game.Alerts.Push($"Wave {Wave} incoming!");
        }

        public void OnEnemyKilled() => Kills++;
    }
}
