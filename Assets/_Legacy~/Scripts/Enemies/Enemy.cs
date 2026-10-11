using System.Collections.Generic;
using UnityEngine;

namespace DungeonConveyor
{
    public enum EnemyKind { Spider, Runner, Brute }

    public class Enemy : MonoBehaviour
    {
        public static readonly List<Enemy> All = new List<Enemy>();

        struct Stats
        {
            public float Health, Speed, Bite, Scale;
            public int Reward;
        }

        static readonly Stats[] KindStats =
        {
            new Stats { Health = 30, Speed = 1.5f, Bite = 6, Scale = 1f, Reward = 2 },
            new Stats { Health = 14, Speed = 2.6f, Bite = 3, Scale = 0.7f, Reward = 1 },
            new Stats { Health = 160, Speed = 0.95f, Bite = 22, Scale = 1.7f, Reward = 10 },
        };
        const float BiteInterval = 0.7f;

        static GameObject[] templates;
        static Transform root;

        public EnemyKind Kind { get; private set; }
        public float Health { get; private set; }
        public float MaxHealth { get; private set; }
        public bool Alive => Health > 0f;
        public bool IsAttacking => attacking != null;
        public Vector3 AimPoint => transform.position + Vector3.up * 0.25f * stats.Scale;

        Stats stats;
        Vector3 offset;
        Building attacking;
        float biteTimer, anim, punch;
        Transform body, legsA, legsB;

        public static void Init(Transform parent)
        {
            All.Clear();
            root = new GameObject("Enemies").transform;
            root.SetParent(parent, false);
            var holder = new GameObject("EnemyTemplates");
            holder.transform.SetParent(parent, false);
            holder.SetActive(false);
            templates = new[]
            {
                MakeSpider("Spider", Art.EnemyBody, Art.EnemyEye),
                MakeSpider("Runner", Art.RunnerBody, Art.RunnerGlow),
                MakeSpider("Brute", Art.BruteBody, Art.BruteGlow, crystals: true),
            };
            foreach (var t in templates) t.transform.SetParent(holder.transform, false);
        }

        /// <summary>Model faces +Z. Parts: Body, LegsA, LegsB (alternating tripod gait).</summary>
        static GameObject MakeSpider(string name, Material bodyMat, Material glowMat, bool crystals = false)
        {
            var m = new VoxelModel(name);
            m.Part("Body", new Vector3(0, 0.2f, 0));
            m.Box(new Vector3(0, 0.02f, 0.08f), new Vector3(0.26f, 0.16f, 0.24f), bodyMat, "Body");     // thorax
            m.Box(new Vector3(0, 0.07f, -0.2f), new Vector3(0.34f, 0.26f, 0.36f), bodyMat, "Body");     // abdomen
            m.Box(new Vector3(0, 0.12f, -0.24f), new Vector3(0.18f, 0.1f, 0.2f), glowMat, "Body");      // glowing mark
            m.Box(new Vector3(0.06f, 0.06f, 0.21f), new Vector3(0.05f, 0.05f, 0.03f), glowMat, "Body"); // eyes
            m.Box(new Vector3(-0.06f, 0.06f, 0.21f), new Vector3(0.05f, 0.05f, 0.03f), glowMat, "Body");
            m.Box(new Vector3(0.04f, -0.03f, 0.23f), new Vector3(0.03f, 0.08f, 0.05f), bodyMat, "Body"); // fangs
            m.Box(new Vector3(-0.04f, -0.03f, 0.23f), new Vector3(0.03f, 0.08f, 0.05f), bodyMat, "Body");
            if (crystals)
            {
                m.Box(new Vector3(0.06f, 0.26f, -0.2f), new Vector3(0.08f, 0.22f, 0.08f), glowMat, Quaternion.Euler(10, 30, 20), "Body");
                m.Box(new Vector3(-0.08f, 0.24f, -0.12f), new Vector3(0.07f, 0.18f, 0.07f), glowMat, Quaternion.Euler(-15, 10, -25), "Body");
            }
            m.Part("LegsA", new Vector3(0, 0.2f, 0.05f));
            m.Part("LegsB", new Vector3(0, 0.2f, 0.05f));
            for (int i = 0; i < 4; i++)
                for (int s = -1; s <= 1; s += 2)
                {
                    string part = ((i + (s > 0 ? 0 : 1)) % 2 == 0) ? "LegsA" : "LegsB";
                    float z = 0.12f - i * 0.1f;
                    float splay = (i - 1.5f) * -18f;
                    var upper = Quaternion.Euler(0, splay * s, s * -25f);
                    m.Box(upper * new Vector3(s * 0.17f, 0, 0) + new Vector3(0, 0.04f, z), new Vector3(0.26f, 0.035f, 0.035f), bodyMat, upper, part);
                    var footPos = upper * new Vector3(s * 0.3f, 0, 0) + new Vector3(0, 0.04f, z);
                    m.Box(footPos + new Vector3(s * 0.02f, -0.11f, 0), new Vector3(0.035f, 0.24f, 0.035f), bodyMat, Quaternion.Euler(0, 0, s * 15f), part);
                }
            return m.Finish();
        }

        public static Enemy Spawn(EnemyKind kind, Vector3 pos, float healthMult)
        {
            var go = Instantiate(templates[(int)kind], root);
            var e = go.AddComponent<Enemy>();
            e.Kind = kind;
            e.stats = KindStats[(int)kind];
            e.MaxHealth = e.Health = e.stats.Health * healthMult;
            e.offset = new Vector3(Random.Range(-0.3f, 0.3f), 0, Random.Range(-0.3f, 0.3f));
            e.anim = Random.value * 10f;
            go.transform.position = pos;
            go.transform.localScale = Vector3.one * e.stats.Scale;
            e.body = go.transform.Find("Body");
            e.legsA = go.transform.Find("LegsA");
            e.legsB = go.transform.Find("LegsB");
            All.Add(e);
            return e;
        }

        public static Enemy Nearest(Vector3 p, float range)
        {
            Enemy best = null;
            float bestSq = range * range;
            foreach (var e in All)
            {
                var d = e.transform.position - p;
                d.y = 0;
                float sq = d.sqrMagnitude;
                if (sq <= bestSq) { bestSq = sq; best = e; }
            }
            return best;
        }

        void Update()
        {
            if (Game.IsGameOver) return;
            var map = Game.Map;
            var pos = transform.position;
            bool moving = false;

            if (attacking != null && !attacking.IsDestroyed)
            {
                Face(attacking.Center - pos);
                biteTimer -= Time.deltaTime;
                if (biteTimer <= 0f)
                {
                    biteTimer = BiteInterval;
                    attacking.TakeDamage(stats.Bite);
                    punch = 0.25f;
                }
            }
            else
            {
                attacking = null;
                var cell = GridMap.WorldToCell(pos);
                var next = Game.Flow.Next(cell);
                Vector3 goal;
                if (next.HasValue)
                {
                    var b = map.GetBuilding(next.Value);
                    if (b != null && b.BlocksEnemies && b.DistanceTo(pos) < 0.25f + 0.15f * stats.Scale)
                    {
                        attacking = b;
                        biteTimer = BiteInterval * 0.5f;
                    }
                    goal = GridMap.CellCenter(next.Value) + offset;
                }
                else
                {
                    // Stuck or off the field: head straight for the core.
                    goal = Game.Core != null ? Game.Core.Center : pos;
                    var core = Game.Core;
                    if (core != null && core.DistanceTo(pos) < 0.4f) attacking = core;
                }

                if (attacking == null)
                {
                    var dir = goal - pos;
                    dir.y = 0;
                    if (dir.sqrMagnitude > 0.0001f)
                    {
                        transform.position = pos + dir.normalized * Mathf.Min(stats.Speed * Time.deltaTime, dir.magnitude);
                        Face(dir);
                        moving = true;
                    }
                }
            }

            Animate(moving);
        }

        void Face(Vector3 dir)
        {
            dir.y = 0;
            if (dir.sqrMagnitude < 0.0001f) return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), 540f * Time.deltaTime);
        }

        void Animate(bool moving)
        {
            anim += Time.deltaTime * (moving ? stats.Speed * 9f : 3f);
            float swing = Mathf.Sin(anim) * (moving ? 22f : 6f);
            if (legsA != null) legsA.localRotation = Quaternion.Euler(0, swing, 0);
            if (legsB != null) legsB.localRotation = Quaternion.Euler(0, -swing, 0);
            punch = Mathf.Max(0, punch - Time.deltaTime * 2f);
            if (body != null)
            {
                body.localPosition = new Vector3(0, 0.2f + Mathf.Abs(Mathf.Sin(anim)) * 0.025f, punch * 0.4f);
                body.localScale = Vector3.one * (1f + punch * 0.6f);
            }
        }

        public void TakeDamage(float amount, bool showNumber = true)
        {
            if (!Alive) return;
            Health -= amount;
            punch = Mathf.Max(punch, 0.12f);
            if (showNumber) Effects.Text(AimPoint + Vector3.up * 0.4f, Mathf.CeilToInt(amount).ToString(), Color.white);
            if (Health <= 0f) Die();
        }

        void Die()
        {
            All.Remove(this);
            var color = Kind == EnemyKind.Runner ? new Color(0.6f, 1f, 0.1f) : Kind == EnemyKind.Brute ? new Color(0.8f, 0.3f, 1f) : new Color(1f, 0.2f, 0.4f);
            Effects.Sparks(AimPoint, color, Kind == EnemyKind.Brute ? 40 : 14, stats.Scale);
            Game.Bank.Grant(ResourceType.Gold, stats.Reward);
            if (Kind == EnemyKind.Brute) Game.Bank.Grant(ResourceType.Amethyst, 2);
            Game.Waves.OnEnemyKilled();
            Destroy(gameObject);
        }
    }
}
