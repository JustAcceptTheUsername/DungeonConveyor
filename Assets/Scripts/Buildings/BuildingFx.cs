using System.Collections.Generic;
using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>
    /// Particle effects of a building model, set up on its prefab (Assets/Prefabs/Models/Buildings) and made by
    /// Dungeon > Create Building Effects. Each shot or action plays FireEffect at the next of the Muzzles in turn
    /// (child points, so they turn with a tower's Head); LoopEffect runs at LoopPoint while the building is working.
    /// Effect sizes and speeds are in model units (1 = one cell): the effects scale with the model.
    /// Does nothing until Init, so the sprite bakes (ModelArt) see only the bare model.
    /// </summary>
    public class BuildingFx : MonoBehaviour
    {
        [Tooltip("Where shots or one-off effects come from, used in turn (e.g. one per gun barrel). Their +Z points out of the barrel.")]
        public Transform[] Muzzles = new Transform[0];
        [Tooltip("Played at the next muzzle on every shot or action.")]
        public ParticleSystem FireEffect;
        [Tooltip("FireEffect is spawned loose in the world instead of on the muzzle, so it outlives the building (land mines).")]
        public bool DetachFireEffect;
        [Tooltip("Seconds a detached fire effect lives before it is removed.")]
        public float DetachedLifetime = 4f;
        [Tooltip("Runs while the building is working (flame jet, drilling, refining...).")]
        public ParticleSystem LoopEffect;
        public Transform LoopPoint;
        [Tooltip("The loop runs all the time, not just while working.")]
        public bool LoopAlways;

        readonly List<ParticleSystem> fire = new List<ParticleSystem>();
        ParticleSystem loop;
        int order, next;
        bool ready, looping;

        /// <summary>Instantiates the effects onto their points; sortingOrder is the building's (drawn just above it).</summary>
        public void Init(int sortingOrder)
        {
            if (ready) return;
            ready = true;
            order = sortingOrder + 1;
            if (FireEffect != null && !DetachFireEffect)
                foreach (var m in Muzzles)
                    fire.Add(m != null ? Attach(FireEffect, m) : null);
            if (LoopEffect != null) loop = Attach(LoopEffect, LoopPoint != null ? LoopPoint : transform);
            SetWorking(LoopAlways);
        }

        /// <summary>World position of the muzzle the next Fire uses.</summary>
        public Vector3 NextMuzzle
        {
            get
            {
                var m = Muzzles.Length > 0 ? Muzzles[next % Muzzles.Length] : null;
                return m != null ? m.position : transform.position;
            }
        }

        public bool HasMuzzles => Muzzles.Length > 0;

        /// <summary>Plays the fire effect at the next muzzle and returns that muzzle's world position.</summary>
        public Vector3 Fire()
        {
            var pos = NextMuzzle;
            if (!ready || FireEffect == null) { Advance(); return pos; }
            if (DetachFireEffect)
            {
                var src = Muzzles.Length > 0 ? Muzzles[next % Muzzles.Length] : null;
                if (src == null) src = transform;
                var ps = Instantiate(FireEffect, src.position, src.rotation);
                ps.transform.localScale = src.lossyScale;
                Prepare(ps);
                ps.Play(true);
                Destroy(ps.gameObject, DetachedLifetime);
            }
            else if (fire.Count > 0 && fire[next % fire.Count] != null)
                fire[next % fire.Count].Play(true);
            Advance();
            return pos;
        }

        /// <summary>Starts or stops the loop effect (no-op while it already is in that state).</summary>
        public void SetWorking(bool working)
        {
            working |= LoopAlways;
            if (loop == null || working == looping) return;
            looping = working;
            if (working) loop.Play(true);
            else loop.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        void Advance() { if (Muzzles.Length > 0) next = (next + 1) % Muzzles.Length; }

        ParticleSystem Attach(ParticleSystem prefab, Transform point)
        {
            var ps = Instantiate(prefab, point, false);
            ps.transform.localPosition = Vector3.zero;
            ps.transform.localRotation = Quaternion.identity;
            Prepare(ps);
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return ps;
        }

        void Prepare(ParticleSystem ps)
        {
            foreach (var r in ps.GetComponentsInChildren<ParticleSystemRenderer>(true)) r.sortingOrder = order;
            foreach (var p in ps.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = p.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                main.playOnAwake = false;
            }
        }
    }
}
