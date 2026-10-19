using System.Collections.Generic;
using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>
    /// Enemy waves. After a build phase, each wave sends more and tougher enemies (new kinds unlock as waves go on),
    /// shared out between the spawn bridges; every Run.BossEvery-th wave is a lone boss instead. Moves every enemy along
    /// the flow field, which is rebuilt whenever buildings change; enemies that reach the Core damage it.
    /// Each map has a set number of waves (Run.Current.Waves). Clearing every RunSettings.WavesPerCard-th wave (all its
    /// enemies gone) earns an upgrade pick (CardsOwed); once the map's last wave is cleared and picked for, the map is
    /// cleared (StageCleared).
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
        [Tooltip("Boss health as a share of the total health of the normal wave it replaces.")]
        public float BossHealthShare = 0.7f;
        [Tooltip("Picks a card automatically instead of pausing for the player (PlaytestBot).")]
        public bool AutoPickCards;

        public int Wave { get; private set; }
        public float NextWaveIn { get; private set; }
        public int Kills { get; private set; }
        public int Leaked { get; private set; }
        public int Alive => Enemy.All.Count + pending.Count;
        public FlowField Flow { get; private set; }
        /// <summary>Waves of this map, numbered across the run.</summary>
        public int FirstWave => Run.FirstWave;
        public int LastWave => Run.LastWave;
        public bool AllWavesSent => Wave >= LastWave;
        public bool BossNext => Run.IsBossWave(Wave + 1) && !AllWavesSent;
        /// <summary>Card waves (every RunSettings.WavesPerCard-th) cleared but not yet picked for.</summary>
        public int CardsOwed => owed.Count;
        /// <summary>The wave the current card pick is for.</summary>
        public int PickingFor => owed.Count > 0 ? owed.Peek() : clearedUpTo;
        /// <summary>The next wave whose clearing earns a card.</summary>
        public int NextCardWave
        {
            get
            {
                int every = Mathf.Max(1, RunSettings.Instance.WavesPerCard);
                return (clearedUpTo / every + 1) * every;
            }
        }
        /// <summary>The three cards on offer while CardsOwed > 0.</summary>
        public List<Run.Card> Offer { get; private set; }
        /// <summary>
        /// How many waves the current picks were earned by clearing together (sent before the field was clear). More
        /// than one boosts every pick's rarity (Run.LuckFor).
        /// </summary>
        public int ClearedTogether { get; private set; } = 1;
        /// <summary>Rarity luck of the current picks.</summary>
        public float PickLuck => Run.LuckFor(ClearedTogether);
        /// <summary>Kills, bounty gold and leaks of the waves since the last card.</summary>
        public struct WaveStats { public int Wave, Kills, Gold, Leaked; }
        /// <summary>Stats of the waves the current card pick is for (snapshotted when its card wave was cleared).</summary>
        public WaveStats LastCleared => lastCleared;
        /// <summary>Running stats since the last card.</summary>
        public WaveStats Current => new WaveStats { Wave = Wave, Kills = current.Kills, Gold = current.Gold, Leaked = current.Leaked };
        /// <summary>The boss while it is alive, else null.</summary>
        public Enemy Boss
        {
            get
            {
                foreach (var e in Enemy.All)
                    if (e.IsBoss && e.Health > 0f) return e;
                return null;
            }
        }
        /// <summary>The boss's Health/MaxHealth when the Core fell (-1 if no boss was alive, or the Core still stands).</summary>
        public float BossHealthAtLoss { get; private set; } = -1f;
        public bool StageCleared => AllWavesSent && clearedUpTo >= Wave && CardsOwed == 0;

        IsoGrid map;
        BuildController build;
        EnemyCatalog catalog;
        Material material;
        Transform root;
        readonly Queue<(EnemyCatalog.Entry def, int bridge, float hp)> pending = new Queue<(EnemyCatalog.Entry, int, float)>();
        float spawnTimer;
        bool flowDirty;
        int clearedUpTo; // wave number
        readonly Queue<int> owed = new Queue<int>(); // card waves cleared, waiting for their pick
        WaveStats current, lastCleared;
        bool lossWatched;

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
            Wave = clearedUpTo = Run.FirstWave - 1;
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
            if (!lossWatched)
            {
                lossWatched = true;
                build.Core.OnDestroyed += () =>
                {
                    var boss = Boss;
                    BossHealthAtLoss = boss != null ? Mathf.Clamp01(boss.Health / boss.MaxHealth) : -1f;
                };
            }

            if (!AllWavesSent)
            {
                NextWaveIn -= Time.deltaTime;
                if (NextWaveIn <= 0f) StartWave();
            }

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
                build.Core.TakeDamage(e.CoreDamage, e.IsBoss);
                Leaked++;
                current.Leaked++;
                Fx.Burst(e.transform.position, new Color(1f, 0.3f, 0.2f), 0.6f);
                e.Remove();
            }

            // Every wave sent so far is over once the field is empty. Each card wave (every WavesPerCard-th) among them
            // earns a card, and the HUD pauses the game for the pick right away.
            if (pending.Count == 0 && Enemy.All.Count == 0 && Wave > clearedUpTo)
            {
                int every = Mathf.Max(1, RunSettings.Instance.WavesPerCard);
                int together = Wave - clearedUpTo;
                for (int w = clearedUpTo + 1; w <= Wave; w++)
                    if (w % every == 0) owed.Enqueue(w);
                clearedUpTo = Wave;
                if (owed.Count > 0)
                {
                    ClearedTogether = together;
                    lastCleared = current;
                    lastCleared.Wave = Wave;
                    current = default;
                }
            }
            if (CardsOwed > 0 && Offer == null)
            {
                Offer = Run.Draw(build.Catalog, BuiltTowerIds(), PickLuck);
                if (AutoPickCards || Offer.Count == 0) Pick(Offer.Count > 0 ? Offer[0] : null);
            }
        }

        HashSet<string> BuiltTowerIds()
        {
            var ids = new HashSet<string>();
            foreach (var b in build.Buildings)
                if (b is Tower t) ids.Add(t.Def.Id);
            return ids;
        }

        /// <summary>Takes one of the offered cards (null skips the pick).</summary>
        public void Pick(Run.Card card)
        {
            if (CardsOwed <= 0) return;
            if (card != null) Run.Apply(card);
            owed.Dequeue();
            Offer = null;
        }

        /// <summary>Called by Combat when an enemy dies: pay the bounty.</summary>
        public void OnKilled(Enemy e)
        {
            Kills++;
            current.Kills++;
            current.Gold += e.Bounty;
            build.Earn(e.Bounty, e.transform.position);
        }

        /// <summary>Start the next wave now; pays a bonus for the skipped waiting time.</summary>
        public void CallEarly()
        {
            if (build.Core == null || build.Core.Destroyed || AllWavesSent) return;
            build.Earn(Mathf.FloorToInt(Mathf.Max(0f, NextWaveIn) * EarlyBonusPerSecond));
            StartWave();
        }

        public int EarlyBonus => AllWavesSent ? 0 : Mathf.FloorToInt(Mathf.Max(0f, NextWaveIn) * EarlyBonusPerSecond);

        void StartWave()
        {
            Wave++;
            NextWaveIn = Mathf.Max(MinWaveGap, WaveGap - Wave * 0.5f);
            if (catalog == null || catalog.Entries.Count == 0) return;

            // Difficulty only changes enemy health and which kinds come (earlier, and more of the tough ones).
            var difficulty = Run.Difficulty;
            float hp = (1f + 0.15f * (Wave - 1) + 0.012f * Wave * Wave) * Mathf.Max(0.05f, difficulty.HealthMultiplier);
            int count = 4 + Wave * 2 + Wave * Wave / 8;
            int bridges = Mathf.Clamp(1 + Wave / 3, 1, Mathf.Max(1, map.Bridges.Count));

            var unlocked = catalog.Entries.FindAll(e => e.FirstWave - difficulty.EnemyUnlockShift <= Wave);
            if (unlocked.Count == 0) unlocked.Add(catalog.Entries[0]);
            unlocked.Sort((a, b) => a.FirstWave.CompareTo(b.FirstWave)); // weakest first, so the last is the toughest
            var weights = new float[unlocked.Count];
            float total = 0f;
            for (int i = 0; i < unlocked.Count; i++)
                total += weights[i] = unlocked[i].Weight * Mathf.Pow(1f + Mathf.Max(0f, difficulty.TougherEnemyBias), i);

            var list = new List<EnemyCatalog.Entry>();
            for (int i = 0; i < count; i++)
            {
                float roll = Random.value * total;
                var pick = unlocked[0];
                for (int k = 0; k < unlocked.Count; k++)
                {
                    roll -= weights[k];
                    if (roll <= 0f) { pick = unlocked[k]; break; }
                }
                list.Add(pick);
            }
            if (Run.IsBossWave(Wave))
            {
                // Only the boss comes: the toughest kind unlocked, carrying most of the wave's health and all its bounty.
                float health = 0f, coreDamage = 0f;
                int bounty = 0;
                foreach (var e in list)
                {
                    health += e.Health * hp;
                    coreDamage += e.CoreDamage;
                    bounty += e.Bounty;
                }
                var kind = unlocked[unlocked.Count - 1];
                var bridge = map.Bridges.Count > 0 ? map.Bridges[Random.Range(0, map.Bridges.Count)] : null;
                if (bridge == null) return;
                var boss = Enemy.Spawn(kind, hp, map, bridge, root, material);
                boss.MakeBoss(health * BossHealthShare, Mathf.Max(kind.CoreDamage, coreDamage * 0.5f), bounty);
                return;
            }
            // Every fifth wave ends with a heavy hitter.
            if (Wave % 5 == 0) list.Add(unlocked[unlocked.Count - 1]);
            for (int i = 0; i < list.Count; i++) pending.Enqueue((list[i], i % bridges, hp));
        }
    }
}
