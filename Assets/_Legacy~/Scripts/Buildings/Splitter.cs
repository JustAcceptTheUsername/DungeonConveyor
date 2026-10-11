using System.Collections.Generic;
using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>Takes items from any side and deals them round-robin to the other three sides.</summary>
    public class Splitter : Building
    {
        const float TransitTime = 0.3f;

        Item held;
        Dir heldFrom;
        Vector3 entry;
        float t;
        int next;

        public override bool CanAccept(ResourceType type, Vector2Int from) => held == null && DirUtil.Between(Origin, from) != null;

        public override void Accept(Item item, Vector2Int from)
        {
            held = item;
            heldFrom = DirUtil.Between(Origin, from).Value;
            entry = Center + heldFrom.World() * 0.5f;
            t = 0;
        }

        protected override void Update()
        {
            base.Update();
            if (held == null) return;
            t += Time.deltaTime / TransitTime;
            var p = Vector3.Lerp(entry, Center, Mathf.Clamp01(t));
            p.y = 0.32f;
            held.T.position = p;
            if (t < 1f) return;

            for (int i = 0; i < 4; i++)
            {
                var d = (Dir)((next + i) % 4);
                if (d == heldFrom) continue;
                if (Push(held.Type, Origin, Origin + d.Offset(), held))
                {
                    held = null;
                    next = ((int)d + 1) % 4;
                    return;
                }
            }
        }

        public override void Cleanup()
        {
            ItemPool.Release(held);
            held = null;
        }

        public override void Describe(List<(string, string)> lines)
        {
            lines.Add(("Mode", "Round-robin"));
        }
    }
}
