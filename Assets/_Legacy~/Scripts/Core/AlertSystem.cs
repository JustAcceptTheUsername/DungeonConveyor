using System.Collections.Generic;
using UnityEngine;

namespace DungeonConveyor
{
    public enum AlertLevel { Info, Warning, Danger }

    public class AlertSystem : MonoBehaviour
    {
        public struct Alert
        {
            public string Text;
            public AlertLevel Level;
        }

        const float MessageLife = 5f;

        public readonly List<Alert> Current = new List<Alert>();
        readonly List<(string text, float until)> messages = new List<(string, float)>();
        float timer;

        /// <summary>Show a transient message (wave start, objective complete...).</summary>
        public void Push(string text)
        {
            messages.Add((text, Time.unscaledTime + MessageLife));
            Refresh();
        }

        void Update()
        {
            timer -= Time.unscaledDeltaTime;
            if (timer > 0) return;
            timer = 0.5f;
            Refresh();
        }

        void Refresh()
        {
            Current.Clear();
            messages.RemoveAll(m => m.until < Time.unscaledTime);
            foreach (var m in messages) Current.Add(new Alert { Text = m.text, Level = AlertLevel.Info });

            var seen = new HashSet<string>();
            foreach (var b in Game.Build.All)
            {
                var a = b.Alert;
                if (a != null && seen.Add(a))
                    Current.Add(new Alert { Text = a, Level = a.StartsWith("Conveyor") ? AlertLevel.Danger : AlertLevel.Warning });
            }

            var core = Game.Core;
            if (core != null)
            {
                if (Time.time - core.LastDamageTime < 2f)
                    Current.Add(new Alert { Text = "Core Under Attack!", Level = AlertLevel.Danger });
                else
                {
                    bool breach = false;
                    foreach (var e in Enemy.All)
                        if (e.IsAttacking || core.DistanceTo(e.transform.position) < 9f) { breach = true; break; }
                    if (breach) Current.Add(new Alert { Text = "Enemy Breach!", Level = AlertLevel.Warning });
                }
            }
            if (Current.Count > 5) Current.RemoveRange(5, Current.Count - 5);
        }
    }
}
