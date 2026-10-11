using System.Collections.Generic;
using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>
    /// Enemy waves. After a build phase, each wave sends more and tougher enemies (new kinds unlock as waves go on),
    /// shared out between the spawn bridges. Moves every enemy along the flow field, which is rebuilt whenever
    /// buildings change; enemies that reach the Core damage it.
    /// </summary>
    [RequireComponent(typeof(IsoGrid), typeof(BuildController))]
    public class WaveManager : MonoBehaviour
    {
        public float FirstWaveDelay = 45f;
        public float WaveGap = 35f;
        public float MinWaveGap = 22f;
        public float SpawnInterval = 0.7f;
        [Tooltip("Gold per second of waiting skipped when sending a wave early.")]
        public float EarlyBonusPerSecond = 2f;

        public int Wave { get; private set; }
        public float NextWaveIn { get; private set; }
        public int Kills { get; private set; }
        public int Leaked { get; private set; }
        public int Alive => Enemy.All.Count + pending.Count;
        public FlowField Flow { get; private set; }

        IsoGrid map;
        BuildController build;
        EnemyCatalog catalog;
        Material material;
        Transform root;
        readonly Queue<(EnemyCatalog.Entry def, int bridge, float hp)> pending = new Queue<(EnemyCatalog.Entry, int, float)>();
        float spawnTimer;
        bool flowDirty;

        void Start()
        {
            map = GetComponent<IsoGrid>();
            build = GetComponent<BuildController>();
            catalog = EnemyCatalog.Instance;
            Enemy.All.Clear(); // a restarted game must not inherit the previous game's (destroyed) enemies
            material = GetComponent<MapView>().SpriteMaterial;
            GameArt.EnsureRendered(transform);
            root = new GameObject("Enemies").transform;
            root.SetParent(transform, false);
            Flow = new FlowField(map);
            build.BuildingsChanged += () => flowDirty = true;
            NextWaveIn = FirstWaveDelay;
            if (catalog == null || catalog.Entries.Count == 0)
                Debug.LogWarning("WaveManager: Resources/EnemyCatalog.asset missing or empty (Dungeon > Sync Enemy Catalog).");
        }

        void Update()
        {
            if (flowDirty)
            {
                flowDirty = false;
                Flow.Rebuild();
            }
            if (build.Core == null || build.Core.Destroyed) return;

            NextWaveIn -= Time.deltaTime;
            if (NextWaveIn <= 0f) StartWave();

            spawnTimer -= Time.deltaTime;
            while (pending.Count > 0 && spawnTimer <= 0f && map.Bridges.Count > 0)
            {
                spawnTimer += SpawnInterval;
                var (def, bridge, hp) = pending.Dequeue();
                Enemy.Spawn(def, hp, map, map.Bridges[bridge % map.Bridges.Count], root, material);
            }
            if (pending.Count == 0) spawnTimer = Mathf.Max(spawnTimer, 0f);

            for (int i = Enemy.All.Count - 1; i >= 0; i--)
            {
                var e = Enemy.All[i];
                if (!e.Step(Time.deltaTime, Flow)) continue;
                build.Core.TakeDamage(e.Def.CoreDamage);
                Leaked++;
                Fx.Burst(e.transform.position, new Color(1f, 0.3f, 0.2f), 0.6f);
                e.Remove();
            }
        }

        /// <summary>Called by Combat when an enemy dies: pay the bounty.</summary>
        public void OnKilled(Enemy e)
        {
            Kills++;
            build.Earn(e.Def.Bounty);
        }

        /// <summary>Start the next wave now; pays a bonus for the skipped waiting time.</summary>
        public void CallEarly()
        {
            if (build.Core == null || build.Core.Destroyed) return;
            build.Earn(Mathf.FloorToInt(Mathf.Max(0f, NextWaveIn) * EarlyBonusPerSecond));
            StartWave();
        }

        public int EarlyBonus => Mathf.FloorToInt(Mathf.Max(0f, NextWaveIn) * EarlyBonusPerSecond);

        void StartWave()
        {
            Wave++;
            NextWaveIn = Mathf.Max(MinWaveGap, WaveGap - Wave * 0.5f);
            if (catalog == null || catalog.Entries.Count == 0) return;

            float hp = 1f + 0.15f * (Wave - 1) + 0.012f * Wave * Wave;
            int count = 4 + Wave * 2 + Wave * Wave / 8;
            int bridges = Mathf.Clamp(1 + Wave / 3, 1, Mathf.Max(1, map.Bridges.Count));

            var unlocked = catalog.Entries.FindAll(e => e.FirstWave <= Wave);
            if (unlocked.Count == 0) unlocked.Add(catalog.Entries[0]);
            float total = 0f;
            foreach (var e in unlocked) total += e.Weight;

            var list = new List<EnemyCatalog.Entry>();
            for (int i = 0; i < count; i++)
            {
                float roll = Random.value * total;
                var pick = unlocked[0];
                foreach (var e in unlocked)
                {
                    roll -= e.Weight;
                    if (roll <= 0f) { pick = e; break; }
                }
                list.Add(pick);
            }
            // Every fifth wave ends with a heavy hitter.
            if (Wave % 5 == 0) list.Add(unlocked[unlocked.Count - 1]);
            for (int i = 0; i < list.Count; i++) pending.Enqueue((list[i], i % bridges, hp));
        }
    }
}
