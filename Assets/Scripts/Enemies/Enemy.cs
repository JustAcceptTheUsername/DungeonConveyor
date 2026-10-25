using System.Collections.Generic;
using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>
    /// One enemy. Rides a meteor down from space onto its landing site, then follows the flow field cell by cell over
    /// the planet to the Core, where it deals its damage and is gone. Moved by WaveManager; hurt and slowed by Combat
    /// (only once it has landed).
    /// Traits (EnemyCatalog): fast ones run quicker, armored ones shrug off half of bullet and laser damage, fliers
    /// cross walls and cliffs on their own flow field (and can't be hit by mines or flames), saboteurs head for the
    /// nearest belt first and wreck a couple of tiles. Elite waves add a modifier: a shield to break first, splitting
    /// in two on death, sabotage, speed or regeneration. Walking uphill slows ground enemies down.
    /// </summary>
    public class Enemy : MonoBehaviour
    {
        public static readonly List<Enemy> All = new List<Enemy>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => All.Clear();

        /// <summary>Seconds a meteor takes from the edge of space to the ground.</summary>
        public const float FallTime = 1.6f;
        const float FallHeight = 22f;
        const int FallingSteps = 200000;

        public EnemyCatalog.Entry Def { get; private set; }
        public float Health { get; private set; }
        public float MaxHealth { get; private set; }
        public bool Alive => Health > 0f;
        /// <summary>Down on the planet (not still falling in its meteor): only then can it be hurt.</summary>
        public bool Landed { get; private set; }
        /// <summary>The cell it is on (the nearer end of the step it is taking).</summary>
        public Vector2Int Cell => t < 0.5f ? fromCell : toCell;
        /// <summary>Where it stands on the ground (without hovering).</summary>
        public Vector3 GroundPos { get; private set; }
        /// <summary>Steps left to the Core; enemies still falling count extra so landed ones are ahead of them.</summary>
        public int StepsToCore { get; private set; } = FlowField.Unreachable;
        /// <summary>Speed multiplier for this frame (slow fields lower it).</summary>
        public float SlowFactor = 1f;
        /// <summary>A boss: one big enemy that is a whole wave on its own.</summary>
        public bool IsBoss { get; private set; }
        public float CoreDamage { get; private set; }
        public int Bounty { get; private set; }
        public EnemyTrait Traits { get; private set; }
        public EliteModifier Elite { get; private set; }
        public bool IsFlying => (Traits & EnemyTrait.Flying) != 0 || Def.Hover > 0f;
        public bool IsArmored => (Traits & EnemyTrait.Armored) != 0;
        public bool IsSaboteur => (Traits & EnemyTrait.Saboteur) != 0 && BeltsToWreck > 0;
        /// <summary>Belt tiles a saboteur still means to wreck before going for the Core.</summary>
        public int BeltsToWreck { get; set; }
        /// <summary>Shield (elite): soaks up damage before health does.</summary>
        public float Shield { get; private set; }
        public float MaxShield { get; private set; }
        /// <summary>Splits into two smaller copies when killed (elite).</summary>
        public bool Splits { get; private set; }
        /// <summary>Burning when it died, and poisoned (for Combustion).</summary>
        public bool WasBurning => burnTime > 0f;
        public bool WasPoisoned => poisonTime > 0f;
        public bool IsChilled => chillTime > 0f;
        /// <summary>Set by Combat when it died to a missile or mortar (Chain Reaction).</summary>
        public bool KilledByExplosion { get; set; }
        /// <summary>The cell a saboteur wrecks when it reports ReachedBelt (its own, or where its lava blob landed).</summary>
        public Vector2Int WreckCell { get; private set; }
        /// <summary>Busy with an attack telegraph (chewing, lobbing, slamming) or enraged: stands still meanwhile.</summary>
        public bool Attacking => attack != Attack.None;
        float speedScale = 1f, size = 1f, regen;

        public enum StepResult { Moving, ReachedCore, ReachedBelt }

        // Elemental effects (see Combat.ElementalHit).
        float burnDps, burnTime, chill, chillTime, poisonPerStack, poisonTime;
        int poisonStacks;

        IsoGrid map;
        Transform body, bar;
        SpriteRenderer barBack, barFill, status, meteor;
        float modelHeight = 0.5f;
        Vector2Int fromCell, toCell;
        float fromLevel, toLevel, t = 1f, stepLength = 0.001f, bob, bobSpeed = 1f, fallTime;
        Vector3 fallFrom, facing;

        // Feedback layer (null when EnemyCatalog.EnemyFeedback is off), and the crumbling death.
        EnemyLook look;
        Color statusTint;
        float dieTime = -1f;
        Vector3 dieFrom, dieAxis, dieScale;
        Quaternion dieRotation;

        // Rigid-part animation (null for one-piece models or when EnemyCatalog.RigAnimation is off).
        EnemyRig rig;
        GameObject model;
        Material spriteMaterial;
        Vector3 lastGround, flyFacing; // flyFacing: a flier's eased heading
        bool hasLastGround;
        float hatchHold, jitter = 1f, frameDt, turnRate, bank;

        // Attack telegraphs (EnemyCatalog.RigSettings): a clip plays while it stands, the gameplay lands on its event.
        enum Attack { None, Chew, Lob, Slam, Enrage }
        Attack attack;
        int chewClip = -1, lobClip = -1, slamClip = -1, enrageClip = -1;
        float attackTime, attackDwell, attackResolveAt, blobLandsAt;
        string attackEvent;
        bool attackEventHit, attackResolved;
        Vector2Int lobTarget;
        Fx.Blob blob;
        System.Action<string> onRigEvent;

        static Camera cam;
        static void Billboard(Transform tr)
        {
            if (cam == null) cam = Camera.main;
            if (cam != null) tr.rotation = cam.transform.rotation;
        }

        static Sprite pixel;
        static Sprite Pixel
        {
            get
            {
                if (pixel != null) return pixel;
                var tex = new Texture2D(1, 1) { filterMode = FilterMode.Point };
                tex.SetPixel(0, 0, Color.white);
                tex.Apply();
                return pixel = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0f, 0.5f), 1f);
            }
        }

        public static Enemy Spawn(EnemyCatalog.Entry def, float healthScale, IsoGrid map, IsoGrid.SpawnSite site, Transform parent, Material material)
        {
            var go = new GameObject(def.Name);
            go.transform.SetParent(parent, false);
            var e = go.AddComponent<Enemy>();
            e.Init(def, healthScale, map, site, material);
            All.Add(e);
            return e;
        }

        void Init(EnemyCatalog.Entry def, float healthScale, IsoGrid grid, IsoGrid.SpawnSite site, Material material)
        {
            Def = def;
            map = grid;
            MaxHealth = Health = def.Health * healthScale;
            CoreDamage = def.CoreDamage;
            Bounty = def.Bounty;
            Traits = def.Traits;
            if ((Traits & EnemyTrait.Fast) != 0) speedScale = 1.35f;
            BeltsToWreck = (Traits & EnemyTrait.Saboteur) != 0 ? 2 : 0;
            bob = Random.Range(0f, 6.28f);

            body = new GameObject("Body").transform;
            body.SetParent(transform, false);
            spriteMaterial = material;
            var cfg = EnemyLook.Settings;
            // A crowd of one kind shouldn't move in lockstep: a little spread in size and bob speed.
            if (EnemyLook.On)
            {
                jitter = 1f + Random.Range(-cfg.ScaleJitter, cfg.ScaleJitter);
                bobSpeed = 1f + Random.Range(-cfg.BobJitter, cfg.BobJitter);
            }
            BuildModel(def.Model, def.Gait, def.Clips);
            enabled = false; // Update only runs while crumbling

            bar = new GameObject("Bar").transform;
            bar.SetParent(transform, false);
            barBack = NewSprite("HealthBack", bar, material);
            barFill = NewSprite("Health", bar, material);
            barBack.sprite = barFill.sprite = Pixel;
            barBack.color = look != null ? new Color(0.106f, 0.063f, 0.125f, 0.92f) : new Color(0f, 0f, 0f, 0.7f);
            barBack.enabled = barFill.enabled = false;
            status = NewSprite("Status", transform, material);
            status.sprite = Fx.Soft;
            status.enabled = false;

            // The meteor: a glowing ball falling from high above the landing site, slanting in from one side.
            meteor = NewSprite("Meteor", transform, material);
            meteor.sprite = Fx.Soft;
            meteor.color = new Color(1f, 0.6f, 0.2f, 1f);
            meteor.transform.localScale = Vector3.one * 1.3f;
            fromCell = toCell = site.Landing;
            fromLevel = toLevel = map.HeightAt(site.Landing);
            var up = map.Up(site.Landing);
            var side = Vector3.ProjectOnPlane(Random.onUnitSphere, up).normalized;
            fallFrom = SurfacePoint(site.Landing, fromLevel) + (up + side * 0.6f).normalized * FallHeight;
            facing = side;
            body.gameObject.SetActive(false);
            StepsToCore = FallingSteps;
            GroundPos = fallFrom;
            transform.position = fallFrom;
        }

        /// <summary>
        /// The model under body, with its rig and feedback layer (replacing any earlier ones): prefab (a sphere if null),
        /// gait = the loop, clips = the rest (EnemyCatalog.Entry.Clips).
        /// </summary>
        void BuildModel(GameObject prefab, EnemyGait gait, EnemyGait[] clips)
        {
            if (model != null)
            {
                rig?.Dispose();
                look?.Dispose();
                Destroy(model);
                rig = null;
                look = null;
            }
            bool wasActive = body.gameObject.activeSelf;
            body.gameObject.SetActive(true); // renderer bounds are empty while inactive
            if (prefab != null) model = Instantiate(prefab, body, false);
            else
            {
                model = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                model.transform.SetParent(body, false);
                model.transform.localPosition = Vector3.up * 0.5f;
            }
            PlanetArt.StripColliders(model);
            model.transform.localScale *= Def.Scale * jitter;
            model.transform.localRotation = Quaternion.Euler(0f, EnemyCatalog.Instance != null ? EnemyCatalog.Instance.YawOffset : 0f, 0f);
            var bounds = new Bounds(body.position, Vector3.zero);
            foreach (var r in model.GetComponentsInChildren<Renderer>())
            {
                bounds.Encapsulate(r.bounds);
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            modelHeight = Mathf.Max(0.2f, bounds.max.y - body.position.y);
            if (EnemyRig.On) rig = EnemyRig.Create(model, gait, clips, IsFlying); // before the look adds its own meshes
            chewClip = lobClip = slamClip = enrageClip = -1;
            if (rig != null)
            {
                if (onRigEvent == null) onRigEvent = OnRigEvent;
                rig.OnEvent = onRigEvent;
                chewClip = rig.Clip("Chew");
                lobClip = rig.Clip("Lob");
                slamClip = rig.Clip("Slam");
                if (slamClip < 0) slamClip = rig.Clip("Dive"); // a flier's attack on the Core
                enrageClip = rig.Clip("Enrage");
            }
            if (EnemyLook.On) look = new EnemyLook(model, transform, spriteMaterial, new Vector2(bounds.size.x, bounds.size.z));
            body.gameObject.SetActive(wasActive);
        }

        /// <summary>
        /// Turns this enemy into a boss: health, Core damage and bounty set, drawn bigger, walks slower; with its own
        /// model (EnemyCatalog.Entry.BossModel) and clips if it has one. Call before it lands.
        /// </summary>
        public void MakeBoss(float health, float coreDamage, int bounty, float scale = 2f, float speed = 0.6f)
        {
            // A boss model is built at boss size already: it is not scaled up again.
            bool own = Def.BossModel != null && !Landed;
            if (own)
                BuildModel(Def.BossModel, Def.BossGait != null ? Def.BossGait : Def.Gait,
                    Def.BossClips != null && Def.BossClips.Length > 0 ? Def.BossClips : Def.Clips);
            IsBoss = true;
            MaxHealth = Health = health;
            CoreDamage = coreDamage;
            Bounty = bounty;
            size = own ? 1f : scale;
            speedScale = speed;
            body.localScale = Vector3.one * size;
            meteor.transform.localScale = Vector3.one * 1.3f * scale;
        }

        /// <summary>Gives this enemy an elite wave's modifier (and a bigger bounty).</summary>
        public void MakeElite(EliteModifier m)
        {
            Elite = m;
            Bounty = Mathf.CeilToInt(Bounty * 1.5f);
            switch (m)
            {
                case EliteModifier.Shielded: MaxShield = Shield = MaxHealth * 0.6f; break;
                case EliteModifier.Splitting: Splits = true; break;
                case EliteModifier.Saboteurs: Traits |= EnemyTrait.Saboteur; BeltsToWreck = Mathf.Max(BeltsToWreck, 2); break;
                case EliteModifier.Swift: speedScale *= 1.4f; break;
                case EliteModifier.Regenerating: regen = 0.04f; break;
            }
            body.localScale = Vector3.one * size * 1.12f;
        }

        /// <summary>Skips the meteor: down on its cell right away (split-off copies).</summary>
        public void LandNow()
        {
            if (Landed) return;
            Landed = true;
            meteor.enabled = false;
            body.gameObject.SetActive(true);
            StepsToCore = FlowField.Unreachable - 1;
            t = 1f;
            rig?.BeginPop(Time.time);
            hasLastGround = false;
            Place();
        }

        /// <summary>Damage multiplier against this enemy for an attack kind (armor halves bullets and lasers).</summary>
        public float Resist(AttackKind kind) => IsArmored && (kind == AttackKind.Bullet || kind == AttackKind.Laser) ? 0.5f : 1f;

        // ---------------------------------------------------------------- elemental effects

        public void Ignite(float dps, float seconds)
        {
            burnDps = Mathf.Max(burnDps * (burnTime > 0f ? 1f : 0f), dps);
            burnTime = Mathf.Max(burnTime, seconds);
        }

        public void Chill(float slow, float seconds)
        {
            chill = Mathf.Max(chill * (chillTime > 0f ? 1f : 0f), slow);
            chillTime = Mathf.Max(chillTime, seconds);
        }

        public void Poison(float dpsPerStack, int maxStacks, float seconds)
        {
            poisonPerStack = Mathf.Max(poisonPerStack, dpsPerStack);
            poisonStacks = Mathf.Min(maxStacks, poisonStacks + 1);
            poisonTime = seconds;
        }

        /// <summary>Advances burn, chill and poison; applies the chill to SlowFactor. Returns damage over time dealt.</summary>
        public float TickEffects(float dt)
        {
            float dot = 0f;
            if (burnTime > 0f) { burnTime -= dt; dot += burnDps * dt; }
            if (poisonTime > 0f) { poisonTime -= dt; dot += poisonPerStack * poisonStacks * dt; }
            else poisonStacks = 0;
            if (chillTime > 0f) { chillTime -= dt; SlowFactor = Mathf.Min(SlowFactor, 1f - chill); }
            if (regen > 0f && Landed && Alive) Health = Mathf.Min(MaxHealth, Health + MaxHealth * regen * dt);

            // A glow around the body shows what it suffers from (an elite shield shows as a blue halo).
            var tint = Shield > 0f ? new Color(0.35f, 0.6f, 1f, 0.5f) : Color.clear;
            if (chillTime > 0f) tint = new Color(0.55f, 0.85f, 1f, 0.55f);
            if (poisonTime > 0f) tint = Color.Lerp(tint, new Color(0.6f, 1f, 0.35f, 0.55f), 0.5f);
            if (burnTime > 0f) tint = Color.Lerp(tint, new Color(1f, 0.55f, 0.3f, 0.6f), 0.4f + 0.3f * Mathf.Abs(Mathf.Sin(Time.time * 12f)));
            // With the feedback layer the tint goes on the body itself (EnemyLook), where the model can't hide it.
            statusTint = tint;
            status.enabled = look == null && Landed && tint.a > 0f;
            status.color = tint;
            return dot;
        }

        SpriteRenderer NewSprite(string name, Transform parent, Material material)
        {
            var sr = new GameObject(name).AddComponent<SpriteRenderer>();
            sr.transform.SetParent(parent, false);
            if (material != null) sr.sharedMaterial = material;
            return sr;
        }

        Vector3 SurfacePoint(Vector2Int c, float level) => map.Up(c) * map.RadiusAt(level);

        /// <summary>
        /// Falls, or moves along its flow field: saboteurs toward the nearest belt while they still mean to wreck one,
        /// fliers over everything, the rest along the ground. Reports reaching the Core or a belt this frame.
        /// </summary>
        public StepResult Step(float dt, EnemyFlows flows)
        {
            frameDt = dt;
            if (!Landed)
            {
                Fall(dt);
                return StepResult.Moving;
            }
            if (!Alive) return StepResult.Moving; // killed this frame: Combat removes it, it attacks no more

            if (attack != Attack.None) return TickAttack(dt);

            if (hatchHold > 0f)
            {
                // Still climbing out of its meteor shell.
                hatchHold -= dt;
                Place();
                return StepResult.Moving;
            }

            var flow = IsFlying ? flows.Air : flows.Ground;
            bool hunting = IsSaboteur && flows.Belts != null && flows.Belts.Distance(toCell) != FlowField.Unreachable;
            if (hunting) flow = flows.Belts;
            // A lobber stops short of the belt and spits at it.
            int arriveAt = hunting && CanLob ? Mathf.Max(0, EnemyRig.Settings.LobRange) : 0;
            // Uphill is slow going on foot.
            float climb = !IsFlying && toLevel > fromLevel && t < 1f ? 0.65f : 1f;
            float move = Def.Speed * speedScale * SlowFactor * climb * dt;
            while (move > 0f)
            {
                float left = (1f - t) * stepLength;
                if (left > move)
                {
                    t += move / stepLength;
                    break;
                }
                move -= left;
                t = 1f;
                bool moving = ChooseNext(flow, arriveAt, out bool arrived);
                if (arrived)
                {
                    Place();
                    return Arrive(hunting, flows);
                }
                if (!moving) break;
            }
            Place();
            return StepResult.Moving;
        }

        // ---------------------------------------------------------------- attack telegraphs

        bool CanLob => rig != null && lobClip >= 0 && EnemyRig.Settings.LobDwell > 0f;

        /// <summary>
        /// At its target: a belt (saboteur) or the Core. With a matching clip and a dwell above 0 it starts that attack
        /// (and reports Moving until the gameplay lands); otherwise it reports the arrival right away, as before.
        /// </summary>
        StepResult Arrive(bool belt, EnemyFlows flows)
        {
            WreckCell = toCell;
            var cfg = EnemyRig.Settings;
            if (belt)
            {
                if (CanLob && FindBelt(flows.Belts, out lobTarget))
                {
                    BeginAttack(Attack.Lob, lobClip, cfg.LobDwell, "Launch");
                    return StepResult.Moving;
                }
                if (rig != null && chewClip >= 0 && cfg.ChewDwell > 0f)
                {
                    BeginAttack(Attack.Chew, chewClip, cfg.ChewDwell, "Bite");
                    return StepResult.Moving;
                }
                return StepResult.ReachedBelt;
            }
            if (rig != null && slamClip >= 0 && cfg.SlamDwell > 0f)
            {
                BeginAttack(Attack.Slam, slamClip, cfg.SlamDwell, "Impact");
                return StepResult.Moving;
            }
            return StepResult.ReachedCore;
        }

        /// <summary>The belt cell down the belt flow from here (a lobber stops a few cells short of it).</summary>
        bool FindBelt(FlowField belts, out Vector2Int cell)
        {
            cell = toCell;
            if (belts == null) return false;
            for (int i = 0; i <= EnemyRig.Settings.LobRange + 2; i++)
            {
                int d = belts.Distance(cell);
                if (d == 0) return true;
                if (d == FlowField.Unreachable || !belts.Next(cell, out var next)) return false;
                cell = next;
            }
            return false;
        }

        void BeginAttack(Attack kind, int clip, float dwell, string fireOn)
        {
            attack = kind;
            attackTime = 0f;
            attackDwell = dwell;
            attackEvent = fireOn;
            attackEventHit = attackResolved = false;
            attackResolveAt = rig.Play(clip, dwell, fireOn);
        }

        void OnRigEvent(string name)
        {
            if (attack == Attack.None || attackResolved) return;
            if (name == attackEvent || name == EnemyRig.EndEvent) attackEventHit = true;
        }

        /// <summary>While attacking: stands, plays the clip, and lands the gameplay on its event (at the latest in time).</summary>
        StepResult TickAttack(float dt)
        {
            attackTime += dt;
            Place(); // ticks the rig: its events come in through OnRigEvent
            bool fire = !attackResolved && (attackEventHit || attackTime >= attackResolveAt);
            switch (attack)
            {
                case Attack.Chew:
                    if (fire)
                    {
                        attackResolved = true;
                        EnemyLook.Chips(AimPoint, 5, body.localScale.x, 1.2f);
                        if (attackTime >= attackDwell) EndAttack();
                        return StepResult.ReachedBelt;
                    }
                    if (attackResolved && attackTime >= attackDwell) EndAttack();
                    return StepResult.Moving;
                case Attack.Lob:
                    if (fire)
                    {
                        attackResolved = true;
                        Launch();
                    }
                    if (attackResolved && attackTime >= blobLandsAt)
                    {
                        blob = null; // it splashes on its own
                        WreckCell = lobTarget;
                        EndAttack();
                        return StepResult.ReachedBelt;
                    }
                    return StepResult.Moving;
                case Attack.Slam:
                    if (!fire) return StepResult.Moving;
                    attackResolved = true;
                    Fx.Burst(GroundPos + GroundPos.normalized * ConveyorArt.SurfaceHeight, new Color(0.62f, 0.57f, 0.5f, 0.85f), 1.1f * body.localScale.x);
                    EndAttack();
                    return StepResult.ReachedCore;
                default: // Enrage: a short stand while the clip plays
                    if (attackTime >= attackDwell) EndAttack();
                    return StepResult.Moving;
            }
        }

        /// <summary>The lava blob leaves the Muzzle node (or RigSettings.LobMuzzle) for the target belt.</summary>
        void Launch()
        {
            var cfg = EnemyRig.Settings;
            var from = rig.Muzzle != null ? rig.Muzzle.position : model.transform.TransformPoint(cfg.LobMuzzle);
            var to = SurfacePoint(lobTarget, map.HeightAt(lobTarget)) + map.Up(lobTarget) * (ConveyorArt.SurfaceHeight + 0.05f);
            float flight = Mathf.Max(0.05f, cfg.LobFlight);
            blob = Fx.Lob(from, to, flight, cfg.LobArc, 0.3f * body.localScale.x * Mathf.Max(0.5f, Def.Scale / 0.6f));
            blobLandsAt = attackTime + flight;
        }

        void EndAttack()
        {
            // A lob plays out on its own (the spitter recovers while the blob flies); the rest stop with the attack.
            if (attack != Attack.Lob) rig?.Stop();
            attack = Attack.None;
            hasLastGround = false; // no jump in the walk phase
        }

        /// <summary>Killed or gone mid-attack: nothing lands any more (a blob in the air fizzles).</summary>
        void CancelAttack()
        {
            blob?.Fizzle();
            blob = null;
            attack = Attack.None;
            attackResolved = true;
        }

        /// <summary>Plates broke off: a short enraged stand with the Enrage clip (unless it is busy attacking).</summary>
        void Enrage()
        {
            if (IsBoss) CameraController.Shake(0.25f);
            Sfx.Play(Sfx.Lib.MeteorImpact, AimPoint);
            if (rig == null || enrageClip < 0 || attack != Attack.None) return;
            if (IsBoss) Sfx.Play(Sfx.Lib.BossLand, AimPoint);
            float hold = EnemyRig.Settings.EnrageHold;
            if (hold <= 0f || hatchHold > 0f)
            {
                rig.Play(enrageClip);
                return;
            }
            // It stands for the whole clip (sped up to fit the hold), so the roar and the recovery are seen: stopping
            // the clip when the hold ran out cut it off before the roar.
            attack = Attack.Enrage;
            attackTime = 0f;
            attackDwell = rig.Play(enrageClip, hold);
            attackEvent = null;
            attackResolved = true;
        }

        void Fall(float dt)
        {
            fallTime += dt;
            float k = Mathf.Clamp01(fallTime / FallTime);
            var ground = SurfacePoint(toCell, toLevel);
            transform.position = Vector3.Lerp(fallFrom, ground, k * k);
            PlanetArt.Billboard(meteor.transform);
            if (Random.value < dt * 40f) Fx.Spawn(transform.position, new Color(1f, 0.45f, 0.15f, 0.7f), 0.5f * size, 0.1f, 0.5f);
            if (k < 1f) return;

            Landed = true;
            meteor.enabled = false;
            body.gameObject.SetActive(true);
            Fx.Explosion(ground, 1.2f * size, new Color(1f, 0.55f, 0.2f, 0.9f));
            Sfx.Play(Sfx.Lib.MeteorImpact, ground);
            Sfx.Play(IsBoss ? Sfx.Lib.BossLand : Sfx.Lib.LandFor(Def), ground);
            if (IsBoss) CameraController.Shake(0.5f);
            if (rig != null && EnemyRig.Settings.MeteorShell != null)
            {
                // It hatches: the shell cracks open on the spot and the critter pops out of it.
                var cfg = EnemyRig.Settings;
                MeteorHatch.Play(ground + ground.normalized * ConveyorArt.SurfaceHeight, ground.normalized, facing,
                    modelHeight * size, transform.parent);
                rig.BeginPop(Time.time + cfg.CrackAt);
                hatchHold = cfg.HatchHold;
            }
            else rig?.BeginPop(Time.time);
            hasLastGround = false;
            t = 1f;
            Place();
        }

        bool ChooseNext(FlowField flow, int arriveAt, out bool arrived)
        {
            arrived = false;
            var here = toCell;
            fromCell = toCell;
            fromLevel = toLevel;
            StepsToCore = flow.Distance(here);
            if (StepsToCore <= arriveAt)
            {
                arrived = true;
                return false;
            }
            if (!flow.Next(here, out var next)) return false; // walled in for now; try again next frame
            toCell = next;
            toLevel = map.HeightAt(next);
            t = 0f;
            stepLength = Mathf.Max(0.001f, Vector3.Distance(SurfacePoint(fromCell, fromLevel), SurfacePoint(toCell, toLevel)));
            return true;
        }

        void Place()
        {
            var a = SurfacePoint(fromCell, fromLevel);
            var b = SurfacePoint(toCell, toLevel);
            GroundPos = Vector3.Slerp(a, b, t);
            var up = GroundPos.normalized;
            // A lobber turns to face the belt it spits at.
            var heading = Vector3.ProjectOnPlane(attack == Attack.Lob ? SurfacePoint(lobTarget, map.HeightAt(lobTarget)) - GroundPos : b - a, up);
            if (heading.sqrMagnitude > 1e-6f) facing = heading.normalized;
            else facing = Vector3.ProjectOnPlane(facing, up).normalized;

            float time = Time.time * bobSpeed;
            // Walkers with a rig bob in their gait instead.
            float hop = Def.Hover > 0f ? Mathf.Sin(time * 6f + bob) * 0.05f
                : rig != null && rig.Walks ? 0f : Mathf.Abs(Mathf.Sin(time * 9f + bob)) * 0.03f;
            transform.position = GroundPos + up * (ConveyorArt.SurfaceHeight + Def.Hover + hop);
            if (facing.sqrMagnitude > 1e-6f)
            {
                var rotation = Quaternion.LookRotation(facing, up);
                if (rig != null && IsFlying)
                {
                    // Fliers ease round into the new heading (the steps turn sharply at cell borders) and bank by how
                    // fast that heading turns, so the bank swells and settles instead of coming in pulses.
                    var cfg = EnemyRig.Settings;
                    flyFacing = Vector3.ProjectOnPlane(flyFacing, up);
                    if (flyFacing.sqrMagnitude < 1e-6f) flyFacing = facing;
                    flyFacing.Normalize();
                    if (frameDt > 0f)
                    {
                        var next = cfg.FlyTurnResponse > 0f
                            ? Vector3.Slerp(flyFacing, facing, 1f - Mathf.Exp(-cfg.FlyTurnResponse * frameDt)) : facing;
                        float turn = Vector3.SignedAngle(flyFacing, next, up) / frameDt;
                        flyFacing = next.normalized;
                        turnRate = Mathf.Lerp(turnRate, turn, 1f - Mathf.Exp(-cfg.BankResponse * frameDt));
                        bank = Mathf.Clamp(-turnRate * cfg.BankPerTurn, -cfg.BankMax, cfg.BankMax);
                    }
                    rotation = Quaternion.LookRotation(flyFacing, up) * Quaternion.Euler(0f, 0f, bank);
                }
                body.rotation = rotation;
            }
            if (rig != null)
            {
                float walked = hasLastGround ? Vector3.Distance(GroundPos, lastGround) : 0f;
                lastGround = GroundPos;
                hasLastGround = true;
                rig.Tick(walked, frameDt);
            }
            frameDt = 0f; // time only counts once per Step

            if (look != null)
                look.Tick(Health / MaxHealth, statusTint, GroundPos, up, facing, body.localScale.x, IsFlying ? Def.Hover + 0.1f : 0f);
            else
            {
                status.transform.position = transform.position + up * modelHeight * 0.5f * size;
                status.transform.localScale = Vector3.one * modelHeight * 1.6f * size;
                Billboard(status.transform);
            }
            UpdateBar(up);
        }

        void UpdateBar(Vector3 up)
        {
            bool show = Health < MaxHealth;
            barBack.enabled = barFill.enabled = show;
            if (!show) return;
            bar.position = transform.position + up * (modelHeight * size + 0.15f);
            Billboard(bar);
            float f = Mathf.Clamp01(Health / MaxHealth);
            if (look != null)
            {
                // Thicker bar (about 5 px at default zoom) in a dark ink border, so it reads on the bright ground.
                float w = IsBoss ? 1.2f : 0.6f, h = EnemyLook.Settings.BarHeight, border = 0.025f;
                var at = new Vector3(-w * 0.5f, 0f, 0f);
                barBack.transform.localPosition = at - new Vector3(border, 0f, 0f);
                barBack.transform.localScale = new Vector3(w + border * 2f, h + border * 2f, 1f);
                barFill.transform.localPosition = at + new Vector3(0f, 0f, -0.002f);
                barFill.transform.localScale = new Vector3(w * f, h, 1f);
            }
            else
            {
                float width = IsBoss ? 1.1f : 0.5f;
                var pos = new Vector3(-width * 0.5f, 0f, 0f);
                barBack.transform.localPosition = pos;
                barBack.transform.localScale = new Vector3(width, 0.06f, 1f);
                barFill.transform.localPosition = pos + new Vector3(0f, 0f, -0.002f);
                barFill.transform.localScale = new Vector3(width * f, 0.045f, 1f);
            }
            barFill.color = Color.Lerp(new Color(1f, 0.2f, 0.15f), new Color(0.4f, 1f, 0.3f), f);
        }

        /// <summary>World point turrets aim at (middle of the body).</summary>
        public Vector3 AimPoint => transform.position + transform.position.normalized * modelHeight * 0.5f * size;

        /// <summary>
        /// Deals damage (the shield takes it first); returns true if this killed it. feedback: flash and chips (off for
        /// damage over time).
        /// </summary>
        public bool TakeDamage(float amount, bool feedback = true)
        {
            if (!Alive) return false;
            if (feedback && look != null && Landed && amount > 0f)
            {
                look.Hit(AimPoint, body.localScale.x);
                rig?.Hit();
            }
            if (Shield > 0f)
            {
                float soaked = Mathf.Min(Shield, amount);
                Shield -= soaked;
                amount -= soaked;
                if (Shield <= 0f)
                {
                    Fx.Burst(AimPoint, new Color(0.4f, 0.7f, 1f, 0.9f), 0.6f * size);
                    Sfx.Play(Sfx.Lib.ShieldBreak, AimPoint);
                }
            }
            Health -= amount;
            // Armour plates break off at health thresholds (and it rages).
            if (rig != null && rig.Plates > rig.PlatesBroken && Alive && Landed && amount > 0f)
            {
                var up = GroundPos.normalized;
                if (rig.BreakPlates(Health / MaxHealth, transform.parent, GroundPos + up * ConveyorArt.SurfaceHeight, up) > 0) Enrage();
            }
            return !Alive;
        }

        /// <summary>
        /// Takes it out of the game right away (out of All, so nothing targets or counts it). A killed one then
        /// crumbles into the ground for a moment before it is destroyed (with the feedback layer on).
        /// </summary>
        public void Remove()
        {
            All.Remove(this);
            CancelAttack();
            if (look != null && !Alive && Landed && dieTime < 0f) Crumble();
            else if (dieTime < 0f) Destroy(gameObject);
        }

        void Crumble()
        {
            dieTime = Time.time;
            enabled = true;
            barBack.enabled = barFill.enabled = status.enabled = false;
            var up = GroundPos.normalized;
            dieFrom = transform.position;
            dieRotation = body.rotation;
            dieScale = body.localScale;
            dieAxis = Vector3.Cross(up, Vector3.ProjectOnPlane(Random.onUnitSphere, up)).normalized;
            if (dieAxis.sqrMagnitude < 0.5f) dieAxis = Vector3.Cross(up, facing).normalized;
            float s = body.localScale.x;
            Fx.Burst(GroundPos + up * (ConveyorArt.SurfaceHeight + 0.1f), new Color(0.62f, 0.57f, 0.5f, 0.75f), 0.9f * s);
            EnemyLook.Chips(AimPoint, 6, s, 1.2f);
            // With separate parts it falls apart into gravel instead of tipping over in one piece.
            rig?.BeginCrumble(GroundPos + up * ConveyorArt.SurfaceHeight, up);
        }

        /// <summary>The crumble: squashes, tips over and sinks into the ground (fliers drop first), then is gone.</summary>
        void Update()
        {
            if (dieTime < 0f) return;
            float k = (Time.time - dieTime) / (rig != null ? rig.CrumbleSeconds : look.DeathSeconds);
            if (k >= 1f)
            {
                Destroy(gameObject);
                return;
            }
            var up = GroundPos.normalized;
            var ground = GroundPos + up * ConveyorArt.SurfaceHeight;
            if (rig != null)
            {
                rig.TickCrumble(k);
                look.Crumble(k, GroundPos, up, facing, dieScale.x);
                return;
            }
            float sink = k * k * modelHeight * dieScale.x * 1.1f;
            transform.position = Vector3.Lerp(dieFrom, ground, Mathf.Clamp01(k * 3f)) - up * sink;
            body.rotation = Quaternion.AngleAxis(28f * k, dieAxis) * dieRotation;
            body.localScale = Vector3.Scale(dieScale, new Vector3(1f + 0.25f * k, 1f - 0.5f * k, 1f + 0.25f * k));
            look.Crumble(k, GroundPos, up, facing, dieScale.x);
        }

        void OnDestroy()
        {
            blob?.Fizzle();
            rig?.Dispose();
            look?.Dispose();
        }
    }
}
