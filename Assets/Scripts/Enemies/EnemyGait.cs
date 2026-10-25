using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>
    /// One looping walk cycle for a rigid-part enemy, baked from the keyed Blender action (the `Walk` action in
    /// blender-work/Enemies/&lt;Id&gt;.blend, exported by _tools/dc_enemy_export.py). Each track drives one named part
    /// node: per key, an offset from the part's rest position, a rotation on top of its rest rotation and a scale
    /// factor, all in Unity space. EnemyRig plays it with the phase locked to the distance walked.
    /// </summary>
    [CreateAssetMenu(menuName = "Dungeon/Enemy Gait")]
    public class EnemyGait : ScriptableObject
    {
        [System.Serializable]
        public class Track
        {
            [Tooltip("Name of the part node in the model (e.g. Body, Leg_L1).")]
            public string Part;
            public Vector3[] Position;
            public Quaternion[] Rotation;
            public Vector3[] Scale;
        }

        [Tooltip("Length of the source cycle in frames (at SourceFps).")]
        public int Frames = 12;
        public float SourceFps = 24f;
        [Tooltip("Distance walked per cycle at model scale 1, in world units. For no foot sliding it matches the feet's " +
                 "travel over one full cycle (two steps).")]
        public float StrideLength = 0.34f;
        [Tooltip("Phase (0-1) of each key.")]
        public float[] KeyTimes;
        public Track[] Tracks;

        public int Keys => KeyTimes != null ? KeyTimes.Length : 0;
    }
}
