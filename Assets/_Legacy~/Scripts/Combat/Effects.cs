using System.Collections.Generic;
using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>Particles, beams and floating combat text.</summary>
    public static class Effects
    {
        public struct FloatText
        {
            public Vector3 Pos;
            public string Text;
            public Color Color;
            public float Born;
            public float Size;
        }

        public const float FloatTextLife = 0.9f;
        public static readonly List<FloatText> Texts = new List<FloatText>();

        static ParticleSystem sparks;
        static Material beamMat;

        public static void Init(Transform parent)
        {
            Texts.Clear();

            var go = new GameObject("Sparks");
            go.transform.SetParent(parent, false);
            sparks = go.AddComponent<ParticleSystem>();
            sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = sparks.main;
            main.loop = false;
            main.playOnAwake = false;
            main.maxParticles = 3000;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.16f);
            main.gravityModifier = 1.2f;
            var emission = sparks.emission;
            emission.rateOverTime = 0;
            var shape = sparks.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.1f;
            var col = sparks.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(FadeOut());
            var size = sparks.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1, 1, 0.2f));
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = Art.Additive;
            sparks.Play();

            beamMat = Art.Transparent(new Color(1f, 0.55f, 0.15f, 0.9f));
            beamMat.SetColor("_BaseColor", new Color(4f, 1.6f, 0.3f, 1f));
        }

        static Gradient FadeOut()
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 0.5f), new GradientAlphaKey(0, 1) });
            return g;
        }

        public static void Sparks(Vector3 pos, Color color, int count, float size = 1f)
        {
            if (sparks == null) return;
            var p = new ParticleSystem.EmitParams
            {
                position = pos,
                applyShapeToPosition = true,
                startColor = color * 1.5f,
            };
            if (size != 1f) p.startSize = 0.12f * size;
            sparks.Emit(p, count);
        }

        public static void Flash(Vector3 pos, Color color)
        {
            if (sparks == null) return;
            var p = new ParticleSystem.EmitParams
            {
                position = pos,
                velocity = Vector3.up * 0.2f,
                startColor = color * 2f,
                startSize = 0.45f,
                startLifetime = 0.07f,
            };
            sparks.Emit(p, 1);
        }

        public static void Explosion(Vector3 pos)
        {
            Sparks(pos + Vector3.up * 0.4f, new Color(1f, 0.5f, 0.1f), 40, 2f);
            Sparks(pos + Vector3.up * 0.4f, new Color(0.4f, 0.4f, 0.4f), 20, 2.5f);
        }

        public static void Text(Vector3 pos, string text, Color color, float size = 1f)
        {
            if (Texts.Count > 120) Texts.RemoveAt(0);
            Texts.Add(new FloatText { Pos = pos, Text = text, Color = color, Born = Time.time, Size = size });
        }

        public static ParticleSystem CreateFlame(Transform parent)
        {
            var go = new GameObject("Flame");
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(7f, 9f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.2f, 0.4f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.6f, 0.15f), new Color(1f, 0.3f, 0.05f));
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 14f;
            shape.radius = 0.05f;
            var emission = ps.emission;
            emission.rateOverTime = 70;
            emission.enabled = false;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.5f, 1, 2.5f));
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.9f, 0.5f), 0), new GradientColorKey(new Color(1f, 0.35f, 0.05f), 0.5f), new GradientColorKey(new Color(0.3f, 0.05f, 0.02f), 1) },
                new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0, 1) });
            col.color = new ParticleSystem.MinMaxGradient(g);
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = Art.Additive;
            ps.Play();
            return ps;
        }

        public static LineRenderer CreateBeam(Transform parent)
        {
            var go = new GameObject("Beam");
            go.transform.SetParent(parent, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = beamMat;
            lr.positionCount = 2;
            lr.widthMultiplier = 0.12f;
            lr.useWorldSpace = true;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.enabled = false;
            return lr;
        }
    }
}
