using System.Collections.Generic;
using UnityEngine;

namespace DungeonConveyor
{
    /// <summary>
    /// A Goldberg sphere: an icosahedron with each edge cut into n, whose vertices become the tiles. Every tile is a
    /// hexagon except 12 pentagons (one at each icosahedron corner). About 10 n² + 2 tiles.
    /// </summary>
    public static class HexSphere
    {
        /// <summary>Tile centres (unit vectors) and each tile's neighbours (unordered; 6, or 5 for a pentagon).</summary>
        public static void Generate(int n, out List<Vector3> centres, out List<List<int>> neighbours)
        {
            n = Mathf.Max(1, n);
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            var ico = new[]
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            };
            int[] faces =
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };

            // Subdivide every face, sharing the vertices along its edges.
            var points = new List<Vector3>();
            var index = new Dictionary<Vector3Int, int>();
            var links = new List<HashSet<int>>();
            int Vertex(Vector3 p)
            {
                p.Normalize();
                var key = Vector3Int.RoundToInt(p * 100000f);
                if (!index.TryGetValue(key, out int i))
                {
                    i = index[key] = points.Count;
                    points.Add(p);
                    links.Add(new HashSet<int>());
                }
                return i;
            }
            void Link(int a, int b)
            {
                links[a].Add(b);
                links[b].Add(a);
            }
            var grid = new int[n + 1, n + 1];
            for (int f = 0; f < 20; f++)
            {
                Vector3 a = ico[faces[f * 3]], b = ico[faces[f * 3 + 1]], c = ico[faces[f * 3 + 2]];
                for (int i = 0; i <= n; i++)
                    for (int j = 0; i + j <= n; j++)
                        grid[i, j] = Vertex(a + (b - a) * i / n + (c - a) * j / n);
                for (int i = 0; i < n; i++)
                    for (int j = 0; i + j < n; j++)
                    {
                        Link(grid[i, j], grid[i + 1, j]);
                        Link(grid[i, j], grid[i, j + 1]);
                        Link(grid[i + 1, j], grid[i, j + 1]);
                    }
            }

            centres = points;
            neighbours = new List<List<int>>(points.Count);
            foreach (var l in links) neighbours.Add(new List<int>(l));
        }
    }
}
