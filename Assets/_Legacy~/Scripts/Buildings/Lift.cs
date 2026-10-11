using System.Collections.Generic;
using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>
    /// Launches items in an arc to the next Lift facing the same way (2–5 tiles ahead), letting belts
    /// cross over walls and other lines. Without a partner it simply passes items forward.
    /// </summary>
    public class Lift : Building
    {
        const int Capacity = 4;
        const float TopHeight = 1.7f;
        const float ArcSpeed = 5f;

        class Transit
        {
            public Item Item;
            public Vector3 From, To;
            public float T, Duration, Arc;
        }

        readonly List<Transit> transits = new List<Transit>();

        Vector3 Top => Center + Vector3.up * TopHeight;

        Lift FindPartner()
        {
            for (int k = 2; k <= 5; k++)
                if (Map.GetBuilding(Origin + Facing.Offset() * k) is Lift l && l.Facing == Facing)
                    return l;
            return null;
        }

        public override bool CanAccept(ResourceType type, Vector2Int from)
        {
            var dir = DirUtil.Between(Origin, from);
            if (dir == null || dir.Value == Facing || transits.Count >= Capacity) return false;
            var partner = FindPartner();
            return partner == null || partner.transits.Count < Capacity;
        }

        public override void Accept(Item item, Vector2Int from)
        {
            var dir = DirUtil.Between(Origin, from) ?? Facing.Opposite();
            var entry = Center + dir.World() * 0.5f + Vector3.up * 0.2f;
            var partner = FindPartner();
            if (partner != null)
            {
                float dist = Vector3.Distance(Center, partner.Center);
                partner.transits.Add(new Transit { Item = item, From = entry, To = partner.Top, Duration = dist / ArcSpeed + 0.2f, Arc = 1.2f });
            }
            else
            {
                transits.Add(new Transit { Item = item, From = entry, To = Top, Duration = 0.35f, Arc = 0f });
            }
        }

        protected override void Update()
        {
            base.Update();
            for (int i = 0; i < transits.Count; i++)
            {
                var tr = transits[i];
                tr.T = Mathf.Min(1f, tr.T + Time.deltaTime / tr.Duration);
                var p = Vector3.Lerp(tr.From, tr.To, tr.T);
                p.y += Mathf.Sin(tr.T * Mathf.PI) * tr.Arc;
                tr.Item.T.position = p;
            }

            // Output the oldest arrived item.
            if (transits.Count > 0 && transits[0].T >= 1f)
            {
                var tr = transits[0];
                if (Push(tr.Item.Type, Origin, Origin + Facing.Offset(), tr.Item))
                    transits.RemoveAt(0);
            }
        }

        public override void Cleanup()
        {
            foreach (var tr in transits) ItemPool.Release(tr.Item);
            transits.Clear();
        }

        public override void Describe(List<(string, string)> lines)
        {
            var partner = FindPartner();
            lines.Add(("Linked", partner != null ? $"{Vector2Int.Distance(Origin, partner.Origin):0} tiles" : "No partner"));
            lines.Add(("Load", $"{transits.Count}/{Capacity}"));
        }
    }
}
