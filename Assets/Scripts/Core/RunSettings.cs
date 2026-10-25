using System.Collections.Generic;
using UnityEngine;

namespace DungeonConveyor
{
    public enum Rarity { Common, Rare, Epic, Legendary }

    /// <summary>
    /// Tuning for the roguelike run, in one place: upgrade-card rarity odds and power, the rarity boost for clearing
    /// several waves at once, the difficulties, and the building shop. Lives at Resources/RunSettings.asset (created
    /// by the editor, see RunSettingsSync); edit it in the Inspector. Without the asset the defaults below are used.
    /// </summary>
    [CreateAssetMenu(menuName = "Dungeon/Run Settings")]
    public class RunSettings : ScriptableObject
    {
        [Header("Card picks")]
        [Tooltip("An upgrade card is earned each time this many waves have been cleared (waves 5, 10, 15... with 5). " +
                 "The game pauses for the pick as soon as that wave is cleared.")]
        [Min(1)] public int WavesPerCard = 5;

        [Header("Card rarity: base chance weights (relative, they don't need to add up to 100)")]
        public float CommonWeight = 62f;
        public float RareWeight = 26f;
        public float EpicWeight = 10f;
        public float LegendaryWeight = 2f;

        [Header("Card rarity: how strong each rarity's bonus is (x the common card's)")]
        public float CommonPower = 1f;
        public float RarePower = 1.6f;
        public float EpicPower = 2.4f;
        public float LegendaryPower = 3.5f;

        [Header("Rarity luck")]
        [Tooltip("Luck gained per extra wave cleared together with a card wave (2 waves at once = 1 extra). Luck multiplies the weight " +
                 "of each rarity by (1 + luck) ^ tier (Common tier 0 ... Legendary tier 3), so it favours rarer cards.")]
        public float LuckPerExtraWave = 0.6f;
        [Tooltip("Luck added for the rest of the run by the legendary Fortune's Favor card (stacks).")]
        public float FortuneLuck = 0.35f;
        [Tooltip("Chance that a legendary card is Fortune's Favor rather than a big stat or element card.")]
        [Range(0f, 1f)] public float FortuneShareOfLegendaries = 0.3f;

        [System.Serializable]
        public class Difficulty
        {
            public string Name;
            [TextArea] public string Description;
            [Tooltip("Enemy health multiplier (bosses too).")]
            public float HealthMultiplier = 1f;
            [Tooltip("Tougher enemy kinds show up this many waves earlier (negative = later).")]
            public int EnemyUnlockShift;
            [Tooltip("Shifts each wave's mix toward the toughest kinds unlocked: each kind's weight is multiplied by " +
                     "(1 + bias) ^ its toughness rank. 0 = the catalog's weights.")]
            public float TougherEnemyBias;
            [Tooltip("Wave a run starts from by default on this difficulty (rounded down to the start of its map). " +
                     "The player can still change it in the difficulty menu.")]
            [Min(1)] public int StartWave = 1;
        }

        [Header("Starting from a later wave (per map skipped)")]
        [Tooltip("Gold added to the starting gold for each map skipped.")]
        public int GoldPerSkippedMap = 150;
        [Tooltip("Crystals banked for each map skipped.")]
        public int CrystalsPerSkippedMap = 60;
        [Tooltip("Rarity luck of the cards dealt for skipped waves, per map skipped (later maps hand out rarer cards).")]
        public float LuckPerSkippedMap = 0.15f;

        [Header("Difficulty (chosen in the main menu; only changes enemy health and kinds)")]
        public int DefaultDifficulty = 1;
        public List<Difficulty> Difficulties = new List<Difficulty>
        {
            new Difficulty
            {
                Name = "Delver", HealthMultiplier = 0.75f, EnemyUnlockShift = -2, TougherEnemyBias = 0f,
                Description = "A gentle descent. Enemies have 25% less health, and the tougher creatures arrive a little later.",
            },
            new Difficulty
            {
                Name = "Warden", HealthMultiplier = 1f, EnemyUnlockShift = 0, TougherEnemyBias = 0f,
                Description = "The dungeon as intended. Enemies come at full strength, and new creatures appear on schedule.",
            },
            new Difficulty
            {
                Name = "Overseer", HealthMultiplier = 1.35f, EnemyUnlockShift = 3, TougherEnemyBias = 0.5f, StartWave = 11,
                Description = OverseerText,
            },
            new Difficulty
            {
                Name = "Abyssal", HealthMultiplier = 1.8f, EnemyUnlockShift = 6, TougherEnemyBias = 1f, StartWave = 21,
                Description = AbyssalText,
            },
        };

        public const string OverseerText = "For seasoned keepers. Enemies have 35% more health, and tougher creatures arrive 3 waves early and make up more of each wave.";
        public const string AbyssalText = "The deep wakes. Enemies have 80% more health, and the worst creatures crawl out 6 waves early and swarm every wave.";

        [System.Serializable]
        public class ShopItem
        {
            [Tooltip("Building catalog Id.")]
            public string Id;
            [Tooltip("Crystals to unlock it.")]
            public int Crystals = 100;
            [Tooltip("Shows up in the shop after clearing this many maps (1 = after the first map).")]
            public int AvailableAfterMap = 1;
        }

        [Header("Buildings and shop")]
        [Tooltip("Buildings unlocked at the start of every run.")]
        public List<string> StartingBuildings = new List<string> { "Conveyor", "Miner", "Turret", "Flamethrower" };
        [Tooltip("Locked buildings and their unlock price. The shop opens on the map-cleared screen (after the boss).")]
        public List<ShopItem> Shop = new List<ShopItem>
        {
            new ShopItem { Id = "Wall", Crystals = 40 },
            new ShopItem { Id = "Splitter", Crystals = 60 },
            new ShopItem { Id = "LandMine", Crystals = 80 },
            new ShopItem { Id = "Collector", Crystals = 120 },
            new ShopItem { Id = "Mortar", Crystals = 150 },
            new ShopItem { Id = "LaserTower", Crystals = 180, AvailableAfterMap = 2 },
            new ShopItem { Id = "MineShaft", Crystals = 200, AvailableAfterMap = 2 },
            new ShopItem { Id = "MissileLauncher", Crystals = 220, AvailableAfterMap = 2 },
            new ShopItem { Id = "TeslaCoil", Crystals = 260, AvailableAfterMap = 3 },
            new ShopItem { Id = "ShieldGenerator", Crystals = 280, AvailableAfterMap = 3 },
        };
        [Tooltip("Price for a locked building that has no Shop entry.")]
        public int DefaultShopPrice = 150;
        [Tooltip("Crystals banked per crystal delivered to a Core.")]
        public int CrystalsPerItem = 1;
        [Tooltip("Extra crystals banked for a refined crystal.")]
        public int RefinedCrystalBonus = 1;

        public float Weight(Rarity r) =>
            r == Rarity.Common ? CommonWeight : r == Rarity.Rare ? RareWeight : r == Rarity.Epic ? EpicWeight : LegendaryWeight;

        public float Power(Rarity r) =>
            r == Rarity.Common ? CommonPower : r == Rarity.Rare ? RarePower : r == Rarity.Epic ? EpicPower : LegendaryPower;

        public ShopItem ShopEntry(string id)
        {
            var item = Shop.Find(i => i.Id == id);
            return item ?? new ShopItem { Id = id, Crystals = DefaultShopPrice };
        }

        static RunSettings instance;
        public static RunSettings Instance
        {
            get
            {
                if (instance == null) instance = Resources.Load<RunSettings>("RunSettings");
                if (instance == null) instance = CreateInstance<RunSettings>();
                return instance;
            }
        }
    }
}
