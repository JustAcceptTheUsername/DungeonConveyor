using UnityEngine;
using UnityEngine.InputSystem;

namespace DungeonConveyor
{
    /// <summary>
    /// Orthographic camera for the isometric map: left/middle-drag pans (the grabbed point sticks to the cursor),
    /// but only after the cursor moves a few pixels so left-clicks still reach the map;
    /// scroll zooms toward the cursor, and the view is clamped so it never leaves the map.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CameraController : MonoBehaviour
    {
        public IsoGrid Map;
        [Tooltip("Smallest orthographic size (most zoomed in).")]
        public float MinSize = 3f;
        [Tooltip("Extra world units of empty space allowed around the map edge.")]
        public float Margin = 2f;
        [Tooltip("Size multiplier per scroll notch.")]
        public float ZoomStep = 1.15f;
        public float ZoomSmoothing = 14f;
        [Tooltip("Pixels the cursor must move while held before a click turns into a pan.")]
        public float DragThreshold = 6f;

        /// <summary>True while a pan is in progress (including the frame the button is released).</summary>
        public bool IsDragging => dragging;

        Camera cam;
        float targetSize;
        Vector2 zoomAnchor;    // screen point the zoom is centred on
        bool pressed, pressedOnUI, dragging;
        Vector2 pressScreen;
        Vector3 dragGrab;      // world point held under the cursor while dragging
        BuildController build;

        void Awake()
        {
            cam = GetComponent<Camera>();
            cam.orthographic = true;
            if (Map == null) Map = FindAnyObjectByType<IsoGrid>();
            targetSize = cam.orthographicSize;
        }

        void LateUpdate()
        {
            var mouse = Mouse.current;
            if (mouse == null || Map == null) return;
            Vector2 screen = mouse.position.ReadValue();

            float scroll = mouse.scroll.ReadValue().y;
            if (scroll != 0f)
            {
                targetSize *= scroll > 0f ? 1f / ZoomStep : ZoomStep;
                zoomAnchor = screen;
            }
            targetSize = Mathf.Clamp(targetSize, MinSize, MaxSize());

            if (!Mathf.Approximately(cam.orthographicSize, targetSize))
            {
                // Keep the world point under the anchor fixed while the size changes.
                Vector3 before = ScreenToWorld(zoomAnchor);
                cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, targetSize, 1f - Mathf.Exp(-ZoomSmoothing * Time.unscaledDeltaTime));
                if (Mathf.Abs(cam.orthographicSize - targetSize) < 0.001f) cam.orthographicSize = targetSize;
                transform.position += before - ScreenToWorld(zoomAnchor);
            }

            // A press only becomes a drag once the cursor moves past the threshold, so plain clicks stay clicks.
            if (build == null) build = Map.GetComponent<BuildController>();
            bool leftPans = build == null || !build.CapturesLeftDrag; // left-drag draws belts while placing conveyors
            bool held = (leftPans && mouse.leftButton.isPressed) || mouse.middleButton.isPressed;
            if (held && !pressed)
            {
                pressed = true;
                pressedOnUI = HUD.IsPointerOverUI(screen);
                pressScreen = screen;
                dragGrab = ScreenToWorld(screen);
            }
            else if (!held) pressed = dragging = false;
            if (pressed && !pressedOnUI && !dragging && (screen - pressScreen).sqrMagnitude > DragThreshold * DragThreshold) dragging = true;
            if (dragging) transform.position += dragGrab - ScreenToWorld(screen);

            ClampToMap();
        }

        Vector3 ScreenToWorld(Vector2 screen)
        {
            var p = cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 0f));
            p.z = 0f;
            return p;
        }

        Bounds PlayArea()
        {
            var b = Map.WorldBounds;
            b.Expand(Margin * 2f);
            return b;
        }

        /// <summary>Zoomed out far enough to see the whole map, but no further.</summary>
        float MaxSize()
        {
            var b = PlayArea();
            return Mathf.Max(MinSize, Mathf.Min(b.extents.y, b.extents.x / cam.aspect));
        }

        void ClampToMap()
        {
            var b = PlayArea();
            float halfH = cam.orthographicSize, halfW = halfH * cam.aspect;
            var p = transform.position;
            p.x = b.size.x <= halfW * 2f ? b.center.x : Mathf.Clamp(p.x, b.min.x + halfW, b.max.x - halfW);
            p.y = b.size.y <= halfH * 2f ? b.center.y : Mathf.Clamp(p.y, b.min.y + halfH, b.max.y - halfH);
            transform.position = p;
        }
    }
}
