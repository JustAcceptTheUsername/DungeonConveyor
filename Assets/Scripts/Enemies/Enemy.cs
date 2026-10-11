using System.Collections.Generic;
using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>
    /// One enemy. Walks in along its spawn bridge, then follows the flow field cell by cell to the Core, where it
    /// deals its damage and is gone. Moved by WaveManager; hurt and slowed by Combat.
    /// </summary>
    public class Enemy : MonoBehaviour
    {
        public static readonly List<Enemy> All = new List<Enemy>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => All.Clear();

        public EnemyCatalog.Entry Def { get; private set; }
        public float Health { get; private set; }
        public float MaxHealth { get; private set; }
        public bool Alive => Health > 0f;
        /// <summary>Fractional grid position (cell (x, y) centred at (x, y)).</summary>
        public Vector2 GridPos { get; private set; }
        public Vector2Int Cell => Vector2Int.RoundToInt(GridPos);
        /// <summary>Steps left to the Core; bridges count extra so enemies on the map are ahead of them.</summary>
        public int StepsToCore { get; private set; } = FlowField.Unreachable;
        /// <summary>Speed multiplier for this frame (slow fields lower it).</summary>
        public float SlowFactor = 1f;

        IsoGrid map;
        SpriteRenderer body, barBack, barFill;
        readonly Queue<(Vector2Int cell, float level)> bridgePath = new Queue<(Vector2Int, float)>();
        Vector2 from, to;
        float fromLevel, toLevel, level, bob;
        const int OnBridgeSteps = 100000;

        static Sprite pixel;
        static Sprite Pixel
        {
            get
            {
                if (pixel != null) return pixel;
                var t = new Texture2D(1, 1) { filterMode = FilterMode.Point };
                t.SetPixel(0, 0, Color.white);
                t.Apply();
                return pixel = Sprite.Create(t, new Rect(0, 0, 1, 1), new Vector2(0f, 0.5f), 1f);
            }
        }

        public static Enemy Spawn(EnemyCatalog.Entry def, float healthScale, IsoGrid map, IsoGrid.Bridge bridge, Transform parent, Material material)
        {
            var go = new GameObject(def.Name);
            go.transform.SetParent(parent, false);
            var e = go.AddComponent<Enemy>();
            e.Init(def, healthScale, map, bridge, material);
            All.Add(e);
            return e;
        }

        void Init(EnemyCatalog.Entry def, float healthScale, IsoGrid grid, IsoGrid.Bridge bridge, Material material)
        {
            Def = def;
            map = grid;
            MaxHealth = Health = def.Health * healthScale;
            bob = Random.Range(0f, 6.28f);

            body = NewSprite("Body", material);
            barBack = NewSprite("HealthBack", material);
            barFill = NewSprite("Health", material);
            barBack.sprite = barFill.sprite = Pixel;
            barBack.color = new Color(0f, 0f, 0f, 0.7f);
            barBack.enabled = barFill.enabled = false;

            // Walk the bridge from its far (off-screen) end back to the landing tile.
            float bridgeLevel = map.BridgeLevel(bridge);
            for (int i = bridge.Cells.Count - 2; i >= 0; i--) bridgePath.Enqueue((bridge.Cells[i], bridgeLevel));
            bridgePath.Enqueue((bridge.Landing, map.HeightAt(bridge.Landing)));
            var start = bridge.SpawnPoint;
            GridPos = from = to = start;
            level = fromLevel = toLevel = bridgeLevel;
            StepsToCore = OnBridgeSteps + bridgePath.Count;
            Place(Dir.Of(bridge.Landing - start));
        }

        SpriteRenderer NewSprite(string name, Material material)
        {
            var sr = new GameObject(name).AddComponent<SpriteRenderer>();
            sr.transform.SetParent(transform, false);
            if (material != null) sr.sharedMaterial = material;
            return sr;
        }

        /// <summary>Moves toward the Core. Returns true when it has reached the Core this frame.</summary>
        public bool Step(float dt, FlowField flow)
        {
            float move = Def.Speed * SlowFactor * dt;
            int facing = Dir.Of(Vector2Int.RoundToInt((to - from) * 2f));
            while (move > 0f)
            {
                var gap = to - GridPos;
                float len = gap.magnitude;
                if (len > move)
                {
                    GridPos += gap / len * move;
                    break;
                }
                GridPos = to;
                move -= len;
                bool moving = ChooseNext(flow, out bool arrived);
                if (arrived) return true;
                if (!moving) break;
                facing = Dir.Of(Vector2Int.RoundToInt(to - from));
            }

            float t = (from - to).sqrMagnitude > 0f ? 1f - (to - GridPos).magnitude / (to - from).magnitude : 1f;
            level = Mathf.Lerp(fromLevel, toLevel, t);
            Place(facing);
            return false;
        }

        bool ChooseNext(FlowField flow, out bool arrived)
        {
            arrived = false;
            var here = Vector2Int.RoundToInt(to);
            from = to;
            fromLevel = toLevel;
            if (bridgePath.Count > 0)
            {
                var (cell, lvl) = bridgePath.Dequeue();
                to = cell;
                toLevel = lvl;
                StepsToCore = OnBridgeSteps + bridgePath.Count;
                return true;
            }
            StepsToCore = flow.Distance(here);
            if (StepsToCore == 0)
            {
                arrived = true;
                return false;
            }
            if (!flow.Next(here, out var next)) return false; // walled in for now; try again next frame
            to = next;
            toLevel = map.HeightAt(next);
            return true;
        }

        void Place(int facing)
        {
            var sprites = Def.Sprites;
            if (sprites != null && sprites.Length == 4) body.sprite = sprites[Mathf.Clamp(facing, 0, 3)];
            float hop = Def.Hover > 0f ? Mathf.Sin(Time.time * 6f + bob) * 0.05f : Mathf.Abs(Mathf.Sin(Time.time * 9f + bob)) * 0.03f;
            transform.position = map.GridToWorld(GridPos, level) + Vector3.up * (ConveyorArt.SurfaceHeight + Def.Hover + hop);

            int order = map.SortOrder(Cell) + 3;
            body.sortingOrder = order;
            barBack.sortingOrder = order;
            barFill.sortingOrder = order + 1;
            UpdateBar();
        }

        void UpdateBar()
        {
            bool show = Health < MaxHealth;
            barBack.enabled = barFill.enabled = show;
            if (!show) return;
            const float width = 0.5f;
            var top = body.sprite != null ? body.sprite.bounds.max.y : 0.5f;
            var pos = new Vector3(-width * 0.5f, top + 0.08f, 0f);
            barBack.transform.localPosition = pos;
            barBack.transform.localScale = new Vector3(width, 0.06f, 1f);
            float f = Mathf.Clamp01(Health / MaxHealth);
            barFill.transform.localPosition = pos + new Vector3(0f, 0f, -0.001f);
            barFill.transform.localScale = new Vector3(width * f, 0.045f, 1f);
            barFill.color = Color.Lerp(new Color(1f, 0.2f, 0.15f), new Color(0.4f, 1f, 0.3f), f);
        }

        /// <summary>World point turrets aim at (middle of the body).</summary>
        public Vector3 AimPoint => transform.position + Vector3.up * 0.2f;

        /// <summary>Deals damage; returns true if this killed it.</summary>
        public bool TakeDamage(float amount)
        {
            if (!Alive) return false;
            Health -= amount;
            return !Alive;
        }

        public void Remove()
        {
            All.Remove(this);
            Destroy(gameObject);
        }
    }
}
