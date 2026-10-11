using System.Collections.Generic;
using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>One kind of ore: how it looks, how rare it is, how much a patch holds and what it sells for.</summary>
    [System.Serializable]
    public class OreType
    {
        public string Name = "Sapphire";
        [Tooltip("Tint of the tiles under the patch.")]
        public Color Color = new Color(0.22f, 0.5f, 1f);
        [Tooltip("Gold paid per crystal delivered to the Core (refined crystals pay more).")]
        public int Value = 5;
        [Tooltip("Relative chance for a patch to be this ore.")]
        public float Weight = 1f;
        [Tooltip("Patches of this ore only appear at least this many cells from the base.")]
        public float MinBaseDistance;
        [Tooltip("Crystals each tile of a patch holds (min..max); the patch is used up when they're mined.")]
        public Vector2Int AmountPerTile = new Vector2Int(20, 40);
        [Tooltip("Picked at random for each tile of the patch.")]
        public List<GameObject> Deposits = new List<GameObject>();
        [Tooltip("The raw crystal that rides the belts.")]
        public GameObject Item;
        [Tooltip("What a Refiner turns it into.")]
        public GameObject RefinedItem;
    }

    /// <summary>
    /// References to non-building art and the ore types: crystal models, cliffs and the rock wall around the map.
    /// Lives at Resources/ArtLibrary.asset and is filled in by the editor (ArtLibrarySync) from Assets/Models; any
    /// slot can be swapped by hand in the Inspector.
    /// </summary>
    public class ArtLibrary : ScriptableObject
    {
        [Header("Ores")]
        public List<OreType> Ores = new List<OreType>();
        public float DepositScale = 0.6f;
        public float ItemScale = 0.5f;

        [Header("Terrain")]
        [Tooltip("Hung under map edge tiles that face the camera.")]
        public List<GameObject> CliffEdges = new List<GameObject>();
        public GameObject CliffCorner;
        [Tooltip("Model yaw that turns a cliff edge to face grid -y; -x edges use this + 90. The cliff models face +Z, so 180.")]
        public float CliffYaw = 180f;

        [Header("Rock wall around the map")]
        [Tooltip("1x1 rock blocks of different heights; sorted by height at runtime, so order doesn't matter.")]
        public List<GameObject> RockWalls = new List<GameObject>();
        [Tooltip("Occasional replacements for tall rocks (e.g. ones with crystals).")]
        public List<GameObject> RockAccents = new List<GameObject>();
        [Range(0f, 1f)] public float RockAccentChance = 0.12f;

        static ArtLibrary instance;
        public static ArtLibrary Instance => instance != null ? instance : instance = Resources.Load<ArtLibrary>("ArtLibrary");
    }
}
