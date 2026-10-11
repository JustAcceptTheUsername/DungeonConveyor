using System.Collections.Generic;
using UnityEngine;

namespace DungeonConveyor
{
    public class Conveyor : Building
    {
        const float Gap = 0.34f;
        const float ItemHeight = 0.17f;

        public static float SpeedForLevel(int level) => 0.8f + 0.8f * level; // tiles/sec: 1.6, 2.4, 3.2

        class Slot
        {
            public Item Item;
            public float P;          // 0 = entry edge, 0.5 = centre, 1 = exit edge
            public Vector3 Entry;
        }

        readonly List<Slot> slots = new List<Slot>(); // [0] is the front-most item
        MeshFilter meshFilter;
        MeshRenderer meshRenderer;
        BuildingVisuals.BeltShape shape;
        float blockedTime;
        int passed;
        float rateTimer;

        public float Speed => SpeedForLevel(Level);
        public float ItemsPerSecond { get; private set; }
        public bool Jammed => blockedTime > 3f;
        public Vector2Int OutputCell => Origin + Facing.Offset();

        protected override void OnCreated()
        {
            meshFilter = Visual.gameObject.AddComponent<MeshFilter>();
            meshRenderer = Visual.gameObject.AddComponent<MeshRenderer>();
            ApplyMesh();
        }

        void ApplyMesh()
        {
            var (mesh, mats) = BuildingVisuals.BeltMesh(shape, Level);
            meshFilter.sharedMesh = mesh;
            meshRenderer.sharedMaterials = mats;
        }

        protected override void OnUpgraded() => ApplyMesh();

        bool FeedsInto(Vector2Int cell)
        {
            var b = Map.GetBuilding(cell);
            if (b is Conveyor c) return c.OutputCell == Origin;
            return b != null && !(b is Wall) && !(b is Turret) && !(b is Flamethrower) && !(b is Collector) && !(b is CoreBuilding);
        }

        public override void OnNeighborChanged()
        {
            var back = Origin - Facing.Offset();
            var left = Origin + Facing.Left().Offset();
            var right = Origin + Facing.Right().Offset();
            bool fromBack = FeedsInto(back), fromLeft = FeedsInto(left), fromRight = FeedsInto(right);
            shape = !fromBack && fromLeft && !fromRight ? BuildingVisuals.BeltShape.FromLeft
                  : !fromBack && fromRight && !fromLeft ? BuildingVisuals.BeltShape.FromRight
                  : BuildingVisuals.BeltShape.Straight;
            ApplyMesh();
        }

        public override bool CanAccept(ResourceType type, Vector2Int from)
        {
            var dir = DirUtil.Between(Origin, from);
            if (dir == null || dir.Value == Facing) return false;
            return slots.Count == 0 || slots[slots.Count - 1].P >= Gap;
        }

        public override void Accept(Item item, Vector2Int from)
        {
            var dir = DirUtil.Between(Origin, from) ?? Facing.Opposite();
            var slot = new Slot { Item = item, P = 0f, Entry = Center + dir.World() * 0.5f };
            slots.Add(slot);
            Place(slot);
        }

        protected override void Update()
        {
            base.Update();
            float step = Speed * Time.deltaTime;
            bool blocked = false;

            for (int i = 0; i < slots.Count; i++)
            {
                var s = slots[i];
                float limit = i == 0 ? 1f : slots[i - 1].P - Gap;
                s.P = Mathf.Min(s.P + step, Mathf.Max(s.P, limit));
                Place(s);
            }

            if (slots.Count > 0 && slots[0].P >= 1f)
            {
                var front = slots[0];
                if (Push(front.Item.Type, Origin, OutputCell, front.Item))
                {
                    slots.RemoveAt(0);
                    passed++;
                }
                else blocked = true;
            }

            blockedTime = blocked ? blockedTime + Time.deltaTime : 0f;

            rateTimer += Time.deltaTime;
            if (rateTimer >= 2f)
            {
                ItemsPerSecond = passed / rateTimer;
                passed = 0;
                rateTimer = 0;
            }
        }

        void Place(Slot s)
        {
            var center = Center;
            var exit = center + Facing.World() * 0.5f;
            var p = s.P < 0.5f ? Vector3.Lerp(s.Entry, center, s.P * 2f) : Vector3.Lerp(center, exit, (s.P - 0.5f) * 2f);
            p.y = ItemHeight;
            s.Item.T.position = p;
        }

        public override void Cleanup()
        {
            foreach (var s in slots) ItemPool.Release(s.Item);
            slots.Clear();
        }

        public override void Describe(List<(string, string)> lines)
        {
            lines.Add(("Speed", $"{Speed:0.0}/s"));
            lines.Add(("Items", $"{ItemsPerSecond:0.0}/s"));
            lines.Add(("Load", $"{slots.Count}/3"));
        }

        // A belt backed up behind a full turret is working as intended.
        public override string Alert =>
            Jammed && !(Map.GetBuilding(OutputCell) is Turret || Map.GetBuilding(OutputCell) is Flamethrower) ? "Conveyor Jammed" : null;
    }
}
