using System.Collections.Generic;
using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>
    /// Enemy waves. After a build phase, each wave sends more and tougher enemies (new kinds unlock as waves go on),
    /// shared out between the spawn bridges; every Run.BossEvery-th wave is a lone boss instead. Moves every enemy along
    /// the flow field, which is rebuilt whenever buildings change; enemies that reach the Core damage it.
    /// Each map has a set number of waves (Run.Current.Waves). Every wave whose enemies are all gone earns an upgrade
    /// pick (CardsOwed); once the map's last wave is cleared and picked for, the map is cleared (StageCleared).
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
        /// <summary>Cleared waves still waiting for their upgrade pick.</summary>
        public int CardsOwed => clearedUpTo - picked;
        /// <summary>The wave the current card pick is for.</summary>
        public int PickingFor => picked + 1;
        /// <summary>The three cards on offer while CardsOwed > 0.</summary>
        public List<Run.Card> Offer { get; private set; }
        /// <summary>Kills, bounty gold and leaks of one wave (or, with overlapping waves, of everything cleared together).</summary>
        public struct WaveStats { public int Wave, Kills, Gold, Leaked; }
        /// <summary>Stats of the wave the current card pick is for (snapshotted when the wave counted as cleared).</summary>
        public WaveStats LastCleared => lastCleared;
        /// <summary>Running stats since the last clear.</summary>
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
        int clearedUpTo, picked; // wave numbers
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
            Wave = clearedUpTo = picked = Run.FirstWave - 1;
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

            // Every wave sent so far is over once the field is empty: each one earns a card.
            if (pending.Count == 0 && Enemy.All.Count == 0 && Wave > clearedUpTo)
            {
                clearedUpTo = Wave;
                lastCleared = current;
                lastCleared.Wave = Wave;
                current = default;
            }
            if (CardsOwed > 0 && Offer == null)
            {
                Offer = Run.Draw(build.Catalog, BuiltTowerIds());
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
            picked++;
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
