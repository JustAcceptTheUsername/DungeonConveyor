using System.Collections.Generic;
using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>
    /// Runs every defensive building (see <see cref="AttackKind"/>): picks the enemy closest to the Core within range
    /// and attacks it with bullets, homing missiles, lobbed mortar shells, a laser beam, chain lightning, a flame
    /// field, a slow field or a mine blast. Flame and slow towers show their damage field on the ground; any tower's
    /// range shows while hovering it or placing one. Ranges are world distances (a cell is about one unit across),
    /// and enemies can only be hit once they have landed.
    /// Run upgrades scale each tower's damage, attack speed and range, and add elemental damage to its hits: fire
    /// burns, lightning arcs to a second enemy, frost chills (slows), poison stacks (see ElementalHit).
    /// On top of that: towers on high ground reach further, crystals fed in by belt boost a tower for a while (Tower),
    /// armored enemies take half damage from bullets and lasers, fliers are out of reach of mines and flames, and the
    /// run's synergy cards add their combos (Combustion, Conduction, Shatter, Chain Reaction, Overclock). A Bastion
    /// Core (Meta) shoots too.
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

        [Header("Elements (per point of element damage)")]
        [Tooltip("Burn damage per second, for BurnSeconds.")]
        public float BurnDpsPerPoint = 0.5f;
        public float BurnSeconds = 3f;
        [Tooltip("Lightning arcs to the nearest other enemy within this many cells.")]
        public float ArcJump = 2f;
        public float ChillBase = 0.15f, ChillPerPoint = 0.02f, ChillMax = 0.5f, ChillSeconds = 1.5f;
        [Tooltip("Poison damage per second per stack.")]
        public float PoisonDpsPerPoint = 0.4f;
        public int PoisonMaxStacks = 8;
        public float PoisonSeconds = 4f;
        [Tooltip("Continuous attacks (laser, flame) apply elements as if hitting this many times per second.")]
        public float ContinuousHitsPerSecond = 2f;

        [Header("High ground")]
        [Tooltip("Extra range per elevation level a tower stands above level 0 (a fraction of its range).")]
        public float RangePerLevel = 0.06f;
        public float MaxHighGroundBonus = 0.36f;

        [Header("Ore boosts")]
        public float BoostFireRate = 0.4f, BoostDamage = 0.35f, BoostRange = 1f;

        [Header("Bastion Core")]
        public float CoreRange = 4f, CoreFireRate = 1.25f, CoreDamage = 18f;

        public float DamageDealt { get; private set; }

        class Shot
        {
            public AttackKind Kind;
            public SpriteRenderer Sr;
            public Vector3 Pos, Start, End;
            public Enemy Target;
            public float Damage, Splash, Age, Life;
            public Color Color;
            public float[] Elements;
            public Tower Source;
        }

        IsoGrid map;
        BuildController build;
        WaveManager waves;
        TileHover hover;
        readonly List<Shot> shots = new List<Shot>();
        readonly Dictionary<Tower, LineRenderer> fields = new Dictionary<Tower, LineRenderer>();
        readonly List<(Vector3 at, float radius, float damage)> deathBlasts = new List<(Vector3, float, float)>();
        LineRenderer rangePreview;
        Transform root;
        float coreCooldown;

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
            rangePreview = Fx.NewLine("Range", root, new Color(1f, 1f, 1f, 0.6f), 0.06f);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            foreach (var e in Enemy.All) e.SlowFactor = 1f;
            for (int i = Enemy.All.Count - 1; i >= 0; i--)
            {
                var e = Enemy.All[i];
                float dot = e.TickEffects(dt);
                if (dot > 0f) Hit(e, dot, AttackKind.None, null, quiet: true);
            }
            CleanupDead();

            // Iterate over a copy: mines remove themselves when they go off.
            var towers = new List<Tower>();
            foreach (var b in build.Buildings)
                if (b is Tower t) towers.Add(t);
            foreach (var t in towers) Run(t, dt);
            BastionCore(dt);

            UpdateShots(dt);
            UpdateFields(towers);
            UpdateRangePreview();
            Fx.Tick(dt);
        }

        // ---------------------------------------------------------------- towers

        /// <summary>Range bonus (fraction) for standing on high ground.</summary>
        public float HighGround(int level) => Mathf.Clamp(level * RangePerLevel, 0f, MaxHighGroundBonus);

        /// <summary>A placed tower's range: upgrades, high ground and its emerald boost.</summary>
        public float RangeOf(Tower t) =>
            RangeOf(t.Def) * (t.Def.Attack == AttackKind.Mine ? 1f : 1f + HighGround(t.Level)) + (t.Boosted(OreBoost.Range) ? BoostRange : 0f);

        /// <summary>Range of a tower about to be built at this level.</summary>
        public float RangeAt(BuildingCatalog.Entry def, int level) => RangeOf(def) * (def.Attack == AttackKind.Mine ? 1f : 1f + HighGround(level));

        /// <summary>Overclock: a belt next to the tower is carrying crystals.</summary>
        bool Overclocked(Tower t)
        {
            if (!DungeonConveyor.Run.Has(DungeonConveyor.Run.Synergy.Overclock)) return false;
            foreach (var c in t.Cells)
                for (int d = 0; d < map.Sides(c); d++)
                    if (map.BuildingAt(map.Neighbour(c, d)) is Conveyor belt && belt.Items.Count > 0) return true;
            return false;
        }

        void Run(Tower t, float dt)
        {
            var def = t.Def;
            var center = t.Center;
            var mods = DungeonConveyor.Run.For(def.Id);
            t.TickBoost(dt);
            float range = RangeOf(t);
            float damage = def.Damage * (1f + mods.Damage) * (t.Boosted(OreBoost.Damage) ? 1f + BoostDamage : 1f);
            float rate = (1f + mods.FireRate) * (t.Boosted(OreBoost.FireRate) ? 1f + BoostFireRate : 1f) * (Overclocked(t) ? 1.25f : 1f);
            var elements = mods.Elements;
            t.Cooldown -= dt;
            if (t.AnyBoost && Random.value < dt * 4f)
                Fx.Spawn(center + t.Up * 0.3f + Random.insideUnitSphere * 0.35f, new Color(t.BoostColor.r, t.BoostColor.g, t.BoostColor.b, 0.8f), 0.18f, 0.05f, 0.6f, t.Up * 0.8f);
            if (t.CanAim)
            {
                // Track the enemy it would shoot next, between shots too.
                var aim = PickTarget(def, center, range, def.Attack == AttackKind.Mortar ? MortarMinRange : 0f, t.Target);
                if (aim != null) t.Aim(aim.GroundPos - center);
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
                        if (InRange(e, center, range)) e.SlowFactor = Mathf.Min(e.SlowFactor, 1f - Mathf.Clamp01(def.Damage));
                    return;

                case AttackKind.Flame:
                {
                    bool any = false;
                    foreach (var e in Enemy.All)
                        if (!e.IsFlying && InRange(e, center, range))
                        {
                            any = true;
                            Hit(e, damage * rate * dt, AttackKind.Flame, t);
                            ElementalHit(e, elements, dt * ContinuousHitsPerSecond, t);
                            if (Random.value < dt * 6f) Fx.Spawn(e.AimPoint, new Color(1f, 0.5f, 0.1f, 0.8f), 0.25f, 0.6f, 0.35f, e.GroundPos.normalized * 0.5f);
                        }
                    t.SetWorking(any);
                    if (any) Sfx.Loop(t, Sfx.Lib.LoopFor(def), center); else Sfx.StopLoop(t);
                    bool jet = t.Effects != null && t.Effects.LoopEffect != null;
                    if (any && !jet && Random.value < dt * 12f)
                    {
                        var dir = Vector3.ProjectOnPlane(Random.onUnitSphere, t.Up).normalized * Random.Range(0f, range);
                        Fx.Spawn(Muzzle(t), new Color(1f, 0.55f, 0.15f, 0.7f), 0.2f, 0.7f, 0.45f, dir);
                    }
                    CleanupDead();
                    return;
                }

                case AttackKind.Mine:
                    foreach (var e in Enemy.All)
                    {
                        if (e.IsFlying || !InRange(e, center, def.Range)) continue;
                        Splash(center, def.Splash, damage, true, elements, AttackKind.Mine, t);
                        if (HasFireEffect(t)) t.PlayEffect();
                        else Fx.Explosion(center, def.Splash, new Color(1f, 0.6f, 0.2f, 0.9f));
                        Sfx.Play(Sfx.Lib.ShootFor(def), center);
                        Sfx.Play(Sfx.Lib.MineBlast, center);
                        build.RemoveBuilding(t, refund: false);
                        return;
                    }
                    return;

                case AttackKind.Laser:
                {
                    t.Target = PickTarget(def, center, range, 0f, t.Target);
                    if (t.Beam == null) t.Beam = Fx.NewLine("Laser", t.transform, new Color(1f, 0.25f, 0.3f, 0.9f), 0.06f);
                    t.SetWorking(t.Target != null);
                    if (t.Target == null) { t.Beam.enabled = false; Sfx.StopLoop(t); return; }
                    t.Beam.loop = false;
                    t.Beam.positionCount = 2;
                    t.Beam.SetPosition(0, Muzzle(t));
                    t.Beam.SetPosition(1, t.Target.AimPoint);
                    t.Beam.enabled = true;
                    if (Random.value < dt * 15f) Fx.Flash(t.Target.AimPoint, new Color(1f, 0.4f, 0.4f, 0.8f), 0.3f);
                    var beam = Sfx.Lib.For(def.Id);
                    if (beam != null && beam.Loop.IsSet) Sfx.Loop(t, beam.Loop, center);
                    else Sfx.Play(Sfx.Lib.ShootFor(def), center);
                    Hit(t.Target, damage * rate * dt, AttackKind.Laser, t);
                    ElementalHit(t.Target, elements, dt * ContinuousHitsPerSecond, t);
                    CleanupDead();
                    return;
                }
            }

            // Discrete attacks.
            if (t.Cooldown > 0f) return;
            float minRange = def.Attack == AttackKind.Mortar ? MortarMinRange : 0f;
            t.Target = PickTarget(def, center, range, minRange, null);
            if (t.Target == null) return;
            t.Cooldown = 1f / Mathf.Max(0.05f, def.FireRate * rate);
            var muzzle = Muzzle(t);
            // The model's own muzzle effect replaces the generic flash; either way the next barrel fires next time.
            bool flashed = HasFireEffect(t);
            if (t.Effects != null) t.Effects.Fire();

            switch (def.Attack)
            {
                case AttackKind.Bullet:
                    if (!flashed) Fx.Flash(muzzle, new Color(1f, 0.8f, 0.3f));
                    Fire(AttackKind.Bullet, muzzle, t.Target, damage, 0f, new Color(1f, 0.85f, 0.4f), 0.14f, t).Elements = elements;
                    Sfx.Play(Sfx.Lib.ShootFor(def), muzzle);
                    break;
                case AttackKind.Missile:
                    if (!flashed) Fx.Flash(muzzle, new Color(1f, 0.6f, 0.2f));
                    Fire(AttackKind.Missile, muzzle, t.Target, damage, def.Splash, new Color(1f, 0.5f, 0.2f), 0.22f, t).Elements = elements;
                    Sfx.Play(Sfx.Lib.ShootFor(def), muzzle);
                    break;
                case AttackKind.Mortar:
                {
                    if (!flashed) Fx.Flash(muzzle, new Color(1f, 0.7f, 0.3f), 0.5f);
                    var shot = Fire(AttackKind.Mortar, muzzle, null, damage, def.Splash, new Color(1f, 0.75f, 0.35f), 0.24f, t);
                    shot.Elements = elements;
                    shot.End = t.Target.GroundPos;
                    shot.Life = MortarFlightTime;
                    Sfx.Play(Sfx.Lib.ShootFor(def), muzzle);
                    break;
                }
                case AttackKind.Tesla:
                    Chain(t, muzzle, damage, elements);
                    Sfx.Play(Sfx.Lib.ShootFor(def), muzzle);
                    break;
            }
        }

        /// <summary>Bastion Core: shoots the nearest enemy in reach.</summary>
        void BastionCore(float dt)
        {
            if (Meta.ActiveCore != Meta.CoreType.Bastion || build.Core == null || build.Core.Destroyed) return;
            coreCooldown -= dt;
            if (coreCooldown > 0f) return;
            var center = build.Core.Center;
            Enemy best = null;
            float bestD = CoreRange * CoreRange;
            foreach (var e in Enemy.All)
            {
                if (!e.Alive || !e.Landed) continue;
                float d = (e.GroundPos - center).sqrMagnitude;
                if (d <= bestD) { bestD = d; best = e; }
            }
            if (best == null) return;
            coreCooldown = 1f / CoreFireRate;
            var from = center + build.Core.Up * 1.4f;
            Fx.Flash(from, new Color(1f, 0.85f, 0.4f));
            Fire(AttackKind.Bullet, from, best, CoreDamage * (1f + 0.05f * waves.Wave), 0f, new Color(1f, 0.9f, 0.5f), 0.18f, null);
            Sfx.Play(Sfx.Lib.CoreShoot, from);
        }

        /// <summary>Where the tower's next shot starts: its model's next muzzle point if it has any, else a spot above its centre.</summary>
        Vector3 Muzzle(Tower t)
        {
            if (t.Effects == null || !t.Effects.HasMuzzles) return t.Center + t.Up * 0.55f;
            return t.Effects.NextMuzzle;
        }

        static bool HasFireEffect(Tower t) => t.Effects != null && t.Effects.FireEffect != null;

        static bool InRange(Enemy e, Vector3 center, float range) => e.Landed && (e.GroundPos - center).sqrMagnitude <= range * range;

        /// <summary>Keeps a still-valid current target (beams), else the enemy closest to the Core in range. Mortars can't hit fliers.</summary>
        Enemy PickTarget(BuildingCatalog.Entry def, Vector3 center, float range, float minRange, Enemy current)
        {
            bool groundOnly = def.Attack == AttackKind.Mortar;
            if (current != null && current.Alive && InRange(current, center, range) && !(groundOnly && current.IsFlying)) return current;
            Enemy best = null;
            foreach (var e in Enemy.All)
            {
                if (!e.Alive || !InRange(e, center, range) || (groundOnly && e.IsFlying)) continue;
                if (minRange > 0f && InRange(e, center, minRange)) continue;
                if (best == null || e.StepsToCore < best.StepsToCore) best = e;
            }
            return best;
        }

        /// <summary>Range with the run's upgrades.</summary>
        public static float RangeOf(BuildingCatalog.Entry def) =>
            def.Range + (def.Attack == AttackKind.Mine ? 0f : DungeonConveyor.Run.For(def.Id).Range);

        void Chain(Tower t, Vector3 muzzle, float damage, float[] elements)
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
                    if (!e.Alive || !e.Landed || hits.Contains(e)) continue;
                    float d = (e.GroundPos - last.GroundPos).sqrMagnitude;
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
                    points.Add(Vector3.Lerp(prev, end, k / 3f) + Random.insideUnitSphere * 0.12f);
                points.Add(end);
                prev = end;
                Fx.Flash(end, new Color(0.6f, 0.9f, 1f, 0.9f), 0.35f);
                Hit(e, damage, AttackKind.Tesla, t);
                ElementalHit(e, elements, 1f, t);
            }
            t.Beam.loop = false;
            t.Beam.positionCount = points.Count;
            t.Beam.SetPositions(points.ToArray());
            t.Beam.enabled = true;
            t.BeamTimer = 0.15f;
            CleanupDead();
        }

        // ---------------------------------------------------------------- projectiles

        Shot Fire(AttackKind kind, Vector3 from, Enemy target, float damage, float splash, Color color, float size, Tower source)
        {
            var sr = new GameObject(kind.ToString()).AddComponent<SpriteRenderer>();
            sr.transform.SetParent(root, false);
            if (Fx.Material != null) sr.sharedMaterial = Fx.Material;
            sr.sprite = Fx.Soft;
            sr.color = color;
            sr.transform.localScale = Vector3.one * size;
            sr.transform.position = from;
            var shot = new Shot { Kind = kind, Sr = sr, Pos = from, Start = from, Target = target, Damage = damage, Splash = splash, Color = color, Source = source };
            if (target != null) shot.End = target.AimPoint;
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
                    // Lob: straight line plus a parabola away from the planet.
                    float t = Mathf.Clamp01(s.Age / s.Life);
                    var up = (s.Start + s.End).normalized;
                    s.Pos = Vector3.Lerp(s.Start, s.End, t) + up * (4f * t * (1f - t) * 1.6f);
                    done = t >= 1f;
                    if (done)
                    {
                        Splash(s.End, s.Splash, s.Damage, true, s.Elements, AttackKind.Mortar, s.Source);
                        Fx.Explosion(s.End, s.Splash, new Color(1f, 0.55f, 0.2f, 0.85f));
                        Sfx.Play(Sfx.Lib.ImpactFor(s.Source != null ? s.Source.Def : null), s.End);
                    }
                }
                else
                {
                    if (s.Target != null && s.Target.Alive) s.End = s.Target.AimPoint;
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
                            Splash(s.Target != null ? s.Target.GroundPos : s.End, s.Splash, s.Damage, false, s.Elements, AttackKind.Missile, s.Source);
                            Fx.Explosion(s.End, Mathf.Max(0.6f, s.Splash), new Color(1f, 0.5f, 0.2f, 0.85f));
                            Sfx.Play(Sfx.Lib.ImpactFor(s.Source != null ? s.Source.Def : null), s.End);
                        }
                        else if (s.Target != null && s.Target.Alive)
                        {
                            Hit(s.Target, s.Damage, AttackKind.Bullet, s.Source);
                            ElementalHit(s.Target, s.Elements, 1f, s.Source);
                            Fx.Flash(s.End, new Color(1f, 0.9f, 0.5f), 0.2f);
                            Sfx.Play(Sfx.Lib.BulletHit, s.End);
                        }
                    }
                }

                if (s.Sr != null)
                {
                    s.Sr.transform.position = s.Pos;
                    PlanetArt.Billboard(s.Sr.transform);
                }
                if (!done && s.Age < 6f) continue;
                if (s.Sr != null) Destroy(s.Sr.gameObject);
                shots.RemoveAt(i);
            }
            CleanupDead();
        }

        void Splash(Vector3 center, float radius, float damage, bool groundOnly, float[] elements, AttackKind kind, Tower source)
        {
            foreach (var e in Enemy.All)
            {
                if (groundOnly && e.IsFlying) continue;
                if (!InRange(e, center, radius)) continue;
                Hit(e, damage, kind, source, explosion: true);
                ElementalHit(e, elements, 1f, source);
            }
            CleanupDead();
        }

        /// <summary>
        /// The run's elemental bonus on one hit: each element's points as extra damage, plus its effect. weight is 1 for
        /// a single hit; continuous attacks pass a per-frame fraction, which scales the damage and the chance of an arc
        /// or a poison stack.
        /// </summary>
        void ElementalHit(Enemy e, float[] elements, float weight, Tower source)
        {
            if (elements == null || e == null || !e.Alive) return;
            float fire = elements[(int)Element.Fire], bolt = elements[(int)Element.Lightning];
            float frost = elements[(int)Element.Frost], poison = elements[(int)Element.Poison];
            float extra = (fire + frost + poison) * weight;
            if (extra > 0f) Hit(e, extra, AttackKind.None, source);
            if (bolt > 0f) Hit(e, bolt * weight * ConductionOn(e), AttackKind.None, source);
            if (fire > 0f) e.Ignite(fire * BurnDpsPerPoint, BurnSeconds);
            if (frost > 0f) e.Chill(Mathf.Min(ChillMax, ChillBase + ChillPerPoint * frost), ChillSeconds);
            if (poison > 0f && Random.value < weight) e.Poison(poison * PoisonDpsPerPoint, PoisonMaxStacks, PoisonSeconds);
            if (bolt > 0f && Random.value < weight) Arc(e, bolt, source);
        }

        /// <summary>Conduction: lightning hurts chilled enemies twice as much.</summary>
        static float ConductionOn(Enemy e) => DungeonConveyor.Run.Has(DungeonConveyor.Run.Synergy.Conduction) && e.IsChilled ? 2f : 1f;

        /// <summary>Lightning jumps from one enemy to the nearest other one in reach.</summary>
        void Arc(Enemy from, float damage, Tower source)
        {
            Enemy next = null;
            float best = ArcJump * ArcJump;
            foreach (var o in Enemy.All)
            {
                if (o == from || !o.Alive || !o.Landed) continue;
                float d = (o.GroundPos - from.GroundPos).sqrMagnitude;
                if (d <= best) { best = d; next = o; }
            }
            if (next == null) return;
            Fx.Bolt(from.AimPoint, next.AimPoint, DungeonConveyor.Run.ElementColors[(int)Element.Lightning]);
            Hit(next, damage * ConductionOn(next), AttackKind.None, source);
        }

        /// <summary>One hit, after armor and synergies; credits the tower that dealt it. quiet: no hit flash (damage over time).</summary>
        void Hit(Enemy e, float damage, AttackKind kind, Tower source, bool explosion = false, bool quiet = false)
        {
            if (!e.Alive) return;
            damage *= e.Resist(kind);
            if (kind == AttackKind.Tesla) damage *= ConductionOn(e);
            if (explosion && e.IsChilled && DungeonConveyor.Run.Has(DungeonConveyor.Run.Synergy.Shatter)) damage *= 1.4f;
            float dealt = Mathf.Min(damage, e.Health + e.Shield);
            DamageDealt += dealt;
            if (source != null) source.DamageDealt += dealt;
            if (e.IsBoss && explosion) CameraController.Shake(0.12f);
            if (e.TakeDamage(damage, !quiet) && (kind == AttackKind.Missile || kind == AttackKind.Mortar)) e.KilledByExplosion = true;
        }

        /// <summary>Removes dead enemies and pays their bounty; then sets off any death explosions (synergies).</summary>
        void CleanupDead()
        {
            for (int pass = 0; pass < 4; pass++)
            {
                deathBlasts.Clear();
                for (int i = Enemy.All.Count - 1; i >= 0; i--)
                {
                    var e = Enemy.All[i];
                    if (e.Alive) continue;
                    if (DungeonConveyor.Run.Has(DungeonConveyor.Run.Synergy.Combustion) && e.WasBurning && e.WasPoisoned)
                        deathBlasts.Add((e.GroundPos, 1.6f, e.MaxHealth * 0.3f));
                    else if (DungeonConveyor.Run.Has(DungeonConveyor.Run.Synergy.ChainReaction) && e.KilledByExplosion)
                        deathBlasts.Add((e.GroundPos, 1.2f, e.MaxHealth * 0.25f));
                    waves.OnKilled(e);
                    Fx.Burst(e.AimPoint, new Color(0.7f, 1f, 0.4f, 0.9f), 0.5f);
                    Sfx.Play(e.IsBoss ? Sfx.Lib.BossDeath : Sfx.Lib.DeathFor(e.Def), e.AimPoint);
                    if (e.IsBoss) CameraController.Shake(0.7f);
                    e.Remove();
                }
                if (deathBlasts.Count == 0) return;
                foreach (var (at, radius, damage) in deathBlasts)
                {
                    Fx.Explosion(at, radius, new Color(0.9f, 0.55f, 1f, 0.85f));
                    Sfx.Play(Sfx.Lib.Explosion, at);
                    foreach (var o in Enemy.All)
                        if (o.Alive && !o.IsFlying && InRange(o, at, radius)) Hit(o, damage, AttackKind.None, null, explosion: true);
                }
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
                var lr = Fx.NewLine("Field", root, color, 0.07f);
                Fx.Ring(lr, t.Center, RangeOf(t));
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
                // Gentle pulse so fields read as active; the ring follows range changes (boosts).
                Fx.Ring(pair.Value, pair.Key.Center, RangeOf(pair.Key));
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
                Fx.Ring(rangePreview, map.FootprintCenter(build.GhostOrigin.Value, placing.Size, build.GhostLevel), RangeAt(placing, build.GhostLevel));
                return;
            }
            if (hover != null && hover.HasCell && map.BuildingAt(hover.Cell) is Tower t && t.Def.Range > 0f)
                Fx.Ring(rangePreview, t.Center, RangeOf(t));
        }
    }
}
