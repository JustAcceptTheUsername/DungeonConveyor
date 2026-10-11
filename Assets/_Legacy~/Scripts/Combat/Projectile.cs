using System.Collections.Generic;
using UnityEngine;

namespace DungeonConveyor
{
    public class Projectile : MonoBehaviour
    {
        const float Speed = 22f;

        static readonly Stack<Projectile> pool = new Stack<Projectile>();
        static GameObject template;
        static Transform root;

        Enemy target;
        Vector3 lastTargetPos;
        float damage;

        public static void Init(Transform parent)
        {
            pool.Clear();
            root = new GameObject("Projectiles").transform;
            root.SetParent(parent, false);
            template = new VoxelModel("Projectile").Box(Vector3.zero, new Vector3(0.07f, 0.07f, 0.45f), Art.Projectile).Finish(false);
            template.transform.SetParent(root, false);
            template.SetActive(false);
        }

        public static void Fire(Vector3 from, Enemy target, float damage)
        {
            var p = pool.Count > 0 ? pool.Pop() : Instantiate(template, root).AddComponent<Projectile>();
            p.transform.position = from;
            p.target = target;
            p.lastTargetPos = target.AimPoint;
            p.damage = damage;
            p.gameObject.SetActive(true);
        }

        void Update()
        {
            if (target != null && target.Alive) lastTargetPos = target.AimPoint;
            var to = lastTargetPos - transform.position;
            float step = Speed * Time.deltaTime;
            if (to.magnitude <= step)
            {
                if (target != null && target.Alive) target.TakeDamage(damage);
                Effects.Sparks(lastTargetPos, new Color(1f, 0.7f, 0.2f), 5);
                gameObject.SetActive(false);
                pool.Push(this);
                return;
            }
            transform.rotation = Quaternion.LookRotation(to);
            transform.position += to.normalized * step;
        }
    }
}
