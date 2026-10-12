using System.Collections.Generic;
using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>
    /// Runs every defensive building (see <see cref="AttackKind"/>): picks the enemy closest to the Core within range
    /// and attacks it with bullets, homing missiles, lobbed mortar shells, a laser beam, chain lightning, a flame
    /// field, a slow field or a mine blast. Flame and slow towers show their damage field on the ground; any tower's
    /// range shows while hovering it or placing one.
    /// </summary>
    [RequireComponent(typeof(IsoGrid), typeof(BuildController), typeof(WaveManager))]
    public class Combat : MonoBehaviour
    {
        public float BulletSpeed = 14f;
        public float MissileSpeed = 6f;
        public float MortarFlightTime = 1.1f;
        [Tooltip("Mortars can't hit enemies closer than this (cells).")]
        public float MortarMinRange = 1.5f;
        [Tooltip("Max jump between Tesla chain links (cells).")]
        public float TeslaJump = 2.2f;

        public float DamageDealt { get; private set; }

        class Shot
        {
            public AttackKind Kind;
            public SpriteRenderer Sr;
            public Vector3 Pos, Start, End;
            public Vector2 GridEnd;
            public Enemy Target;
            public float Damage, Splash, Age, Life;
            public Color Color;
        }

        IsoGrid map;
        BuildController build;
        WaveManager waves;
        TileHover hover;
        readonly List<Shot> shots = new List<Shot>();
        readonly Dictionary<Tower, LineRenderer> fields = new Dictionary<Tower, LineRenderer>();
        LineRenderer rangePreview;
        Transform root;

        static readonly Color FlameField = new Color(1f, 0.45f, 0.1f, 0.55f), SlowField = new Color(0.4f, 0.8f, 1f, 0.55f);

        void Start()
        {
            map = GetComponent<IsoGrid>();
            build = GetComponent<BuildController>();
            waves = GetComponent<WaveManager>();
            hover = GetComponent<TileHover>();
            root = new GameObject("Combat").transform;
            root.SetParent(transform, false);
            Fx.Init(transform, GetComponent<MapView>().SpriteMaterial);
            rangePreview = Fx.NewLine("Range", root, new Color(1f, 1f, 1f, 0.6f), 0.04f);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            foreach (var e in Enemy.All) e.SlowFactor = 1f;

            // Iterate over a copy: mines remove themselves when they go off.
            var towers = new List<Tower>();
            foreach (var b in build.Buildings)
                if (b is Tower t) towers.Add(t);
            foreach (var t in towers) Run(t, dt);

            UpdateShots(dt);
            UpdateFields(towers);
            UpdateRangePreview();
            Fx.Tick(dt);
        }

        // ---------------------------------------------------------------- towers

        void Run(Tower t, float dt)
        {
            var def = t.Def;
            var center = t.GridCenter;
            t.Cooldown -= dt;
            if (t.CanAim)
            {
                // Track the enemy it would shoot next, between shots too.
                var aim = PickTarget(center, def.Range, def.Attack == AttackKind.Mortar ? MortarMinRange : 0f, t.Target);
                if (aim != null) t.Aim(aim.GridPos - center);
            }
            if (t.BeamTimer > 0f)
            {
                t.BeamTimer -= dt;
                if (t.BeamTimer <= 0f && t.Beam != null && def.Attack == AttackKind.Tesla) t.Beam.enabled = false;
            }

            switch (def.Attack)
            {
                case AttackKind.Slow:
                    foreach (var e in Enemy.All)
                        if (InRange(e, center, def.Range)) e.SlowFactor = Mathf.Min(e.SlowFactor, 1f - Mathf.Clamp01(def.Damage));
                    return;

                case AttackKind.Flame:
                {
                    bool any = false;
                    foreach (var e in Enemy.All)
                        if (InRange(e, center, def.Range))
                        {
                            any = true;
                            Hit(e, def.Damage * dt);
                            if (Random.value < dt * 6f) Fx.Spawn(e.AimPoint, new Color(1f, 0.5f, 0.1f, 0.8f), 0.25f, 0.6f, 0.35f, Vector3.up * 0.5f);
                        }
                    if (any && Random.value < dt * 12f)
                    {
                        var dir = Random.insideUnitCircle * def.Range;
                        Fx.Spawn(Muzzle(t), new Color(1f, 0.55f, 0.15f, 0.7f), 0.2f, 0.7f, 0.45f, map.GridToWorld(center + dir) - map.GridToWorld(center));
                    }
                    CleanupDead();
                    return;
                }

                case AttackKind.Mine:
                    foreach (var e in Enemy.All)
                    {
                        if (e.Def.Hover > 0f || !InRange(e, center, def.Range)) continue;
                        Splash(center, def.Splash, def.Damage, true);
                        Fx.Explosion(map.GridToWorld(center, t.Level), def.Splash, new Color(1f, 0.6f, 0.2f, 0.9f));
                        build.RemoveBuilding(t, refund: false);
                        return;
                    }
                    return;

                case AttackKind.Laser:
                {
                    t.Target = PickTarget(center, def.Range, 0f, t.Target);
                    if (t.Beam == null) t.Beam = Fx.NewLine("Laser", t.transform, new Color(1f, 0.25f, 0.3f, 0.9f), 0.06f);
                    if (t.Target == null) { t.Beam.enabled = false; return; }
                    t.Beam.loop = false;
                    t.Beam.positionCount = 2;
                    t.Beam.SetPosition(0, Muzzle(t));
                    t.Beam.SetPosition(1, t.Target.AimPoint);
                    t.Beam.enabled = true;
                    if (Random.value < dt * 15f) Fx.Flash(t.Target.AimPoint, new Color(1f, 0.4f, 0.4f, 0.8f), 0.3f);
                    Hit(t.Target, def.Damage * dt);
                    CleanupDead();
                    return;
                }
            }

            // Discrete attacks.
            if (t.Cooldown > 0f) return;
            float minRange = def.Attack == AttackKind.Mortar ? MortarMinRange : 0f;
            t.Target = PickTarget(center, def.Range, minRange, null);
            if (t.Target == null) return;
            t.Cooldown = 1f / Mathf.Max(0.05f, def.FireRate);
            var muzzle = Muzzle(t);

            switch (def.Attack)
            {
                case AttackKind.Bullet:
                    Fx.Flash(muzzle, new Color(1f, 0.8f, 0.3f));
                    Fire(AttackKind.Bullet, muzzle, t.Target, def.Damage, 0f, new Color(1f, 0.85f, 0.4f), 0.14f);
                    break;
                case AttackKind.Missile:
                    Fx.Flash(muzzle, new Color(1f, 0.6f, 0.2f));
                    Fire(AttackKind.Missile, muzzle, t.Target, def.Damage, def.Splash, new Color(1f, 0.5f, 0.2f), 0.22f);
                    break;
                case AttackKind.Mortar:
                {
                    Fx.Flash(muzzle, new Color(1f, 0.7f, 0.3f), 0.5f);
                    var shot = Fire(AttackKind.Mortar, muzzle, null, def.Damage, def.Splash, new Color(1f, 0.75f, 0.35f), 0.24f);
                    shot.GridEnd = t.Target.GridPos;
                    shot.End = t.Target.transform.position;
                    shot.Life = MortarFlightTime;
                    break;
                }
                case AttackKind.Tesla:
                    Chain(t, muzzle);
                    break;
            }
        }

        Vector3 Muzzle(Tower t) => t.transform.position + Vector3.up * 0.55f;

        static bool InRange(Enemy e, Vector2 center, float range) => (e.GridPos - center).sqrMagnitude <= range * range;

        /// <summary>Keeps a still-valid current target (beams), else the enemy closest to the Core in range.</summary>
        Enemy PickTarget(Vector2 center, float range, float minRange, Enemy current)
        {
            if (current != null && current.Alive && InRange(current, center, range)) return current;
            Enemy best = null;
            foreach (var e in Enemy.All)
            {
                if (!e.Alive || !InRange(e, center, range)) continue;
                if (minRange > 0f && InRange(e, center, minRange)) continue;
                if (best == null || e.StepsToCore < best.StepsToCore) best = e;
            }
            return best;
        }

        void Chain(Tower t, Vector3 muzzle)
        {
            var def = t.Def;
            var hits = new List<Enemy> { t.Target };
            int links = Mathf.Max(1, Mathf.RoundToInt(def.Splash));
            while (hits.Count < links)
            {
                var last = hits[hits.Count - 1];
                Enemy next = null;
                float best = TeslaJump * TeslaJump;
                foreach (var e in Enemy.All)
                {
                    if (!e.Alive || hits.Contains(e)) continue;
                    float d = (e.GridPos - last.GridPos).sqrMagnitude;
                    if (d <= best) { best = d; next = e; }
                }
                if (next == null) break;
                hits.Add(next);
            }

            if (t.Beam == null) t.Beam = Fx.NewLine("Tesla", t.transform, new Color(0.6f, 0.85f, 1f, 0.95f), 0.05f);
            var points = new List<Vector3> { muzzle };
            var prev = muzzle;
            foreach (var e in hits)
            {
                // Jagged bolt: a couple of jittered midpoints per link.
                var end = e.AimPoint;
                for (int k = 1; k <= 2; k++)
                    points.Add(Vector3.Lerp(prev, end, k / 3f) + (Vector3)Random.insideUnitCircle * 0.12f);
                points.Add(end);
                prev = end;
                Fx.Flash(end, new Color(0.6f, 0.9f, 1f, 0.9f), 0.35f);
                Hit(e, def.Damage);
            }
            t.Beam.loop = false;
            t.Beam.positionCount = points.Count;
            t.Beam.SetPositions(points.ToArray());
            t.Beam.enabled = true;
            t.BeamTimer = 0.15f;
            CleanupDead();
        }

        // ---------------------------------------------------------------- projectiles

        Shot Fire(AttackKind kind, Vector3 from, Enemy target, float damage, float splash, Color color, float size)
        {
            var sr = new GameObject(kind.ToString()).AddComponent<SpriteRenderer>();
            sr.transform.SetParent(root, false);
            if (Fx.Material != null) sr.sharedMaterial = Fx.Material;
            sr.sprite = Fx.Soft;
            sr.color = color;
            sr.sortingOrder = Fx.Order;
            sr.transform.localScale = Vector3.one * size;
            sr.transform.position = from;
            var shot = new Shot { Kind = kind, Sr = sr, Pos = from, Start = from, Target = target, Damage = damage, Splash = splash, Color = color };
            if (target != null)
            {
                shot.End = target.AimPoint;
                shot.GridEnd = target.GridPos;
            }
            shots.Add(shot);
            return shot;
        }

        void UpdateShots(float dt)
        {
            for (int i = shots.Count - 1; i >= 0; i--)
            {
                var s = shots[i];
                s.Age += dt;
                bool done;
                if (s.Kind == AttackKind.Mortar)
                {
                    // Lob: straight line plus a parabola on top.
                    float t = Mathf.Clamp01(s.Age / s.Life);
                    s.Pos = Vector3.Lerp(s.Start, s.End, t) + Vector3.up * (4f * t * (1f - t) * 1.6f);
                    done = t >= 1f;
                    if (done)
                    {
                        Splash(s.GridEnd, s.Splash, s.Damage, true);
                        Fx.Explosion(s.End, s.Splash, new Color(1f, 0.55f, 0.2f, 0.85f));
                    }
                }
                else
                {
                    if (s.Target != null && s.Target.Alive)
                    {
                        s.End = s.Target.AimPoint;
                        s.GridEnd = s.Target.GridPos;
                    }
                    float speed = s.Kind == AttackKind.Missile ? MissileSpeed : BulletSpeed;
                    var to = s.End - s.Pos;
                    float step = speed * dt;
                    done = to.magnitude <= step;
                    s.Pos = done ? s.End : s.Pos + to.normalized * step;
                    if (s.Kind == AttackKind.Missile && Random.value < dt * 30f)
                        Fx.Spawn(s.Pos, new Color(0.7f, 0.7f, 0.7f, 0.5f), 0.1f, 0.25f, 0.4f);
                    if (done)
                    {
                        if (s.Kind == AttackKind.Missile)
                        {
                            Splash(s.GridEnd, s.Splash, s.Damage, false);
                            Fx.Explosion(s.End, Mathf.Max(0.6f, s.Splash), new Color(1f, 0.5f, 0.2f, 0.85f));
                        }
                        else if (s.Target != null && s.Target.Alive)
                        {
                            Hit(s.Target, s.Damage);
                            Fx.Flash(s.End, new Color(1f, 0.9f, 0.5f), 0.2f);
                        }
                    }
                }

                if (s.Sr != null) s.Sr.transform.position = s.Pos;
                if (!done && s.Age < 6f) continue;
                if (s.Sr != null) Destroy(s.Sr.gameObject);
                shots.RemoveAt(i);
            }
            CleanupDead();
        }

        void Splash(Vector2 center, float radius, float damage, bool groundOnly)
        {
            foreach (var e in Enemy.All)
            {
                if (groundOnly && e.Def.Hover > 0f) continue;
                if ((e.GridPos - center).sqrMagnitude <= radius * radius) Hit(e, damage);
            }
            CleanupDead();
        }

        void Hit(Enemy e, float damage)
        {
            if (!e.Alive) return;
            DamageDealt += Mathf.Min(damage, e.Health);
            e.TakeDamage(damage);
        }

        /// <summary>Removes dead enemies and pays their bounty.</summary>
        void CleanupDead()
        {
            for (int i = Enemy.All.Count - 1; i >= 0; i--)
            {
                var e = Enemy.All[i];
                if (e.Alive) continue;
                waves.OnKilled(e);
                Fx.Burst(e.AimPoint, new Color(0.7f, 1f, 0.4f, 0.9f), 0.5f);
                e.Remove();
            }
        }

        // ---------------------------------------------------------------- damage fields and ranges

        /// <summary>Flame and slow towers always show their field on the ground.</summary>
        void UpdateFields(List<Tower> towers)
        {
            foreach (var t in towers)
            {
                if (t.Def.Attack != AttackKind.Flame && t.Def.Attack != AttackKind.Slow) continue;
                if (fields.ContainsKey(t)) continue;
                var color = t.Def.Attack == AttackKind.Flame ? FlameField : SlowField;
                var lr = Fx.NewLine("Field", root, color, 0.05f, map.SortOrder(t.Origin) + 2);
                Fx.Ring(lr, map, t.GridCenter, t.Def.Range, t.Level);
                fields[t] = lr;
            }
            var gone = new List<Tower>();
            foreach (var pair in fields)
                if (pair.Key == null) gone.Add(pair.Key);
            foreach (var t in gone)
            {
                if (fields[t] != null) Destroy(fields[t].gameObject);
                fields.Remove(t);
            }
            foreach (var pair in fields)
            {
                // Gentle pulse so fields read as active.
                var c = pair.Value.startColor;
                c.a = 0.35f + 0.2f * Mathf.Sin(Time.time * 3f);
                pair.Value.startColor = pair.Value.endColor = c;
            }
        }

        /// <summary>Range ring for the tower being placed, or the placed tower under the cursor.</summary>
        void UpdateRangePreview()
        {
            rangePreview.enabled = false;
            var placing = build.Placing;
            if (placing != null && placing.Attack != AttackKind.None && build.GhostOrigin.HasValue)
            {
                var o = build.GhostOrigin.Value;
                var center = (Vector2)o + (Vector2)(placing.Size - Vector2Int.one) * 0.5f;
                Fx.Ring(rangePreview, map, center, placing.Range, map.HeightAt(o));
                return;
            }
            if (hover != null && hover.HasCell && map.BuildingAt(hover.Cell) is Tower t && t.Def.Range > 0f)
                Fx.Ring(rangePreview, map, t.GridCenter, t.Def.Range, t.Level);
        }
    }
}
