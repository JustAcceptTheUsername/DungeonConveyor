using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace DungeonConveyor.EditorTools
{
    /// <summary>
    /// Dungeon > Build Isometric Grid: creates a 50x50 isometric Grid + Ground tilemap in the open scene,
    /// generating a placeholder diamond floor tile if needed, and frames the main camera on it.
    /// </summary>
    public static class IsoGridBuilder
    {
        const int Size = 50;
        const int TexW = 128, TexH = 64; // 2:1 diamond, 128 px per unit -> one cell is 1 x 0.5 world units
        const string SpritePath = "Assets/Tilemap/IsoFloor.png";
        const string TilePath = "Assets/Tilemap/Tiles/IsoFloor.asset";

        [MenuItem("Dungeon/Build Isometric Grid")]
        static void Build()
        {
            var existing = Object.FindAnyObjectByType<IsoGrid>();
            if (existing != null)
            {
                if (!EditorUtility.DisplayDialog("Build Isometric Grid",
                        $"'{existing.name}' already exists. Replace it? Any tiles painted on it will be lost.", "Replace", "Cancel"))
                    return;
                Undo.DestroyObjectImmediate(existing.gameObject);
            }

            var tile = GetOrCreateFloorTile();

            var gridGo = new GameObject("IsoGrid");
            Undo.RegisterCreatedObjectUndo(gridGo, "Build Isometric Grid");
            var grid = gridGo.AddComponent<Grid>();
            grid.cellLayout = GridLayout.CellLayout.Isometric;
            grid.cellSize = new Vector3(1f, 0.5f, 1f);

            var groundGo = new GameObject("Ground");
            groundGo.transform.SetParent(gridGo.transform, false);
            var ground = groundGo.AddComponent<Tilemap>();
            groundGo.AddComponent<TilemapRenderer>();

            var cells = new Vector3Int[Size * Size];
            var tiles = new TileBase[Size * Size];
            for (int x = 0; x < Size; x++)
                for (int y = 0; y < Size; y++)
                {
                    cells[x * Size + y] = new Vector3Int(x, y, 0);
                    tiles[x * Size + y] = tile;
                }
            ground.SetTiles(cells, tiles);
            ground.CompressBounds();

            var iso = gridGo.AddComponent<IsoGrid>();
            iso.Width = Size;
            iso.Height = Size;
            iso.Ground = ground;

            FrameCamera(iso);
            Selection.activeGameObject = gridGo;
            EditorSceneManager.MarkSceneDirty(gridGo.scene);
        }

        static Tile GetOrCreateFloorTile()
        {
            if (!File.Exists(SpritePath))
            {
                var tex = DrawDiamond();
                File.WriteAllBytes(SpritePath, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(SpritePath);
            }

            var importer = (TextureImporter)AssetImporter.GetAtPath(SpritePath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = TexW;
            importer.filterMode = FilterMode.Point; // hard pixel edges so neighbouring diamonds interlock without seams
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteAlignment = (int)SpriteAlignment.Center;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
            var tile = AssetDatabase.LoadAssetAtPath<Tile>(TilePath);
            if (tile == null)
            {
                tile = ScriptableObject.CreateInstance<Tile>();
                AssetDatabase.CreateAsset(tile, TilePath);
            }
            tile.sprite = sprite;
            tile.colliderType = Tile.ColliderType.None;
            EditorUtility.SetDirty(tile);
            AssetDatabase.SaveAssets();
            return tile;
        }

        /// <summary>Placeholder stone floor: flat diamond with a darker rim so the grid lines read.</summary>
        static Texture2D DrawDiamond()
        {
            var tex = new Texture2D(TexW, TexH, TextureFormat.RGBA32, false);
            var fill = new Color(0.34f, 0.31f, 0.36f);
            var rim = new Color(0.19f, 0.17f, 0.21f);
            var px = new Color[TexW * TexH];
            for (int y = 0; y < TexH; y++)
                for (int x = 0; x < TexW; x++)
                {
                    // Pixel centres never land exactly on the edge, so adjacent tiles fill each other's gaps exactly.
                    float u = Mathf.Abs((x + 0.5f) / (TexW * 0.5f) - 1f);
                    float v = Mathf.Abs((y + 0.5f) / (TexH * 0.5f) - 1f);
                    float d = u + v;
                    if (d >= 1f) { px[y * TexW + x] = Color.clear; continue; }
                    float edgePx = (1f - d) * TexH * 0.5f;
                    float noise = (Hash(x, y) - 0.5f) * 0.04f;
                    var c = edgePx < 1.5f ? rim : fill;
                    px[y * TexW + x] = new Color(c.r + noise, c.g + noise, c.b + noise, 1f);
                }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        static float Hash(int x, int y)
        {
            uint h = (uint)(x * 374761393 + y * 668265263);
            h = (h ^ (h >> 13)) * 1274126177;
            return (h & 0xFFFF) / 65535f;
        }

        static void FrameCamera(IsoGrid iso)
        {
            var cam = Camera.main;
            if (cam == null) return;
            Undo.RecordObject(cam, "Frame Camera");
            Undo.RecordObject(cam.transform, "Frame Camera");

            var b = iso.WorldBounds;
            cam.orthographic = true;
            cam.orthographicSize = Mathf.Max(b.extents.y, b.extents.x / (16f / 9f)) * 1.05f;
            cam.transform.SetPositionAndRotation(new Vector3(b.center.x, b.center.y, -10f), Quaternion.identity);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.015f, 0.02f);

            var controller = cam.GetComponent<CameraController>();
            if (controller == null) controller = Undo.AddComponent<CameraController>(cam.gameObject);
            controller.Map = iso;
        }
    }
}
