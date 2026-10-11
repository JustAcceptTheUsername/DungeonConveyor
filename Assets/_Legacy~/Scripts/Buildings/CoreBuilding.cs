using System.Collections.Generic;
using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>The main building: storage drop-off from every side plus a built-in laser.</summary>
    public class CoreBuilding : Building
    {
        public static float RangeForLevel(int level) => 6f + level * 0.5f;

        Transform spin, muzzle;
        LineRenderer beam;
        Enemy target;
        float cooldown, beamTime;

        public float Range => RangeForLevel(Level);
        public float Damage => 14f * (1f + 0.5f * (Level - 1));

        protected override void OnCreated()
        {
            spin = Visual.Find("Spin");
            muzzle = Visual.Find("Muzzle");
            beam = Effects.CreateBeam(transform);
        }

        public override bool CanAccept(ResourceType type, Vector2Int from) => true;

        public override void Accept(Item item, Vector2Int from)
        {
            Game.Bank.Deliver(item.Type, 1);
            ItemPool.Release(item);
        }

        protected override void Update()
        {
            base.Update();
            if (spin != null) spin.Rotate(0, 40f * Time.deltaTime, 0, Space.World);

            cooldown -= Time.deltaTime;
            beamTime -= Time.deltaTime;
            if (target == null || !target.Alive || Vector3.Distance(target.transform.position, Center) > Range)
                target = Enemy.Nearest(Center, Range);
            if (target != null && cooldown <= 0f)
            {
                cooldown = 0.8f;
                beamTime = 0.12f;
                var from = muzzle != null ? muzzle.position : Center + Vector3.up * 2f;
                var to = target.transform.position + Vector3.up * 0.3f;
                beam.SetPosition(0, from);
                beam.SetPosition(1, to);
                Effects.Sparks(to, new Color(1f, 0.6f, 0.1f), 6);
                target.TakeDamage(Damage);
            }
            beam.enabled = beamTime > 0f;
        }

        public override void Describe(List<(string, string)> lines)
        {
            lines.Add(("Laser", $"{Damage:0} dmg, {Range:0.0} range"));
        }
    }
}
