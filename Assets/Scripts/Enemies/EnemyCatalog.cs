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
            [Tooltip("Walk cycle for a rigid-part model (EnemyRig), or the Fly loop of a flier. Empty: the parts don't walk " +
                     "(hatch and crumble still work).")]
            public EnemyGait Gait;
            [Tooltip("More clips by action name (<Id>_<Action>): Fly (a flier's loop, wins over Gait), Chew (saboteur at a " +
                     "belt, wrecks on Bite), Lob (spits at a belt from range, launches on Launch), Slam (at the Core, " +
                     "damages on Impact), Enrage (when armour plates break off). Missing ones keep the old instant behaviour.")]
            public EnemyGait[] Clips;
            [Tooltip("Model used when this kind comes as a boss (e.g. OreGolem_Boss for the Brute). Empty: the normal model, bigger.")]
            public GameObject BossModel;
            [Tooltip("The boss model's own walk. Empty: Gait.")]
            public EnemyGait BossGait;
            [Tooltip("The boss model's own clips. Empty: Clips.")]
            public EnemyGait[] BossClips;

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
            public float FlashSeconds = 0.08f;
            [Tooltip("How white the hit flash paints the body (1 = fully white).")]
            [Range(0f, 1f)] public float FlashStrength = 1f;
            [Tooltip("Extra brightness of the flash above white (HDR emission), so bloom and the grade can't grey it out.")]
            public float FlashEmission = 1f;
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
            [Tooltip("Health bar fill at full, half and low health (bright and saturated: low health must still read on " +
                     "the dark back).")]
            public Color BarFull = new Color(0.36f, 1f, 0.36f, 1f), BarHalf = new Color(1f, 0.9f, 0.22f, 1f),
                BarLow = new Color(1f, 0.27f, 0.2f, 1f);
            [Tooltip("Gap between the body and its bar, along the camera's up (world units at size 1).")]
            public float BarMargin = 0.1f;
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
            [Tooltip("Tallest a shell gets (world units), so a big enemy's shell doesn't swallow its neighbours.")]
            public float ShellMaxHeight = 1.4f;
            [Tooltip("Largest the crack flash gets (world units across).")]
            public float CrackFlashMax = 0.6f;
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

            [Header("Clips and fliers")]
            [Tooltip("Playback speed of the one-shot clips (Chew, Lob, Slam, Enrage).")]
            public float ActionSpeed = 1f;
            [Tooltip("Amplitude of the one-shot clips relative to the keys (Body/Torso squash included).")]
            public float ActionAmplitude = 1f;
            [Tooltip("Wing beats of a flier's Fly loop relative to the keyed rate (the loop is time-based, not distance).")]
            public float FlapRate = 1f;
            [Tooltip("How fast a flier's heading eases round to a new direction (per second; 0 = turns at once, as the " +
                     "steps do at cell borders). The bank follows this eased turn.")]
            public float FlyTurnResponse = 6f;
            [Tooltip("Most a flier banks into a turn (degrees).")]
            public float BankMax = 22f;
            [Tooltip("Bank per degree per second of turning.")]
            public float BankPerTurn = 0.08f;
            [Tooltip("How fast the bank follows the turning (per second).")]
            public float BankResponse = 7f;

            [Header("Attack telegraphs (gameplay; 0 = the old instant behaviour)")]
            [Tooltip("Seconds a saboteur with a Chew clip stands chewing at a belt; the belt is wrecked on Bite (or when " +
                     "the time is up). The clip is sped up so Bite lands inside this time.")]
            public float ChewDwell = 0.5f;
            [Tooltip("Seconds a saboteur with a Lob clip winds up before the blob leaves (on Launch).")]
            public float LobDwell = 0.6f;
            [Tooltip("How many cells short of the belt a lobber stops and spits.")]
            public int LobRange = 2;
            [Tooltip("Flight time of the lava blob; the belt is wrecked where it lands.")]
            public float LobFlight = 0.45f;
            [Tooltip("Height of the blob's arc relative to the distance.")]
            public float LobArc = 0.45f;
            [Tooltip("Where the blob leaves without a Muzzle node (model units: x right, y up, z forward).")]
            public Vector3 LobMuzzle = new Vector3(0f, 0.35f, 0.25f);
            [Tooltip("Seconds an enemy with a Slam clip winds up at the Core; the damage lands on Impact.")]
            public float SlamDwell = 0.6f;

            [Header("Armour plates (Plate_* parts)")]
            [Tooltip("Health fractions at which the Plate_* parts break off (spread evenly over the thresholds).")]
            public float[] PlateBreakAt = { 0.75f, 0.5f, 0.25f };
            [Tooltip("Seconds a broken plate flies and sinks before it is gone.")]
            public float PlateSeconds = 1f;
            [Tooltip("Speed of a plate flying off (world units per second at scale 1).")]
            public float PlateImpulse = 1.6f;
            [Tooltip("Most seconds the enemy stands for its Enrage clip when a plate breaks: it stands for the whole clip, " +
                     "sped up to fit (0 = keeps walking while the clip plays).")]
            public float EnrageHold = 1f;
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
