using System.Collections.Generic;
using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>
    /// Crystal logistics. Miners dig crystals of their patch's ore (until it runs dry) and push them onto an adjacent
    /// belt whose input faces the miner. Items ride belts keeping their spacing and queue when blocked. At the end of
    /// a belt they pass to the next belt (head-on, or merging in from its side), into a Splitter (handed out in turn
    /// to its output belts), into a Refiner (processed, then sent out), or into the Core, which sells them for gold.
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

        /// <summary>Crystals sold to the Core so far (for stats/playtests).</summary>
        public int Delivered { get; private set; }
        public int DeliveredRefined { get; private set; }
        public int GoldFromOre { get; private set; }
        /// <summary>Items merged onto a belt from its side so far (for stats/playtests).</summary>
        public int SideLoads { get; private set; }

        IsoGrid map;
        BuildController build;
        Material material;
        Vector3 xStep, yStep;
        readonly List<Conveyor> outputScratch = new List<Conveyor>();

        public int ValueOf(BeltItem item) => Mathf.RoundToInt(IsoGrid.OreTypeOf(item.Kind).Value * (item.Refined ? RefinedMultiplier : 1f));

        void Start()
        {
            map = GetComponent<IsoGrid>();
            build = GetComponent<BuildController>();
            material = GetComponent<MapView>().SpriteMaterial;
            xStep = map.CellCenter(Vector2Int.right) - map.CellCenter(Vector2Int.zero);
            yStep = map.CellCenter(Vector2Int.up) - map.CellCenter(Vector2Int.zero);
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
            for (int x = 0; x < m.Size.x; x++)
                for (int y = 0; y < m.Size.y; y++)
                    if (map.IsOre(m.Origin + new Vector2Int(x, y))) oreCells++;

            float rate = oreCells * MinerRatePerOre * (m.Def.Id == "MineShaft" ? MineShaftMultiplier : 1f);
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
                        m.NextPatch = (m.NextPatch + i + 1) % m.Patches.Count;
                        break;
                    }
                }
            }
            if (m.Stored.Count == 0) return;
            var belt = NextOutput(m, ref m.NextOutput);
            if (belt == null) return;
            var item = new BeltItem { Kind = m.Stored.Dequeue() };
            item.Renderer = NewItemRenderer(item);
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
                    item.Renderer.sprite = ItemSprite(item);
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
            for (int x = -1; x <= b.Size.x; x++)
                for (int y = -1; y <= b.Size.y; y++)
                {
                    var c = b.Origin + new Vector2Int(x, y);
                    if (b.Covers(c)) continue;
                    if (map.BuildingAt(c) is Conveyor belt && b.Covers(belt.InCell)) outputScratch.Add(belt);
                }
            return outputScratch;
        }

        SpriteRenderer NewItemRenderer(BeltItem item)
        {
            var sr = new GameObject("Crystal").AddComponent<SpriteRenderer>();
            if (material != null) sr.sharedMaterial = material;
            sr.sprite = ItemSprite(item);
            return sr;
        }

        static Sprite ItemSprite(BeltItem item)
        {
            var art = GameArt.Ore(item.Kind);
            return art == null ? null : item.Refined ? art.Refined : art.Item;
        }

        /// <summary>Puts an item (new, or coming out of a building) at the start of a belt.</summary>
        void Launch(Conveyor belt, BeltItem item)
        {
            item.Progress = 0f;
            item.Renderer.enabled = true;
            item.Renderer.transform.SetParent(belt.transform, true);
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
                item.Progress = Mathf.Min(item.Progress + BeltSpeed * dt, Mathf.Max(item.Progress, limit));

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
                    item.Renderer.transform.SetParent(next.transform, true);
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
                int value = Mathf.RoundToInt(ValueOf(item) * Run.IncomeMultiplier);
                Destroy(item.Renderer.gameObject);
                build.Earn(value, build.Core.transform.position);
                build.Core.PlayEffect();
                var settings = RunSettings.Instance;
                Run.AddCrystals(settings.CrystalsPerItem + (item.Refined ? settings.RefinedCrystalBonus : 0));
                Delivered++;
                if (item.Refined) DeliveredRefined++;
                GoldFromOre += value;
                return true;
            }
            if (!target.TryAccept(item)) return Wait(item);
            // Inside a building: keep the object but hide it until it comes out again.
            item.Renderer.enabled = false;
            item.Renderer.transform.SetParent(target.transform, true);
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
            item.Renderer.transform.SetParent(next.transform, true);
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

        /// <summary>Puts the item on the belt's centre line, following curves and ramps.</summary>
        void Place(Conveyor belt, BeltItem item)
        {
            float s = Mathf.Clamp01(item.Progress);
            var uv = ConveyorArt.CenterLine(belt.In, belt.Out, s);
            float lift = Mathf.Lerp(belt.InLift, belt.OutLift, s) * IsoGrid.StepHeight;
            item.Renderer.transform.position = map.CellCenter(belt.Cell) + xStep * (uv.x - 0.5f) + yStep * (uv.y - 0.5f)
                + Vector3.up * (belt.Level * IsoGrid.StepHeight + lift + ConveyorArt.SurfaceHeight);
            item.Renderer.sortingOrder = map.SortOrder(belt.Cell) + 2;
        }
    }
}
