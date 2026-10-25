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
            [Tooltip("Walk cycle for a rigid-part model (EnemyRig). Empty: the parts don't walk (hatch and crumble still work).")]
            public EnemyGait Gait;

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

        /// <summary>Tuning of the rigid-part animation (EnemyRig, MeteorHatch).</summary>
        [System.Serializable]
        public class RigSettings
        {
            [Tooltip("Pose updates per second: limbs and body hold their pose between steps (12 = on twos at 24 fps). " +
                     "Travel over the planet stays smooth.")]
            public float PoseFps = 12f;
            [Tooltip("Blend between the keyed poses at the current phase (off: hold each key, exactly as keyed).")]
            public bool Interpolate = true;
            [Tooltip("Multiplies each gait's StrideLength (distance walked per cycle). Lower = quicker steps.")]
            public float StrideScale = 1f;
            [Tooltip("Body bob and lean, relative to the keyed walk.")]
            public float BodyBounce = 1.8f;
            [Tooltip("Body squash and stretch, relative to the keyed walk.")]
            public float BodySquash = 2.2f;
            [Tooltip("Leg swing and lift, relative to the keyed walk.")]
            public float LegSwing = 1.25f;
            [Tooltip("Meteor shell the enemies hatch from (pieces Core, Crater, Shell_n). Empty: no hatch.")]
            public GameObject MeteorShell;
            [Tooltip("Shell height relative to the enemy's height.")]
            public float ShellSize = 1.5f;
            [Tooltip("Seconds after landing when the shell cracks open and the enemy pops out.")]
            public float CrackAt = 0.15f;
            [Tooltip("Seconds the pop out of the shell takes.")]
            public float PopSeconds = 0.35f;
            [Tooltip("Seconds a hatching enemy waits in its shell before it starts walking (0 = walks off at once).")]
            public float HatchHold = 0.35f;
            [Tooltip("Seconds until the shell pieces have sunk into the ground.")]
            public float ShellSeconds = 1f;
            [Tooltip("Seconds of the crumble into gravel on death (0.6-1).")]
            public float CrumbleSeconds = 0.8f;
            [Tooltip("Speed of the parts flying off on death (world units per second at scale 1).")]
            public float CrumbleImpulse = 0.55f;
            [Tooltip("Tumble of the parts on death (degrees per second).")]
            public float CrumbleSpin = 540f;
            [Tooltip("Gravity on the falling parts (world units per second squared at scale 1).")]
            public float CrumbleGravity = 9f;
        }

        [Tooltip("Extra yaw if the models don't face +Z.")]
        public float YawOffset;
        [Tooltip("Ink outline, hit flash and chips, damage cracks, crumbling death, status on the body, ground shadow, " +
                 "bigger health bars and crowd variation. Off = the plain look.")]
        public bool EnemyFeedback = true;
        public FeedbackSettings Feedback = new FeedbackSettings();
        [Tooltip("Rigid-part animation for models with separate parts: stepped walk locked to distance, hatching out of " +
                 "the meteor shell, crumbling into gravel. Off = they glide as one piece like the old models.")]
        public bool RigAnimation = true;
        public RigSettings Rig = new RigSettings();
        public List<Entry> Entries = new List<Entry>();

        static EnemyCatalog instance;
        public static EnemyCatalog Instance => instance != null ? instance : instance = Resources.Load<EnemyCatalog>("EnemyCatalog");
    }
}
