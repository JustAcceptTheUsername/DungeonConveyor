using System.Collections.Generic;
using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>
    /// Progress kept between runs. Every run, won or lost, earns Essence (per wave cleared, more for bosses and
    /// cleared maps); the main menu's Upgrades screen spends it on permanent upgrades: more starting gold and crystals,
    /// a tougher Core, a fourth card to choose from, buildings unlocked from the start, and other Core types (one is
    /// active at a time). Saved in PlayerPrefs.
    /// </summary>
    public static class Meta
    {
        public enum CoreType { Standard, Bastion, Merchant }

        public class Upgrade
        {
            public string Id, Name, Text;
            public int MaxLevel = 1;
            public int[] Costs;         // per level
            public string UnlocksBuilding;
            public CoreType? Core;
        }

        public static readonly Upgrade[] Upgrades =
        {
            new Upgrade { Id = "gold", Name = "War Chest", Text = "+150 starting gold per level.", MaxLevel = 5, Costs = new[] { 20, 35, 55, 80, 110 } },
            new Upgrade { Id = "crystals", Name = "Crystal Cache", Text = "+50 starting crystals per level.", MaxLevel = 3, Costs = new[] { 25, 45, 70 } },
            new Upgrade { Id = "corehp", Name = "Reinforced Core", Text = "+10% Core health per level.", MaxLevel = 5, Costs = new[] { 20, 35, 55, 80, 110 } },
            new Upgrade { Id = "card", Name = "Fourth Card", Text = "Every upgrade pick offers 4 cards instead of 3.", Costs = new[] { 120 } },
            new Upgrade { Id = "wall", Name = "Mason's Plans", Text = "Walls are unlocked from the start of every run.", Costs = new[] { 30 }, UnlocksBuilding = "Wall" },
            new Upgrade { Id = "splitter", Name = "Logistics Plans", Text = "Splitters are unlocked from the start of every run.", Costs = new[] { 40 }, UnlocksBuilding = "Splitter" },
            new Upgrade { Id = "mortar", Name = "Siege Plans", Text = "Mortars are unlocked from the start of every run.", Costs = new[] { 70 }, UnlocksBuilding = "Mortar" },
            new Upgrade { Id = "bastion", Name = "Bastion Core", Text = "A Core type: it shoots the nearest enemy within 4 cells (pick it in the Core list).", Costs = new[] { 90 }, Core = CoreType.Bastion },
            new Upgrade { Id = "merchant", Name = "Merchant Core", Text = "A Core type: crystals sell for 20% more gold, but the Core has 15% less health.", Costs = new[] { 90 }, Core = CoreType.Merchant },
        };

        public static readonly string[] CoreNames = { "Standard Core", "Bastion Core", "Merchant Core" };
        public static readonly string[] CoreTexts =
        {
            "No frills.",
            "Shoots the nearest enemy within 4 cells.",
            "Crystals sell for 20% more gold; 15% less health.",
        };

        const string EssenceKey = "Meta.Essence", LevelKey = "Meta.Level.", CoreKey = "Meta.Core";

        public static int Essence
        {
            get => PlayerPrefs.GetInt(EssenceKey, 0);
            private set { PlayerPrefs.SetInt(EssenceKey, Mathf.Max(0, value)); PlayerPrefs.Save(); }
        }

        /// <summary>Essence earned so far in the current run (for the end screens).</summary>
        public static int EarnedThisRun { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => EarnedThisRun = 0;

        /// <summary>A new run starts counting from zero.</summary>
        public static void BeginRun() => EarnedThisRun = 0;

        /// <summary>Playtests set this: no Essence earned or spent, upgrades read as level 0, the save untouched.</summary>
        public static bool Disabled;

        public static void Earn(int amount)
        {
            if (amount <= 0 || Disabled) return;
            EarnedThisRun += amount;
            Essence += amount;
        }

        public const int PerWave = 1, PerBoss = 8, PerMap = 15;

        public static int Level(string id) => Disabled ? 0 : PlayerPrefs.GetInt(LevelKey + id, 0);
        public static Upgrade Find(string id) => System.Array.Find(Upgrades, u => u.Id == id);
        public static bool Maxed(Upgrade u) => Level(u.Id) >= u.MaxLevel;
        public static int NextCost(Upgrade u) => Maxed(u) ? 0 : u.Costs[Mathf.Min(Level(u.Id), u.Costs.Length - 1)];

        public static bool TryBuy(Upgrade u)
        {
            if (Disabled || Maxed(u) || Essence < NextCost(u)) return false;
            Essence -= NextCost(u);
            PlayerPrefs.SetInt(LevelKey + u.Id, Level(u.Id) + 1);
            PlayerPrefs.Save();
            return true;
        }

        public static bool CoreOwned(CoreType t) => t == CoreType.Standard || System.Array.Exists(Upgrades, u => u.Core == t && Level(u.Id) > 0);

        public static CoreType ActiveCore
        {
            get
            {
                var t = (CoreType)Mathf.Clamp(PlayerPrefs.GetInt(CoreKey, 0), 0, 2);
                return CoreOwned(t) ? t : CoreType.Standard;
            }
            set { if (Disabled) return; PlayerPrefs.SetInt(CoreKey, (int)value); PlayerPrefs.Save(); }
        }

        // ---------------------------------------------------------------- effects

        public static int StartingGoldBonus => 150 * Level("gold");
        public static int StartingCrystals => 50 * Level("crystals");
        public static float CoreHealthMultiplier => (1f + 0.1f * Level("corehp")) * (ActiveCore == CoreType.Merchant ? 0.85f : 1f);
        public static float CoreSellMultiplier => ActiveCore == CoreType.Merchant ? 1.2f : 1f;
        public static int CardChoices => Level("card") > 0 ? 4 : 3;

        /// <summary>Buildings unlocked from the start by upgrades.</summary>
        public static IEnumerable<string> StartingUnlocks()
        {
            foreach (var u in Upgrades)
                if (u.UnlocksBuilding != null && Level(u.Id) > 0) yield return u.UnlocksBuilding;
        }
    }
}
