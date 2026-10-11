using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DungeonConveyor
{
    /// <summary>Placement, selection, selling and upgrading of buildings.</summary>
    public class BuildController : MonoBehaviour
    {
        public readonly List<Building> All = new List<Building>();

        public BuildingDef Placing { get; private set; }
        public Dir Facing { get; private set; } = Dir.E;
        public Building Selected { get; private set; }
        public Vector2Int? HoverCell { get; private set; }
        public string PlaceError { get; private set; }

        Transform buildingRoot;
        Transform ghost;
        BuildingDef ghostDef;
        Renderer[] ghostRenderers;
        Transform footprint, rangeRing, selectFrame;
        MeshRenderer footprintRenderer;
        static readonly Dictionary<int, Mesh> frameMeshes = new Dictionary<int, Mesh>();

        bool dragging;
        Vector2Int lastDragCell;

        void Awake()
        {
            buildingRoot = new GameObject("Buildings").transform;
            buildingRoot.SetParent(transform, false);

            footprint = MakeQuad("Footprint", Art.CellOk).transform;
            footprintRenderer = footprint.GetComponent<MeshRenderer>();
            rangeRing = MakeDisc("Range").transform;
            selectFrame = new GameObject("SelectFrame").transform;
            selectFrame.SetParent(transform, false);
            selectFrame.gameObject.AddComponent<MeshFilter>();
            selectFrame.gameObject.AddComponent<MeshRenderer>().sharedMaterial = Art.CellOk;
            footprint.gameObject.SetActive(false);
            rangeRing.gameObject.SetActive(false);
            selectFrame.gameObject.SetActive(false);
        }

        public int Count(Type t)
        {
            int n = 0;
            foreach (var b in All) if (t.IsInstanceOfType(b)) n++;
            return n;
        }

        // ---------------------------------------------------------------- modes

        public void BeginPlacing(BuildingDef def)
        {
            Placing = def;
            Select(null);
            dragging = false;
        }

        public void Cancel()
        {
            Placing = null;
            dragging = false;
        }

        public void Select(Building b)
        {
            Selected = b;
        }

        // ---------------------------------------------------------------- placement rules

        public static Vector2Int OriginFor(BuildingDef def, Vector3 world) =>
            new Vector2Int(Mathf.FloorToInt(world.x - def.Size * 0.5f + 0.5f), Mathf.FloorToInt(world.z - def.Size * 0.5f + 0.5f));

        public bool CanPlace(BuildingDef def, Vector2Int origin, out string error)
        {
            var map = Game.Map;
            bool ore = false;
            for (int x = 0; x < def.Size; x++)
                for (int y = 0; y < def.Size; y++)
                {
                    var c = new Vector2Int(origin.x + x, origin.y + y);
                    if (!map.IsFloor(c)) { error = "Blocked"; return false; }
                    if (map.GetBuilding(c) != null) { error = "Occupied"; return false; }
                    if (map.OreAt(c) >= 0) ore = true;
                }
            if (def.RequiresOre && !ore) { error = "Needs ore underneath"; return false; }
            if (!Game.Bank.CanAfford(def.Cost)) { error = "Not enough resources"; return false; }
            error = null;
            return true;
        }

        public Building Place(BuildingDef def, Vector2Int origin, Dir facing, bool free = false)
        {
            if (!free && !Game.Bank.TrySpend(def.Cost)) return null;
            var b = Building.Create(def, origin, facing, buildingRoot);
            All.Add(b);
            Game.Map.SetBuilding(b, true);
            if (b is CoreBuilding core) Game.Core = core;
            RefreshAround(b);
            return b;
        }

        void RefreshAround(Building b)
        {
            var seen = new HashSet<Building>();
            for (int x = -1; x <= b.Size; x++)
                for (int y = -1; y <= b.Size; y++)
                {
                    var n = Game.Map.GetBuilding(new Vector2Int(b.Origin.x + x, b.Origin.y + y));
                    if (n != null && seen.Add(n)) n.OnNeighborChanged();
                }
        }

        void Remove(Building b)
        {
            All.Remove(b);
            Game.Map.SetBuilding(b, false);
            b.Cleanup();
            b.MarkDestroyed();
            if (Selected == b) Select(null);
            RefreshAround(b);
            Destroy(b.gameObject);
        }

        public void Sell(Building b)
        {
            if (b == null || b is CoreBuilding) return;
            var refund = b.TotalSpent * 0.5f;
            Game.Bank.Refund(refund);
            Effects.Text(b.Center + Vector3.up, "+" + refund.Gold, ResourceInfo.Colors[0]);
            Remove(b);
        }

        public void Upgrade(Building b)
        {
            if (b == null || !b.CanUpgrade || !Game.Bank.TrySpend(b.UpgradeCost)) return;
            b.Upgrade();
            Effects.Sparks(b.Center + Vector3.up * 0.5f, new Color(1f, 0.8f, 0.3f), 20);
        }

        /// <summary>Called when a building's health reaches zero.</summary>
        public void DestroyBuilding(Building b)
        {
            if (b.IsDestroyed) return;
            Effects.Explosion(b.Center);
            if (b is CoreBuilding)
            {
                b.MarkDestroyed();
                Game.GameOver();
                return;
            }
            Remove(b);
        }

        // ---------------------------------------------------------------- input

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || mouse == null) return;

            HandleHotkeys(kb);
            if (Game.IsGameOver) { Cancel(); UpdateVisuals(); return; }

            var mp = mouse.position.ReadValue();
            bool overUI = HUD.IsPointerOverUI(mp);
            var ground = overUI ? null : Game.Cam.GroundPoint(mp);
            HoverCell = ground.HasValue ? GridMap.WorldToCell(ground.Value) : (Vector2Int?)null;

            if (Placing != null)
            {
                if (mouse.rightButton.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame) { Cancel(); UpdateVisuals(); return; }
                if (kb.rKey.wasPressedThisFrame) Facing = Facing.Right();

                if (ground.HasValue)
                {
                    var origin = OriginFor(Placing, ground.Value);
                    CanPlace(Placing, origin, out var err);
                    PlaceError = err;

                    if (mouse.leftButton.wasPressedThisFrame)
                    {
                        if (Placing.Size == 1 && (Placing == Defs.Conveyor || Placing == Defs.Wall))
                        {
                            dragging = true;
                            lastDragCell = origin;
                            DragPlace(origin, Facing);
                        }
                        else if (CanPlace(Placing, origin, out _))
                            Place(Placing, origin, Facing);
                    }
                    else if (dragging && mouse.leftButton.isPressed && origin != lastDragCell)
                        DragTo(origin);
                }
                if (!mouse.leftButton.isPressed) dragging = false;
            }
            else
            {
                PlaceError = null;
                if (!overUI && mouse.leftButton.wasPressedThisFrame)
                    Select(HoverCell.HasValue ? Game.Map.GetBuilding(HoverCell.Value) : null);
                if (mouse.rightButton.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame) Select(null);
                if (Selected != null)
                {
                    if (kb.deleteKey.wasPressedThisFrame || kb.xKey.wasPressedThisFrame) Sell(Selected);
                    else if (kb.uKey.wasPressedThisFrame) Upgrade(Selected);
                    else if (kb.rKey.wasPressedThisFrame) Selected.SetFacing(Selected.Facing.Right());
                }
            }
            UpdateVisuals();
        }

        void HandleHotkeys(Keyboard kb)
        {
            if (kb.spaceKey.wasPressedThisFrame) Game.TogglePause();
            if (kb.fKey.wasPressedThisFrame) Game.CycleSpeed();
            if (kb.nKey.wasPressedThisFrame && !Game.IsGameOver) Game.Waves.CallEarly();
            if (Game.IsGameOver) return;
            var digits = new[] { kb.digit1Key, kb.digit2Key, kb.digit3Key, kb.digit4Key, kb.digit5Key, kb.digit6Key, kb.digit7Key, kb.digit8Key, kb.digit9Key };
            for (int i = 0; i < digits.Length && i < Defs.Buildable.Count; i++)
                if (digits[i].wasPressedThisFrame) BeginPlacing(Defs.Buildable[i]);
        }

        void DragTo(Vector2Int target)
        {
            int guard = 0;
            while (lastDragCell != target && guard++ < 64)
            {
                var delta = target - lastDragCell;
                var step = Mathf.Abs(delta.x) >= Mathf.Abs(delta.y)
                    ? new Vector2Int(Math.Sign(delta.x), 0)
                    : new Vector2Int(0, Math.Sign(delta.y));
                var dir = DirUtil.Between(lastDragCell, lastDragCell + step).Value;
                if (Placing == Defs.Conveyor)
                {
                    Facing = dir;
                    if (Game.Map.GetBuilding(lastDragCell) is Conveyor prev) prev.SetFacing(dir);
                }
                lastDragCell += step;
                DragPlace(lastDragCell, dir);
            }
        }

        void DragPlace(Vector2Int cell, Dir dir)
        {
            var existing = Game.Map.GetBuilding(cell);
            if (existing is Conveyor c && Placing == Defs.Conveyor) { c.SetFacing(dir); return; }
            if (CanPlace(Placing, cell, out _)) Place(Placing, cell, Placing == Defs.Conveyor ? dir : Facing);
        }

        // ---------------------------------------------------------------- ghost & overlays

        void UpdateVisuals()
        {
            bool showGhost = Placing != null && HoverCell.HasValue;
            if (ghostDef != Placing) RebuildGhost();

            if (showGhost)
            {
                var ground = Game.Cam.GroundPoint(Mouse.current.position.ReadValue()) ?? Vector3.zero;
                var origin = OriginFor(Placing, ground);
                bool ok = CanPlace(Placing, origin, out _);
                var center = new Vector3(origin.x + Placing.Size * 0.5f, 0, origin.y + Placing.Size * 0.5f);
                ghost.gameObject.SetActive(true);
                ghost.position = center;
                ghost.rotation = Placing.Rotatable ? Facing.Rotation() : Quaternion.identity;
                var mat = ok ? Art.GhostOk : Art.GhostBad;
                foreach (var r in ghostRenderers)
                {
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                    r.sharedMaterials = mats;
                }
                footprint.gameObject.SetActive(true);
                footprint.position = center + Vector3.up * 0.04f;
                footprint.localScale = new Vector3(Placing.Size, 1, Placing.Size);
                footprintRenderer.sharedMaterial = ok ? Art.CellOk : Art.CellBad;
                ShowRange(Placing.Range, center);
            }
            else
            {
                if (ghost != null) ghost.gameObject.SetActive(false);
                footprint.gameObject.SetActive(false);
                if (Selected == null) rangeRing.gameObject.SetActive(false);
            }

            if (Selected != null && !Selected.IsDestroyed)
            {
                selectFrame.gameObject.SetActive(true);
                selectFrame.position = Selected.Center + Vector3.up * 0.2f;
                selectFrame.GetComponent<MeshFilter>().sharedMesh = FrameMesh(Selected.Size);
                float range = Selected is Defense d ? d.Range : Selected is CoreBuilding core ? core.Range : 0f;
                if (!showGhost) ShowRange(range, Selected.Center);
            }
            else selectFrame.gameObject.SetActive(false);
        }

        void ShowRange(float range, Vector3 center)
        {
            rangeRing.gameObject.SetActive(range > 0);
            rangeRing.position = center + Vector3.up * 0.05f;
            rangeRing.localScale = new Vector3(range, 1, range);
        }

        void RebuildGhost()
        {
            if (ghost != null) Destroy(ghost.gameObject);
            ghost = null;
            ghostDef = Placing;
            if (Placing == null) return;
            ghost = BuildingVisuals.Create(Placing, transform);
            if (Placing == Defs.Conveyor)
            {
                var (mesh, _) = BuildingVisuals.BeltMesh(BuildingVisuals.BeltShape.Straight, 1);
                ghost.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                ghost.gameObject.AddComponent<MeshRenderer>();
            }
            if (Placing.Rotatable)
            {
                // Direction arrow
                var arrow = new VoxelModel("Arrow")
                    .Box(new Vector3(0, 0.05f, 0.1f), new Vector3(0.12f, 0.04f, 0.5f), Art.GhostOk)
                    .Box(new Vector3(0, 0.05f, 0.38f), new Vector3(0.3f, 0.04f, 0.1f), Art.GhostOk, "", 45f)
                    .Finish(false);
                arrow.transform.SetParent(ghost, false);
                arrow.transform.localPosition = new Vector3(0, 0, Placing.Size * 0.5f + 0.2f);
            }
            foreach (var l in ghost.GetComponentsInChildren<Light>()) l.enabled = false;
            ghostRenderers = ghost.GetComponentsInChildren<Renderer>();
            foreach (var r in ghostRenderers)
                if (r is MeshRenderer mr) mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        GameObject MakeQuad(string name, Material m)
        {
            var mb = new MeshBuilder();
            mb.Face(m, Vector3.zero, Vector3.up, Vector3.right * 0.5f, Vector3.forward * 0.5f);
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            mb.Attach(go, name, false);
            return go;
        }

        GameObject MakeDisc(string name)
        {
            const int segments = 64;
            var verts = new List<Vector3> { Vector3.zero };
            var tris = new List<int>();
            for (int i = 0; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                verts.Add(new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)));
                if (i > 0) { tris.Add(0); tris.Add(i); tris.Add(i + 1); }
            }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = Art.RangeRing;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        static Mesh FrameMesh(int size)
        {
            if (frameMeshes.TryGetValue(size, out var m) && m != null) return m;
            var mb = new MeshBuilder();
            float h = size * 0.5f, t = 0.08f;
            mb.Box(new Vector3(0, 0, h - t * 0.5f), new Vector3(size, 0.04f, t), Art.CellOk);
            mb.Box(new Vector3(0, 0, -h + t * 0.5f), new Vector3(size, 0.04f, t), Art.CellOk);
            mb.Box(new Vector3(h - t * 0.5f, 0, 0), new Vector3(t, 0.04f, size), Art.CellOk);
            mb.Box(new Vector3(-h + t * 0.5f, 0, 0), new Vector3(t, 0.04f, size), Art.CellOk);
            m = mb.Build("Frame" + size);
            frameMeshes[size] = m;
            return m;
        }
    }
}
