using System;
using System.Collections.Generic;
using UnityEngine;

namespace DungeonConveyor
{
    public enum BuildTab { Build, Machines, Defenses, Utilities, Resources }

    public class BuildingDef
    {
        public string Id, Name, Description;
        public Type Type;
        public Cost Cost;
        public int Size = 1;
        public float Health = 100;
        public bool Rotatable = true;
        public bool BlocksEnemies = true;
        public bool RequiresOre;
        public int MaxLevel = 3;
        public float Range;            // shown as a ring while placing / selected
        public BuildTab Tab;
        public Texture2D Icon;
    }

    public static class Defs
    {
        public static BuildingDef Conveyor, Splitter, Lift, Miner, Turret, Flamethrower, Wall, Collector, Core;
        public static List<BuildingDef> Buildable;

        public static void Init()
        {
            Conveyor = new BuildingDef
            {
                Id = "Conveyor", Name = "Conveyor", Type = typeof(Conveyor), Cost = new Cost(10), Health = 100,
                BlocksEnemies = false, Tab = BuildTab.Utilities,
                Description = "Moves items. Drag to lay a line.",
            };
            Splitter = new BuildingDef
            {
                Id = "Splitter", Name = "Splitter", Type = typeof(Splitter), Cost = new Cost(60), Health = 120,
                BlocksEnemies = false, Rotatable = false, Tab = BuildTab.Utilities,
                Description = "Splits items evenly to all other sides.",
            };
            Lift = new BuildingDef
            {
                Id = "Lift", Name = "Lift", Type = typeof(Lift), Cost = new Cost(80, iron: 10), Health = 150,
                BlocksEnemies = false, Tab = BuildTab.Utilities,
                Description = "Launches items over up to 5 tiles to another Lift facing the same way.",
            };
            Miner = new BuildingDef
            {
                Id = "Miner", Name = "Miner", Type = typeof(Miner), Cost = new Cost(120), Size = 2, Health = 250,
                RequiresOre = true, Tab = BuildTab.Machines,
                Description = "Place over ore. Outputs on the arrow side.",
            };
            Turret = new BuildingDef
            {
                Id = "Turret", Name = "Turret", Type = typeof(Turret), Cost = new Cost(150, iron: 20), Health = 220,
                Rotatable = false, Range = DungeonConveyor.Turret.RangeForLevel(1), Tab = BuildTab.Defenses,
                Description = "Shoots enemies. Uses ore as ammo (or Iron from storage).",
            };
            Flamethrower = new BuildingDef
            {
                Id = "Flamethrower", Name = "Flamethrower", Type = typeof(Flamethrower), Cost = new Cost(250, crystal: 20), Health = 260,
                Rotatable = false, Range = DungeonConveyor.Flamethrower.RangeForLevel(1), Tab = BuildTab.Defenses,
                Description = "Burns everything in a cone. Uses ore as fuel (or Iron from storage).",
            };
            Wall = new BuildingDef
            {
                Id = "Wall", Name = "Wall", Type = typeof(Wall), Cost = new Cost(25), Health = 500,
                Rotatable = false, Tab = BuildTab.Defenses,
                Description = "Cheap and tough. Enemies must chew through it.",
            };
            Collector = new BuildingDef
            {
                Id = "Collector", Name = "Collector", Type = typeof(Collector), Cost = new Cost(200, iron: 20), Health = 300,
                Rotatable = false, Tab = BuildTab.Machines,
                Description = "Remote drop-off: items delivered here go to storage.",
            };
            Core = new BuildingDef
            {
                Id = "Core", Name = "Core", Type = typeof(CoreBuilding), Cost = new Cost(0), Size = 3, Health = 2500,
                Rotatable = false, MaxLevel = 5, Range = CoreBuilding.RangeForLevel(1),
                Description = "Protect it! Accepts items from every side.",
            };

            Buildable = new List<BuildingDef> { Conveyor, Splitter, Lift, Miner, Turret, Flamethrower, Wall, Collector };
        }

        public static bool InTab(BuildingDef d, BuildTab tab) => tab == BuildTab.Build || d.Tab == tab ||
            (tab == BuildTab.Machines && d.Tab == BuildTab.Utilities);
    }
}
