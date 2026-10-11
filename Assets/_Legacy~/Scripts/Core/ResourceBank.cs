using System.Collections.Generic;
using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>Global resource stockpile with per-minute income tracking.</summary>
    public class ResourceBank
    {
        const float RateWindow = 60f;

        readonly int[] amounts = new int[ResourceInfo.Count];
        readonly int[] totalCollected = new int[ResourceInfo.Count];
        readonly Queue<(float time, int type, int amount)> history = new Queue<(float, int, int)>();
        readonly int[] windowSums = new int[ResourceInfo.Count];

        public int this[ResourceType t] => amounts[(int)t];
        public int TotalCollected(ResourceType t) => totalCollected[(int)t];

        public ResourceBank(Cost start)
        {
            for (int i = 0; i < ResourceInfo.Count; i++) amounts[i] = start[i];
        }

        /// <summary>Income (mined/delivered) — counts toward the per-minute rate.</summary>
        public void Deliver(ResourceType t, int amount)
        {
            amounts[(int)t] += amount;
            totalCollected[(int)t] += amount;
            history.Enqueue((Time.time, (int)t, amount));
            windowSums[(int)t] += amount;
        }

        /// <summary>Bonus income (kills, objectives) — not counted toward the rate.</summary>
        public void Grant(ResourceType t, int amount) => amounts[(int)t] += amount;

        public bool CanAfford(Cost c)
        {
            for (int i = 0; i < ResourceInfo.Count; i++)
                if (amounts[i] < c[i]) return false;
            return true;
        }

        public bool TrySpend(Cost c)
        {
            if (!CanAfford(c)) return false;
            for (int i = 0; i < ResourceInfo.Count; i++) amounts[i] -= c[i];
            return true;
        }

        public bool TrySpend(ResourceType t, int amount)
        {
            if (amounts[(int)t] < amount) return false;
            amounts[(int)t] -= amount;
            return true;
        }

        public void Refund(Cost c)
        {
            for (int i = 0; i < ResourceInfo.Count; i++) amounts[i] += c[i];
        }

        public int PerMinute(ResourceType t)
        {
            Trim();
            return windowSums[(int)t];
        }

        void Trim()
        {
            float cutoff = Time.time - RateWindow;
            while (history.Count > 0 && history.Peek().time < cutoff)
            {
                var h = history.Dequeue();
                windowSums[h.type] -= h.amount;
            }
        }
    }
}
