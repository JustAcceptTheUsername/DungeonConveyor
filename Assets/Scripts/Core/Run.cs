using System.Collections.Generic;
using UnityEngine;

namespace DungeonConveyor
{
    public enum Element { Fire, Lightning, Frost, Poison }

    /// <summary>
    /// The roguelike run: a series of stages, each a freshly generated map that is cleared by surviving its waves
    /// (every BossEvery-th wave is a lone boss, so with the default 10 waves each stage ends with one). After every
    /// cleared wave the player picks one of three upgrade cards, each of a rolled rarity (common, rare, epic,
    /// legendary; odds and power in RunSettings), rarer when several waves were cleared together or with Fortune's
    /// Favor taken; when the stage's last wave is cleared the shop opens (crystals banked at the Core unlock buildings)
    /// and the next, bigger stage loads. Upgrades, unlocks, crystals and leftover gold carry over. The difficulty
    /// (picked in the main menu) only changes enemy health and kinds. Losing the Core ends the run.
    /// Static so it survives the scene reload between stages; a new Play (or Restart) starts over.
    /// </summary>
    public static class Run
    {
        public struct StageConfig
        {
            public int Size;     // map is Size x Size cells
            public int Bridges;  // enemy spawn bridges
            public int Cores;    // Cores to defend
            public int Waves;    // waves to clear before the next map
        }

        /// <summary>Stage layouts in order; the last one repeats. Maps never exceed MaxMapSize. (Sizes 1.5x Dido's first pass: more room to build.)</summary>
        static readonly StageConfig[] Stages =
        {
            new StageConfig { Size = 30, Bridges = 1, Cores = 1, Waves = 10 },
            new StageConfig { Size = 45, Bridges = 1, Cores = 1, Waves = 10 },
            new StageConfig { Size = 60, Bridges = 2, Cores = 1, Waves = 10 },
            new StageConfig { Size = 75, Bridges = 2, Cores = 2, Waves = 10 },
            new StageConfig { Size = 90, Bridges = 3, Cores = 2, Waves = 10 },
            new StageConfig { Size = 105, Bridges = 4, Cores = 3, Waves = 10 },
        };

        public const int MaxMapSize = 105;
        /// <summary>Every this many waves (counted across the run) only the boss comes.</summary>
        public const int BossEvery = 10;

        // ---------------------------------------------------------------- upgrades

        /// <summary>Bonuses for one tower type, or for every tower (Global).</summary>
        public class Mods
        {
            public float Damage;    // +fraction
            public float FireRate;  // +fraction (also laser/flame damage per second)
            public float Range;     // +cells
            public readonly float[] Elements = new float[4]; // flat damage per hit, by Element

            public void Add(Mods o)
            {
                Damage += o.Damage;
                FireRate += o.FireRate;
                Range += o.Range;
                for (int i = 0; i < Elements.Length; i++) Elements[i] += o.Elements[i];
            }
        }

        public enum CardIcon { Fire, Lightning, Frost, Poison, Damage, Speed, Range, Gold }

        /// <summary>What a card does. The first four match <see cref="Element"/>.</summary>
        public enum CardKind { Fire, Lightning, Frost, Poison, Damage, FireRate, Range, Income, Fortune }

        public class Card
        {
            public string Title, Tag, Text;
            public Color Color;      // the card's theme (element / stat), used for its tag
            public string TowerId;   // null = every tower
            public Mods Mods = new Mods();
            public float Income;     // +fraction of gold from crystals
            public float Luck;       // + rarity luck for the rest of the run (Fortune's Favor)
            public CardIcon Icon;
            public CardKind Kind;
            public Rarity Rarity;
            public bool PerTower;    // a one-tower-type card (in the card gallery: not tied to a tower yet)
        }

        public static readonly Mods Global = new Mods();
        static readonly Dictionary<string, Mods> perTower = new Dictionary<string, Mods>();
        public static readonly List<Card> Taken = new List<Card>();

        /// <summary>Multiplies gold from crystals sold at a Core.</summary>
        public static float IncomeMultiplier { get; private set; } = 1f;

        /// <summary>Rarity luck from cards (Fortune's Favor); see RunSettings.LuckPerExtraWave for how luck works.</summary>
        public static float Luck { get; private set; }

        public static readonly string[] RarityNames = { "COMMON", "RARE", "EPIC", "LEGENDARY" };
        /// <summary>Common gray, rare green, epic purple, legendary gold.</summary>
        public static readonly Color[] RarityColors =
        {
            new Color(0.72f, 0.72f, 0.74f), new Color(0.3f, 0.88f, 0.38f), new Color(0.72f, 0.4f, 1f), new Color(1f, 0.78f, 0.2f),
        };
        public static Color ColorOf(Rarity r) => RarityColors[(int)r];

        /// <summary>Chance of each rarity (sums to 1) for a card rolled with this much luck.</summary>
        public static float[] RarityChances(float luck)
        {
            var settings = RunSettings.Instance;
            var p = new float[4];
            float total = 0f;
            for (int i = 0; i < 4; i++)
            {
                p[i] = Mathf.Max(0f, settings.Weight((Rarity)i)) * Mathf.Pow(1f + Mathf.Max(0f, luck), i);
                total += p[i];
            }
            for (int i = 0; i < 4; i++) p[i] = total > 0f ? p[i] / total : i == 0 ? 1f : 0f;
            return p;
        }

        /// <summary>Luck for a pick earned by clearing <paramref name="wavesTogether"/> waves at once (run luck included).</summary>
        public static float LuckFor(int wavesTogether) => Luck + RunSettings.Instance.LuckPerExtraWave * Mathf.Max(0, wavesTogether - 1);

        static Rarity RollRarity(float luck)
        {
            var p = RarityChances(luck);
            float roll = Random.value;
            for (int i = 0; i < 3; i++)
            {
                roll -= p[i];
                if (roll < 0f) return (Rarity)i;
            }
            return Rarity.Legendary;
        }

        /// <summary>Global plus this tower type's bonuses.</summary>
        public static Mods For(string towerId)
        {
            var m = new Mods();
            m.Add(Global);
            if (towerId != null && perTower.TryGetValue(towerId, out var own)) m.Add(own);
            return m;
        }

        public static float DamageMultiplier(string id) => 1f + For(id).Damage;
        public static float FireRateMultiplier(string id) => 1f + For(id).FireRate;

        public static readonly Color[] ElementColors =
        {
            new Color(1f, 0.45f, 0.12f), new Color(0.55f, 0.8f, 1f), new Color(0.6f, 0.95f, 1f), new Color(0.55f, 0.95f, 0.25f),
        };
        static readonly string[] ElementNames = { "FIRE", "LIGHTNING", "FROST", "POISON" };
        static readonly string[] ElementEffects =
        {
            "and sets enemies on fire", "and arcs to a nearby enemy", "and chills (slows) enemies", "and poisons enemies (stacking)",
        };

        public static void Apply(Card c)
        {
            if (c.TowerId == null) Global.Add(c.Mods);
            else
            {
                if (!perTower.TryGetValue(c.TowerId, out var m)) perTower[c.TowerId] = m = new Mods();
                m.Add(c.Mods);
            }
            IncomeMultiplier += c.Income;
            Luck += c.Luck;
            Taken.Add(c);
        }

        /// <summary>
        /// Three different upgrade cards, each rolling its own rarity with the given luck (see LuckFor). Tower-specific
        /// ones only name unlocked towers and favour tower types the player has built.
        /// </summary>
        public static List<Card> Draw(BuildingCatalog catalog, ICollection<string> builtIds, float luck)
        {
            var towers = new List<BuildingCatalog.Entry>();
            if (catalog != null)
                foreach (var e in catalog.Entries)
                    if (e.Purchasable && IsUnlocked(e.Id) && e.Attack != AttackKind.None && e.Attack != AttackKind.Slow) towers.Add(e);

            var cards = new List<Card>();
            for (int guard = 0; cards.Count < 3 && guard < 50; guard++)
            {
                var c = Roll(towers, builtIds, RollRarity(luck));
                if (c != null && !cards.Exists(x => x.Title == c.Title && x.Tag == c.Tag)) cards.Add(c);
            }
            return cards;
        }

        static Card Roll(List<BuildingCatalog.Entry> towers, ICollection<string> builtIds, Rarity rarity)
        {
            if (rarity == Rarity.Legendary && Random.value < RunSettings.Instance.FortuneShareOfLegendaries)
                return Make(CardKind.Fortune, rarity, null, false);

            bool perType = towers.Count > 0 && Random.value < 0.45f;
            BuildingCatalog.Entry t = null;
            if (perType)
            {
                var built = towers.FindAll(e => builtIds.Contains(e.Id));
                var from = built.Count > 0 && Random.value < 0.75f ? built : towers;
                t = from[Random.Range(0, from.Count)];
            }

            CardKind kind;
            if (Random.value < 0.55f) kind = (CardKind)Random.Range(0, 4);
            else if (t != null) kind = t.Attack == AttackKind.Mine ? CardKind.Damage : (CardKind)Random.Range((int)CardKind.Damage, (int)CardKind.Range + 1);
            else kind = (CardKind)Random.Range((int)CardKind.Damage, (int)CardKind.Income + 1);
            return Make(kind, rarity, t, t != null);
        }

        /// <summary>
        /// Builds a card. Its bonus is the common amount times the rarity's power (RunSettings). perTower with no tower
        /// makes the gallery version ("one tower type"); global cards are weaker than per-tower ones.
        /// </summary>
        public static Card Make(CardKind kind, Rarity rarity, BuildingCatalog.Entry tower, bool perTowerCard)
        {
            var settings = RunSettings.Instance;
            float p = settings.Power(rarity);
            bool one = perTowerCard;
            string scope = tower != null ? tower.Name.ToUpperInvariant() : one ? "ONE TOWER TYPE" : "ALL TOWERS";
            string who = tower != null ? tower.Name + "s" : one ? "One tower type" : "All towers";
            // "One tower type" is singular: "deals", not "deal".
            string V(string verb) => tower == null && one ? verb + (verb.EndsWith("h") ? "es" : "s") : verb;
            var c = new Card { Kind = kind, Rarity = rarity, TowerId = tower?.Id, PerTower = one };

            if (kind <= CardKind.Poison)
            {
                int el = (int)kind;
                int amount = Mathf.Max(1, Mathf.RoundToInt((one ? 6 : 3) * p));
                c.Mods.Elements[el] = amount;
                c.Title = $"+{amount} {ElementNames[el]}";
                c.Tag = "ELEMENT · " + scope;
                c.Text = $"{who}: each hit deals +{amount} {ElementNames[el].ToLowerInvariant()} damage {ElementEffects[el]}.";
                c.Color = ElementColors[el];
                c.Icon = (CardIcon)el; // Element and CardIcon list the four elements in the same order
                return c;
            }

            c.Color = new Color(1f, 0.78f, 0.35f);
            c.Tag = "STAT · " + scope;
            switch (kind)
            {
                case CardKind.Damage:
                    c.Mods.Damage = Round((one ? 0.3f : 0.12f) * p);
                    c.Icon = CardIcon.Damage;
                    c.Title = $"+{c.Mods.Damage * 100f:0}% DAMAGE";
                    c.Text = $"{who} {V("deal")} {c.Mods.Damage * 100f:0}% more damage.";
                    break;
                case CardKind.FireRate:
                    c.Mods.FireRate = Round((one ? 0.25f : 0.1f) * p);
                    c.Icon = CardIcon.Speed;
                    c.Title = $"+{c.Mods.FireRate * 100f:0}% ATTACK SPEED";
                    c.Text = $"{who} {V("attack")} {c.Mods.FireRate * 100f:0}% faster.";
                    break;
                case CardKind.Range:
                    c.Mods.Range = Mathf.Round((one ? 1f : 0.5f) * p * 10f) / 10f;
                    c.Icon = CardIcon.Range;
                    c.Title = $"+{c.Mods.Range:0.#} RANGE";
                    c.Text = $"{who} {V("reach")} {c.Mods.Range:0.#} {(c.Mods.Range == 1f ? "cell" : "cells")} further.";
                    break;
                case CardKind.Income:
                    c.Income = Round(0.2f * p);
                    c.Icon = CardIcon.Gold;
                    c.Tag = "ECONOMY";
                    c.Title = $"+{c.Income * 100f:0}% CRYSTAL GOLD";
                    c.Text = $"Crystals sold at the Core pay {c.Income * 100f:0}% more gold.";
                    c.Color = UIStyle.GoldColor;
                    break;
                default: // Fortune
                    c.Luck = settings.FortuneLuck;
                    c.Icon = CardIcon.Gold;
                    c.Tag = "FORTUNE · WHOLE RUN";
                    c.Title = "FORTUNE'S FAVOR";
                    c.Text = "For the rest of the run, rare, epic and legendary cards turn up more often.";
                    c.Color = ColorOf(Rarity.Legendary);
                    break;
            }
            return c;
        }

        /// <summary>Percent bonuses rounded to whole percents.</summary>
        static float Round(float fraction) => Mathf.Round(fraction * 100f) / 100f;

        /// <summary>Every card that can show up, for the gallery: each kind at each rarity, per-tower ones untied.</summary>
        public static List<Card> AllCards()
        {
            var list = new List<Card>();
            for (int r = 0; r < 4; r++)
            {
                var rarity = (Rarity)r;
                for (var k = CardKind.Fire; k <= CardKind.Income; k++) list.Add(Make(k, rarity, null, false));
                for (var k = CardKind.Fire; k <= CardKind.Range; k++) list.Add(Make(k, rarity, null, true));
            }
            list.Add(Make(CardKind.Fortune, Rarity.Legendary, null, false));
            return list;
        }

        // ---------------------------------------------------------------- buildings and crystals

        static readonly HashSet<string> unlocked = new HashSet<string>();
        static bool unlocksSeeded;

        /// <summary>Crystals banked this run (delivered to a Core); spent in the shop between maps.</summary>
        public static int Crystals { get; private set; }

        public static void AddCrystals(int n) => Crystals += Mathf.Max(0, n);

        /// <summary>Starting buildings, plus whatever was bought in the shop this run.</summary>
        public static bool IsUnlocked(string id)
        {
            if (!unlocksSeeded)
            {
                unlocksSeeded = true;
                foreach (var s in RunSettings.Instance.StartingBuildings) unlocked.Add(s);
            }
            return unlocked.Contains(id);
        }

        /// <summary>Buys a building in the shop; false if it is unaffordable or not on sale yet.</summary>
        public static bool TryUnlock(string id)
        {
            if (IsUnlocked(id)) return false;
            var item = RunSettings.Instance.ShopEntry(id);
            if (Crystals < item.Crystals || Stage + 1 < item.AvailableAfterMap) return false;
            Crystals -= item.Crystals;
            unlocked.Add(id);
            return true;
        }

        /// <summary>Opens every building (playtests).</summary>
        public static void UnlockAll(BuildingCatalog catalog)
        {
            IsUnlocked("");
            if (catalog != null) foreach (var e in catalog.Entries) unlocked.Add(e.Id);
        }

        // ---------------------------------------------------------------- difficulty

        const string DifficultyKey = "Difficulty";

        /// <summary>The difficulty picked in the main menu (remembered between sessions).</summary>
        public static int DifficultyIndex
        {
            get
            {
                var list = RunSettings.Instance.Difficulties;
                return Mathf.Clamp(PlayerPrefs.GetInt(DifficultyKey, RunSettings.Instance.DefaultDifficulty), 0, Mathf.Max(0, list.Count - 1));
            }
            set => PlayerPrefs.SetInt(DifficultyKey, value);
        }

        public static RunSettings.Difficulty Difficulty
        {
            get
            {
                var list = RunSettings.Instance.Difficulties;
                return list.Count > 0 ? list[DifficultyIndex] : new RunSettings.Difficulty { Name = "Warden" };
            }
        }

        // ---------------------------------------------------------------- progress

        /// <summary>0-based stage index.</summary>
        public static int Stage { get; private set; }
        /// <summary>Gold brought over from the previous stage.</summary>
        public static int CarriedGold { get; private set; }

        static StageConfig StageAt(int i)
        {
            var c = Stages[Mathf.Min(i, Stages.Length - 1)];
            c.Size = Mathf.Min(c.Size, MaxMapSize);
            c.Waves = Mathf.Max(1, c.Waves);
            return c;
        }

        public static StageConfig Current => StageAt(Stage);

        /// <summary>Waves are numbered across the whole run, so difficulty keeps climbing from stage to stage.</summary>
        public static int FirstWave
        {
            get
            {
                int n = 1;
                for (int i = 0; i < Stage; i++) n += StageAt(i).Waves;
                return n;
            }
        }

        public static int LastWave => FirstWave + Current.Waves - 1;
        public static bool IsBossWave(int wave) => wave > 0 && wave % BossEvery == 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Reset()
        {
            Stage = 0;
            CarriedGold = 0;
            IncomeMultiplier = 1f;
            Luck = 0f;
            Crystals = 0;
            unlocked.Clear();
            unlocksSeeded = false;
            Global.Damage = Global.FireRate = Global.Range = 0f;
            for (int i = 0; i < Global.Elements.Length; i++) Global.Elements[i] = 0f;
            perTower.Clear();
            Taken.Clear();
        }

        /// <summary>Moves on to the next stage, keeping the gold left over.</summary>
        public static void Advance(int goldLeft)
        {
            Stage++;
            CarriedGold = Mathf.Max(0, goldLeft);
        }
    }

    /// <summary>Game speed chosen in the HUD (0.5x to 3x); Time.timeScale follows it unless the game is paused.</summary>
    public static class GameSpeed
    {
        public static readonly float[] Choices = { 0.5f, 1f, 2f, 3f };
        public static float Speed = 1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => Speed = 1f;

        public static void Apply(bool paused) => Time.timeScale = paused ? 0f : Speed;
    }
}
