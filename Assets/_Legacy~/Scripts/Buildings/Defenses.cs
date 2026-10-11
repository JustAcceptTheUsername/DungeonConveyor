using System.Collections.Generic;
using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>Shared ammo + targeting logic. Any ore delivered by belt becomes ammo; when empty it draws Iron from storage.</summary>
    public abstract class Defense : Building
    {
        protected float Ammo;
        protected Transform Head, Muzzle;
        protected Enemy Target;
        float retarget;

        protected abstract float MaxAmmo { get; }
        protected abstract float AmmoPerItem { get; }
        public abstract float Range { get; }

        public bool OutOfAmmo => Ammo <= 0f && Game.Bank[ResourceType.Iron] <= 0;

        protected override void OnCreated()
        {
            Head = Visual.Find("Head");
            Muzzle = Head != null ? Head.Find("Muzzle") : null;
            Ammo = MaxAmmo * 0.5f;
        }

        public override bool CanAccept(ResourceType type, Vector2Int from) => Ammo + AmmoPerItem <= MaxAmmo;

        public override void Accept(Item item, Vector2Int from)
        {
            Ammo += AmmoPerItem;
            ItemPool.Release(item);
        }

        protected bool UseAmmo(float amount)
        {
            if (Ammo < amount && Game.Bank.TrySpend(ResourceType.Iron, 1)) Ammo += AmmoPerItem;
            if (Ammo < amount) return false;
            Ammo -= amount;
            return true;
        }

        protected void UpdateTarget()
        {
            retarget -= Time.deltaTime;
            bool valid = Target != null && Target.Alive && Vector3.Distance(Target.transform.position, Center) <= Range;
            if (valid && retarget > 0) return;
            retarget = 0.25f;
            Target = Enemy.Nearest(Center, Range);
        }

        protected bool Aim(Vector3 point, float degreesPerSec)
        {
            if (Head == null) return true;
            var dir = point - Head.position;
            dir.y = 0;
            if (dir.sqrMagnitude < 0.001f) return true;
            var want = Quaternion.LookRotation(dir);
            Head.rotation = Quaternion.RotateTowards(Head.rotation, want, degreesPerSec * Time.deltaTime);
            return Quaternion.Angle(Head.rotation, want) < 8f;
        }

        protected Vector3 MuzzlePos => Muzzle != null ? Muzzle.position : Center + Vector3.up * 0.7f;

        public override void Describe(List<(string, string)> lines)
        {
            lines.Add(("Range", $"{Range:0.0}"));
            lines.Add(("Ammo", $"{Mathf.FloorToInt(Ammo)}/{MaxAmmo:0}"));
        }

        public override string Alert => OutOfAmmo ? "Turret Low Ammo" : null;
    }

    public class Turret : Defense
    {
        public static float RangeForLevel(int level) => 5.5f + level * 0.75f;

        float cooldown;

        public override float Range => RangeForLevel(Level);
        public float Damage => 9f * (1f + 0.6f * (Level - 1));
        public float FireRate => 2f + 0.5f * (Level - 1);
        protected override float MaxAmmo => 60;
        protected override float AmmoPerItem => 6;

        protected override void Update()
        {
            base.Update();
            cooldown -= Time.deltaTime;
            UpdateTarget();
            if (Target == null) return;
            bool aimed = Aim(Target.transform.position, 540f);
            if (aimed && cooldown <= 0f && UseAmmo(1f))
            {
                cooldown = 1f / FireRate;
                Projectile.Fire(MuzzlePos, Target, Damage);
                Effects.Flash(MuzzlePos, new Color(1f, 0.7f, 0.2f));
            }
        }

        public override void Describe(List<(string, string)> lines)
        {
            lines.Add(("Damage", $"{Damage:0} x {FireRate:0.0}/s"));
            base.Describe(lines);
        }
    }

    public class Flamethrower : Defense
    {
        public static float RangeForLevel(int level) => 3.2f + level * 0.4f;
        const float ConeAngle = 30f;

        ParticleSystem flame;
        bool firing;

        public override float Range => RangeForLevel(Level);
        public float Dps => 40f * (1f + 0.6f * (Level - 1));
        protected override float MaxAmmo => 40;    // seconds of fuel
        protected override float AmmoPerItem => 3;

        protected override void OnCreated()
        {
            base.OnCreated();
            flame = Effects.CreateFlame(Muzzle != null ? Muzzle : Visual);
        }

        protected override void Update()
        {
            base.Update();
            UpdateTarget();
            bool shouldFire = Target != null && Aim(Target.transform.position, 360f) && UseAmmo(Time.deltaTime);
            if (shouldFire != firing)
            {
                firing = shouldFire;
                var em = flame.emission;
                em.enabled = firing;
            }
            if (!firing) return;

            var origin = Center;
            var forward = Head != null ? Head.forward : Vector3.forward;
            forward.y = 0;
            for (int i = Enemy.All.Count - 1; i >= 0; i--)
            {
                var e = Enemy.All[i];
                var to = e.transform.position - origin;
                to.y = 0;
                if (to.magnitude <= Range && Vector3.Angle(forward, to) <= ConeAngle)
                    e.TakeDamage(Dps * Time.deltaTime, false);
            }
        }

        public override void Describe(List<(string, string)> lines)
        {
            lines.Add(("Damage", $"{Dps:0}/s (area)"));
            base.Describe(lines);
        }
    }

    public class Wall : Building
    {
        public override void Describe(List<(string, string)> lines) => lines.Add(("Role", "Blocks enemies"));
    }

    /// <summary>Remote drop-off point: anything delivered goes straight into storage.</summary>
    public class Collector : Building
    {
        int collected;

        public override bool CanAccept(ResourceType type, Vector2Int from) => true;

        public override void Accept(Item item, Vector2Int from)
        {
            Game.Bank.Deliver(item.Type, 1);
            collected++;
            ItemPool.Release(item);
        }

        public override void Describe(List<(string, string)> lines) => lines.Add(("Collected", Fmt.Short(collected)));
    }
}
