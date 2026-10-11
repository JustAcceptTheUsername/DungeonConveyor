using UnityEngine;
using UnityEngine.InputSystem;

namespace DungeonConveyor
{
    /// <summary>Outlines the cell under the cursor (hidden while the camera is being dragged).</summary>
    [RequireComponent(typeof(IsoGrid))]
    public class TileHover : MonoBehaviour
    {
        IsoGrid map;
        Camera cam;
        CameraController camController;
        SpriteRenderer highlight;
        BuildController build;

        /// <summary>Cell under the cursor this frame, if any.</summary>
        public bool HasCell { get; private set; }
        public Vector2Int Cell { get; private set; }

        void Start()
        {
            map = GetComponent<IsoGrid>();
            build = GetComponent<BuildController>();
            cam = Camera.main;
            if (cam != null) camController = cam.GetComponent<CameraController>();

            highlight = new GameObject("Hover").AddComponent<SpriteRenderer>();
            highlight.transform.SetParent(transform, false);
            highlight.sprite = BlockArt.Highlight;
            var view = GetComponent<MapView>();
            if (view != null && view.SpriteMaterial != null) highlight.sharedMaterial = view.SpriteMaterial;
            highlight.sortingOrder = short.MaxValue;
            highlight.enabled = false;
        }

        void Update()
        {
            var mouse = Mouse.current;
            HasCell = false;
            if (mouse != null && cam != null && !(camController != null && camController.IsDragging))
            {
                Vector2 screen = mouse.position.ReadValue();
                if (!HUD.IsPointerOverUI(screen))
                {
                    HasCell = map.Pick(cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 0f)), out var cell);
                    Cell = cell;
                }
            }

            // While placing, the build ghost shows its own footprint instead.
            highlight.enabled = HasCell && (build == null || build.Placing == null);
            if (HasCell) highlight.transform.position = map.SurfaceCenter(Cell);
        }
    }
}
