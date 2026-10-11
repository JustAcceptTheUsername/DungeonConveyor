using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DungeonConveyor
{
    /// <summary>Accumulates boxes/quads into one mesh with a submesh per material.</summary>
    public class MeshBuilder
    {
        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<Vector3> normals = new List<Vector3>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<List<int>> tris = new List<List<int>>();
        public readonly List<Material> Materials = new List<Material>();

        public bool IsEmpty => verts.Count == 0;

        int Sub(Material m)
        {
            int i = Materials.IndexOf(m);
            if (i >= 0) return i;
            Materials.Add(m);
            tris.Add(new List<int>());
            return Materials.Count - 1;
        }

        /// <summary>Quad centred at c facing n, with half-extents along u (right) and v (up) as seen by the viewer.</summary>
        public void Face(Material m, Vector3 c, Vector3 n, Vector3 u, Vector3 v, float uvScale = 1f)
        {
            var list = tris[Sub(m)];
            int s = verts.Count;
            verts.Add(c - u - v); verts.Add(c - u + v); verts.Add(c + u + v); verts.Add(c + u - v);
            for (int i = 0; i < 4; i++) normals.Add(n);
            float w = u.magnitude * 2f * uvScale, h = v.magnitude * 2f * uvScale;
            uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(0, h)); uvs.Add(new Vector2(w, h)); uvs.Add(new Vector2(w, 0));
            list.Add(s); list.Add(s + 1); list.Add(s + 2);
            list.Add(s); list.Add(s + 2); list.Add(s + 3);
        }

        public void Box(Vector3 center, Vector3 size, Material side, Material top = null, Quaternion? rotation = null, bool bottom = false)
        {
            var r = rotation ?? Quaternion.identity;
            top = top != null ? top : side;
            Vector3 ex = r * Vector3.right * (size.x * 0.5f);
            Vector3 ey = r * Vector3.up * (size.y * 0.5f);
            Vector3 ez = r * Vector3.forward * (size.z * 0.5f);
            Vector3 nx = ex.normalized, ny = ey.normalized, nz = ez.normalized;

            // For each face: viewer looks along -n; u = Cross(v, -n) gives the viewer's right.
            Face(top, center + ey, ny, ex, ez);
            if (bottom) Face(side, center - ey, -ny, -ex, ez);
            Face(side, center + ex, nx, ez, ey);
            Face(side, center - ex, -nx, -ez, ey);
            Face(side, center + ez, nz, -ex, ey);
            Face(side, center - ez, -nz, ex, ey);
        }

        public Mesh Build(string name = "Mesh")
        {
            var mesh = new Mesh { name = name };
            if (verts.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = tris.Count;
            for (int i = 0; i < tris.Count; i++) mesh.SetTriangles(tris[i], i);
            mesh.RecalculateBounds();
            return mesh;
        }

        public MeshRenderer Attach(GameObject go, string name = "Mesh", bool shadows = true)
        {
            // Not `??`: Unity's missing components are "fake null" and bypass it.
            if (!go.TryGetComponent(out MeshFilter mf)) mf = go.AddComponent<MeshFilter>();
            if (!go.TryGetComponent(out MeshRenderer mr)) mr = go.AddComponent<MeshRenderer>();
            mf.sharedMesh = Build(name);
            mr.sharedMaterials = Materials.ToArray();
            mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return mr;
        }
    }

    /// <summary>
    /// Builds a blocky model out of boxes. Boxes are grouped by named parts so parts can be animated
    /// (e.g. a turret head). Each part becomes one MeshRenderer.
    /// </summary>
    public class VoxelModel
    {
        readonly Dictionary<string, (Transform t, MeshBuilder mb)> parts = new Dictionary<string, (Transform, MeshBuilder)>();
        public readonly GameObject Root;

        public VoxelModel(string name)
        {
            Root = new GameObject(name);
            parts[""] = (Root.transform, new MeshBuilder());
        }

        public Transform Part(string name, Vector3 localPos, string parent = "")
        {
            if (parts.TryGetValue(name, out var p)) return p.t;
            var go = new GameObject(name);
            go.transform.SetParent(parts[parent].t, false);
            go.transform.localPosition = localPos;
            parts[name] = (go.transform, new MeshBuilder());
            return go.transform;
        }

        /// <summary>Adds a box; center is local to the part.</summary>
        public VoxelModel Box(Vector3 center, Vector3 size, Material m, string part = "", float yaw = 0f, Material top = null)
        {
            parts[part].mb.Box(center, size, m, top, yaw != 0f ? Quaternion.Euler(0, yaw, 0) : (Quaternion?)null);
            return this;
        }

        public VoxelModel Box(Vector3 center, Vector3 size, Material m, Quaternion rot, string part = "")
        {
            parts[part].mb.Box(center, size, m, null, rot);
            return this;
        }

        public GameObject Finish(bool shadows = true)
        {
            foreach (var kv in parts)
                if (!kv.Value.mb.IsEmpty)
                    kv.Value.mb.Attach(kv.Value.t.gameObject, kv.Key.Length > 0 ? kv.Key : Root.name, shadows);
            return Root;
        }
    }
}
