using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Turns the empty black background into a lava abyss with a rocky cliff skirt around the map.
/// Attach to an empty GameObject, assign prefabs/material, then call Build() after your map is generated
/// (or tick buildOnStart). Assumes a rectangular grid of tiles on the XZ plane with the surface at y = mapY.
/// </summary>
public class AbyssBackground : MonoBehaviour
{
    [Header("Map (match your tilemap)")]
    public int width = 64;              // tiles along X
    public int height = 64;             // tiles along Z
    public float tileSize = 1f;
    public Vector3 mapOrigin = Vector3.zero;   // world position of the CORNER of tile (0,0)
    public float mapY = 0f;             // top surface of the tiles

    [Header("Cliff skirt")]
    public GameObject[] edgePrefabs;    // CliffEdge_A/B/C  (outward = +Z, face at z = +0.5)
    public GameObject cornerPrefab;     // CliffCorner      (outward = +X and +Z)
    public int seed = 0;                // match your map seed so the skirt varies with it

    [Header("Abyss")]
    public Material abyssMaterial;      // URP/Lit with Abyss_Albedo/Normal/Emission, tiling ~ (size/18)
    public float abyssDepth = 9f;       // how far below the map
    public float abyssSize = 400f;
    public bool buildOnStart = true;

    [Header("Optional floating rocks")]
    public GameObject floatingRockPrefab;
    public int floatingRockCount = 8;

    readonly List<GameObject> spawned = new List<GameObject>();

    void Start() { if (buildOnStart) Build(); }

    public void Build()
    {
        Clear();
        var rng = new System.Random(seed);
        Transform root = transform;

        // --- abyss plane -------------------------------------------------
        if (abyssMaterial != null)
        {
            var plane = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(plane.GetComponent<Collider>());
            plane.name = "AbyssPlane";
            plane.transform.SetParent(root, false);
            Vector3 c = MapCenter();
            plane.transform.position = new Vector3(c.x, mapY - abyssDepth, c.z);
            plane.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            plane.transform.localScale = Vector3.one * abyssSize;
            var r = plane.GetComponent<MeshRenderer>();
            r.sharedMaterial = abyssMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            spawned.Add(plane);
        }

        // --- cliff skirt ---------------------------------------------------
        if (edgePrefabs != null && edgePrefabs.Length > 0)
        {
            for (int x = 1; x < width - 1; x++)
            {
                Spawn(Pick(rng), TileCenter(x, height - 1), 0f);    // +Z edge
                Spawn(Pick(rng), TileCenter(x, 0), 180f);           // -Z edge
            }
            for (int z = 1; z < height - 1; z++)
            {
                Spawn(Pick(rng), TileCenter(width - 1, z), 90f);    // +X edge
                Spawn(Pick(rng), TileCenter(0, z), -90f);           // -X edge
            }
        }
        if (cornerPrefab != null)
        {
            Spawn(cornerPrefab, TileCenter(width - 1, height - 1), 0f);
            Spawn(cornerPrefab, TileCenter(width - 1, 0), 90f);
            Spawn(cornerPrefab, TileCenter(0, 0), 180f);
            Spawn(cornerPrefab, TileCenter(0, height - 1), -90f);
        }

        // --- floating rocks around the island --------------------------------
        if (floatingRockPrefab != null)
        {
            Vector3 c = MapCenter();
            float rad = Mathf.Max(width, height) * tileSize * 0.5f;
            for (int i = 0; i < floatingRockCount; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float d = rad * (1.15f + (float)rng.NextDouble() * 0.5f);
                var p = new Vector3(c.x + Mathf.Cos(a) * d, mapY - 1f - (float)rng.NextDouble() * 4f, c.z + Mathf.Sin(a) * d);
                var go = Instantiate(floatingRockPrefab, p, Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0), root);
                go.transform.localScale *= tileSize * (0.8f + (float)rng.NextDouble() * 1.2f);
                spawned.Add(go);
            }
        }
    }

    public void Clear()
    {
        foreach (var g in spawned) if (g) Destroy(g);
        spawned.Clear();
    }

    GameObject Pick(System.Random rng) => edgePrefabs[rng.Next(edgePrefabs.Length)];

    Vector3 TileCenter(int x, int z) =>
        mapOrigin + new Vector3((x + 0.5f) * tileSize, mapY - mapOrigin.y, (z + 0.5f) * tileSize);

    Vector3 MapCenter() => mapOrigin + new Vector3(width * tileSize * 0.5f, 0, height * tileSize * 0.5f);

    void Spawn(GameObject prefab, Vector3 pos, float yaw)
    {
        var go = Instantiate(prefab, pos, Quaternion.Euler(0f, yaw, 0f), transform);
        go.transform.localScale *= tileSize;
        spawned.Add(go);
    }
}
