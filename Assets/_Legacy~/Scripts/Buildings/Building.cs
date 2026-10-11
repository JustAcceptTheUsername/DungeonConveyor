using System.Collections.Generic;
using UnityEngine;

namespace DungeonConveyor
{
    public abstract class Building : MonoBehaviour
    {
        public BuildingDef Def { get; private set; }
        public Vector2Int Origin { get; private set; }
        public Dir Facing { get; private set; }
        public int Level { get; private set; } = 1;
        public float Health { get; protected set; }
        public float MaxHealth { get; protected set; }
        public Cost TotalSpent { get; private set; }
        public bool IsDestroyed { get; private set; }
        public float LastDamageTime { get; private set; } = -99f;

        protected Transform Visual;
        protected GridMap Map => Game.Map;

        public int Size => Def.Size;
        public virtual bool BlocksEnemies => Def.BlocksEnemies;
        public Vector3 Center => new Vector3(Origin.x + Size * 0.5f, 0, Origin.y + Size * 0.5f);
        public bool CanUpgrade => Level < Def.MaxLevel;
        public Cost UpgradeCost => Def.Cost * (0.8f + Level * 0.7f);

        public static Building Create(BuildingDef def, Vector2Int origin, Dir facing, Transform parent)
        {
            var go = new GameObject(def.Name);
            go.transform.SetParent(parent, false);
            var b = (Building)go.AddComponent(def.Type);
            b.Def = def;
            b.Origin = origin;
            b.Facing = def.Rotatable ? facing : Dir.N;
            b.MaxHealth = b.Health = def.Health;
            b.TotalSpent = def.Cost;
            go.transform.position = b.Center;
            b.Visual = BuildingVisuals.Create(def, go.transform);
            b.Visual.localRotation = b.Facing.Rotation();
            b.OnCreated();
            return b;
        }

        public IEnumerable<Vector2Int> Cells()
        {
            for (int x = 0; x < Size; x++)
                for (int y = 0; y < Size; y++)
                    yield return new Vector2Int(Origin.x + x, Origin.y + y);
        }

        public bool Occupies(Vector2Int c) =>
            c.x >= Origin.x && c.y >= Origin.y && c.x < Origin.x + Size && c.y < Origin.y + Size;

        /// <summary>Distance from a world point to the building's footprint (0 if inside).</summary>
        public float DistanceTo(Vector3 p)
        {
            float dx = Mathf.Max(Origin.x - p.x, 0, p.x - (Origin.x + Size));
            float dz = Mathf.Max(Origin.y - p.z, 0, p.z - (Origin.y + Size));
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        public void SetFacing(Dir d)
        {
            if (!Def.Rotatable) return;
            Facing = d;
            Visual.localRotation = d.Rotation();
            OnNeighborChanged();
        }

        protected virtual void OnCreated() { }

        // ---------------------------------------------------------------- items

        public virtual bool CanAccept(ResourceType type, Vector2Int from) => false;
        public virtual void Accept(Item item, Vector2Int from) => ItemPool.Release(item);

        /// <summary>Offer an item (by type) to whatever sits at `target`. Creates the item only on success.</summary>
        protected bool Push(ResourceType type, Vector2Int from, Vector2Int target, Item existing = null)
        {
            var b = Map.GetBuilding(target);
            if (b == null || b == this || !b.CanAccept(type, from)) return false;
            b.Accept(existing ?? ItemPool.Get(type), from);
            return true;
        }

        /// <summary>Cells just outside the footprint on the given side, paired with the footprint cell they touch.</summary>
        protected IEnumerable<(Vector2Int from, Vector2Int to)> Edge(Dir side)
        {
            var o = side.Offset();
            foreach (var c in Cells())
                if (!Occupies(c + o)) yield return (c, c + o);
        }

        public virtual void OnNeighborChanged() { }

        /// <summary>Release anything held (items) before removal.</summary>
        public virtual void Cleanup() { }

        // ---------------------------------------------------------------- health & upgrades

        public void TakeDamage(float amount)
        {
            if (IsDestroyed) return;
            Health -= amount;
            LastDamageTime = Time.time;
            if (Health <= 0) Game.Build.DestroyBuilding(this);
        }

        public void MarkDestroyed() => IsDestroyed = true;

        public void Upgrade()
        {
            TotalSpent += UpgradeCost;
            Level++;
            MaxHealth *= 1.3f;
            Health = MaxHealth;
            OnUpgraded();
            BuildingVisuals.SetLevelPips(transform, Level, Size);
        }

        protected virtual void OnUpgraded() { }

        protected virtual void Update()
        {
            // Slow self-repair when left alone.
            if (Health < MaxHealth && Time.time - LastDamageTime > 8f)
                Health = Mathf.Min(MaxHealth, Health + MaxHealth * 0.02f * Time.deltaTime);
        }

        /// <summary>Stat lines for the selection panel.</summary>
        public virtual void Describe(List<(string label, string value)> lines) { }

        /// <summary>Alert text this building wants to raise, if any.</summary>
        public virtual string Alert => null;
    }
}
