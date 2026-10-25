using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>
    /// One keyed clip for a rigid-part enemy, baked from a Blender action (blender-work/Enemies/&lt;Id&gt;_&lt;Action&gt;.json,
    /// loaded with JsonUtility.FromJsonOverwrite). The action's name is the part of the asset name after the last '_'
    /// (PebbleSkitterer_Walk = Walk), or Action if set. Each track drives one named part node, local to that node's own
    /// parent (so a Golem's Fist follows its Forearm, Arm and Torso): per key, an offset from the part's rest position,
    /// a rotation on top of its rest rotation (or the whole local rotation with AbsoluteRotation) and a scale factor,
    /// all in Unity space.
    /// EnemyRig plays the loop (Walk: phase locked to the distance walked; Fly, or TimeBased: on time) and the one-shots
    /// (Chew, Lob, Slam/Dive, Enrage) on top of it, firing the Events as the clip passes them. Optional fields missing
    /// from a json keep their defaults (Loop true, amplitudes -1 = the RigSettings knobs).
    /// </summary>
    [CreateAssetMenu(menuName = "Dungeon/Enemy Gait")]
    public class EnemyGait : ScriptableObject
    {
        [System.Serializable]
        public class Track
        {
            [Tooltip("Name of the part node in the model (e.g. Body, Leg_L1, Fist_R).")]
            public string Part;
            public Vector3[] Position;
            public Quaternion[] Rotation;
            public Vector3[] Scale;
        }

        /// <summary>A named moment in the clip (Bite, Launch, Impact) that gameplay waits for.</summary>
        [System.Serializable]
        public class ClipEvent
        {
            [Tooltip("Phase (0-1) of the clip.")]
            public float Time;
            public string Name;
        }

        [Tooltip("Action name (Walk, Fly, Chew, Lob, Slam, Enrage). Empty: taken from the asset name after the last '_'.")]
        public string Action;
        [Tooltip("Length of the source cycle in frames (at SourceFps).")]
        public int Frames = 12;
        public float SourceFps = 24f;
        [Tooltip("Loops (Walk, Fly, a chew that keeps chewing) or plays once (Lob, Slam, Enrage).")]
        public bool Loop = true;
        [Tooltip("Distance walked per cycle at model scale 1, in world units. For no foot sliding it matches the feet's " +
                 "travel over one full cycle (two steps). Only used by the Walk loop.")]
        public float StrideLength = 0.34f;
        [Tooltip("A loop played on time (CyclesPerSecond) instead of locked to the distance walked. A flier's loop always is.")]
        public bool TimeBased;
        [Tooltip("Cycles per second of a time-based loop (0 = the source rate, SourceFps / Frames).")]
        public float CyclesPerSecond;
        [Tooltip("Rotation keys are the parts' whole local rotation instead of a delta on top of the rest rotation.")]
        public bool AbsoluteRotation;
        [Tooltip("Amplitude overrides for this clip (-1 = the RigSettings knob for the loop, 1 for a one-shot). " +
                 "1 = exactly as keyed: clips authored 1:1 (e.g. a Fly with root-hung wings) want 1.")]
        public float BodyBounce = -1f, BodySquash = -1f, LegSwing = -1f;
        [Tooltip("Plays exactly as keyed: all three amplitudes 1, whatever the knobs say (parented rigs such as the Golem's " +
                 "Torso > Arm > Forearm > Fist chain, where the gains would compound).")]
        public bool Raw;
        [Tooltip("Phase (0-1) of each key.")]
        public float[] KeyTimes;
        public Track[] Tracks;
        [Tooltip("Named moments (Bite, Launch, Impact) at a phase of the clip.")]
        public ClipEvent[] Events;

        public int Keys => KeyTimes != null ? KeyTimes.Length : 0;
        public bool Playable => Keys > 0 && Tracks != null;
        /// <summary>Seconds per cycle at the source rate.</summary>
        public float Seconds => Mathf.Max(1, Frames) / Mathf.Max(1f, SourceFps);
        /// <summary>Cycles per second when played on time.</summary>
        public float Rate => CyclesPerSecond > 0f ? CyclesPerSecond : 1f / Seconds;

        /// <summary>The action name (cached; see Action).</summary>
        public string ActionName
        {
            get
            {
                if (!string.IsNullOrEmpty(Action)) return Action;
                if (actionName == null)
                {
                    var n = name;
                    int i = n.LastIndexOf('_');
                    actionName = i >= 0 ? n.Substring(i + 1) : n;
                }
                return actionName;
            }
        }
        [System.NonSerialized] string actionName;

        /// <summary>Phase (0-1) of the first event called name, or -1.</summary>
        public float EventTime(string eventName)
        {
            if (Events != null)
                for (int i = 0; i < Events.Length; i++)
                    if (Events[i] != null && Events[i].Name == eventName) return Events[i].Time;
            return -1f;
        }
    }
}
