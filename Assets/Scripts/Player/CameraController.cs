using UnityEngine;
using UnityEngine.InputSystem;

namespace DungeonConveyor
{
    /// <summary>
    /// Perspective orbit camera around the planet.
    /// - Left- or middle-drag turns the planet: the point grabbed stays under the cursor (or, off the planet, the drag
    ///   spins it). A drag only starts once the cursor moves a few pixels, so plain clicks still reach the ground.
    ///   While drawing belts or walls only the middle button turns it.
    /// - Scroll zooms; close in, the view tilts toward the horizon.
    /// - Arrow keys / WASD also turn it.
    /// The scene's directional light follows the camera, so the side you look at is always lit.
    /// Starts looking down on the main base.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CameraController : MonoBehaviour
    {
        public IsoGrid Map;
        [Tooltip("Closest distance between the camera and the ground.")]
        public float MinAltitude = 5f;
        [Tooltip("Farthest distance from the planet centre, in planet radii.")]
        public float MaxDistance = 6f;
        [Tooltip("Distance multiplier per scroll notch.")]
        public float ZoomStep = 1.12f;
        public float ZoomSmoothing = 12f;
        [Tooltip("Pixels the cursor must move while held before a click turns into a drag.")]
        public float DragThreshold = 6f;
        [Tooltip("View tilt toward the horizon when fully zoomed in, in degrees.")]
        public float CloseTilt = 38f;
        [Tooltip("Degrees per second the keys turn the planet when zoomed out (slower close in).")]
        public float KeySpeed = 70f;
        public float FieldOfView = 40f;
        [Tooltip("The sun's direction relative to the camera.")]
        public Vector3 SunAngles = new Vector3(28f, 32f, 0f);
        public Color Ambient = new Color(0.3f, 0.32f, 0.42f);

        /// <summary>True while a drag is turning the planet (including the frame the button is released).</summary>
        public bool IsDragging => dragging;

        static float trauma;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => trauma = 0f;

        /// <summary>
        /// Shakes the view a little (boss hits and landings): amount 0..1 adds up and decays; the shake grows with its
        /// square, so small knocks stay subtle. Off with the HUD's reduced-motion setting.
        /// </summary>
        public static void Shake(float amount)
        {
            if (HudFx.ReducedMotion) return;
            trauma = Mathf.Clamp01(trauma + amount);
        }

        Camera cam;
        Quaternion orbit = Quaternion.identity;   // camera rotation before tilting; looks at the planet centre
        float distance, targetDistance;
        bool pressed, pressedOnUI, dragging, placed;
        Vector2 pressScreen, lastScreen;
        Vector3 grab;                             // planet point (unit vector) held under the cursor, if any
        bool grabbing;
        BuildController build;
        Light sun;

        void Awake()
        {
            cam = GetComponent<Camera>();
            cam.orthographic = false;
            cam.fieldOfView = FieldOfView;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 3000f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            if (Map == null) Map = FindAnyObjectByType<IsoGrid>();
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) { sun = l; break; }
        }

        float Radius => Map != null && Map.N > 0 ? Map.Radius : 10f;
        float MinDist => Radius + IsoGrid.MaxLevel * IsoGrid.StepHeight + MinAltitude;
        float MaxDist => Mathf.Max(MinDist + 1f, Radius * MaxDistance);

        void LateUpdate()
        {
            if (Map == null || Map.N <= 0) return;
            if (!placed) PlaceOverBase();

            var mouse = Mouse.current;
            if (mouse != null)
            {
                Vector2 screen = mouse.position.ReadValue();
                float scroll = mouse.scroll.ReadValue().y;
                if (scroll != 0f && !HUD.IsPointerOverUI(screen)) targetDistance *= scroll > 0f ? 1f / ZoomStep : ZoomStep;
                Drag(mouse, screen);
            }
            Keys();

            targetDistance = Mathf.Clamp(targetDistance, MinDist, MaxDist);
            distance = Mathf.Lerp(distance, targetDistance, 1f - Mathf.Exp(-ZoomSmoothing * Time.unscaledDeltaTime));
            Apply();
        }

        void PlaceOverBase()
        {
            placed = true;
            var up = Map.BaseUp(0);
            var along = Vector3.ProjectOnPlane(Vector3.up, up);
            if (along.sqrMagnitude < 1e-4f) along = Vector3.ProjectOnPlane(Vector3.forward, up);
            orbit = Quaternion.LookRotation(-up, along);
            distance = targetDistance = Mathf.Lerp(MinDist, MaxDist, 0.3f);
            Apply();
        }

        /// <summary>Close in, the camera swings back over the ground and looks across it.</summary>
        float Tilt => CloseTilt * (1f - Mathf.InverseLerp(MinDist, Mathf.Lerp(MinDist, MaxDist, 0.6f), distance));

        void Apply()
        {
            var anchor = orbit * Vector3.back * Radius;          // ground point the view is centred on
            var rotation = orbit * Quaternion.Euler(-Tilt, 0f, 0f);
            transform.SetPositionAndRotation(anchor - rotation * Vector3.forward * (distance - Radius), rotation);
            if (trauma > 0f && Application.isPlaying)
            {
                float k = trauma * trauma, time = Time.unscaledTime * 30f;
                transform.rotation *= Quaternion.Euler(
                    (Mathf.PerlinNoise(time, 0.1f) - 0.5f) * 2.4f * k,
                    (Mathf.PerlinNoise(0.7f, time) - 0.5f) * 2.4f * k,
                    (Mathf.PerlinNoise(time, 3.3f) - 0.5f) * 1.6f * k);
                trauma = Mathf.Max(0f, trauma - Time.unscaledDeltaTime * 1.6f);
            }

            if (sun != null) sun.transform.rotation = rotation * Quaternion.Euler(SunAngles);
            var toSun = sun != null ? -sun.transform.forward : -transform.forward;
            var sunColor = sun != null ? sun.color * sun.intensity : Color.white;
            Shader.SetGlobalVector("_PlanetSunDir", toSun);
            Shader.SetGlobalColor("_PlanetSunColor", sunColor);
            Shader.SetGlobalColor("_PlanetAmbient", Ambient);
        }

        void Drag(Mouse mouse, Vector2 screen)
        {
            // A press only becomes a drag once the cursor moves past the threshold, so plain clicks stay clicks.
            if (build == null) build = Map.GetComponent<BuildController>();
            bool leftTurns = build == null || !build.CapturesLeftDrag; // left-drag draws belts while placing them
            bool held = (leftTurns && mouse.leftButton.isPressed) || mouse.middleButton.isPressed;
            if (held && !pressed)
            {
                pressed = true;
                pressedOnUI = HUD.IsPointerOverUI(screen);
                pressScreen = lastScreen = screen;
                grabbing = HitSphere(screen, out grab);
            }
            else if (!held) pressed = dragging = false;
            if (pressed && !pressedOnUI && !dragging && (screen - pressScreen).sqrMagnitude > DragThreshold * DragThreshold) dragging = true;

            if (dragging)
            {
                if (grabbing && HitSphere(screen, out var now))
                    orbit = Quaternion.FromToRotation(now, grab) * orbit; // turn so the grabbed point is back under the cursor
                else
                {
                    var delta = screen - lastScreen;
                    float degPerPixel = 0.25f * Mathf.InverseLerp(Radius, MaxDist, distance) + 0.03f;
                    orbit = orbit * Quaternion.Euler(-delta.y * degPerPixel, delta.x * degPerPixel, 0f); // the planet follows the drag
                    grabbing = false;
                }
            }
            lastScreen = screen;
        }

        void Keys()
        {
            var k = Keyboard.current;
            if (k == null) return;
            float x = (k.dKey.isPressed || k.rightArrowKey.isPressed ? 1f : 0f) - (k.aKey.isPressed || k.leftArrowKey.isPressed ? 1f : 0f);
            float y = (k.wKey.isPressed || k.upArrowKey.isPressed ? 1f : 0f) - (k.sKey.isPressed || k.downArrowKey.isPressed ? 1f : 0f);
            if (x == 0f && y == 0f) return;
            float speed = KeySpeed * Mathf.Lerp(0.25f, 1f, Mathf.InverseLerp(MinDist, MaxDist, distance)) * Time.unscaledDeltaTime;
            // Turning the orbit about its own x / y swings the camera toward its up / left.
            orbit = orbit * Quaternion.Euler(y * speed, -x * speed, 0f);
        }

        /// <summary>Where the ray under a screen point meets the planet's ground sphere, as a unit vector.</summary>
        bool HitSphere(Vector2 screen, out Vector3 dir)
        {
            dir = default;
            var ray = cam.ScreenPointToRay(screen);
            float r = Radius;
            float b = Vector3.Dot(ray.origin, ray.direction);
            float c = ray.origin.sqrMagnitude - r * r;
            float disc = b * b - c;
            if (disc < 0f) return false;
            float t = -b - Mathf.Sqrt(disc);
            if (t < 0f) return false;
            dir = (ray.origin + ray.direction * t).normalized;
            return true;
        }
    }
}
