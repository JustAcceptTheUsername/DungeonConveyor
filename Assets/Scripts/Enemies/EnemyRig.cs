using System.Collections.Generic;
using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>
    /// Procedural animation for rigid-part enemies (one node per part, pivot at the joint; no skinning, no Animator).
    /// Built for any model that follows the part naming convention (see notes/enemy-review/pilot/PILOT.md and
    /// notes/enemy-review/rollout/CODE.md):
    /// - Loop: a walker's Walk with its phase locked to the distance walked (slows with chill and slow fields, holds
    ///   when the enemy stops); a flier's Fly on time instead (RigSettings.FlapRate). A random phase per enemy, the
    ///   poses stepped at RigSettings.PoseFps (12 fps: held between steps) while travel over the planet stays smooth.
    /// - One-shots (Chew, Lob, Slam, Enrage, any EnemyGait in the clip set): Play one; the parts it has keys for play
    ///   it instead of the loop, the rest keep the loop. OnEvent fires as the clip passes each of its events (and "End"
    ///   when a one-shot is over).
    /// - Parts can hang off each other (Torso > Arm_L > Forearm_L > Fist_L): every track is local to its own parent.
    ///   Tracks on Body or Torso get the bounce and squash knobs, the others the leg swing.
    /// - Pop: the hatch out of the meteor shell (or a split-off copy appearing): pops up, overshoots, squash-lands.
    /// - Hit: a one-step squash.
    /// - Plates: Plate_* parts break off at health thresholds (RigSettings.PlateBreakAt) and fly off.
    /// - Crumble: on death every part flies off with a small impulse, tumbles, lands and sinks (gravel), and the glow
    ///   parts cool down in steps.
    /// Owned and ticked by Enemy (no Update of its own); allocation-free per frame. Off when EnemyCatalog.RigAnimation is.
    /// </summary>
    public sealed class EnemyRig
    {
        public static bool On => EnemyCatalog.Instance == null || EnemyCatalog.Instance.RigAnimation;
        public static EnemyCatalog.RigSettings Settings => EnemyCatalog.Instance != null ? EnemyCatalog.Instance.Rig : DefaultSettings;
        static readonly EnemyCatalog.RigSettings DefaultSettings = new EnemyCatalog.RigSettings();

        /// <summary>Event fired when a one-shot clip is over.</summary>
        public const string EndEvent = "End";

        // Pop: (time 0-1, scale x/z, scale y, rise as a fraction of the model height), played in pose steps.
        static readonly Vector4[] PopKeys =
        {
            new Vector4(0.00f, 0.30f, 0.30f, 0.45f),
            new Vector4(0.22f, 0.85f, 1.35f, 0.75f),
            new Vector4(0.45f, 1.10f, 0.95f, 0.35f),
            new Vector4(0.65f, 1.32f, 0.66f, 0.00f), // squash on landing
            new Vector4(0.82f, 0.92f, 1.12f, 0.03f),
            new Vector4(1.00f, 1.00f, 1.00f, 0.00f),
        };

        /// <summary>
        /// Fired from Tick as a playing clip passes one of its events (name as keyed, e.g. Bite, Launch, Impact) and
        /// with EndEvent when a one-shot is over. Set once; may Play or Stop from inside.
        /// </summary>
        public System.Action<string> OnEvent;

        readonly EnemyCatalog.RigSettings cfg;
        readonly Transform model, pop;
        readonly Vector3 popRestPos, popRestScale;
        readonly float height; // model height in pop-node units
        readonly bool flier;

        // Clips: set[0] = the loop (Walk, or Fly for a flier; may be null), the rest one-shots or extra loops.
        readonly EnemyGait[] set;
        readonly int[] trackOf; // [clip * driven.Length + part] -> track index in that clip, or -1
        readonly Transform[] driven;
        readonly Vector3[] drivenPos, drivenScale;
        readonly Quaternion[] drivenRot;
        readonly byte[] drivenKind; // Body/Torso, limb (Leg_, Foot_, Wing_, Arm_, Forearm_, Fist_), other
        readonly bool[] drivenGone;
        const byte KindBody = 0, KindLimb = 1, KindOther = 2;

        // A glowing Core part that swells by scale (the spitter's Lob) glows brighter with it.
        readonly int coreIndex = -1, coreSlot;
        readonly Renderer coreRenderer;
        int corePaint = -1;
        bool coreBright;
        // The glow slots on the Core's Body (a spitter's cracks) pulse with the Core's swell.
        readonly Renderer[] swellRenderers;
        readonly int[] swellSlots, swellPaint;
        float phase, nextPose, lastStep = -1f, popStart = -1f, hitPose;
        int playing = -1, playSerial;
        float playStart, playRate = 1f, playPrev, playPhase;

        // Pose steps on the walk's keys (RigSettings.PoseOnKeys): the keys shown at the last step.
        int loopKeyShown = -1, clipKeyShown = -1;
        // The next pose step goes halfway from what is shown to the new pose (a one-shot starting or stopping).
        bool blendNext, blending;
        // The loop key closest to rest: the walk resumes there after a one-shot.
        readonly float neutralPhase;
        // Released: the enemy walks on while the one-shot plays out (limbs back on the loop; a looping clip stops at
        // the end of its cycle, finishAt in cycles).
        bool released;
        float finishAt = -1f;
        float loopPrev; // the loop phase at the last pose step (loop events)

        // Travel curve (a hopper's surge): the body's offset along its facing from steady travel, in pop-node units.
        readonly bool travel;
        readonly float travelMean;
        float travelOffset, popRise;

        // Crystal_* parts grow as it eats (Grow).
        readonly Transform[] crystals;
        readonly Vector3[] crystalRest;
        readonly bool[] drivenCrystal;
        float grow = 1f, crystalMul = 1f;
        int growKick;

        // Plate seams (a Seam* glow part on each Plate_*) flare as the plate's threshold comes near.
        readonly Renderer[] seamRenderer;
        readonly int[] seamSlot, seamPaint;
        readonly float[] plateAt;
        float health01 = 1f, seamShown = 1f, seamRest = 1f;
        int seamPlate = -1;

        // Crumble (every part with a mesh, parents before children)
        readonly Transform[] parts;
        readonly Vector3[] partFrom, partVel, partAxis;
        readonly Quaternion[] partRot;
        readonly float[] partSpin, partSize;
        readonly bool[] partGone; // broken-off plates: not part of the body any more
        readonly List<Renderer> glow = new List<Renderer>();  // renderers with a glow material (any slot)
        Material[][] glowSets;                                // per glow renderer: hot, then three cooled slot sets
        float dieTime = -1f;
        Vector3 dieGround, dieUp;
        int glowStep = -1;

        // Plates (Plate_* parts by name), broken off in order.
        readonly int[] plates;      // -> parts index
        readonly float[] plateStart;
        readonly Vector3[] plateFrom, plateVel, plateAxis, plateGround, plateUp;
        readonly Quaternion[] plateRot;
        readonly Vector3[] plateScale;
        int platesBroken, platesFlying;

        public bool Walks => set[0] != null && driven.Length > 0;
        public float CrumbleSeconds => Mathf.Clamp(cfg.CrumbleSeconds, 0.6f, 1f);
        /// <summary>The one-shot playing (index into the clip set), or -1.</summary>
        public int Playing => playing;
        /// <summary>The Muzzle node (where a lobber spits from), or null.</summary>
        public Transform Muzzle { get; }
        public int Plates => plates.Length;
        public int PlatesBroken => platesBroken;
        /// <summary>Broken plates still flying or sinking (they live outside the enemy).</summary>
        public int PlatesFlying => platesFlying;
        /// <summary>True while OnEvent is called for an event of the loop (a Walk's Land or Step), not of a one-shot.</summary>
        public bool InLoopEvent { get; private set; }

        /// <summary>
        /// The rig for model if it has rigid parts (any child node with a mesh besides the root), else null. gait: the
        /// loop; clips: the other clips; flier: plays its loop on time (a clip called Fly wins over gait).
        /// </summary>
        public static EnemyRig Create(GameObject model, EnemyGait gait, EnemyGait[] clips = null, bool flier = false,
            EnemyPaint paint = null)
        {
            var filters = model.GetComponentsInChildren<MeshFilter>(true);
            if (filters.Length < 2) return null; // one merged mesh: nothing to animate
            return new EnemyRig(model.transform, filters, gait, clips, flier, paint ?? new EnemyPaint(model));
        }

        /// <summary>The model's property blocks (the palette colours and the glows driven here share them).</summary>
        public EnemyPaint Paint { get; }

        EnemyRig(Transform model, MeshFilter[] filters, EnemyGait gait, EnemyGait[] clips, bool flier, EnemyPaint paint)
        {
            cfg = Settings;
            Paint = paint;
            this.model = model;
            this.flier = flier;
            // The pop scales the node that holds all parts (the glb's root node), so the model's own scale stays put.
            pop = model.childCount == 1 ? model.GetChild(0) : model;
            popRestPos = pop.localPosition;
            popRestScale = pop.localScale;
            var b = new Bounds();
            bool any = false;
            foreach (var f in filters)
            {
                if (f.sharedMesh == null) continue;
                var mb = f.sharedMesh.bounds;
                var m = pop.worldToLocalMatrix * f.transform.localToWorldMatrix;
                var c = m.MultiplyPoint3x4(mb.center);
                if (!any) { b = new Bounds(c, Vector3.zero); any = true; }
                b.Encapsulate(m.MultiplyPoint3x4(mb.min));
                b.Encapsulate(m.MultiplyPoint3x4(mb.max));
            }
            height = Mathf.Max(0.05f, b.max.y);
            Muzzle = Find(pop, "Muzzle");

            // The clip set: the loop first, then every other playable clip.
            var list = new List<EnemyGait> { null };
            if (clips != null)
                foreach (var c in clips)
                    if (c != null && c.Playable && !list.Contains(c)) list.Add(c);
            EnemyGait loop = gait != null && gait.Playable ? gait : null;
            if (flier)
                foreach (var c in list)
                    if (c != null && c.ActionName == "Fly") { loop = c; break; }
            if (loop != null) list.Remove(loop); // a Fly among the clips moves to the front
            list[0] = loop;
            set = list.ToArray();

            // Every part any clip drives.
            var nodes = new List<Transform>();
            var index = new Dictionary<string, int>();
            foreach (var c in set)
            {
                if (c == null) continue;
                foreach (var tr in c.Tracks)
                {
                    if (tr == null || string.IsNullOrEmpty(tr.Part) || index.ContainsKey(tr.Part)) continue;
                    var node = Find(pop, tr.Part);
                    if (node == null) continue;
                    index[tr.Part] = nodes.Count;
                    nodes.Add(node);
                }
            }
            driven = nodes.ToArray();
            int n = driven.Length;
            trackOf = new int[set.Length * n];
            for (int i = 0; i < trackOf.Length; i++) trackOf[i] = -1;
            for (int c = 0; c < set.Length; c++)
            {
                if (set[c] == null) continue;
                var tracks = set[c].Tracks;
                for (int t = 0; t < tracks.Length; t++)
                    if (tracks[t] != null && tracks[t].Part != null && index.TryGetValue(tracks[t].Part, out int d) && trackOf[c * n + d] < 0)
                        trackOf[c * n + d] = t;
            }
            drivenPos = new Vector3[n];
            drivenRot = new Quaternion[n];
            drivenScale = new Vector3[n];
            drivenKind = new byte[n];
            drivenGone = new bool[n];
            for (int i = 0; i < n; i++)
            {
                drivenPos[i] = driven[i].localPosition;
                drivenRot[i] = driven[i].localRotation;
                drivenScale[i] = driven[i].localScale;
                var nm = driven[i].name;
                drivenKind[i] = nm == "Body" || nm == "Torso" ? KindBody
                    : nm.StartsWith("Leg_") || nm.StartsWith("Foot_") || nm.StartsWith("Wing_") || nm.StartsWith("Arm_")
                      || nm.StartsWith("Forearm_") || nm.StartsWith("Fist_") ? KindLimb : KindOther;
                if (nm != "Core" || coreIndex >= 0) continue;
                var r = driven[i].GetComponent<MeshRenderer>();
                if (r == null) continue;
                var mats = r.sharedMaterials;
                for (int m = 0; m < mats.Length; m++)
                {
                    if (mats[m] == null || !mats[m].name.Contains("Glow")) continue;
                    int id = mats[m].HasProperty(EmissiveFactor) ? EmissiveFactor : mats[m].HasProperty(EmissionColor) ? EmissionColor : -1;
                    if (id == -1) break;
                    coreIndex = i;
                    coreSlot = m;
                    coreRenderer = r;
                    corePaint = paint.Index(r, m);
                    break;
                }
            }
            // The Body's glow slots (cracks) swell with the Core.
            var swell = new List<int>();
            var bodyNode = coreIndex >= 0 ? Find(pop, "Body") : null;
            var bodyRenderer = bodyNode != null ? bodyNode.GetComponent<MeshRenderer>() : null;
            if (bodyRenderer != null && bodyRenderer != coreRenderer)
            {
                var mats = bodyRenderer.sharedMaterials;
                for (int m = 0; m < mats.Length; m++)
                    if (mats[m] != null && mats[m].name.Contains("Glow") && (mats[m].HasProperty(EmissiveFactor) || mats[m].HasProperty(EmissionColor)))
                        swell.Add(m);
            }
            swellRenderers = new Renderer[swell.Count];
            swellSlots = new int[swell.Count];
            swellPaint = new int[swell.Count];
            for (int i = 0; i < swell.Count; i++)
            {
                swellRenderers[i] = bodyRenderer;
                swellSlots[i] = swell[i];
                swellPaint[i] = paint.Index(bodyRenderer, swell[i]);
            }

            phase = Random.value;
            loopPrev = phase;
            nextPose = Time.time + Random.value / Mathf.Max(1f, cfg.PoseFps); // spread the steps over frames
            neutralPhase = NeutralPhase(set[0]);
            travel = set[0] != null && !flier && !set[0].TimeBased && set[0].HasTravel;
            if (travel)
            {
                float sum = 0f;
                const int samples = 96;
                for (int i = 0; i < samples; i++)
                {
                    float t = (i + 0.5f) / samples;
                    sum += set[0].Travel(t) - t;
                }
                travelMean = sum / samples;
            }

            parts = new Transform[filters.Length];
            var plateList = new List<int>();
            for (int i = 0; i < filters.Length; i++)
            {
                parts[i] = filters[i].transform;
                if (parts[i].name.StartsWith("Plate_", System.StringComparison.Ordinal)) plateList.Add(i);
                var mr = filters[i].GetComponent<MeshRenderer>();
                if (mr == null) continue;
                foreach (var m in mr.sharedMaterials)
                    if (m != null && m.name.Contains("Glow")) { glow.Add(mr); break; }
            }
            partFrom = new Vector3[parts.Length];
            partVel = new Vector3[parts.Length];
            partAxis = new Vector3[parts.Length];
            partRot = new Quaternion[parts.Length];
            partSpin = new float[parts.Length];
            partSize = new float[parts.Length];
            partGone = new bool[parts.Length];

            // Crystals (driven or not) for Grow.
            var crystalList = new List<Transform>();
            drivenCrystal = new bool[n];
            for (int i = 0; i < parts.Length; i++)
                if (parts[i].name.StartsWith("Crystal_", System.StringComparison.Ordinal)) crystalList.Add(parts[i]);
            for (int i = 0; i < n; i++) drivenCrystal[i] = crystalList.Contains(driven[i]);
            for (int i = crystalList.Count - 1; i >= 0; i--)
                if (System.Array.IndexOf(driven, crystalList[i]) >= 0) crystalList.RemoveAt(i); // posed with the clips
            crystals = crystalList.ToArray();
            crystalRest = new Vector3[crystals.Length];
            for (int i = 0; i < crystals.Length; i++) crystalRest[i] = crystals[i].localScale;

            plateList.Sort((x, y) => string.CompareOrdinal(parts[x].name, parts[y].name));
            plates = plateList.ToArray();
            int p = plates.Length;
            // Each plate's seam: a Seam* part under it (or Seam_<n> for Plate_<n> anywhere). It moves with its plate,
            // so it is not a piece of its own in the crumble.
            seamRenderer = new Renderer[p];
            seamSlot = new int[p];
            seamPaint = new int[p];
            plateAt = new float[p];
            for (int i = 0; i < p; i++)
            {
                var plate = parts[plates[i]];
                Transform seam = null;
                for (int c = 0; c < plate.childCount && seam == null; c++)
                    if (plate.GetChild(c).name.StartsWith("Seam", System.StringComparison.Ordinal)) seam = plate.GetChild(c);
                if (seam == null) seam = Find(pop, "Seam_" + plate.name.Substring("Plate_".Length));
                if (seam == null) continue;
                if (seam.IsChildOf(plate))
                    for (int k = 0; k < parts.Length; k++)
                        if (parts[k] == seam) partGone[k] = true;
                var sr = seam.GetComponent<MeshRenderer>();
                if (sr == null) continue;
                var mats = sr.sharedMaterials;
                int slot = -1;
                for (int m = 0; m < mats.Length && slot < 0; m++)
                    if (mats[m] != null && mats[m].name.Contains("Glow")) slot = m;
                if (slot < 0 && mats.Length == 1) slot = 0;
                if (slot < 0 || mats[slot] == null) continue;
                int id = mats[slot].HasProperty(EmissiveFactor) ? EmissiveFactor : mats[slot].HasProperty(EmissionColor) ? EmissionColor : -1;
                if (id < 0) continue;
                seamRenderer[i] = sr;
                seamSlot[i] = slot;
                seamPaint[i] = paint.Index(sr, slot);
            }
            var at = cfg.PlateBreakAt;
            for (int i = 0; i < p; i++) plateAt[i] = PlateThreshold(i, p, at);
            plateStart = new float[p];
            plateFrom = new Vector3[p];
            plateVel = new Vector3[p];
            plateAxis = new Vector3[p];
            plateGround = new Vector3[p];
            plateUp = new Vector3[p];
            plateRot = new Quaternion[p];
            plateScale = new Vector3[p];
            for (int i = 0; i < p; i++) plateStart[i] = -1f;
            // Intact seams rest at SeamRestGlow of their material's glow (per renderer, never the shared material).
            seamRest = Mathf.Max(0f, cfg.SeamRestGlow);
            seamShown = seamRest;
            for (int i = 0; i < p; i++) SetSeam(i, seamRest);

            if (n > 0) ApplyPose();
        }

        /// <summary>The phase of the loop key closest to rest (smallest deltas over all tracks, each track normalised).</summary>
        static float NeutralPhase(EnemyGait loop)
        {
            if (loop == null || loop.Keys == 0) return 0f;
            int keys = loop.Keys, best = 0;
            var score = new float[keys];
            foreach (var tr in loop.Tracks)
            {
                if (tr == null) continue;
                float pMax = 0f, rMax = 0f, sMax = 0f;
                for (int k = 0; k < keys; k++)
                {
                    pMax = Mathf.Max(pMax, Key(tr.Position, k, Vector3.zero).magnitude);
                    rMax = Mathf.Max(rMax, Quaternion.Angle(Quaternion.identity, Key(tr.Rotation, k)));
                    sMax = Mathf.Max(sMax, (Key(tr.Scale, k, Vector3.one) - Vector3.one).magnitude);
                }
                for (int k = 0; k < keys; k++)
                {
                    if (pMax > 1e-5f) score[k] += Key(tr.Position, k, Vector3.zero).magnitude / pMax;
                    if (rMax > 0.01f) score[k] += Quaternion.Angle(Quaternion.identity, Key(tr.Rotation, k)) / rMax;
                    if (sMax > 1e-5f) score[k] += (Key(tr.Scale, k, Vector3.one) - Vector3.one).magnitude / sMax;
                }
            }
            for (int k = 1; k < keys; k++)
                if (score[k] < score[best]) best = k;
            return loop.KeyTimes[best];
        }

        /// <summary>The health fraction at which plate i of count breaks off (BreakPlates spreads them over at).</summary>
        static float PlateThreshold(int i, int count, float[] at)
        {
            if (at == null || at.Length == 0) return -1f;
            for (int j = 0; j < at.Length; j++)
            {
                int due = j + 1;
                int target = due >= at.Length ? count : Mathf.RoundToInt(count * due / (float)at.Length);
                if (target > i) return at[j];
            }
            return at[at.Length - 1];
        }

        /// <summary>Index of the key a clip shows at phase t (0-1).</summary>
        static int KeyAt(EnemyGait clip, float t)
        {
            var times = clip.KeyTimes;
            int k = 0;
            for (int i = 0; i < times.Length; i++)
                if (times[i] <= t) k = i;
            return k;
        }

        static Transform Find(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var t = Find(root.GetChild(i), name);
                if (t != null) return t;
            }
            return null;
        }

        /// <summary>Index of the clip for an action (Chew, Lob, Slam, Enrage...), or -1. Compares names: cache it.</summary>
        public int Clip(string action)
        {
            for (int i = 1; i < set.Length; i++)
                if (set[i].ActionName == action) return i;
            return -1;
        }

        public EnemyGait ClipAt(int clip) => clip >= 0 && clip < set.Length ? set[clip] : null;

        /// <summary>
        /// Plays a clip from its start (instead of the loop on the parts it has keys for). within &gt; 0: the clip is sped
        /// up if needed so fitEvent (or its end, if it has no such event) comes no later than that many seconds.
        /// Returns the seconds until fitEvent (or the end) at the speed it plays.
        /// </summary>
        public float Play(int clip, float within = 0f, string fitEvent = null)
        {
            var c = ClipAt(clip);
            if (c == null || clip == 0) return 0f;
            float at = fitEvent != null ? c.EventTime(fitEvent) : -1f;
            if (at < 0f) at = 1f;
            playRate = Mathf.Max(0.05f, cfg.ActionSpeed);
            float until = at / c.Rate / playRate;
            if (within > 0f && until > within)
            {
                playRate *= until / within;
                until = within;
            }
            playing = clip;
            playSerial++;
            playStart = Time.time;
            playPrev = -1e-4f; // an event at 0 fires on the first tick
            playPhase = 0f;
            released = false;
            finishAt = -1f;
            blendNext = true; // one in-between pose on the way in
            nextPose = 0f; // show it right away
            return until;
        }

        /// <summary>
        /// Stops the one-shot (back to the loop on the next pose step, through one in-between pose). The walk resumes
        /// at its most neutral key, so the recovery leads into a matching pose instead of a random mid-stride one.
        /// </summary>
        public void Stop()
        {
            if (playing < 0) return;
            playing = -1;
            playSerial++;
            released = false;
            finishAt = -1f;
            blendNext = true;
            if (set[0] != null && !flier)
            {
                phase = Mathf.Floor(phase) + neutralPhase;
                loopPrev = phase;
            }
            nextPose = 0f;
        }

        /// <summary>
        /// The enemy walks on while the playing clip finishes: its limbs go back to the walk (no frozen legs sliding),
        /// a one-shot plays out on the rest, and a looping clip (Chew) finishes its current cycle and stops.
        /// </summary>
        public void Release()
        {
            if (playing < 0 || released) return;
            released = true;
            var c = set[playing];
            if (c.Loop) finishAt = Mathf.Floor(Mathf.Max(0f, playPrev)) + 1f;
            blendNext = true;
            nextPose = 0f;
        }

        /// <summary>Shows the pose at this moment (e.g. the impact pose a leaving slammer holds) right away.</summary>
        public void PoseNow()
        {
            if (dieTime >= 0f || driven.Length == 0) return;
            blendNext = false;
            ApplyPose();
            lastStep = Time.time;
        }

        /// <summary>
        /// The Crystal_* parts grow a step (RigSettings.CrystalGrowStep, up to CrystalGrowMax), popping a little past
        /// it first. Returns whether they grew.
        /// </summary>
        public bool Grow()
        {
            if (crystals.Length == 0 && System.Array.IndexOf(drivenCrystal, true) < 0) return false;
            float next = Mathf.Min(Mathf.Max(1f, cfg.CrystalGrowMax), grow + Mathf.Max(0f, cfg.CrystalGrowStep));
            if (next <= grow + 1e-4f) return false;
            grow = next;
            growKick = 2;
            nextPose = 0f;
            return true;
        }

        /// <summary>The enemy's health (0-1): the next plate's seam flares as its threshold comes near.</summary>
        public void SetHealth(float value) => health01 = value;

        /// <summary>Hides the model until the pop starts at time `at` (Time.time), then pops it out.</summary>
        public void BeginPop(float at)
        {
            popStart = at;
            nextPose = 0f; // show the hidden state right away
            ApplyPose();
        }

        /// <summary>A hit: the next pose step is squashed.</summary>
        public void Hit()
        {
            hitPose = 1f;
            nextPose = Mathf.Min(nextPose, lastStep + 1f / Mathf.Max(1f, cfg.PoseFps));
        }

        /// <summary>
        /// Every frame while it is alive: walked = world distance covered this frame, dt = frame time (the Fly loop and
        /// the one-shots run on time).
        /// </summary>
        public bool Tick(float walked, float dt = 0f)
        {
            if (dieTime >= 0f) return false;
            var loop = set[0];
            if (loop != null)
            {
                if (flier) phase += dt * Mathf.Max(0f, cfg.FlapRate) * loop.Rate;
                else if (loop.TimeBased) phase += dt * loop.Rate;
                else
                {
                    float stride = loop.StrideLength * Mathf.Max(0.05f, cfg.StrideScale) * model.lossyScale.x;
                    phase += walked / Mathf.Max(0.001f, stride);
                }
                if (phase > 1000f) { phase -= 1000f; loopPrev -= 1000f; }
            }
            float now = Time.time;
            if (playing >= 0) AdvanceClip(now);
            if (travel) ApplyTravel(); // every frame: the surge is travel, not a pose

            // On keys, a walk steps as its phase (or the playing clip's) crosses a key, at most PoseFpsMax per second,
            // and at least PoseFpsMin (PoseFps while popping out); otherwise every walk steps at PoseFps.
            bool onKeys = cfg.PoseOnKeys && loop != null && !flier && !loop.TimeBased;
            bool due = now >= nextPose;
            if (!due && onKeys && now - lastStep >= 1f / Mathf.Max(1f, cfg.PoseFpsMax))
                due = KeyAt(loop, phase - Mathf.Floor(phase)) != loopKeyShown
                      || (playing >= 0 && KeyAt(set[playing], playPhase) != clipKeyShown);
            if (!due) return false;
            if (onKeys)
                nextPose = now + 1f / Mathf.Max(1f, popStart >= 0f || hitPose > 0f ? cfg.PoseFps : Mathf.Min(cfg.PoseFps, cfg.PoseFpsMin));
            else
            {
                float step = 1f / Mathf.Max(1f, cfg.PoseFps);
                nextPose = now - nextPose > step ? now + step : nextPose + step;
            }
            lastStep = now;
            if (loop != null) FireLoopEvents(loop);
            if (dieTime >= 0f) return false; // gone from a callback
            ApplyPose();
            TickPlates(now);
            return true;
        }

        /// <summary>Fires the loop's events its phase passed since the last pose step (in step with the pose shown).</summary>
        void FireLoopEvents(EnemyGait loop)
        {
            float prev = loopPrev, end = phase;
            loopPrev = phase;
            var events = loop.Events;
            if (events == null || events.Length == 0 || OnEvent == null || end <= prev || end - prev > 4f) return;
            int serial = playSerial;
            for (int k = Mathf.FloorToInt(prev); k <= Mathf.FloorToInt(end); k++)
                for (int i = 0; i < events.Length; i++)
                {
                    if (events[i] == null) continue;
                    float at = k + events[i].Time;
                    if (at <= prev || at > end) continue;
                    InLoopEvent = true;
                    OnEvent(events[i].Name);
                    InLoopEvent = false;
                    if (serial != playSerial) return; // a clip started or stopped from the callback
                }
        }

        /// <summary>A walk with a TravelCurve: the body leads or trails steady travel by the curve (pop node, along +Z).</summary>
        void ApplyTravel()
        {
            var loop = set[0];
            float t = phase - Mathf.Floor(phase);
            travelOffset = (loop.Travel(t) - t - travelMean) * loop.StrideLength * Mathf.Max(0.05f, cfg.StrideScale);
            if (popStart < 0f) pop.localPosition = popRestPos + new Vector3(0f, popRise, travelOffset);
        }

        /// <summary>Moves the one-shot on and fires the events it passed.</summary>
        void AdvanceClip(float now)
        {
            var c = set[playing];
            float u = (now - playStart) * playRate * c.Rate; // in cycles
            if (finishAt > 0f) u = Mathf.Min(u, finishAt); // a released loop ends on its cycle
            int serial = playSerial;
            float prev = playPrev;
            float end = c.Loop ? u : Mathf.Min(u, 1f);
            playPrev = u;
            playPhase = c.Loop ? u - Mathf.Floor(u) : Mathf.Min(u, 1f);
            var events = c.Events;
            if (events != null && OnEvent != null && end > prev)
            {
                for (int k = Mathf.Max(0, Mathf.FloorToInt(prev)); k <= Mathf.FloorToInt(end); k++)
                    for (int i = 0; i < events.Length; i++)
                    {
                        if (events[i] == null) continue;
                        float at = k + events[i].Time;
                        if (at <= prev || at > end) continue;
                        OnEvent(events[i].Name);
                        if (serial != playSerial) return; // stopped or restarted from the callback
                    }
            }
            if ((!c.Loop && u >= 1f) || (finishAt > 0f && u >= finishAt))
            {
                Stop();
                OnEvent?.Invoke(EndEvent);
            }
        }

        void ApplyPose()
        {
            float now = Time.time;
            // Pop / hit on the node holding all parts.
            var s = Vector3.one;
            float rise = 0f;
            if (popStart >= 0f)
            {
                float u = (now - popStart) / Mathf.Max(0.05f, cfg.PopSeconds);
                if (u < 0f) s = Vector3.zero;
                else if (u >= 1f) popStart = -1f;
                else
                {
                    int k = 1;
                    while (k < PopKeys.Length - 1 && PopKeys[k].x < u) k++;
                    var a = PopKeys[k - 1];
                    var c = PopKeys[k];
                    float f = Mathf.InverseLerp(a.x, c.x, u);
                    float sx = Mathf.Lerp(a.y, c.y, f), sy = Mathf.Lerp(a.z, c.z, f);
                    s = new Vector3(sx, sy, sx);
                    rise = Mathf.Lerp(a.w, c.w, f) * height;
                }
            }
            if (hitPose > 0f)
            {
                s = Vector3.Scale(s, new Vector3(1.14f, 0.8f, 1.14f));
                hitPose = 0f;
            }
            pop.localScale = Vector3.Scale(popRestScale, s);
            popRise = rise;
            pop.localPosition = popRestPos + new Vector3(0f, rise, popStart >= 0f ? 0f : travelOffset);

            // Crystals that grew pop a little past their new size for two steps.
            crystalMul = grow * (growKick == 2 ? 1.12f : growKick == 1 ? 1.04f : 1f);
            if (growKick > 0) growKick--;
            for (int i = 0; i < crystals.Length; i++)
                if (crystals[i] != null) crystals[i].localScale = crystalRest[i] * crystalMul;
            if (plates.Length > 0) GlowSeam();

            int n = driven.Length;
            if (n == 0) return;
            blending = blendNext;
            blendNext = false;
            // Each part: the one-shot if it has keys for it, else the loop (at the current phase), else its rest.
            float loopT = phase - Mathf.Floor(phase);
            loopKeyShown = set[0] != null ? KeyAt(set[0], loopT) : -1;
            clipKeyShown = playing >= 0 ? KeyAt(set[playing], playPhase) : -1;
            for (int d = 0; d < n; d++)
            {
                if (drivenGone[d]) continue;
                int track = playing >= 0 ? trackOf[playing * n + d] : -1;
                // Released (walking again): the limbs walk, the rest plays the clip out.
                if (track >= 0 && released && drivenKind[d] == KindLimb && set[0] != null && trackOf[d] >= 0) track = -1;
                if (track >= 0)
                {
                    // One-shots play as keyed (times ActionAmplitude) unless the clip overrides its amplitudes.
                    var c = set[playing];
                    float aa = cfg.ActionAmplitude;
                    float amp = Amp(d, c, 1f, 1f) * aa, squash = drivenKind[d] == KindBody ? (c.Raw ? 1f : Or(c.BodySquash, 1f)) * aa : aa;
                    Pose(d, c, c.Tracks[track], playPhase, c.Loop, amp, squash);
                    continue;
                }
                track = set[0] != null ? trackOf[d] : -1;
                if (track >= 0)
                {
                    // The loop gets the RigSettings knobs: bounce and squash on Body/Torso, swing on the limbs.
                    var c = set[0];
                    Pose(d, c, c.Tracks[track], loopT, true, Amp(d, c, cfg.BodyBounce, cfg.LegSwing),
                        drivenKind[d] == KindBody ? (c.Raw ? 1f : Or(c.BodySquash, cfg.BodySquash)) : 1f);
                    continue;
                }
                Set(d, drivenPos[d], drivenRot[d], drivenCrystal[d] ? drivenScale[d] * crystalMul : drivenScale[d]);
            }
            blending = false;
            if (coreIndex >= 0) GlowCore();
        }

        /// <summary>Puts part d in a pose, or halfway there from what it shows on a blend step.</summary>
        void Set(int d, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            var part = driven[d];
            if (blending)
            {
                position = Vector3.LerpUnclamped(part.localPosition, position, 0.5f);
                rotation = Quaternion.Slerp(part.localRotation, rotation, 0.5f);
                scale = Vector3.LerpUnclamped(part.localScale, scale, 0.5f);
            }
            part.localPosition = position;
            part.localRotation = rotation;
            part.localScale = scale;
        }

        /// <summary>The next plate's seam flares (and flickers) as health comes within SeamWarnBand of its threshold.</summary>
        void GlowSeam()
        {
            int next = platesBroken < plates.Length && seamRenderer[platesBroken] != null ? platesBroken : -1;
            float k = seamRest;
            if (next >= 0 && plateAt[next] >= 0f && dieTime < 0f)
            {
                float band = Mathf.Max(0.001f, cfg.SeamWarnBand);
                float close = 1f - Mathf.Clamp01((health01 - plateAt[next]) / band);
                if (close > 0f) k = seamRest + (Mathf.Max(seamRest, cfg.SeamFlare) - seamRest) * close * (0.8f + 0.2f * Mathf.Sin(Time.time * 18f));
            }
            if (next != seamPlate && seamPlate >= 0 && seamShown != seamRest)
            {
                // The flared seam broke off (or died): back to its resting glow.
                SetSeam(seamPlate, seamRest);
                seamShown = seamRest;
            }
            seamPlate = next;
            if (next < 0 || Mathf.Abs(k - seamShown) < 0.01f) return;
            seamShown = k;
            SetSeam(next, k);
        }

        /// <summary>Seam i's glow at k times its material's own (k = 1 clears the block).</summary>
        void SetSeam(int i, float k)
        {
            if (seamRenderer[i] == null) return;
            Paint.SetGlow(seamPaint[i], k);
        }

        static float Or(float value, float fallback) => value >= 0f ? value : fallback;

        /// <summary>Position/rotation amplitude of part d in clip c: bounce on the body, swing on limbs, 1 on the rest.</summary>
        float Amp(int d, EnemyGait c, float bounce, float swing)
        {
            if (c.Raw) return 1f;
            switch (drivenKind[d])
            {
                case KindBody: return Or(c.BodyBounce, bounce);
                case KindLimb: return Or(c.LegSwing, swing);
                default: return 1f;
            }
        }

        /// <summary>The Core's glow follows its swell (scale above rest = brighter).</summary>
        void GlowCore()
        {
            if (drivenGone[coreIndex]) return;
            float f = driven[coreIndex].localScale.x / Mathf.Max(1e-5f, drivenScale[coreIndex].x);
            bool bright = Mathf.Abs(f - 1f) > 0.02f;
            if (!bright && !coreBright) return;
            coreBright = bright;
            if (!bright)
            {
                Paint.SetGlow(corePaint, 1f);
                for (int i = 0; i < swellPaint.Length; i++) Paint.SetGlow(swellPaint[i], 1f);
                return;
            }
            // Linear in the swell and capped (scale squared clipped the orange toward yellow at the Lob hold).
            float k = Mathf.Clamp(f, 0.2f, Mathf.Max(1f, cfg.CoreGlowMax));
            Paint.SetGlow(corePaint, k);
            // The body's cracks pulse with it (a little stronger, so the swell reads past the core itself).
            float kb = Mathf.Clamp(1f + (k - 1f) * 1.5f, 0.2f, 1f + (Mathf.Max(1f, cfg.CoreGlowMax) - 1f) * 1.5f);
            for (int i = 0; i < swellPaint.Length; i++) Paint.SetGlow(swellPaint[i], kb);
        }

        /// <summary>Poses part d from one track at phase t (keys interpolated, or held with Interpolate off).</summary>
        void Pose(int d, EnemyGait clip, EnemyGait.Track tr, float t, bool loop, float amp, float squash)
        {
            var times = clip.KeyTimes;
            int n = times.Length, i0, i1;
            float w;
            if (loop)
            {
                i0 = n - 1;
                for (int i = 0; i < n; i++)
                    if (times[i] <= t) i0 = i;
                i1 = (i0 + 1) % n;
                float t0 = times[i0], t1 = i1 == 0 ? 1f : times[i1];
                w = cfg.Interpolate && t1 > t0 ? Mathf.Clamp01((t - t0) / (t1 - t0)) : 0f;
            }
            else
            {
                // A one-shot holds its first key before it and its last key after it.
                i0 = 0;
                for (int i = 0; i < n; i++)
                    if (times[i] <= t) i0 = i;
                i1 = Mathf.Min(i0 + 1, n - 1);
                float t0 = times[i0], t1 = times[i1];
                w = cfg.Interpolate && t1 > t0 ? Mathf.Clamp01((t - t0) / (t1 - t0)) : 0f;
            }
            var dp = Vector3.LerpUnclamped(Key(tr.Position, i0, Vector3.zero), Key(tr.Position, i1, Vector3.zero), w) * amp;
            var dr = Quaternion.Slerp(Key(tr.Rotation, i0), Key(tr.Rotation, i1), w);
            var ds = Vector3.LerpUnclamped(Key(tr.Scale, i0, Vector3.one), Key(tr.Scale, i1, Vector3.one), w);
            if (squash != 1f) ds = Vector3.LerpUnclamped(Vector3.one, ds, squash);
            Quaternion rotation;
            if (clip.AbsoluteRotation)
                rotation = amp != 1f ? Quaternion.SlerpUnclamped(drivenRot[d], dr, amp) : dr;
            else
            {
                if (amp != 1f) dr = Quaternion.SlerpUnclamped(Quaternion.identity, dr, amp);
                rotation = drivenRot[d] * dr;
            }
            var scale = Vector3.Scale(drivenScale[d], ds);
            if (drivenCrystal[d]) scale *= crystalMul;
            Set(d, drivenPos[d] + dp, rotation, scale);
        }

        static Vector3 Key(Vector3[] keys, int i, Vector3 fallback) => keys != null && keys.Length > 0 ? keys[Mathf.Min(i, keys.Length - 1)] : fallback;

        static Quaternion Key(Quaternion[] keys, int i)
        {
            if (keys == null || keys.Length == 0) return Quaternion.identity;
            var q = keys[Mathf.Min(i, keys.Length - 1)];
            return q.x == 0f && q.y == 0f && q.z == 0f && q.w == 0f ? Quaternion.identity : q;
        }

        // ---------------------------------------------------------------- plates

        /// <summary>
        /// Breaks off the plates due at this health (RigSettings.PlateBreakAt, spread evenly over the Plate_* parts):
        /// they fly off into world (a transform outside the enemy, so they stay where they broke off) with chips.
        /// ground/up: the enemy's point on the ground. Returns how many broke off now.
        /// </summary>
        public int BreakPlates(float health01, Transform world, Vector3 ground, Vector3 up)
        {
            var at = cfg.PlateBreakAt;
            if (plates.Length == 0 || at == null || at.Length == 0 || dieTime >= 0f) return 0;
            int due = 0;
            for (int i = 0; i < at.Length; i++)
                if (health01 <= at[i]) due++;
            int target = due >= at.Length ? plates.Length : Mathf.RoundToInt(plates.Length * due / (float)at.Length);
            int broke = 0;
            float scale = model.lossyScale.x;
            var centre = model.position + up * height * scale * 0.5f;
            while (platesBroken < target)
            {
                int i = platesBroken++;
                var p = parts[plates[i]];
                partGone[plates[i]] = true;
                for (int d = 0; d < driven.Length; d++)
                    if (driven[d] == p) drivenGone[d] = true;
                p.SetParent(world, true);
                plateStart[i] = Time.time;
                plateFrom[i] = p.position;
                plateRot[i] = p.rotation;
                plateScale[i] = p.localScale;
                plateGround[i] = ground;
                plateUp[i] = up;
                var outward = Vector3.ProjectOnPlane(p.position - centre, up);
                if (outward.sqrMagnitude < 1e-6f) outward = Vector3.ProjectOnPlane(Random.onUnitSphere, up);
                outward = outward.normalized;
                plateVel[i] = (outward * Random.Range(0.8f, 1.2f) + up * Random.Range(0.9f, 1.4f)) * cfg.PlateImpulse * scale;
                plateAxis[i] = Vector3.Cross(up, outward).normalized;
                if (plateAxis[i].sqrMagnitude < 0.5f) plateAxis[i] = Random.onUnitSphere;
                EnemyLook.Chips(p.position, 6, scale, 1.4f);
                Fx.Burst(p.position, new Color(0.62f, 0.57f, 0.5f, 0.8f), 0.5f * scale);
                Fx.Flash(p.position, EnemyLook.HostileGlow, 0.45f * scale);
                platesFlying++;
                broke++;
            }
            if (broke > 0) nextPose = 0f;
            return broke;
        }

        /// <summary>Broken plates in flight: arc off, land, sink and are gone (in pose steps).</summary>
        void TickPlates(float now)
        {
            if (platesFlying == 0) return;
            float secs = Mathf.Max(0.2f, cfg.PlateSeconds);
            float g = cfg.CrumbleGravity * model.lossyScale.x;
            for (int i = 0; i < platesBroken; i++)
            {
                if (plateStart[i] < 0f) continue;
                var p = parts[plates[i]];
                float t = now - plateStart[i];
                if (p == null || t >= secs)
                {
                    if (p != null) Object.Destroy(p.gameObject);
                    plateStart[i] = -1f;
                    platesFlying--;
                    continue;
                }
                float k = t / secs, sink = Mathf.Clamp01((k - 0.55f) / 0.45f);
                var up = plateUp[i];
                var pos = plateFrom[i] + plateVel[i] * t - up * (0.5f * g * t * t);
                float size = 0.08f * model.lossyScale.x;
                float floor = size * (1f - sink) - size * 2f * sink;
                float h = Vector3.Dot(pos - plateGround[i], up);
                bool landed = h < floor;
                if (landed) pos += up * (floor - h);
                float spin = cfg.CrumbleSpin * (landed ? Mathf.Min(t, 0.3f) : t);
                p.SetPositionAndRotation(pos, Quaternion.AngleAxis(spin, plateAxis[i]) * plateRot[i]);
                p.localScale = plateScale[i] * (1f - 0.7f * sink);
            }
        }

        /// <summary>After the crumble: moves on the plates still flying (the boss stays until they have landed).</summary>
        public void TickDebris()
        {
            float now = Time.time;
            if (platesFlying == 0 || now < nextPose) return;
            nextPose = now + 1f / Mathf.Max(1f, cfg.PoseFps);
            TickPlates(now);
        }

        /// <summary>The enemy is gone: removes plates still flying (they live outside it).</summary>
        public void Dispose()
        {
            for (int i = 0; i < platesBroken; i++)
            {
                if (plateStart[i] < 0f) continue;
                var p = parts[plates[i]];
                if (p != null) Object.Destroy(p.gameObject);
                plateStart[i] = -1f;
            }
            platesFlying = 0;
        }

        // ---------------------------------------------------------------- crumble

        /// <summary>Death: every part gets a small impulse and spin from here on (call once).</summary>
        public void BeginCrumble(Vector3 ground, Vector3 up)
        {
            dieTime = Time.time;
            dieGround = ground;
            dieUp = up;
            nextPose = 0f;
            popStart = -1f;
            playing = -1;
            if (coreBright && coreRenderer != null)
            {
                Paint.SetGlow(corePaint, 1f);
                for (int i = 0; i < swellPaint.Length; i++) Paint.SetGlow(swellPaint[i], 1f);
                coreBright = false;
            }
            if (seamPlate >= 0) SetSeam(seamPlate, seamRest);
            seamPlate = -1;
            seamShown = seamRest;
            pop.localScale = popRestScale;
            pop.localPosition = popRestPos;
            // One flat list of pieces: a child must not move with its parent on top of its own flight.
            for (int i = 0; i < parts.Length; i++)
                if (!partGone[i] && parts[i] != pop && parts[i].parent != pop) parts[i].SetParent(pop, true);
            var centre = Vector3.zero;
            int count = 0;
            for (int i = 0; i < parts.Length; i++)
                if (!partGone[i]) { centre += parts[i].position; count++; }
            centre /= Mathf.Max(1, count);
            float scale = model.lossyScale.x;
            // The cooled material sets for this enemy (made at death, not per frame; the materials are shared).
            if (glowSets == null && glow.Count > 0)
            {
                glowSets = new Material[glow.Count * 4][];
                for (int r = 0; r < glow.Count; r++)
                {
                    if (glow[r] == null) continue;
                    var hot = glow[r].sharedMaterials;
                    glowSets[r * 4] = hot;
                    for (int c = 0; c < 3; c++)
                    {
                        var set = new Material[hot.Length];
                        for (int m = 0; m < hot.Length; m++)
                            set[m] = hot[m] != null && hot[m].name.Contains("Glow") ? Cooled(hot[m], c) : hot[m];
                        glowSets[r * 4 + 1 + c] = set;
                    }
                }
            }
            for (int i = 0; i < parts.Length; i++)
            {
                if (partGone[i]) continue;
                var p = parts[i];
                partFrom[i] = p.position;
                partRot[i] = p.rotation;
                var mesh = p.GetComponent<MeshFilter>().sharedMesh;
                partSize[i] = mesh != null ? mesh.bounds.extents.magnitude * scale : 0.05f;
                var outward = Vector3.ProjectOnPlane(p.position - centre, up);
                if (outward.sqrMagnitude < 1e-6f) outward = Vector3.ProjectOnPlane(Random.onUnitSphere, up);
                outward = outward.normalized;
                // Small pieces (legs, crystals) skip out further than the big body, which mostly just drops apart.
                float light = Mathf.Clamp(0.08f * scale / Mathf.Max(0.01f, partSize[i]), 0.25f, 1.2f);
                partVel[i] = ((outward * Random.Range(0.6f, 1.2f) + Vector3.ProjectOnPlane(Random.insideUnitSphere, up) * 0.3f) * light
                              + up * Random.Range(0.7f, 1.2f)) * cfg.CrumbleImpulse * scale;
                partAxis[i] = Vector3.Cross(up, outward).normalized;
                if (partAxis[i].sqrMagnitude < 0.5f) partAxis[i] = Random.onUnitSphere;
                partSpin[i] = Random.Range(0.5f, 1f) * cfg.CrumbleSpin * (Random.value < 0.5f ? -1f : 1f);
            }
        }

        /// <summary>The crumble at k (0-1 of CrumbleSeconds), in pose steps: fly, land, then sink into the ground.</summary>
        public void TickCrumble(float k)
        {
            if (dieTime < 0f) return;
            float now = Time.time;
            if (now < nextPose) return;
            float step = 1f / Mathf.Max(1f, cfg.PoseFps);
            nextPose = now + step;
            TickPlates(now);
            float t = now - dieTime;
            float g = cfg.CrumbleGravity * model.lossyScale.x;
            float sink = Mathf.Clamp01((k - 0.5f) / 0.5f);
            for (int i = 0; i < parts.Length; i++)
            {
                if (partGone[i]) continue;
                var p = partFrom[i] + partVel[i] * t - dieUp * (0.5f * g * t * t);
                // Land on the ground (each piece rests on its own size), then sink and shrink into gravel.
                float floor = partSize[i] * 0.35f * (1f - sink) - partSize[i] * 1.2f * sink * sink;
                float h = Vector3.Dot(p - dieGround, dieUp);
                bool landed = h < floor;
                if (landed) p += dieUp * (floor - h);
                float spin = partSpin[i] * (landed ? Mathf.Min(t, 0.25f) : t);
                parts[i].SetPositionAndRotation(p, Quaternion.AngleAxis(spin, partAxis[i]) * partRot[i]);
            }
            // The glow cools down in three steps.
            int stepIndex = k < 0.25f ? 0 : k < 0.5f ? 1 : 2;
            if (stepIndex != glowStep)
            {
                glowStep = stepIndex;
                Paint.Cool(CoolHeat[stepIndex]); // slots with a block (palette or glow) cool here, the rest by material
                if (glowSets != null)
                    for (int i = 0; i < glow.Count; i++)
                        if (glow[i] != null && glowSets[i * 4 + 1 + stepIndex] != null) glow[i].sharedMaterials = glowSets[i * 4 + 1 + stepIndex];
            }
        }

        // Per glow material: three cooled copies (made once, on the first death).
        static readonly Dictionary<Material, Material[]> cooled = new Dictionary<Material, Material[]>();
        static readonly int EmissiveFactor = Shader.PropertyToID("emissiveFactor"), EmissionColor = Shader.PropertyToID("_EmissionColor"),
            BaseColorFactor = Shader.PropertyToID("baseColorFactor"), BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly Color CoolStone = new Color(0.18f, 0.17f, 0.2f, 1f);
        static readonly float[] CoolHeat = { 0.55f, 0.2f, 0f };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => cooled.Clear();

        static Material Cooled(Material hot, int step)
        {
            if (!cooled.TryGetValue(hot, out var set))
            {
                set = new Material[3];
                for (int i = 0; i < 3; i++)
                {
                    var m = new Material(hot) { name = hot.name + " (cooled " + i + ")" };
                    float h = CoolHeat[i];
                    foreach (var id in new[] { EmissiveFactor, EmissionColor })
                        if (m.HasProperty(id)) m.SetColor(id, m.GetColor(id) * h);
                    foreach (var id in new[] { BaseColorFactor, BaseColor })
                        if (m.HasProperty(id)) m.SetColor(id, Color.Lerp(CoolStone, m.GetColor(id), h));
                    set[i] = m;
                }
                cooled[hot] = set;
            }
            return set[step];
        }
    }
}
