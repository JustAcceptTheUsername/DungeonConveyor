using System.Collections.Generic;
using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>
    /// The meteor shell an enemy hatches from (EnemyCatalog.Rig.MeteorShell, e.g. Models/Fx/Meteor.glb): it sits at the
    /// landing spot, cracks open with a hostile-glow burst (the top pieces blow up and out, the middle ones tip over
    /// and fall, the base stays), then the pieces and the crater sink into the ground. Any enemy can use it; Enemy
    /// starts one on landing and pops its rig out at CrackAt. Poses are stepped like the rigs (RigSettings.PoseFps).
    /// Shells are pooled.
    /// Part names: Core (glowing egg, shrinks away), Crater (ground ring, stays until it sinks), Shell_1 = base cap,
    /// Shell_2.. = the rest; pieces higher than the core centre fly, the lower ones tip.
    /// </summary>
    public class MeteorHatch : MonoBehaviour
    {
        static readonly Stack<MeteorHatch> pool = new Stack<MeteorHatch>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => pool.Clear();

        Transform[] pieces;
        Vector3[] restPos, vel, axis;
        Quaternion[] restRot;
        float[] spin;
        bool[] flies;
        Transform core, crater, shell;
        Vector3 coreScale, craterScale;
        float start, nextPose, coreHeight;
        bool cracked;
        EnemyCatalog.RigSettings cfg;

        /// <summary>A shell at ground (on the planet: up = its normal), sized for an enemy `height` world units tall.</summary>
        public static void Play(Vector3 ground, Vector3 up, Vector3 facing, float height, Transform parent)
        {
            var cfg = EnemyRig.Settings;
            if (cfg.MeteorShell == null) return;
            MeteorHatch h = null;
            while (pool.Count > 0 && h == null) h = pool.Pop();
            if (h == null)
            {
                var go = new GameObject("MeteorHatch");
                h = go.AddComponent<MeteorHatch>();
                h.Build(cfg.MeteorShell);
            }
            h.cfg = cfg;
            h.transform.SetParent(parent, false);
            var fwd = Vector3.ProjectOnPlane(facing.sqrMagnitude > 1e-6f ? facing : Random.onUnitSphere, up);
            h.transform.SetPositionAndRotation(ground, Quaternion.LookRotation(fwd.normalized, up) * Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            // The shell model is 0.9 tall at scale 1 (core centre at 0.42).
            h.transform.localScale = Vector3.one * Mathf.Max(0.1f, height * cfg.ShellSize / 0.9f);
            h.gameObject.SetActive(true);
            h.Begin();
        }

        void Build(GameObject prefab)
        {
            shell = Instantiate(prefab, transform, false).transform;
            PlanetArt.StripColliders(shell.gameObject);
            var list = new List<Transform>();
            foreach (var mf in shell.GetComponentsInChildren<MeshFilter>(true))
            {
                var r = mf.GetComponent<Renderer>();
                if (r != null) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var t = mf.transform;
                if (t.name == "Core") core = t;
                else if (t.name == "Crater") crater = t;
                else if (t.name.StartsWith("Shell_")) list.Add(t);
            }
            pieces = list.ToArray();
            int n = pieces.Length;
            restPos = new Vector3[n];
            restRot = new Quaternion[n];
            vel = new Vector3[n];
            axis = new Vector3[n];
            spin = new float[n];
            flies = new bool[n];
            for (int i = 0; i < n; i++)
            {
                restPos[i] = pieces[i].localPosition;
                restRot[i] = pieces[i].localRotation;
            }
            if (core != null) { coreScale = core.localScale; coreHeight = core.localPosition.y; }
            else coreHeight = 0.42f;
            if (crater != null) craterScale = crater.localScale;
        }

        void Begin()
        {
            start = Time.time;
            nextPose = 0f;
            cracked = false;
            for (int i = 0; i < pieces.Length; i++)
            {
                pieces[i].localPosition = restPos[i];
                pieces[i].localRotation = restRot[i];
                pieces[i].localScale = Vector3.one;
                var outward = new Vector3(restPos[i].x, 0f, restPos[i].z);
                if (outward.sqrMagnitude < 1e-4f) outward = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f));
                outward.Normalize();
                flies[i] = restPos[i].y > coreHeight + 0.05f;
                bool baseCap = restPos[i].y < coreHeight * 0.5f;
                vel[i] = baseCap ? Vector3.zero
                    : flies[i] ? outward * Random.Range(1.2f, 1.8f) + Vector3.up * Random.Range(1.8f, 2.6f)
                    : outward * Random.Range(0.5f, 0.8f) + Vector3.up * Random.Range(0.3f, 0.6f);
                axis[i] = Vector3.Cross(Vector3.up, outward);
                spin[i] = baseCap ? 0f : (flies[i] ? Random.Range(300f, 540f) : Random.Range(120f, 200f));
            }
            if (core != null) { core.localScale = coreScale; core.gameObject.SetActive(true); }
            if (crater != null) crater.localScale = craterScale;
            shell.localPosition = Vector3.zero;
            shell.localScale = new Vector3(1.12f, 0.82f, 1.12f); // impact squash
        }

        void Update()
        {
            float now = Time.time, t = now - start;
            float total = Mathf.Max(cfg.CrackAt + 0.3f, cfg.ShellSeconds);
            if (t >= total)
            {
                gameObject.SetActive(false);
                pool.Push(this);
                return;
            }
            if (!cracked && t >= cfg.CrackAt)
            {
                cracked = true;
                var c = transform.TransformPoint(Vector3.up * coreHeight);
                float s = transform.lossyScale.x;
                Fx.Burst(c, EnemyLook.HostileGlow, 1.1f * s);
                Fx.Flash(c, new Color(1f, 0.55f, 0.7f, 1f), 0.9f * s);
                EnemyLook.Chips(c, 5, s, 1.4f);
                nextPose = 0f;
            }
            if (now < nextPose) return;
            nextPose = now + 1f / Mathf.Max(1f, cfg.PoseFps);

            if (!cracked)
            {
                // Settle out of the impact squash, the core pulsing behind the gaps.
                float u = Mathf.Clamp01(t / Mathf.Max(0.01f, cfg.CrackAt));
                shell.localScale = Vector3.LerpUnclamped(new Vector3(1.12f, 0.82f, 1.12f), Vector3.one, u);
                if (core != null) core.localScale = coreScale * (1f + 0.12f * Mathf.Sin(u * Mathf.PI * 3f));
                return;
            }
            shell.localScale = Vector3.one;
            float tc = t - cfg.CrackAt, end = total - cfg.CrackAt;
            float sink = Mathf.Clamp01((tc - end * 0.45f) / (end * 0.55f));
            const float gravity = 9f;
            for (int i = 0; i < pieces.Length; i++)
            {
                var p = restPos[i] + vel[i] * tc + Vector3.down * (0.5f * gravity * tc * tc);
                float floor = 0.06f - 0.25f * sink;
                bool down = p.y < floor && vel[i] != Vector3.zero;
                if (down) p.y = floor;
                if (vel[i] == Vector3.zero) p.y = restPos[i].y - 0.4f * sink;
                pieces[i].localPosition = p;
                float a = spin[i] * (down ? Mathf.Min(tc, 0.3f) : tc);
                pieces[i].localRotation = Quaternion.AngleAxis(a, axis[i]) * restRot[i];
                pieces[i].localScale = Vector3.one * (1f - 0.6f * sink);
            }
            if (core != null)
            {
                float f = Mathf.Clamp01(1f - tc / 0.2f);
                core.localScale = coreScale * f;
                core.gameObject.SetActive(f > 0f);
            }
            if (crater != null) crater.localScale = craterScale * (1f - sink);
        }
    }
}
