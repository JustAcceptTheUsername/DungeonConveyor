using System.Collections.Generic;
using UnityEngine;

namespace DungeonConveyor
{
    public enum Element { Fire, Lightning, Frost, Poison }

    /// <summary>
    /// The roguelike run: a series of stages, each a freshly generated map that is cleared by surviving its waves
    /// (every BossEvery-th wave is a lone boss, so with the default 10 waves each stage ends with one). After every
    /// cleared wave the player picks one of three upgrade cards; when the stage's last wave is cleared the next, bigger
    /// stage loads. Upgrades and leftover gold carry over. Losing the Core ends the run.
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

        public class Card
        {
            public string Title, Tag, Text;
            public Color Color;
            public string TowerId;   // null = every tower
            public Mods Mods = new Mods();
            public float Income;     // +fraction of gold from crystals
            public CardIcon Icon;
            public bool Rare;        // per-tower card
        }

        public static readonly Mods Global = new Mods();
        static readonly Dictionary<string, Mods> perTower = new Dictionary<string, Mods>();
        public static readonly List<Card> Taken = new List<Card>();

        /// <summary>Multiplies gold from crystals sold at a Core.</summary>
        public static float IncomeMultiplier { get; private set; } = 1f;

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
            "and set enemies burning", "and arc to a nearby enemy", "and chill enemies (slow)", "and stack poison",
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
            Taken.Add(c);
        }

        /// <summary>Three different upgrade cards. Tower-specific ones favour tower types the player has built.</summary>
        public static List<Card> Draw(BuildingCatalog catalog, ICollection<string> builtIds)
        {
            var towers = new List<BuildingCatalog.Entry>();
            if (catalog != null)
                foreach (var e in catalog.Entries)
                    if (e.Purchasable && e.Attack != AttackKind.None && e.Attack != AttackKind.Slow) towers.Add(e);

            var cards = new List<Card>();
            for (int guard = 0; cards.Count < 3 && guard < 50; guard++)
            {
                var c = Roll(towers, builtIds);
                if (c != null && !cards.Exists(x => x.Title == c.Title && x.Tag == c.Tag)) cards.Add(c);
            }
            return cards;
        }

        static Card Roll(List<BuildingCatalog.Entry> towers, ICollection<string> builtIds)
        {
            bool perType = towers.Count > 0 && Random.value < 0.45f;
            BuildingCatalog.Entry t = null;
            if (perType)
            {
                var built = towers.FindAll(e => builtIds.Contains(e.Id));
                var from = built.Count > 0 && Random.value < 0.75f ? built : towers;
                t = from[Random.Range(0, from.Count)];
            }
            string scope = t != null ? t.Name.ToUpperInvariant() : "ALL TOWERS";
            string who = t != null ? t.Name + "s" : "All towers";
            var c = new Card { TowerId = t?.Id, Rare = t != null };

            float roll = Random.value;
            if (roll < 0.55f)
            {
                var el = (Element)Random.Range(0, 4);
                int amount = t != null ? 6 : 3;
                c.Mods.Elements[(int)el] = amount;
                c.Title = $"+{amount} {ElementNames[(int)el]}";
                c.Tag = "ELEMENT · " + scope;
                c.Text = $"{who}: hits deal +{amount} {ElementNames[(int)el].ToLowerInvariant()} damage {ElementEffects[(int)el]}.";
                c.Color = ElementColors[(int)el];
                c.Icon = (CardIcon)(int)el; // Element and CardIcon list the four elements in the same order
                return c;
            }

            c.Color = new Color(1f, 0.78f, 0.35f);
            c.Tag = "STAT · " + scope;
            int kind = Random.Range(0, t != null ? 3 : 4);
            bool mine = t != null && t.Attack == AttackKind.Mine;
            if (mine) kind = 0; // fire rate and trigger range mean nothing for mines
            switch (kind)
            {
                case 0:
                    c.Mods.Damage = t != null ? 0.3f : 0.12f;
                    c.Icon = CardIcon.Damage;
                    c.Title = $"+{c.Mods.Damage * 100f:0}% DAMAGE";
                    c.Text = $"{who} deal {c.Mods.Damage * 100f:0}% more damage.";
                    break;
                case 1:
                    c.Mods.FireRate = t != null ? 0.25f : 0.1f;
                    c.Icon = CardIcon.Speed;
                    c.Title = $"+{c.Mods.FireRate * 100f:0}% ATTACK SPEED";
                    c.Text = $"{who} attack {c.Mods.FireRate * 100f:0}% faster.";
                    break;
                case 2:
                    c.Mods.Range = t != null ? 1f : 0.5f;
                    c.Icon = CardIcon.Range;
                    c.Title = $"+{c.Mods.Range:0.#} RANGE";
                    c.Text = $"{who} reach {c.Mods.Range:0.#} cells further.";
                    break;
                default:
                    c.Income = 0.2f;
                    c.Icon = CardIcon.Gold;
                    c.Tag = "ECONOMY";
                    c.Title = "+20% CRYSTAL GOLD";
                    c.Text = "Crystals sold at the Core pay 20% more gold.";
                    c.Color = UIStyle.GoldColor;
                    break;
            }
            return c;
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
