using System.Collections.Generic;
using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>
    /// Procedural animation for rigid-part enemies (one node per part, pivot at the joint; no skinning, no Animator).
    /// Built for any model that follows the part naming convention (see notes/enemy-review/pilot/PILOT.md):
    /// - Walk: an EnemyGait cycle with its phase locked to the distance walked (slows with chill and slow fields,
    ///   holds when the enemy stops), a random phase per enemy, and the poses stepped at RigSettings.PoseFps
    ///   (12 fps: held between steps) while travel over the planet stays smooth every frame.
    /// - Pop: the hatch out of the meteor shell (or a split-off copy appearing): pops up, overshoots, squash-lands.
    /// - Hit: a one-step squash.
    /// - Crumble: on death every part flies off with a small impulse, tumbles, lands and sinks (gravel), and the glow
    ///   parts cool down in steps.
    /// Owned and ticked by Enemy (no Update of its own); allocation-free per frame. Off when EnemyCatalog.RigAnimation is.
    /// </summary>
    public sealed class EnemyRig
    {
        public static bool On => EnemyCatalog.Instance == null || EnemyCatalog.Instance.RigAnimation;
        public static EnemyCatalog.RigSettings Settings => EnemyCatalog.Instance != null ? EnemyCatalog.Instance.Rig : DefaultSettings;
        static readonly EnemyCatalog.RigSettings DefaultSettings = new EnemyCatalog.RigSettings();

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

        readonly EnemyCatalog.RigSettings cfg;
        readonly Transform model, pop;
        readonly Vector3 popRestPos, popRestScale;
        readonly float height; // model height in pop-node units

        // Walk
        readonly EnemyGait gait;
        readonly Transform[] driven;
        readonly Vector3[] drivenPos, drivenScale;
        readonly Quaternion[] drivenRot;
        readonly bool[] drivenBody;
        float phase, nextPose, popStart = -1f, hitPose;

        // Crumble (every part with a mesh, parents before children)
        readonly Transform[] parts;
        readonly Vector3[] partFrom, partVel, partAxis;
        readonly Quaternion[] partRot;
        readonly float[] partSpin, partSize;
        readonly List<Renderer> glow = new List<Renderer>();  // renderers with a glow material (any slot)
        Material[][] glowSets;                                // per glow renderer: hot, then three cooled slot sets
        float dieTime = -1f;
        Vector3 dieGround, dieUp;
        int glowStep = -1;

        public bool Walks => gait != null && driven.Length > 0;
        public float CrumbleSeconds => Mathf.Clamp(cfg.CrumbleSeconds, 0.6f, 1f);

        /// <summary>The rig for model if it has rigid parts (any child node with a mesh besides the root), else null.</summary>
        public static EnemyRig Create(GameObject model, EnemyGait gait)
        {
            var filters = model.GetComponentsInChildren<MeshFilter>(true);
            if (filters.Length < 2) return null; // one merged mesh: nothing to animate
            return new EnemyRig(model.transform, filters, gait);
        }

        EnemyRig(Transform model, MeshFilter[] filters, EnemyGait gait)
        {
            cfg = Settings;
            this.model = model;
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

            this.gait = gait != null && gait.Keys > 1 && gait.Tracks != null ? gait : null;
            var found = new List<int>();
            var nodes = new List<Transform>();
            if (this.gait != null)
                for (int i = 0; i < this.gait.Tracks.Length; i++)
                {
                    var tr = Find(pop, this.gait.Tracks[i].Part);
                    if (tr == null) continue;
                    found.Add(i);
                    nodes.Add(tr);
                }
            driven = nodes.ToArray();
            trackOf = found.ToArray();
            drivenPos = new Vector3[driven.Length];
            drivenRot = new Quaternion[driven.Length];
            drivenScale = new Vector3[driven.Length];
            drivenBody = new bool[driven.Length];
            for (int i = 0; i < driven.Length; i++)
            {
                drivenPos[i] = driven[i].localPosition;
                drivenRot[i] = driven[i].localRotation;
                drivenScale[i] = driven[i].localScale;
                drivenBody[i] = driven[i].name == "Body";
            }
            phase = Random.value;
            nextPose = Time.time + Random.value / Mathf.Max(1f, cfg.PoseFps); // spread the steps over frames

            parts = new Transform[filters.Length];
            for (int i = 0; i < filters.Length; i++)
            {
                parts[i] = filters[i].transform;
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
            if (this.gait != null) ApplyPose();
        }

        readonly int[] trackOf; // driven index -> gait track

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

        /// <summary>Hides the model until the pop starts at time `at` (Time.time), then pops it out.</summary>
        public void BeginPop(float at)
        {
            popStart = at;
            nextPose = 0f; // show the hidden state right away
            ApplyPose();
        }

        /// <summary>A hit: the next pose step is squashed.</summary>
        public void Hit() => hitPose = 1f;

        /// <summary>Every frame while it walks: walked = world distance covered this frame.</summary>
        public void Tick(float walked)
        {
            if (dieTime >= 0f) return;
            if (gait != null)
            {
                float stride = gait.StrideLength * Mathf.Max(0.05f, cfg.StrideScale) * model.lossyScale.x;
                phase += walked / Mathf.Max(0.001f, stride);
                if (phase > 1000f) phase -= 1000f;
            }
            float now = Time.time;
            if (now < nextPose) return;
            float step = 1f / Mathf.Max(1f, cfg.PoseFps);
            nextPose = now - nextPose > step ? now + step : nextPose + step;
            ApplyPose();
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
            pop.localPosition = popRestPos + Vector3.up * rise;

            if (gait == null || driven.Length == 0) return;
            // Walk: the pose at the current phase (keys interpolated, or held with Interpolate off).
            float t = phase - Mathf.Floor(phase);
            var times = gait.KeyTimes;
            int n = times.Length, i0 = n - 1;
            for (int i = 0; i < n; i++)
                if (times[i] <= t) i0 = i;
            int i1 = (i0 + 1) % n;
            float t0 = times[i0], t1 = i1 == 0 ? 1f : times[i1];
            float w = cfg.Interpolate && t1 > t0 ? Mathf.Clamp01((t - t0) / (t1 - t0)) : 0f;
            for (int d = 0; d < driven.Length; d++)
            {
                var tr = gait.Tracks[trackOf[d]];
                float amp = drivenBody[d] ? cfg.BodyBounce : cfg.LegSwing;
                var dp = Vector3.LerpUnclamped(tr.Position[i0], tr.Position[i1], w) * amp;
                var dr = Quaternion.Slerp(tr.Rotation[i0], tr.Rotation[i1], w);
                if (amp != 1f) dr = Quaternion.SlerpUnclamped(Quaternion.identity, dr, amp);
                var ds = Vector3.LerpUnclamped(tr.Scale[i0], tr.Scale[i1], w);
                if (drivenBody[d]) ds = Vector3.LerpUnclamped(Vector3.one, ds, cfg.BodySquash);
                driven[d].localPosition = drivenPos[d] + dp;
                driven[d].localRotation = drivenRot[d] * dr;
                driven[d].localScale = Vector3.Scale(drivenScale[d], ds);
            }
        }

        /// <summary>Death: every part gets a small impulse and spin from here on (call once).</summary>
        public void BeginCrumble(Vector3 ground, Vector3 up)
        {
            dieTime = Time.time;
            dieGround = ground;
            dieUp = up;
            nextPose = 0f;
            popStart = -1f;
            pop.localScale = popRestScale;
            pop.localPosition = popRestPos;
            var centre = Vector3.zero;
            for (int i = 0; i < parts.Length; i++) centre += parts[i].position;
            centre /= Mathf.Max(1, parts.Length);
            float scale = model.lossyScale.x;
            // The cooled material sets for this enemy (made at death, not per frame; the materials are shared).
            if (glowSets == null && glow.Count > 0)
            {
                glowSets = new Material[glow.Count * 4][];
                for (int r = 0; r < glow.Count; r++)
                {
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
            float t = now - dieTime;
            float g = cfg.CrumbleGravity * model.lossyScale.x;
            float sink = Mathf.Clamp01((k - 0.5f) / 0.5f);
            for (int i = 0; i < parts.Length; i++)
            {
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
                if (glowSets != null)
                    for (int i = 0; i < glow.Count; i++) glow[i].sharedMaterials = glowSets[i * 4 + 1 + stepIndex];
            }
        }

        // Per glow material: three cooled copies (made once, on the first death).
        static readonly Dictionary<Material, Material[]> cooled = new Dictionary<Material, Material[]>();
        static readonly int EmissiveFactor = Shader.PropertyToID("emissiveFactor"), EmissionColor = Shader.PropertyToID("_EmissionColor"),
            BaseColorFactor = Shader.PropertyToID("baseColorFactor"), BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly Color CoolStone = new Color(0.18f, 0.17f, 0.2f, 1f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => cooled.Clear();

        static Material Cooled(Material hot, int step)
        {
            if (!cooled.TryGetValue(hot, out var set))
            {
                set = new Material[3];
                float[] heat = { 0.55f, 0.2f, 0f };
                for (int i = 0; i < 3; i++)
                {
                    var m = new Material(hot) { name = hot.name + " (cooled " + i + ")" };
                    float h = heat[i];
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
