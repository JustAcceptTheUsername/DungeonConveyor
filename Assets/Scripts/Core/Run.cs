using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>
    /// The roguelike run: a series of stages, each a freshly generated map that is cleared by surviving its waves
    /// (the last one ends with a boss). After each boss the player picks one of three upgrade cards and the next,
    /// bigger stage loads; upgrades and leftover gold carry over. Losing any Core ends the run.
    /// Static so it survives the scene reload between stages; a new Play (or New Run) starts over.
    /// </summary>
    public static class Run
    {
        public struct StageConfig
        {
            public int Size;     // map is Size x Size cells
            public int Bridges;  // enemy spawn bridges
            public int Cores;    // Cores to defend
        }

        /// <summary>Stage layouts in order; the last one repeats. Maps never exceed MaxMapSize.</summary>
        static readonly StageConfig[] Stages =
        {
            new StageConfig { Size = 20, Bridges = 1, Cores = 1 },
            new StageConfig { Size = 30, Bridges = 1, Cores = 1 },
            new StageConfig { Size = 40, Bridges = 2, Cores = 1 },
            new StageConfig { Size = 50, Bridges = 2, Cores = 2 },
            new StageConfig { Size = 60, Bridges = 3, Cores = 2 },
            new StageConfig { Size = 70, Bridges = 4, Cores = 3 },
        };

        public const int MaxMapSize = 70;
        public const int WavesPerStage = 10;
        /// <summary>A boss ends every this many waves (so every stage ends with one).</summary>
        public const int BossEvery = 10;

        // ---------------------------------------------------------------- upgrades

        public enum Upgrade { AttackSpeed, Damage, Income }

        public static readonly (Upgrade kind, string title, string text, float step)[] Cards =
        {
            (Upgrade.AttackSpeed, "OVERCLOCK", "+15% attack speed for every tower", 0.15f),
            (Upgrade.Damage, "HOT ROUNDS", "+20% damage for every tower", 0.20f),
            (Upgrade.Income, "RICH VEINS", "+25% gold from crystals sold", 0.25f),
        };

        /// <summary>Multiplies tower attack rates (and laser/flame damage per second).</summary>
        public static float FireRateMultiplier { get; private set; } = 1f;
        public static float DamageMultiplier { get; private set; } = 1f;
        /// <summary>Multiplies gold from crystals sold at a Core.</summary>
        public static float IncomeMultiplier { get; private set; } = 1f;

        // ---------------------------------------------------------------- progress

        /// <summary>0-based stage index.</summary>
        public static int Stage { get; private set; }
        /// <summary>Gold brought over from the previous stage.</summary>
        public static int CarriedGold { get; private set; }

        public static StageConfig Current
        {
            get
            {
                var c = Stages[Mathf.Min(Stage, Stages.Length - 1)];
                c.Size = Mathf.Min(c.Size, MaxMapSize);
                return c;
            }
        }

        /// <summary>Waves are numbered across the whole run, so difficulty keeps climbing from stage to stage.</summary>
        public static int FirstWave => Stage * WavesPerStage + 1;
        public static int LastWave => (Stage + 1) * WavesPerStage;
        public static bool IsBossWave(int wave) => wave > 0 && wave % BossEvery == 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Reset()
        {
            Stage = 0;
            CarriedGold = 0;
            FireRateMultiplier = DamageMultiplier = IncomeMultiplier = 1f;
        }

        public static void Apply(Upgrade kind)
        {
            foreach (var c in Cards)
                if (c.kind == kind)
                    switch (kind)
                    {
                        case Upgrade.AttackSpeed: FireRateMultiplier += c.step; break;
                        case Upgrade.Damage: DamageMultiplier += c.step; break;
                        case Upgrade.Income: IncomeMultiplier += c.step; break;
                    }
        }

        /// <summary>Moves on to the next stage, keeping the gold left over.</summary>
        public static void Advance(int goldLeft)
        {
            Stage++;
            CarriedGold = Mathf.Max(0, goldLeft);
        }
    }
}
