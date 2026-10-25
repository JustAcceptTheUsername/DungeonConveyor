using System.Collections.Generic;
using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>Which volume slider a sound answers to (see Sfx.Settings).</summary>
    public enum SoundChannel { Effects, Music, Ambience, Interface }

    /// <summary>
    /// One sound slot: an FMOD event (empty = silent) with its volume, channel and limits so busy sounds don't pile up.
    /// The Inspector shows a dropdown of the FMOD Studio project's events (see SoundDrawer).
    /// </summary>
    [System.Serializable]
    public class Sound
    {
        [Tooltip("FMOD event path, e.g. event:/Gunshot. Empty = no sound.")]
        public string Event = "";
        [Range(0f, 2f)] public float Volume = 1f;
        public SoundChannel Channel = SoundChannel.Effects;
        [Tooltip("Shortest time between two plays, in seconds.")]
        public float MinGap = 0.05f;
        [Tooltip("Most copies playing within half a second.")]
        public int MaxVoices = 4;
        [Tooltip("Played at its place in the world (3D) rather than flat.")]
        public bool Positional = true;

        public Sound() { }

        public Sound(string path, SoundChannel channel = SoundChannel.Effects, float minGap = 0.05f, int maxVoices = 4, bool positional = true, float volume = 1f)
        {
            Event = path ?? "";
            Channel = channel;
            MinGap = minGap;
            MaxVoices = maxVoices;
            Positional = positional;
            Volume = volume;
        }

        public bool IsSet => !string.IsNullOrEmpty(Event);

        public static Sound Ui(string path) => new Sound(path, SoundChannel.Interface, 0.03f, 4, false);
        public static Sound Jingle(string path) => new Sound(path, SoundChannel.Effects, 0.4f, 1, false);
        public static Sound Loop(string path, SoundChannel channel) => new Sound(path, channel, 0f, 1, false);
    }

    /// <summary>A building's own sounds (towers shoot; every building can be placed and destroyed).</summary>
    [System.Serializable]
    public class BuildingSounds
    {
        public string BuildingId;
        [Tooltip("Each shot or attack (towers). Flamethrower and laser: see Loop.")]
        public Sound Shoot = new Sound();
        [Tooltip("Held while the tower attacks continuously (flame, laser). Empty = Shoot is used instead.")]
        public Sound Loop = new Sound("", SoundChannel.Effects, 0f, 8);
        [Tooltip("When its shot lands (missiles, mortar shells).")]
        public Sound Impact = new Sound();
    }

    /// <summary>An enemy type's own sounds.</summary>
    [System.Serializable]
    public class EnemySounds
    {
        public string EnemyId;
        [Tooltip("When it lands from its meteor. Empty = the default.")]
        public Sound Land = new Sound();
        [Tooltip("When it dies. Empty = the default.")]
        public Sound Death = new Sound();
    }

    /// <summary>
    /// Every sound in the game, by category: music, ambience, towers (one entry per building), explosions, buildings
    /// and economy, enemies (one entry per enemy type), waves, rewards and the interface. Lives at
    /// Resources/SoundLibrary.asset (created and kept in sync with the building and enemy catalogs by the editor, see
    /// SoundLibrarySync); pick each slot's FMOD event in the Inspector. Defaults point at the events of the project in
    /// Assets/FMOD/FmodProject.
    /// </summary>
    [CreateAssetMenu(menuName = "Dungeon/Sound Library")]
    public class SoundLibrary : ScriptableObject
    {
        [Header("Music (loops)")]
        public Sound MenuMusic = Sound.Loop("", SoundChannel.Music);
        public Sound GameMusic = Sound.Loop("", SoundChannel.Music);
        [Tooltip("Replaces the game music while a boss is alive.")]
        public Sound BossMusic = Sound.Loop("", SoundChannel.Music);

        [Header("Ambience (loops)")]
        public Sound GameAmbience = Sound.Loop("event:/DistorterAmbience", SoundChannel.Ambience);
        public Sound MenuAmbience = Sound.Loop("", SoundChannel.Ambience);

        [Header("Towers and buildings: one entry per building")]
        public List<BuildingSounds> Buildings = new List<BuildingSounds>();
        [Tooltip("Shot of a tower without its own entry.")]
        public Sound DefaultShoot = new Sound("event:/Gunshot", SoundChannel.Effects, 0.06f, 4);
        [Tooltip("The Bastion Core's shot.")]
        public Sound CoreShoot = new Sound("event:/GunCannon", SoundChannel.Effects, 0.1f, 2);

        [Header("Explosions and hits")]
        [Tooltip("Missile, mortar and synergy blasts without their own Impact.")]
        public Sound Explosion = new Sound("event:/Boulders", SoundChannel.Effects, 0.08f, 3);
        public Sound MineBlast = new Sound("event:/Boulders", SoundChannel.Effects, 0.08f, 3);
        [Tooltip("Bullet hitting an enemy.")]
        public Sound BulletHit = new Sound("", SoundChannel.Effects, 0.05f, 4);
        public Sound ShieldBreak = new Sound("event:/DCFOLYProp_Glass Vial Drop on Concrete Small Bounces Clink_PSE_FESS_NTCV8", SoundChannel.Effects, 0.1f, 3);

        [Header("Buildings and economy")]
        public Sound Place = new Sound("event:/Thump2", SoundChannel.Effects, 0.05f, 3);
        public Sound PlaceBelt = new Sound("event:/Thump", SoundChannel.Effects, 0.05f, 3);
        [Tooltip("The player removes a building (refunded).")]
        public Sound Demolish = new Sound("event:/DCFOLYMisc_Knuckle Crack Hand 2 Variations_PSE_FESS_kK6i9", SoundChannel.Effects, 0.05f, 3);
        [Tooltip("A building is destroyed (wrecked by saboteurs, a mine going off).")]
        public Sound BuildingDestroyed = new Sound("event:/Boulders", SoundChannel.Effects, 0.1f, 3);
        [Tooltip("Clicking where a building can't go.")]
        public Sound CantPlace = Sound.Ui("");
        public Sound CrystalMined = new Sound("event:/DCFOLYProp_Glass Vial Drop on Concrete Small Bounces Clink_PSE_FESS_NTCV8", SoundChannel.Effects, 0.25f, 2);
        [Tooltip("A crystal sold at the Core.")]
        public Sound CrystalSold = new Sound("event:/Coins", SoundChannel.Effects, 0.12f, 3);
        public Sound CrystalFedToTower = new Sound("event:/Sizzling", SoundChannel.Effects, 0.2f, 2);
        public Sound AlloyFused = new Sound("event:/DCFOLYProp_Glass Vial Drop on Concrete Small Bounces Clink_PSE_FESS_NTCV8", SoundChannel.Effects, 0.2f, 2);
        public Sound CoreHit = new Sound("event:/Thump2", SoundChannel.Effects, 0.08f, 3);
        public Sound CoreDestroyed = Sound.Jingle("event:/BossGrowl");

        [Header("Enemies: one entry per enemy type")]
        public List<EnemySounds> Enemies = new List<EnemySounds>();
        public Sound DefaultLand = new Sound("event:/AntSkittering", SoundChannel.Effects, 0.2f, 2);
        public Sound DefaultDeath = new Sound("event:/Thump", SoundChannel.Effects, 0.05f, 4);
        public Sound MeteorImpact = new Sound("event:/Boulders", SoundChannel.Effects, 0.15f, 3);
        public Sound EliteSpawn = new Sound("event:/Snarl", SoundChannel.Effects, 0.5f, 1, false);
        public Sound Split = new Sound("event:/Snarl", SoundChannel.Effects, 0.2f, 2);
        [Tooltip("A saboteur wrecks a belt tile (also plays BuildingDestroyed).")]
        public Sound BeltWrecked = new Sound("", SoundChannel.Effects, 0.1f, 3);
        [Tooltip("Warning a few seconds before a boss wave.")]
        public Sound BossIncoming = Sound.Jingle("event:/BossGrowl");
        public Sound BossLand = new Sound("event:/BossGrowl", SoundChannel.Effects, 0.5f, 1);
        public Sound BossDeath = new Sound("event:/Boulders", SoundChannel.Effects, 0.5f, 1);

        [Header("Waves")]
        public Sound WaveStart = Sound.Jingle("event:/Alert");
        public Sound WaveCleared = Sound.Jingle("event:/Notification");
        [Tooltip("An elite wave starts.")]
        public Sound EliteWave = Sound.Jingle("event:/Snarl");
        public Sound LandingSiteMoved = Sound.Jingle("event:/Alert");
        public Sound MapCleared = Sound.Jingle("event:/Notification");
        public Sound GameOver = Sound.Jingle("event:/BossGrowl");
        [Tooltip("Every fifth kill of a combo.")]
        public Sound Combo = Sound.Jingle("event:/Swish");

        [Header("Rewards")]
        public Sound CardDeal = Sound.Jingle("event:/Swish");
        public Sound CardPick = Sound.Jingle("event:/Swoosh");
        public Sound RelicGained = Sound.Jingle("event:/Notification");
        public Sound SpireClaimed = Sound.Jingle("event:/Notification");
        public Sound ShopUnlock = Sound.Jingle("event:/Coins");
        public Sound UpgradeBought = Sound.Jingle("event:/Coins");
        [Tooltip("A tutorial checklist step done.")]
        public Sound StepDone = Sound.Jingle("event:/Notification");

        [Header("Interface")]
        public Sound Click = Sound.Ui("event:/ButtonClick");
        [Tooltip("The cursor moves onto a button.")]
        public Sound Hover = Sound.Ui("");
        [Tooltip("Picking a building in the build menu.")]
        public Sound Select = Sound.Ui("event:/ButtonClick");
        [Tooltip("Stopping placing (right-click, Esc).")]
        public Sound Cancel = Sound.Ui("");
        public Sound OpenWindow = Sound.Ui("event:/Swish");
        public Sound CloseWindow = Sound.Ui("event:/Swoosh");
        public Sound Toggle = Sound.Ui("event:/ButtonClick");
        [Tooltip("Played when a volume slider is let go, to hear the level (if its own channel has nothing better).")]
        public Sound SliderPreview = Sound.Ui("event:/Coins");
        public Sound PageTurn = Sound.Ui("event:/Swish");

        // ---------------------------------------------------------------- lookups

        public BuildingSounds For(string buildingId) => Buildings.Find(b => b.BuildingId == buildingId);
        public EnemySounds ForEnemy(string enemyId) => Enemies.Find(e => e.EnemyId == enemyId);

        /// <summary>A tower's shot: its own entry, else the default for its attack.</summary>
        public Sound ShootFor(BuildingCatalog.Entry def)
        {
            var own = For(def.Id);
            if (own != null && own.Shoot.IsSet) return own.Shoot;
            return DefaultShoot;
        }

        public Sound ImpactFor(BuildingCatalog.Entry def)
        {
            var own = def != null ? For(def.Id) : null;
            return own != null && own.Impact.IsSet ? own.Impact : Explosion;
        }

        public Sound LoopFor(BuildingCatalog.Entry def)
        {
            var own = For(def.Id);
            if (own != null && own.Loop.IsSet) return own.Loop;
            return ShootFor(def);
        }

        public Sound LandFor(EnemyCatalog.Entry def)
        {
            var own = def != null ? ForEnemy(def.Id) : null;
            return own != null && own.Land.IsSet ? own.Land : DefaultLand;
        }

        public Sound DeathFor(EnemyCatalog.Entry def)
        {
            var own = def != null ? ForEnemy(def.Id) : null;
            return own != null && own.Death.IsSet ? own.Death : DefaultDeath;
        }

        /// <summary>Default sounds for a building by how it attacks (new entries start from these).</summary>
        public static BuildingSounds DefaultsFor(BuildingCatalog.Entry e)
        {
            var b = new BuildingSounds { BuildingId = e.Id };
            switch (e.Attack)
            {
                case AttackKind.Bullet: b.Shoot = new Sound("event:/Gunshot", SoundChannel.Effects, 0.06f, 4); break;
                case AttackKind.Missile: b.Shoot = new Sound("event:/Rocket", SoundChannel.Effects, 0.1f, 3); b.Impact = new Sound("event:/Boulders", SoundChannel.Effects, 0.08f, 3); break;
                case AttackKind.Mortar: b.Shoot = new Sound("event:/GunCannon", SoundChannel.Effects, 0.1f, 3); b.Impact = new Sound("event:/Boulders", SoundChannel.Effects, 0.08f, 3); break;
                case AttackKind.Laser: b.Shoot = new Sound("event:/Ray", SoundChannel.Effects, 0.35f, 2); break;
                case AttackKind.Flame: b.Loop = new Sound("event:/Flamethrower", SoundChannel.Effects, 0f, 8); break;
                case AttackKind.Tesla: b.Shoot = new Sound("event:/Sizzling2", SoundChannel.Effects, 0.1f, 3); break;
                case AttackKind.Mine: b.Shoot = new Sound("event:/Boulders", SoundChannel.Effects, 0.08f, 3); break;
            }
            return b;
        }

        public static EnemySounds DefaultsFor(EnemyCatalog.Entry e) => new EnemySounds { EnemyId = e.Id };

        /// <summary>Adds an entry for every building and enemy type that lacks one; true if any was added.</summary>
        public bool SyncWithCatalogs(BuildingCatalog buildings, EnemyCatalog enemies)
        {
            bool changed = false;
            if (buildings != null)
                foreach (var e in buildings.Entries)
                    if (e.Id != BuildingCatalog.CoreId && For(e.Id) == null) { Buildings.Add(DefaultsFor(e)); changed = true; }
            if (enemies != null)
                foreach (var e in enemies.Entries)
                    if (ForEnemy(e.Id) == null) { Enemies.Add(DefaultsFor(e)); changed = true; }
            return changed;
        }

        static SoundLibrary instance;
        public static SoundLibrary Instance
        {
            get
            {
                if (instance == null) instance = Resources.Load<SoundLibrary>("SoundLibrary");
                if (instance == null)
                {
                    instance = CreateInstance<SoundLibrary>();
                    instance.SyncWithCatalogs(BuildingCatalog.Instance, EnemyCatalog.Instance);
                }
                return instance;
            }
        }
    }
}
