using UnityEngine;
using UnityEngine.InputSystem;

namespace DungeonConveyor
{
    /// <summary>Fixed-pitch "2.5D" camera: WASD/arrows/middle-drag to pan, wheel to zoom, Q/E to rotate 90°.</summary>
    [RequireComponent(typeof(Camera))]
    public class CameraController : MonoBehaviour
    {
        public float Pitch = 52f;
        public float MinDistance = 9f, MaxDistance = 48f;
        public float PanSpeed = 1.1f;   // screen-heights per second at any zoom

        public Camera Cam { get; private set; }
        public Vector3 Focus => focus;

        Vector3 focus;
        float distance = 26f, targetDistance = 26f;
        float yaw = 45f, targetYaw = 45f;
        Vector3? dragAnchor;

        void Awake()
        {
            Cam = GetComponent<Camera>();
        }

        public void LookAt(Vector3 p)
        {
            focus = new Vector3(p.x, 0, p.z);
            Apply();
        }

        public void Zoom(float steps) => targetDistance = Mathf.Clamp(targetDistance * Mathf.Pow(0.88f, steps), MinDistance, MaxDistance);

        void LateUpdate()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            float dt = Time.unscaledDeltaTime;
            var rot = Quaternion.Euler(0, yaw, 0);

            if (kb != null)
            {
                var move = Vector3.zero;
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) move += Vector3.forward;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) move += Vector3.back;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) move += Vector3.left;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) move += Vector3.right;
                float boost = kb.leftShiftKey.isPressed ? 2f : 1f;
                focus += rot * move.normalized * (PanSpeed * distance * boost * dt);

                if (kb.qKey.wasPressedThisFrame) targetYaw -= 90f;
                if (kb.eKey.wasPressedThisFrame) targetYaw += 90f;
            }

            if (mouse != null)
            {
                var mp = mouse.position.ReadValue();
                if (!HUD.IsPointerOverUI(mp))
                {
                    float scroll = mouse.scroll.ReadValue().y;
                    if (Mathf.Abs(scroll) > 0.01f) Zoom(Mathf.Sign(scroll));
                }

                if (mouse.middleButton.wasPressedThisFrame) dragAnchor = GroundPoint(mp);
                if (!mouse.middleButton.isPressed) dragAnchor = null;
                if (dragAnchor.HasValue)
                {
                    Apply();
                    var now = GroundPoint(mp);
                    if (now.HasValue) focus += dragAnchor.Value - now.Value;
                }
            }

            focus.x = Mathf.Clamp(focus.x, 0, GridMap.Width);
            focus.z = Mathf.Clamp(focus.z, 0, GridMap.Height);
            distance = Mathf.Lerp(distance, targetDistance, 1f - Mathf.Exp(-12f * dt));
            yaw = Mathf.LerpAngle(yaw, targetYaw, 1f - Mathf.Exp(-10f * dt));
            Apply();
        }

        void Apply()
        {
            var r = Quaternion.Euler(Pitch, yaw, 0);
            transform.rotation = r;
            transform.position = focus - r * Vector3.forward * distance;
        }

        /// <summary>Screen point to ground plane (y = 0).</summary>
        public Vector3? GroundPoint(Vector2 screen)
        {
            var ray = Cam.ScreenPointToRay(screen);
            if (Mathf.Abs(ray.direction.y) < 1e-4f) return null;
            float t = -ray.origin.y / ray.direction.y;
            return t > 0 ? ray.origin + ray.direction * t : (Vector3?)null;
        }

        /// <summary>The world-space quad the camera sees on the ground (for the minimap).</summary>
        public Vector3[] ViewCorners()
        {
            var w = Screen.width; var h = Screen.height;
            var pts = new[] { new Vector2(0, 0), new Vector2(0, h), new Vector2(w, h), new Vector2(w, 0) };
            var result = new Vector3[4];
            for (int i = 0; i < 4; i++) result[i] = GroundPoint(pts[i]) ?? focus;
            return result;
        }
    }
}
