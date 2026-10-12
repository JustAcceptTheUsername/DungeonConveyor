using System.Collections.Generic;
using UnityEngine;

namespace DungeonConveyor
{
    public enum BuildCategory { Defense, Logistics, Mining }

    /// <summary>How a defensive building hurts enemies.</summary>
    public enum AttackKind
    {
        None,
        Bullet,   // fast single-target shots
        Missile,  // slower homing shot, splash on impact
        Mortar,   // lobbed shell at where the target stands, big splash, can't hit up close
        Laser,    // continuous beam on one target
        Flame,    // damages everything inside its short range
        Tesla,    // lightning that chains between nearby enemies
        Mine,     // explodes when stepped on, then is gone
        Slow,     // field that slows every enemy inside it
    }

    /// <summary>
    /// Every building, including the Core. Lives at Resources/BuildingCatalog.asset and is kept in sync with
    /// Assets/Models/Buildings by the editor (BuildingCatalogSync); costs, names, footprints and combat stats can be
    /// edited in the Inspector.
    /// </summary>
    [CreateAssetMenu(menuName = "Dungeon/Building Catalog")]
    public class BuildingCatalog : ScriptableObject
    {
        [System.Serializable]
        public class Entry
        {
            public string Id;
            public string Name;
            public GameObject Model;
            public BuildCategory Category;
            public int Cost = 100;
            [Tooltip("Footprint in grid cells (x, y).")]
            public Vector2Int Size = Vector2Int.one;
            [Tooltip("Model scale; 1 = one model unit per cell.")]
            public float Scale = 1f;
            [Tooltip("Shown in the build menu. Off for buildings the game places itself (the Core).")]
            public bool Purchasable = true;
            [TextArea] public string Description;

            [Header("Combat")]
            public AttackKind Attack;
            [Tooltip("Reach in cells.")]
            public float Range;
            [Tooltip("Per hit, or per second for Laser and Flame.")]
            public float Damage;
            [Tooltip("Attacks per second (ignored by Laser, Flame, Slow).")]
            public float FireRate = 1f;
            [Tooltip("Splash radius in cells (Missile, Mortar, Mine); chain length for Tesla.")]
            public float Splash;

            [System.NonSerialized] public Sprite Sprite; // rendered at runtime by GameArt; also the menu icon
            // Models with a "Head" node (turrets): the body without the head, and the head alone at
            // HeadDirections evenly spaced yaws, index 0 = facing as modelled. Null otherwise.
            [System.NonSerialized] public Sprite BaseSprite;
            [System.NonSerialized] public Sprite[] HeadSprites;

            public bool NeedsOre => Category == BuildCategory.Mining;
            /// <summary>Towers that shoot (or project a field) must stand next to a conveyor.</summary>
            public bool NeedsConveyor => Attack != AttackKind.None && Attack != AttackKind.Mine;
            /// <summary>Enemies walk over belts and mines; everything else is in their way.</summary>
            public bool BlocksEnemies => Id != ConveyorId && Attack != AttackKind.Mine;
        }

        public const string CoreId = "Core";
        public const string ConveyorId = "Conveyor";
        public const string WallId = "Wall";
        public const string SplitterId = "Splitter";
        public const string RefinerId = "Collector"; // the Refiner reuses the Collector model

        [Tooltip("Placed buildings are live 3D models (BuildingModels) instead of pre-rendered sprites: turret heads " +
                 "turn smoothly and models stay sharp at any zoom. Off = sprites, as before. The build menu icons and " +
                 "placement ghost are sprites either way. DitherLook only affects the sprites.")]
        public bool LiveModels = true;

        [Tooltip("Temporary (art check): towers whose model has a \"Head\" node turn their head toward their target, " +
                 "drawn as a body sprite plus one head sprite per direction (GameArt.RenderHead, Tower.Aim). Off = the " +
                 "single static sprite, exactly as before.")]
        public bool AimingHeads = true;

        [Tooltip("Temporary (style test D): bake building sprites with the dither look (Bayer pattern, smooth light " +
                 "ramp, specular spot) via the DungeonConveyor/DitherBake shader. Off = the normal lit bake.")]
        public bool DitherLook = false;

        [Tooltip("Temporary (style test D2): full-screen Bayer dither of the game view (DitherScreen shader via the " +
                 "renderer's DitherScreen feature). Dots are screen pixels, so they stay crisp at any zoom.")]
        public bool ScreenDither = true;

        public List<Entry> Entries = new List<Entry>();

        public Entry Find(string id) => Entries.Find(e => e.Id == id);

        static BuildingCatalog instance;
        public static BuildingCatalog Instance => instance != null ? instance : instance = Resources.Load<BuildingCatalog>("BuildingCatalog");
    }
}
