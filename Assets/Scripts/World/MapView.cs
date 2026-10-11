using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace DungeonConveyor
{
    /// <summary>
    /// Draws the map at runtime as one stone column SpriteRenderer per cell, so cells can show elevation.
    /// The editor Ground tilemap only defines which cells exist and is hidden in Play mode.
    /// </summary>
    [RequireComponent(typeof(IsoGrid))]
    public class MapView : MonoBehaviour
    {
        public Color Dirt = new Color(0.43f, 0.29f, 0.21f);
        public Color Rock = new Color(0.27f, 0.23f, 0.27f);
        public Color BaseColor = new Color(1f, 0.82f, 0.18f);
        public Color SpawnColor = new Color(0.95f, 0.18f, 0.12f);
        [Tooltip("How much ore tiles blend their ore colour back toward dirt (0 = full ore colour).")]
        [Range(0f, 1f)] public float OreTileFade = 0.35f;
        [Tooltip("Ground fades from dirt to rock between these elevation levels.")]
        public Vector2 RockLevels = new Vector2(2f, 4f);
        [Tooltip("Brightness change per elevation level, so heights read at a glance.")]
        public float LevelShade = 0.05f;
        [Tooltip("Strength and frequency of the large, soft colour patches across the floor.")]
        public float PatchStrength = 0.22f, PatchScale = 0.12f;

        IsoGrid map;
        SpriteRenderer[,] blocks;
        Vector2 patchOffset;
        Material spriteMaterial;
        readonly List<SpriteRenderer> bridgeRenderers = new List<SpriteRenderer>();

        public IReadOnlyList<SpriteRenderer> BridgeRenderers => bridgeRenderers;

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
            if (map.Ground != null) map.Ground.GetComponent<TilemapRenderer>().enabled = false;

            patchOffset = new Vector2(Random.Range(0f, 1000f), Random.Range(0f, 1000f));
            var root = new GameObject("Blocks").transform;
            root.SetParent(transform, false);
            blocks = new SpriteRenderer[map.Width, map.Height];
            for (int x = 0; x < map.Width; x++)
                for (int y = 0; y < map.Height; y++)
                {
                    if (!map.Exists[x, y]) continue;
                    var sr = new GameObject($"Cell {x},{y}").AddComponent<SpriteRenderer>();
                    sr.transform.SetParent(root, false);
                    if (SpriteMaterial != null) sr.sharedMaterial = SpriteMaterial;
                    blocks[x, y] = sr;
                    Refresh(new Vector2Int(x, y));
                }
            // Mined-out patches turn back into plain ground.
            map.PatchDepleted += patch =>
            {
                foreach (var c in patch.Cells) Refresh(c);
            };

            BuildBridges();
        }

        void BuildBridges()
        {
            var root = new GameObject("Bridges").transform;
            root.SetParent(transform, false);
            foreach (var bridge in map.Bridges)
            {
                for (int i = 0; i < bridge.Cells.Count; i++)
                {
                    var c = bridge.Cells[i];
                    IsoGrid.BridgeShape(bridge, i, out int connections, out bool alongX);
                    var sr = new GameObject($"Bridge {c.x},{c.y}").AddComponent<SpriteRenderer>();
                    bridgeRenderers.Add(sr);
                    sr.transform.SetParent(root, false);
                    if (SpriteMaterial != null) sr.sharedMaterial = SpriteMaterial;
                    sr.sprite = BlockArt.Bridge(connections, alongX);
                    sr.transform.position = map.BridgeSurface(bridge, c);
                    sr.sortingOrder = map.SortOrder(c);
                }
            }
        }

        void Refresh(Vector2Int c)
        {
            var sr = blocks[c.x, c.y];
            int level = map.Heights[c.x, c.y];
            sr.sprite = BlockArt.Column(level, (c.x * 73856093) ^ (c.y * 19349663));
            sr.transform.position = map.SurfaceCenter(c);
            sr.sortingOrder = map.SortOrder(c);

            Color tint;
            if (map.IsBase(c)) tint = BaseColor;
            else if (map.IsSpawn(c)) tint = SpawnColor;
            else if (map.IsOre(c)) tint = Color.Lerp(IsoGrid.OreTypeOf(map.PatchAt[c.x, c.y].Kind).Color, Dirt, OreTileFade);
            else tint = Color.Lerp(Dirt, Rock, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(RockLevels.x, RockLevels.y, level)));

            // Low-frequency patches vary slowly from cell to cell, so they read as one continuous floor.
            float patch = Mathf.PerlinNoise(c.x * PatchScale + patchOffset.x, c.y * PatchScale + patchOffset.y) - 0.5f;
            float shade = (1f + level * LevelShade) * (1f + patch * PatchStrength * 2f);
            sr.color = new Color(tint.r * shade, tint.g * shade, tint.b * shade, 1f);
        }
    }
}
