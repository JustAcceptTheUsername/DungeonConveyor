using System;
using System.Collections.Generic;
using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>A chain of goals shown under "Protect the Core". Completing one pays a reward and reveals the next.</summary>
    public class Objectives : MonoBehaviour
    {
        public class Goal
        {
            public string Text;
            public Func<int> Progress;
            public int Target;
            public Cost Reward;
        }

        readonly List<Goal> goals = new List<Goal>();
        int index;

        public Goal Current => index < goals.Count ? goals[index] : null;

        void Awake()
        {
            Func<ResourceType, Func<int>> collected = t => () => Game.Bank.TotalCollected(t);
            Func<int> wave = () => Mathf.Max(0, Game.Waves.Wave - 1); // waves fully survived (roughly)
            Func<Type, Func<int>> count = t => () => Game.Build.Count(t);

            goals.Add(new Goal { Text = "Build Miners", Progress = count(typeof(Miner)), Target = 2, Reward = new Cost(100) });
            goals.Add(new Goal { Text = "Collect Gold", Progress = collected(ResourceType.Gold), Target = 300, Reward = new Cost(150) });
            goals.Add(new Goal { Text = "Build Turrets", Progress = count(typeof(Turret)), Target = 3, Reward = new Cost(150, iron: 30) });
            goals.Add(new Goal { Text = "Collect Iron", Progress = collected(ResourceType.Iron), Target = 200, Reward = new Cost(200) });
            goals.Add(new Goal { Text = "Survive Waves", Progress = wave, Target = 5, Reward = new Cost(400, 20) });
            goals.Add(new Goal { Text = "Collect Crystal", Progress = collected(ResourceType.Crystal), Target = 300, Reward = new Cost(400) });
            goals.Add(new Goal { Text = "Collect Gold", Progress = collected(ResourceType.Gold), Target = 2500, Reward = new Cost(800, 50) });
            goals.Add(new Goal { Text = "Survive Waves", Progress = wave, Target = 10, Reward = new Cost(1000, 100, 50) });
            goals.Add(new Goal { Text = "Collect Amethyst", Progress = collected(ResourceType.Amethyst), Target = 500, Reward = new Cost(1500) });
            goals.Add(new Goal { Text = "Survive Waves", Progress = wave, Target = 20, Reward = new Cost(3000, 200, 200, 200) });
            goals.Add(new Goal { Text = "Survive Waves", Progress = wave, Target = 30, Reward = new Cost(5000) });
        }

        void Update()
        {
            var g = Current;
            if (g == null || g.Progress() < g.Target) return;
            Game.Bank.Refund(g.Reward);
            Game.Alerts.Push($"Objective complete: +{g.Reward.Gold} gold");
            index++;
        }
    }
}
