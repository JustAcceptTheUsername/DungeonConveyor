using System.Collections.Generic;
using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>The flow fields enemies walk: ground route to the Core, fliers' route, saboteurs' route to the belts.</summary>
    public class EnemyFlows
    {
        public FlowField Ground, Air, Belts;
    }

    /// <summary>
    /// Enemy waves. After a build phase, each wave sends more and tougher enemies (new kinds unlock as waves go on),
    /// shared out between the meteor landing sites; every Run.BossEvery-th wave is a lone boss instead. From wave
    /// EliteFrom on, some waves are elite: every enemy in them carries one modifier (shielded, splitting, saboteurs,
    /// swift, regenerating) and pays more. Every few waves the meteors pick a new landing site (or an extra one).
    /// Moves every enemy along its flow field (rebuilt whenever buildings change); enemies that reach the Core damage
    /// it, saboteurs that reach a belt wreck that tile.
    /// Each map has a set number of waves (Run.Current.Waves). Clearing every RunSettings.WavesPerCard-th wave earns an
    /// upgrade pick and clearing a boss wave a relic pick (one of three); once the map's last wave is cleared and
    /// picked for, the map is cleared (StageCleared). Clearing waves earns Essence for the permanent upgrades (Meta).
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
        public float BossHealthShare = 0.8f;
        [Tooltip("Picks a card automatically instead of pausing for the player (PlaytestBot).")]
        public bool AutoPickCards;

        [Header("Difficulty curve")]
        [Tooltip("Enemy health multiplier: 1 + Linear (wave - 1) + Square wave².")]
        public float HealthLinear = 0.2f;
        public float HealthSquare = 0.018f;
        [Tooltip("Enemies per wave: Base + PerWave wave + wave² / SquareDivisor.")]
        public int CountBase = 5;
        public int CountPerWave = 2;
        public int CountSquareDivisor = 6;

        [Header("Elite waves")]
        [Tooltip("First wave (counted across the run) that can be elite.")]
        public int EliteFrom = 4;
        [Tooltip("Chance that a normal wave from EliteFrom on is elite (every third wave always is).")]
        [Range(0f, 1f)] public float EliteChance = 0.25f;

        [Header("Landing sites")]
        [Tooltip("Every this many waves the meteors pick a new landing site (an extra one while the map allows more).")]
        public int ShiftLandingEvery = 4;
        [Tooltip("Most landing sites a map can grow to, beyond the ones it starts with.")]
        public int ExtraLandingSites = 2;

        public int Wave { get; private set; }
        public float NextWaveIn { get; private set; }
        public int Kills { get; private set; }
        public int Leaked { get; private set; }
        public int Alive => Enemy.All.Count + pending.Count;
        public FlowField Flow => flows.Ground;
        public EnemyFlows Flows => flows;
        /// <summary>Waves of this map, numbered across the run.</summary>
        public int FirstWave => Run.FirstWave;
        public int LastWave => Run.LastWave;
        public bool AllWavesSent => Wave >= LastWave;
        public bool BossNext => Run.IsBossWave(Wave + 1) && !AllWavesSent;
        /// <summary>The modifier of the wave on the field (None if it isn't elite).</summary>
        public EliteModifier CurrentElite { get; private set; }
        /// <summary>Modifier the next wave will have (decided ahead, so the HUD can warn).</summary>
        public EliteModifier NextElite { get; private set; }
        /// <summary>Picks (cards and relics) owed for cleared waves.</summary>
        public int CardsOwed => owed.Count;
        /// <summary>The wave the current pick is for.</summary>
        public int PickingFor => owed.Count > 0 ? owed.Peek().wave : clearedUpTo;
        /// <summary>The current pick is a boss relic (else an upgrade card).</summary>
        public bool PickingRelic => owed.Count > 0 && owed.Peek().relic;
        /// <summary>The next wave whose clearing earns a card.</summary>
        public int NextCardWave
        {
            get
            {
                int every = Mathf.Max(1, RunSettings.Instance.WavesPerCard);
                return (clearedUpTo / every + 1) * every;
            }
        }
        /// <summary>The cards (or relics) on offer while CardsOwed > 0.</summary>
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
        /// <summary>Belt tiles wrecked by saboteurs this map.</summary>
        public int BeltsWrecked { get; private set; }

        /// <summary>Raised when a wave starts: its number and modifier.</summary>
        public event System.Action<int, EliteModifier> WaveStarted;
        /// <summary>Raised when an enemy is killed (after its bounty is paid).</summary>
        public event System.Action<Enemy> Killed;
        /// <summary>Raised when the meteors pick a new landing site (true) or move one (false).</summary>
        public event System.Action<bool> LandingSitesChanged;
        /// <summary>Raised when a saboteur wrecks a belt tile.</summary>
        public event System.Action<Vector3> BeltWrecked;

        IsoGrid map;
        BuildController build;
        EnemyCatalog catalog;
        Material material;
        Transform root;
        readonly EnemyFlows flows = new EnemyFlows();
        readonly Queue<(EnemyCatalog.Entry def, int site, float hp, EliteModifier elite)> pending = new Queue<(EnemyCatalog.Entry, int, float, EliteModifier)>();
        float spawnTimer;
        bool flowDirty;
        int clearedUpTo; // wave number
        readonly Queue<(int wave, bool relic)> owed = new Queue<(int, bool)>(); // cleared waves waiting for their pick
        WaveStats current, lastCleared;
        bool lossWatched;
        int maxSites;

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
            flows.Ground = new FlowField(map);
            flows.Air = new FlowField(map, FlowField.Mode.Air);
            flows.Belts = new FlowField(map, FlowField.Mode.Belts);
            build.BuildingsChanged += () => flowDirty = true;
            NextWaveIn = FirstWaveDelay;
            Wave = clearedUpTo = Run.FirstWave - 1;
            maxSites = map.Spawns.Count + ExtraLandingSites;
            NextElite = RollElite(Wave + 1);
            if (catalog == null || catalog.Entries.Count == 0)
                Debug.LogWarning("WaveManager: Resources/EnemyCatalog.asset missing or empty (Dungeon > Sync Enemy Catalog).");
        }

        void Update()
        {
            if (flowDirty)
            {
                flowDirty = false;
                flows.Ground.Rebuild();
                flows.Air.Rebuild();
                flows.Belts.Rebuild();
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
            while (pending.Count > 0 && spawnTimer <= 0f && map.Spawns.Count > 0)
            {
                spawnTimer += SpawnInterval;
                var (def, site, hp, elite) = pending.Dequeue();
                var e = Enemy.Spawn(def, hp, map, map.Spawns[site % map.Spawns.Count], root, material);
                if (elite != EliteModifier.None) e.MakeElite(elite);
            }
            if (pending.Count == 0) spawnTimer = Mathf.Max(spawnTimer, 0f);

            for (int i = Enemy.All.Count - 1; i >= 0; i--)
            {
                var e = Enemy.All[i];
                var result = e.Step(Time.deltaTime, flows);
                if (result == Enemy.StepResult.ReachedBelt)
                {
                    Wreck(e);
                    continue;
                }
                if (result != Enemy.StepResult.ReachedCore) continue;
                build.Core.TakeDamage(e.CoreDamage * Run.CoreDamageTaken, e.IsBoss);
                Sfx.Play(Sfx.Lib.CoreHit, build.Core.Center);
                if (e.IsBoss) CameraController.Shake(0.8f);
                Leaked++;
                current.Leaked++;
                Fx.Burst(e.transform.position, new Color(1f, 0.3f, 0.2f), 0.6f);
                e.Remove();
            }

            // Every wave sent so far is over once the field is empty. Each card wave (every WavesPerCard-th) among them
            // earns a card and each boss wave a relic; the HUD pauses the game for the picks right away.
            if (pending.Count == 0 && Enemy.All.Count == 0 && Wave > clearedUpTo)
            {
                int every = Mathf.Max(1, RunSettings.Instance.WavesPerCard);
                int together = Wave - clearedUpTo;
                bool anyPick = false;
                for (int w = clearedUpTo + 1; w <= Wave; w++)
                {
                    Meta.Earn(Meta.PerWave + (Run.IsBossWave(w) ? Meta.PerBoss : 0));
                    if (Run.IsBossWave(w)) { owed.Enqueue((w, true)); anyPick = true; }
                    if (w % every == 0) { owed.Enqueue((w, false)); anyPick = true; }
                }
                clearedUpTo = Wave;
                CurrentElite = EliteModifier.None;
                Sfx.Play(Sfx.Lib.WaveCleared);
                if (anyPick)
                {
                    ClearedTogether = together;
                    lastCleared = current;
                    lastCleared.Wave = Wave;
                    current = default;
                }
            }
            if (CardsOwed > 0 && Offer == null)
            {
                Offer = PickingRelic ? Run.DrawRelics(3) : Run.Draw(build.Catalog, BuiltTowerIds(), PickLuck, Meta.CardChoices);
                if (AutoPickCards || Offer.Count == 0) Pick(Offer.Count > 0 ? Offer[0] : null);
            }
        }

        /// <summary>
        /// A saboteur wrecks a belt tile (no refund) and moves on: the one it stands on, or where its lava blob landed
        /// (Enemy.WreckCell).
        /// </summary>
        void Wreck(Enemy e)
        {
            e.BeltsToWreck--;
            if (!(map.BuildingAt(e.WreckCell) is Conveyor belt)) return;
            var at = belt.Center;
            build.RemoveBuilding(belt, refund: false);
            BeltsWrecked++;
            Fx.Burst(at, new Color(1f, 0.6f, 0.25f, 0.9f), 0.7f);
            Sfx.Play(Sfx.Lib.BuildingDestroyed, at);
            Sfx.Play(Sfx.Lib.BeltWrecked, at);
            BeltWrecked?.Invoke(at);
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
            if (card != null)
            {
                Run.Apply(card);
                if (card.Kind == Run.CardKind.Relic) Sfx.Play(Sfx.Lib.RelicGained);
            }
            owed.Dequeue();
            Offer = null;
        }

        /// <summary>Called by Combat when an enemy dies: pay the bounty, split elites.</summary>
        public void OnKilled(Enemy e)
        {
            Kills++;
            current.Kills++;
            int bounty = Mathf.RoundToInt(e.Bounty * Run.BountyMultiplier);
            current.Gold += bounty;
            build.Earn(bounty, e.transform.position);
            if (e.Splits && e.Landed)
            {
                Sfx.Play(Sfx.Lib.Split, e.transform.position);
                for (int i = 0; i < 2; i++)
                {
                    var child = Enemy.Spawn(e.Def, e.MaxHealth / Mathf.Max(1f, e.Def.Health) * 0.35f, map, new IsoGrid.SpawnSite { Landing = e.Cell }, root, material);
                    child.LandNow();
                }
            }
            Killed?.Invoke(e);
        }

        /// <summary>Start the next wave now; pays a bonus for the skipped waiting time.</summary>
        public void CallEarly()
        {
            if (build.Core == null || build.Core.Destroyed || AllWavesSent) return;
            build.Earn(Mathf.FloorToInt(Mathf.Max(0f, NextWaveIn) * EarlyBonusPerSecond));
            StartWave();
        }

        public int EarlyBonus => AllWavesSent ? 0 : Mathf.FloorToInt(Mathf.Max(0f, NextWaveIn) * EarlyBonusPerSecond);

        /// <summary>Whether (and how) a wave is elite. Boss waves never are; saboteurs only come while there are belts.</summary>
        EliteModifier RollElite(int wave)
        {
            if (wave < EliteFrom || Run.IsBossWave(wave)) return EliteModifier.None;
            if (wave % 3 != 0 && Random.value >= EliteChance) return EliteModifier.None;
            var options = new List<EliteModifier> { EliteModifier.Shielded, EliteModifier.Splitting, EliteModifier.Swift, EliteModifier.Regenerating, EliteModifier.Saboteurs };
            return options[Random.Range(0, options.Count)];
        }

        void StartWave()
        {
            Wave++;
            NextWaveIn = Mathf.Max(MinWaveGap, WaveGap - Wave * 0.5f);
            CurrentElite = NextElite;
            if (CurrentElite == EliteModifier.Saboteurs && build.Conveyors.Count == 0) CurrentElite = EliteModifier.Shielded;
            NextElite = RollElite(Wave + 1);
            if (ShiftLandingEvery > 0 && Wave > Run.FirstWave && (Wave - Run.FirstWave) % ShiftLandingEvery == 0) ShiftLandingSites();
            WaveStarted?.Invoke(Wave, CurrentElite);
            bool bossWave = Run.IsBossWave(Wave);
            Sfx.Play(Sfx.Lib.WaveStart);
            if (CurrentElite != EliteModifier.None) { Sfx.Play(Sfx.Lib.EliteWave); Sfx.Play(Sfx.Lib.EliteSpawn); }
            if (catalog == null || catalog.Entries.Count == 0) return;

            // Difficulty only changes enemy health and which kinds come (earlier, and more of the tough ones).
            var difficulty = Run.Difficulty;
            float hp = (1f + HealthLinear * (Wave - 1) + HealthSquare * Wave * Wave) * Mathf.Max(0.05f, difficulty.HealthMultiplier);
            int count = CountBase + Wave * CountPerWave + Wave * Wave / Mathf.Max(1, CountSquareDivisor);
            int sites = Mathf.Clamp(1 + Wave / 3, 1, Mathf.Max(1, map.Spawns.Count));

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
            if (bossWave)
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
                var site = map.Spawns.Count > 0 ? map.Spawns[Random.Range(0, map.Spawns.Count)] : null;
                if (site == null) return;
                var boss = Enemy.Spawn(kind, hp, map, site, root, material);
                boss.MakeBoss(health * BossHealthShare, Mathf.Max(kind.CoreDamage, coreDamage * 0.5f), bounty);
                return;
            }
            // Every fifth wave ends with a heavy hitter.
            if (Wave % 5 == 0) list.Add(unlocked[unlocked.Count - 1]);
            for (int i = 0; i < list.Count; i++) pending.Enqueue((list[i], i % sites, hp, CurrentElite));
        }

        /// <summary>A new landing site while the map can take more, else one of the old ones moves.</summary>
        void ShiftLandingSites()
        {
            bool add = map.Spawns.Count < maxSites;
            if (map.MoveOrAddLandingSite(add)) LandingSitesChanged?.Invoke(add);
            Sfx.Play(Sfx.Lib.LandingSiteMoved);
            flowDirty = true;
        }
    }
}
