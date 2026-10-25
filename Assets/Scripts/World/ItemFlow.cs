using System.Collections.Generic;
using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>
    /// Crystal logistics. Miners dig crystals of their patch's ore (until it runs dry) and push them onto an adjacent
    /// belt whose input faces the miner. Items ride belts keeping their spacing and queue when blocked. At the end of
    /// a belt they pass to the next belt (head-on, or merging in from its side), into a Splitter (handed out in turn
    /// to its output belts), into a Refiner (processed, then sent out), into a tower (burnt for a timed boost, see
    /// Tower), or into the Core, which sells them for gold. A Refiner holding two different raw ores fuses them into
    /// an alloy, worth far more than either.
    /// </summary>
    [RequireComponent(typeof(IsoGrid), typeof(BuildController))]
    public class ItemFlow : MonoBehaviour
    {
        [Tooltip("Belt speed in tiles per second.")]
        public float BeltSpeed = 1.4f;
        [Tooltip("Minimum distance between items on a belt, in tiles.")]
        public float Spacing = 0.34f;
        [Tooltip("Crystals per second for each ore tile under a Miner.")]
        public float MinerRatePerOre = 0.15f;
        [Tooltip("Mine Shafts dig this many times faster than Miners.")]
        public float MineShaftMultiplier = 2f;
        [Tooltip("Crystals a miner holds when it has nowhere to put them.")]
        public int MinerStorage = 5;
        [Tooltip("Seconds a Refiner takes per crystal.")]
        public float RefineTime = 1.2f;
        [Tooltip("Refined crystals sell for this many times the raw price.")]
        public float RefinedMultiplier = 2.5f;
        [Tooltip("An alloy sells for this many times its two ores' raw prices together.")]
        public float AlloyMultiplier = 3.5f;
        [Tooltip("Extra crystals banked for an alloy.")]
        public int AlloyCrystalBonus = 3;

        /// <summary>Crystals sold to the Core so far (for stats/playtests).</summary>
        public int Delivered { get; private set; }
        public int DeliveredRefined { get; private set; }
        public int DeliveredAlloys { get; private set; }
        /// <summary>Crystals burnt by towers for boosts.</summary>
        public int FedToTowers { get; private set; }
        public int GoldFromOre { get; private set; }
        /// <summary>Items merged onto a belt from its side so far (for stats/playtests).</summary>
        public int SideLoads { get; private set; }

        IsoGrid map;
        BuildController build;
        readonly List<Conveyor> outputScratch = new List<Conveyor>();

        public int ValueOf(BeltItem item) => item.Alloy
            ? Mathf.RoundToInt((IsoGrid.OreTypeOf(item.Kind).Value + IsoGrid.OreTypeOf(item.AlloyKind).Value) * AlloyMultiplier)
            : Mathf.RoundToInt(IsoGrid.OreTypeOf(item.Kind).Value * (item.Refined ? RefinedMultiplier : 1f));

        void Start()
        {
            map = GetComponent<IsoGrid>();
            build = GetComponent<BuildController>();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            foreach (var b in build.Buildings)
            {
                if (b is Miner m) Mine(m, dt);
                else if (b is Splitter s) TickSplitter(s);
                else if (b is Refiner r) TickRefiner(r, dt);
            }
            foreach (var c in build.Conveyors) Move(c, dt);
        }

        // ---------------------------------------------------------------- producers

        void Mine(Miner m, float dt)
        {
            int oreCells = 0;
            foreach (var c in m.Cells)
                if (map.IsOre(c)) oreCells++;

            float rate = oreCells * MinerRatePerOre * (m.Def.Id == "MineShaft" ? MineShaftMultiplier : 1f) * Run.MinerMultiplier;
            m.SetWorking(m.Stored.Count < MinerStorage && rate > 0f);
            if (m.Stored.Count < MinerStorage && rate > 0f)
            {
                m.Timer += dt * rate;
                while (m.Timer >= 1f && m.Stored.Count < MinerStorage)
                {
                    m.Timer -= 1f;
                    // Take turns between the patches under the miner.
                    for (int i = 0; i < m.Patches.Count; i++)
                    {
                        var patch = m.Patches[(m.NextPatch + i) % m.Patches.Count];
                        if (!map.MineFrom(patch)) continue;
                        m.Stored.Enqueue(patch.Kind);
                        Sfx.Play(Sfx.Lib.CrystalMined, m.Center);
                        m.NextPatch = (m.NextPatch + i + 1) % m.Patches.Count;
                        break;
                    }
                }
            }
            if (m.Stored.Count == 0) return;
            var belt = NextOutput(m, ref m.NextOutput);
            if (belt == null) return;
            var item = new BeltItem { Kind = m.Stored.Dequeue() };
            item.View = NewItemView(item);
            Launch(belt, item);
        }

        void TickSplitter(Splitter s)
        {
            if (s.Buffer.Count == 0) return;
            var belt = NextOutput(s, ref s.NextOutput);
            if (belt == null) return;
            Launch(belt, s.Buffer.Dequeue());
            s.PlayEffect();
        }

        void TickRefiner(Refiner r, float dt)
        {
            bool refining = r.Input.Count > 0 && r.Output.Count < Refiner.Capacity;
            r.SetWorking(refining);
            if (refining)
            {
                r.Progress += dt / Mathf.Max(0.05f, RefineTime);
                if (r.Progress >= 1f)
                {
                    r.Progress = 0f;
                    var item = r.Input.Dequeue();
                    item.Refined = true;
                    // A different raw ore waiting too: fuse the two into an alloy.
                    BeltItem partner = null;
                    foreach (var other in r.Input)
                        if (other.Kind != item.Kind) { partner = other; break; }
                    if (partner != null)
                    {
                        var rest = new List<BeltItem>(r.Input);
                        rest.Remove(partner);
                        r.Input.Clear();
                        foreach (var x in rest) r.Input.Enqueue(x);
                        bool richer = IsoGrid.OreTypeOf(partner.Kind).Value > IsoGrid.OreTypeOf(item.Kind).Value;
                        item.AlloyKind = richer ? item.Kind : partner.Kind;
                        item.Kind = richer ? partner.Kind : item.Kind;
                        item.Alloy = true;
                        Destroy(partner.View);
                        Sfx.Play(Sfx.Lib.AlloyFused, r.Center);
                        Fx.Burst(r.Center + r.Up * 0.6f, Color.Lerp(IsoGrid.OreTypeOf(item.Kind).Color, IsoGrid.OreTypeOf(item.AlloyKind).Color, 0.5f), 0.7f);
                    }
                    var old = item.View;
                    item.View = NewItemView(item);
                    item.View.transform.SetParent(r.transform, false);
                    item.View.SetActive(false);
                    Destroy(old);
                    r.Output.Enqueue(item);
                }
            }
            if (r.Output.Count == 0) return;
            var belt = NextOutput(r, ref r.NextOutput);
            if (belt != null) Launch(belt, r.Output.Dequeue());
        }

        /// <summary>The next belt (round-robin) leading away from the building that has room at its start.</summary>
        Conveyor NextOutput(Building b, ref int next)
        {
            var outputs = OutputBelts(b);
            for (int i = 0; i < outputs.Count; i++)
            {
                var belt = outputs[(next + i) % outputs.Count];
                if (!HasRoomAtStart(belt)) continue;
                next = (next + i + 1) % outputs.Count;
                return belt;
            }
            return null;
        }

        /// <summary>Belts next to the building whose input side faces it.</summary>
        public List<Conveyor> OutputBelts(Building b)
        {
            outputScratch.Clear();
            foreach (var cell in b.Cells)
                for (int d = 0; d < map.Sides(cell); d++)
                {
                    var c = map.Neighbour(cell, d);
                    if (b.Covers(c)) continue;
                    if (map.BuildingAt(c) is Conveyor belt && b.Covers(belt.InCell) && !outputScratch.Contains(belt)) outputScratch.Add(belt);
                }
            return outputScratch;
        }

        /// <summary>The crystal model of the item's ore (refined or raw), or a small cube if the library has none.</summary>
        static GameObject NewItemView(BeltItem item)
        {
            var type = IsoGrid.OreTypeOf(item.Kind);
            var prefab = item.Refined && type.RefinedItem != null ? type.RefinedItem : type.Item;
            GameObject go;
            if (prefab != null)
            {
                go = Instantiate(prefab);
                PlanetArt.StripColliders(go);
                var library = GameArt.Library;
                go.transform.localScale = Vector3.one * (library != null ? library.ItemScale : 0.5f) * (item.Alloy ? 1.4f : 1f);
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(go.GetComponent<Collider>());
                go.transform.localScale = Vector3.one * 0.25f;
            }
            go.name = "Crystal";
            return go;
        }

        /// <summary>Puts an item (new, or coming out of a building) at the start of a belt.</summary>
        void Launch(Conveyor belt, BeltItem item)
        {
            item.Progress = 0f;
            item.View.SetActive(true);
            item.View.transform.SetParent(belt.transform, true);
            belt.Items.Add(item);
            Place(belt, item);
        }

        // ---------------------------------------------------------------- belts

        bool HasRoomAtStart(Conveyor belt) =>
            belt.Items.Count == 0 || belt.Items[belt.Items.Count - 1].Progress >= Spacing;

        void Move(Conveyor belt, float dt)
        {
            var items = belt.Items;
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                // Front item may run to the end of the tile; the rest keep their distance from the one ahead.
                float limit = i == 0 ? 1f : items[i - 1].Progress - Spacing;
                item.Progress = Mathf.Min(item.Progress + BeltSpeed * Run.BeltMultiplier * dt, Mathf.Max(item.Progress, limit));

                if (i == 0 && item.Progress >= 1f && Hand(belt, item))
                {
                    items.RemoveAt(0);
                    i--;
                    continue;
                }
                Place(belt, item);
            }
        }

        /// <summary>Pass the front item on: to a belt taking input from this one, into a building, or to the Core.</summary>
        bool Hand(Conveyor belt, BeltItem item)
        {
            var target = map.BuildingAt(belt.OutCell);
            if (target is Conveyor next)
            {
                if (next.InCell == belt.Cell)
                {
                    // Head-on: respect spacing across the tile boundary too.
                    if (next.Items.Count > 0 && next.Items[next.Items.Count - 1].Progress < Spacing + (item.Progress - 1f)) return Wait(item);
                    item.Progress -= 1f;
                    item.View.transform.SetParent(next.transform, true);
                    next.Items.Add(item);
                    Place(next, item);
                    return true;
                }
                if (next.OutCell == belt.Cell) return Wait(item); // can't feed a belt that flows back into this one
                return SideLoad(next, item) || Wait(item);
            }
            if (target == null) return Wait(item);
            if (target.IsCore)
            {
                int value = Mathf.RoundToInt(ValueOf(item) * Run.IncomeMultiplier * Meta.CoreSellMultiplier);
                Destroy(item.View);
                build.Earn(value, build.Core.transform.position);
                build.Core.PlayEffect();
                var settings = RunSettings.Instance;
                Run.AddCrystals(settings.CrystalsPerItem + (item.Refined ? settings.RefinedCrystalBonus : 0) + (item.Alloy ? AlloyCrystalBonus : 0) + Run.CrystalBonus);
                Delivered++;
                if (item.Refined) DeliveredRefined++;
                if (item.Alloy) DeliveredAlloys++;
                GoldFromOre += value;
                // A coin pops off the Core, and the till rings.
                var up = build.Core.Up;
                Fx.Spawn(build.Core.Center + up * 1.2f, UIStyle.GoldColor, 0.35f, 0.15f, 0.6f, up * 1.6f + Random.insideUnitSphere * 0.4f);
                Sfx.Play(Sfx.Lib.CrystalSold, build.Core.Center);
                return true;
            }
            if (!target.TryAccept(item)) return Wait(item);
            if (target is Tower)
            {
                // Burnt for its boost.
                FedToTowers++;
                Sfx.Play(Sfx.Lib.CrystalFedToTower, target.Center);
                Fx.Burst(target.Center + target.Up * 0.5f, IsoGrid.OreTypeOf(item.Kind).Color, 0.4f);
                Destroy(item.View);
                return true;
            }
            // Inside a building: keep the object but hide it until it comes out again.
            item.View.SetActive(false);
            item.View.transform.SetParent(target.transform, true);
            return true;
        }

        /// <summary>
        /// Merges an item onto a belt from its side, halfway along the tile, if no item there is closer than the
        /// spacing. Keeps the belt's items ordered front to back.
        /// </summary>
        bool SideLoad(Conveyor next, BeltItem item)
        {
            const float at = 0.5f;
            var items = next.Items;
            int index = items.Count;
            for (int i = 0; i < items.Count; i++)
            {
                if (Mathf.Abs(items[i].Progress - at) < Spacing) return false;
                if (items[i].Progress < at) { index = i; break; }
            }
            item.Progress = at;
            item.View.transform.SetParent(next.transform, true);
            items.Insert(index, item);
            Place(next, item);
            SideLoads++;
            return true;
        }

        static bool Wait(BeltItem item)
        {
            item.Progress = 1f; // dead end or full: wait at the edge
            return false;
        }

        /// <summary>Puts the item on the belt's centre line, following curves and ramps, standing upright.</summary>
        void Place(Conveyor belt, BeltItem item)
        {
            float s = Mathf.Clamp01(item.Progress);
            var uv = ConveyorArt.CenterLine(belt.In, belt.Out, s);
            float lift = Mathf.Lerp(belt.InLift, belt.OutLift, s);
            var up = map.Up(belt.Cell);
            // The belt sprite lies with its +z into the ground; turning it -90 degrees about x stands +y up.
            item.View.transform.SetPositionAndRotation(map.CellPoint(belt.Cell, uv, belt.Level + lift) + up * ConveyorArt.SurfaceHeight,
                belt.transform.rotation * Quaternion.Euler(-90f, 0f, 0f));
        }
    }
}
