using UnityEngine;
using UnityEngine.InputSystem;

namespace DungeonConveyor
{
    /// <summary>Top-down texture of the map, rotated to match the camera. Click to jump the camera.</summary>
    public class Minimap
    {
        const float RefreshInterval = 0.2f;

        readonly Texture2D tex;
        readonly Color32[] pixels;
        Color32[] baseLayer;
        float timer;

        public Minimap()
        {
            tex = new Texture2D(GridMap.Width, GridMap.Height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "Minimap",
            };
            pixels = new Color32[GridMap.Width * GridMap.Height];
        }

        public void Tick()
        {
            timer -= Time.unscaledDeltaTime;
            if (timer > 0 || Game.Map == null) return;
            timer = RefreshInterval;
            var map = Game.Map;
            if (baseLayer == null) BuildBase(map);

            System.Array.Copy(baseLayer, pixels, pixels.Length);
            foreach (var b in Game.Build.All)
            {
                Color32 c = b is CoreBuilding ? new Color32(255, 190, 60, 255)
                          : b is Conveyor ? new Color32(150, 90, 30, 255)
                          : b is Wall ? new Color32(140, 140, 150, 255)
                          : new Color32(235, 130, 30, 255);
                foreach (var cell in b.Cells()) pixels[cell.y * GridMap.Width + cell.x] = c;
            }
            foreach (var e in Enemy.All)
            {
                var cell = GridMap.WorldToCell(e.transform.position);
                if (GridMap.InBounds(cell)) pixels[cell.y * GridMap.Width + cell.x] = new Color32(255, 40, 70, 255);
            }
            tex.SetPixels32(pixels);
            tex.Apply(false);
        }

        void BuildBase(GridMap map)
        {
            baseLayer = new Color32[pixels.Length];
            for (int x = 0; x < GridMap.Width; x++)
                for (int y = 0; y < GridMap.Height; y++)
                {
                    Color32 c;
                    var t = map.Tiles[x, y];
                    if (t == Tile.Rock) c = new Color32(18, 14, 18, 255);
                    else if (t == Tile.Lava) c = new Color32(200, 70, 10, 255);
                    else if (map.Ore[x, y] >= 0) c = ResourceInfo.Colors[map.Ore[x, y]] * 0.75f;
                    else c = new Color32(70, 48, 40, 255);
                    baseLayer[y * GridMap.Width + x] = c;
                }
            foreach (var s in map.SpawnCells)
                baseLayer[s.y * GridMap.Width + s.x] = new Color32(150, 255, 40, 255);
        }

        public void Draw(Rect area)
        {
            if (Game.Cam == null) return;
            float yaw = Game.Cam.transform.eulerAngles.y;
            var center = area.center;
            float size = area.width / Mathf.Sqrt(2f); // rotated square must fit
            var mapRect = new Rect(center.x - size / 2, center.y - size / 2, size, size);

            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && area.Contains(e.mousePosition))
            {
                var world = ScreenToWorld(e.mousePosition, center, size, yaw);
                if (world.HasValue) { Game.Cam.LookAt(world.Value); e.Use(); }
            }
            if (e.type == EventType.MouseDrag && e.button == 0 && area.Contains(e.mousePosition))
            {
                var world = ScreenToWorld(e.mousePosition, center, size, yaw);
                if (world.HasValue) { Game.Cam.LookAt(world.Value); e.Use(); }
            }

            var matrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(-yaw, center);
            GUI.DrawTexture(mapRect, tex);
            // Camera focus marker
            var f = Game.Cam.Focus;
            var fp = new Vector2(mapRect.x + f.x / GridMap.Width * size, mapRect.yMax - f.z / GridMap.Height * size);
            UIStyle.Fill(new Rect(fp.x - 3, fp.y - 3, 6, 6), Color.white);
            GUI.matrix = matrix;
        }

        static Vector3? ScreenToWorld(Vector2 mouse, Vector2 center, float size, float yaw)
        {
            // Undo the GUI rotation (RotateAroundPivot(-yaw)) by rotating the offset by +yaw.
            var d = mouse - center;
            float a = yaw * Mathf.Deg2Rad;
            var local = new Vector2(d.x * Mathf.Cos(a) - d.y * Mathf.Sin(a), d.x * Mathf.Sin(a) + d.y * Mathf.Cos(a));
            float u = local.x / size + 0.5f, v = 0.5f - local.y / size;
            if (u < 0 || u > 1 || v < 0 || v > 1) return null;
            return new Vector3(u * GridMap.Width, 0, v * GridMap.Height);
        }
    }
}
