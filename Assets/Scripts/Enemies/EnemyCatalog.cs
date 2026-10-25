using System.Collections.Generic;
using UnityEngine;

namespace DungeonConveyor
{
    [System.Flags]
    public enum EnemyTrait { None = 0, Fast = 1, Armored = 2, Flying = 4, Saboteur = 8 }

    /// <summary>Modifier on an elite wave: every enemy in it has it.</summary>
    public enum EliteModifier { None, Shielded, Splitting, Saboteurs, Swift, Regenerating }

    /// <summary>
    /// Every enemy type. Lives at Resources/EnemyCatalog.asset and is kept in sync with Assets/Models/Enemies by the
    /// editor (EnemyCatalogSync); stats can be tuned in the Inspector.
    /// </summary>
    public class EnemyCatalog : ScriptableObject
    {
        [System.Serializable]
        public class Entry
        {
            public string Id;
            public string Name;
            public GameObject Model;
            public float Scale = 0.6f;
            public float Health = 50f;
            [Tooltip("Cells per second.")]
            public float Speed = 1.2f;
            [Tooltip("Damage dealt to the Core on arrival.")]
            public float CoreDamage = 10f;
            [Tooltip("Gold for killing it.")]
            public int Bounty = 4;
            [Tooltip("First wave it can appear in.")]
            public int FirstWave = 1;
            [Tooltip("Relative share of a wave once unlocked.")]
            public float Weight = 1f;
            [Tooltip("Drawn this far above the ground (fliers).")]
            public float Hover;
            [Tooltip("Fast: 35% quicker. Armored: half damage from bullets and lasers. Flying: flies straight over walls " +
                     "and cliffs, safe from mines and flames. Saboteur: goes for your belts first and wrecks them.")]
            public EnemyTrait Traits;

            [System.NonSerialized] public Sprite[] Sprites; // per Dir, rendered at runtime by GameArt
        }

        /// <summary>Tuning of the enemy feedback layer (EnemyLook).</summary>
        [System.Serializable]
        public class FeedbackSettings
        {
            public Color InkColor = new Color(0.106f, 0.063f, 0.125f, 1f);
            [Tooltip("Outline width in world units (buildings use about 0.012).")]
            public float InkWidth = 0.018f;
            [Tooltip("The outline is never thinner than this many pixels (zoomed out).")]
            public float InkMinPixels = 1.5f;
            [Tooltip("White flash on a hit.")]
            public float FlashSeconds = 0.065f;
            [Tooltip("Rock chips per hit (at most one spray every 0.12 s per enemy).")]
            public int ChipsPerHit = 3;
            [Tooltip("Health fractions below which the first and second crack stages show.")]
            public Vector2 CrackAt = new Vector2(0.66f, 0.33f);
            [Tooltip("Crack cells per model unit (higher = smaller pieces).")]
            public float CrackScale = 3.5f;
            [Tooltip("How strongly burn, chill, poison and shield tint the body.")]
            public float StatusStrength = 1.3f;
            [Tooltip("Seconds a killed enemy takes to crumble into the ground.")]
            public float DeathSeconds = 0.6f;
            public float ShadowAlpha = 0.6f;
            [Tooltip("Shadow size relative to the model's footprint.")]
            public float ShadowSize = 1.1f;
            [Tooltip("How far the shadow leans away from the sun (world units at size 1), so it shows past the body.")]
            public float ShadowOffset = 0.1f;
            [Tooltip("Random per-enemy size spread (0.06 = ±6%).")]
            [Range(0f, 0.2f)] public float ScaleJitter = 0.06f;
            [Tooltip("Random per-enemy bob speed spread.")]
            [Range(0f, 0.5f)] public float BobJitter = 0.15f;
            [Tooltip("Health bar height in world units (about 5 px at default zoom).")]
            public float BarHeight = 0.09f;
        }

        [Tooltip("Extra yaw if the models don't face +Z.")]
        public float YawOffset;
        [Tooltip("Ink outline, hit flash and chips, damage cracks, crumbling death, status on the body, ground shadow, " +
                 "bigger health bars and crowd variation. Off = the plain look.")]
        public bool EnemyFeedback = true;
        public FeedbackSettings Feedback = new FeedbackSettings();
        public List<Entry> Entries = new List<Entry>();

        static EnemyCatalog instance;
        public static EnemyCatalog Instance => instance != null ? instance : instance = Resources.Load<EnemyCatalog>("EnemyCatalog");
    }
}
