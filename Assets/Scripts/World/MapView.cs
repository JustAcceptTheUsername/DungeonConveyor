using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace DungeonConveyor
{
    /// <summary>
    /// Draws the planet: a few meshes of raised, bevelled hexagon tiles (the bevel reads as the grid lines) with
    /// cliff walls down to lower neighbours, coloured per cell (ground by height, ore, base, landing sites, the
    /// pentagon spires). The meshes double as colliders for picking cells. Also adds a glowing atmosphere and a star field.
    /// </summary>
    [RequireComponent(typeof(IsoGrid))]
    public class MapView : MonoBehaviour
    {
        public Color BaseColor = new Color(1f, 0.82f, 0.18f);
        public Color SpawnColor = new Color(0.95f, 0.18f, 0.12f);
        [Tooltip("How much ore tiles blend their ore colour back toward the ground (0 = full ore colour).")]
        [Range(0f, 1f)] public float OreTileFade = 0.35f;
        [Tooltip("Brightness change per elevation level, so heights read at a glance.")]
        public float LevelShade = 0.05f;
        [Tooltip("Strength and frequency of the large, soft colour patches across the ground.")]
        public float PatchStrength = 0.22f, PatchScale = 0.12f;
        [Tooltip("Width of the darker rim around each tile (the grid lines), in cells.")]
        [Range(0f, 0.2f)] public float Bevel = 0.06f;
        [Tooltip("Colour of the 12 pentagon rock spires.")]
        public Color SpireColor = new Color(0.32f, 0.27f, 0.38f);
        [Tooltip("Brightness of the grid lines relative to the tile (lower = darker, more visible lines).")]
        [Range(0f, 1f)] public float BevelShade = 0.5f;
        public int StarCount = 900;
        [Header("Atmosphere glow")]
        [Tooltip("Size of the glowing shell relative to the highest ground.")]
        public float AtmosphereScale = 1.35f;
        [Tooltip("Lower = the glow reaches further in from the rim.")]
        public float AtmospherePower = 2f;
        [Tooltip("Brightness of the glow (it is additive, so this acts as its opacity).")]
        public float AtmosphereStrength = 1.1f;

        const float WallShade = 0.62f;

        IsoGrid map;
        Material spriteMaterial;
        Vector3 patchOffset;
        PlanetArt.Palette palette;

        class Face
        {
            public Mesh Mesh;
            public MeshCollider Collider;
            public Color[] Colors;
            public float[] Shade;                     // per vertex: multiplies the cell colour
            public readonly List<Vector2Int> TriangleCells = new List<Vector2Int>();
        }

        Face[] faces;
        // Each cell's chunk, and its vertices in that chunk's mesh.
        int[] chunkOf;
        int[,] firstVertex, vertexCount;

        /// <summary>The sprite material the editor tiles render with; every runtime sprite uses it too.</summary>
        public Material SpriteMaterial
        {
            get
            {
                if (spriteMaterial == null)
                {
                    var ground = GetComponent<IsoGrid>().Ground;
                    if (ground != null) spriteMaterial = ground.GetComponent<TilemapRenderer>().sharedMaterial;
                }
                return spriteMaterial;
            }
        }

        void Start()
        {
            map = GetComponent<IsoGrid>();
            palette = PlanetArt.Current;
            patchOffset = new Vector3(Random.Range(0f, 1000f), Random.Range(0f, 1000f), Random.Range(0f, 1000f));
            BuildFaces();
            BuildAtmosphere();
            BuildStars();

            // Mined-out patches turn back into plain ground.
            map.PatchDepleted += patch =>
            {
                foreach (var c in patch.Cells) Recolor(c);
                foreach (var f in faces) f.Mesh.colors = f.Colors;
            };
            // A landing site moved or was added: repaint the ground (sites are coloured red).
            map.SpawnsChanged += () =>
            {
                for (int x = 0; x < map.Width; x++) Recolor(new Vector2Int(x, 0));
                foreach (var f in faces) f.Mesh.colors = f.Colors;
            };
        }

        // ---------------------------------------------------------------- ground

        /// <summary>The ground in a few chunks of consecutive cells (smaller colliders and colour uploads).</summary>
        void BuildFaces()
        {
            var root = new GameObject("Planet").transform;
            root.SetParent(transform, false);
            var material = PlanetArt.NewMaterial("PlanetLit");
            int chunks = Mathf.Max(1, Mathf.CeilToInt(map.CellCount / 1500f));
            int perChunk = Mathf.CeilToInt(map.CellCount / (float)chunks);
            faces = new Face[chunks];
            chunkOf = new int[map.Width];
            firstVertex = new int[map.Width, map.Height];
            vertexCount = new int[map.Width, map.Height];

            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var shade = new List<float>();
            var tris = new List<int>();
            for (int f = 0; f < chunks; f++)
            {
                var face = faces[f] = new Face();
                verts.Clear(); normals.Clear(); shade.Clear(); tris.Clear();
                for (int i = f * perChunk; i < Mathf.Min(map.CellCount, (f + 1) * perChunk); i++)
                {
                    var c = new Vector2Int(i, 0);
                    chunkOf[i] = f;
                    firstVertex[c.x, c.y] = verts.Count;
                    AddCell(face, c, verts, normals, shade, tris);
                    vertexCount[c.x, c.y] = verts.Count - firstVertex[c.x, c.y];
                }

                var mesh = face.Mesh = new Mesh { name = "PlanetChunk" + f, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                mesh.SetVertices(verts);
                mesh.SetNormals(normals);
                mesh.SetTriangles(tris, 0);
                mesh.RecalculateBounds();
                face.Shade = shade.ToArray();
                face.Colors = new Color[verts.Count];

                var go = new GameObject("Chunk" + f);
                go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = material;
                face.Collider = go.AddComponent<MeshCollider>();
            }
            for (int x = 0; x < map.Width; x++)
                for (int y = 0; y < map.Height; y++)
                    Recolor(new Vector2Int(x, y));
            foreach (var face in faces)
            {
                face.Mesh.colors = face.Colors;
                face.Collider.sharedMesh = face.Mesh;
            }
        }

        /// <summary>The tile top (inner hexagon plus a darker bevel ring) and walls down to any lower neighbour.</summary>
        void AddCell(Face face, Vector2Int c, List<Vector3> verts, List<Vector3> normals, List<float> shade, List<int> tris)
        {
            float r = map.RadiusAt(map.Heights[c.x, c.y]);
            var up = map.Up(c);
            int m = map.Sides(c);
            // Bevel is in cells; the corners are about 0.58 cells from the centre.
            float keep = 1f - Bevel / 0.29f;
            var outer = new Vector3[m];
            var inner = new Vector3[m];
            for (int k = 0; k < m; k++)
            {
                var corner = map.Corner(c, k);
                outer[k] = corner * r;
                inner[k] = Vector3.Lerp(up, corner, keep).normalized * r;
            }
            Poly(inner, up, 1f);
            for (int k = 0; k < m; k++)
            {
                int k1 = (k + 1) % m;
                Poly(new[] { outer[k], outer[k1], inner[k1], inner[k] }, up, BevelShade);
            }

            // Walls: the shared edge with each lower neighbour (side d runs from corner d - 1 to corner d), from this top down to theirs.
            for (int d = 0; d < m; d++)
            {
                var n = map.Neighbour(c, d);
                int drop = map.Heights[c.x, c.y] - map.Heights[n.x, n.y];
                if (drop <= 0) continue;
                float lower = map.RadiusAt(map.Heights[n.x, n.y]);
                Vector3 e0 = map.Corner(c, d - 1), e1 = map.Corner(c, d);
                var outward = Vector3.ProjectOnPlane((e0 + e1) * 0.5f - up, up).normalized;
                Poly(new[] { e0 * r, e1 * r, e1 * lower, e0 * lower }, outward, WallShade * Mathf.Lerp(1f, 0.75f, Mathf.Clamp01((drop - 1) / 4f)));
            }

            // A convex polygon as a triangle fan.
            void Poly(Vector3[] p, Vector3 normal, float s)
            {
                int i = verts.Count;
                foreach (var q in p) { verts.Add(q); normals.Add(normal); shade.Add(s); }
                // Front faces wind so their normal points out (Unity: Cross(b - a, c - a) faces the viewer).
                bool flip = Vector3.Dot(Vector3.Cross(p[1] - p[0], p[2] - p[0]), normal) < 0f;
                for (int k = 1; k < p.Length - 1; k++)
                {
                    tris.Add(i);
                    tris.Add(i + (flip ? k + 1 : k));
                    tris.Add(i + (flip ? k : k + 1));
                    face.TriangleCells.Add(c);
                }
            }
        }

        void Recolor(Vector2Int c)
        {
            var face = faces[chunkOf[c.x]];
            var tint = CellColor(c);
            int start = firstVertex[c.x, c.y], end = start + vertexCount[c.x, c.y];
            for (int i = start; i < end; i++)
            {
                float s = face.Shade[i];
                face.Colors[i] = new Color(tint.r * s, tint.g * s, tint.b * s, 1f);
            }
        }

        Color CellColor(Vector2Int c)
        {
            int level = map.Heights[c.x, c.y];
            var ground = Color.Lerp(palette.Low, palette.High, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(IsoGrid.MinLevel, IsoGrid.MaxLevel, level)));
            Color tint;
            if (map.IsPentagon(c)) return SpireColor;
            if (map.IsBase(c)) tint = BaseColor;
            else if (map.IsSpawn(c)) tint = SpawnColor;
            else if (map.IsOre(c)) tint = Color.Lerp(IsoGrid.OreTypeOf(map.PatchAt[c.x, c.y].Kind).Color, ground, OreTileFade);
            else tint = ground;

            // Low-frequency patches vary slowly from cell to cell, so they read as one continuous ground.
            var p = map.Up(c) * map.Radius * PatchScale + patchOffset;
            float patch = (Mathf.PerlinNoise(p.x, p.y) + Mathf.PerlinNoise(p.y, p.z)) * 0.5f - 0.5f;
            float shade = (1f + level * LevelShade) * (1f + patch * PatchStrength * 2f);
            return new Color(tint.r * shade, tint.g * shade, tint.b * shade, 1f);
        }

        // ---------------------------------------------------------------- picking

        /// <summary>The cell whose ground (top or wall) the ray hits first.</summary>
        public bool Pick(Ray ray, out Vector2Int cell)
        {
            cell = default;
            if (faces == null) return false;
            float best = float.MaxValue;
            bool found = false;
            foreach (var face in faces)
            {
                if (!face.Collider.Raycast(ray, out var hit, 10000f) || hit.distance >= best) continue;
                int t = hit.triangleIndex;
                if (t < 0 || t >= face.TriangleCells.Count) continue;
                best = hit.distance;
                cell = face.TriangleCells[t];
                found = true;
            }
            return found;
        }

        /// <summary>Is this collider part of the planet's ground?</summary>
        public bool IsGround(Collider c)
        {
            if (faces == null) return false;
            foreach (var face in faces)
                if (face.Collider == c) return true;
            return false;
        }

        // ---------------------------------------------------------------- sky

        void BuildAtmosphere()
        {
            var shell = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(shell.GetComponent<Collider>());
            shell.name = "Atmosphere";
            shell.transform.SetParent(transform, false);
            shell.transform.localScale = Vector3.one * (map.RadiusAt(IsoGrid.MaxLevel) * AtmosphereScale) * 2f;
            var material = PlanetArt.NewMaterial("Atmosphere");
            material.SetColor("_Color", palette.Atmosphere);
            material.SetFloat("_Power", AtmospherePower);
            material.SetFloat("_Strength", AtmosphereStrength);
            shell.GetComponent<MeshRenderer>().sharedMaterial = material;

            var cam = Camera.main;
            if (cam != null) cam.backgroundColor = palette.Space;
        }

        /// <summary>Small quads scattered on a huge sphere around everything, facing the centre.</summary>
        void BuildStars()
        {
            const float distance = 900f;
            var verts = new List<Vector3>();
            var colors = new List<Color>();
            var tris = new List<int>();
            for (int i = 0; i < StarCount; i++)
            {
                var dir = Random.onUnitSphere;
                var right = Vector3.Cross(dir, Random.onUnitSphere).normalized;
                var upv = Vector3.Cross(dir, right);
                float size = Random.Range(0.6f, 2.2f) * (Random.value < 0.06f ? 2f : 1f);
                var center = dir * distance;
                float b = Random.Range(0.35f, 1f);
                var color = Color.Lerp(new Color(0.7f, 0.8f, 1f), new Color(1f, 0.9f, 0.75f), Random.value) * b;
                color.a = 1f;
                int k = verts.Count;
                verts.Add(center + (-right - upv) * size); verts.Add(center + (right - upv) * size);
                verts.Add(center + (right + upv) * size); verts.Add(center + (-right + upv) * size);
                for (int j = 0; j < 4; j++) colors.Add(color);
                tris.Add(k); tris.Add(k + 1); tris.Add(k + 2);
                tris.Add(k); tris.Add(k + 2); tris.Add(k + 3);
            }
            var mesh = new Mesh { name = "Stars" };
            mesh.SetVertices(verts);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * distance * 3f);
            var go = new GameObject("Stars");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = PlanetArt.NewMaterial("VertexColorUnlit");
        }
    }
}
