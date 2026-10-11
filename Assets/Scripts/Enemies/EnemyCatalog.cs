using System.Collections.Generic;
using UnityEngine;

namespace DungeonConveyor
{
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

            [System.NonSerialized] public Sprite[] Sprites; // per Dir, rendered at runtime by GameArt
        }

        [Tooltip("Extra yaw if the models don't face +Z.")]
        public float YawOffset;
        public List<Entry> Entries = new List<Entry>();

        static EnemyCatalog instance;
        public static EnemyCatalog Instance => instance != null ? instance : instance = Resources.Load<EnemyCatalog>("EnemyCatalog");
    }
}
