using System.Collections.Generic;
using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>Extracts ore from the deposits under its footprint and pushes it out of its front side.</summary>
    public class Miner : Building
    {
        const float SecondsPerOreTile = 4f;
        const int BufferSize = 6;

        readonly List<ResourceType> veins = new List<ResourceType>();
        readonly Queue<ResourceType> buffer = new Queue<ResourceType>();
        Transform spin;
        float timer;
        int nextVein;

        public float Interval => veins.Count == 0 ? float.PositiveInfinity : SecondsPerOreTile / veins.Count / (1f + 0.5f * (Level - 1));
        public float PerMinute => 60f / Interval;

        protected override void OnCreated()
        {
            foreach (var c in Cells())
            {
                int ore = Map.OreAt(c);
                if (ore >= 0) veins.Add((ResourceType)ore);
            }
            spin = Visual.Find("Spin");
        }

        protected override void Update()
        {
            base.Update();
            if (veins.Count == 0) return;

            if (buffer.Count < BufferSize)
            {
                timer += Time.deltaTime;
                if (timer >= Interval)
                {
                    timer -= Interval;
                    buffer.Enqueue(veins[nextVein++ % veins.Count]);
                }
            }
            if (spin != null && buffer.Count < BufferSize) spin.Rotate(0, 360f * Time.deltaTime, 0, Space.Self);

            if (buffer.Count > 0)
                foreach (var (from, to) in Edge(Facing))
                    if (Push(buffer.Peek(), from, to)) { buffer.Dequeue(); break; }
        }

        public override void Describe(List<(string, string)> lines)
        {
            lines.Add(("Ore tiles", veins.Count.ToString()));
            lines.Add(("Output", $"{PerMinute:0}/min"));
            lines.Add(("Buffer", $"{buffer.Count}/{BufferSize}"));
        }
    }
}
