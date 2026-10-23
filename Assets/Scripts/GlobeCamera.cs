using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// The camera of the strategic map: it flies round the globe, always looking at its centre, with north up.
///  - Hold the left mouse button and drag: the globe turns under the hand (and goes on turning a little after it is let go).
///  - Mouse wheel: closer to the Earth and farther from it.
///  - Arrow keys or WASD: move the view over the globe without the mouse.
///  - FocusOn(latitude, longitude) takes the camera smoothly to a place (any input of the player stops it).
/// A click (a press and release that did not move) is reported by TryGetClick, so that the one who listens can tell a click on a mission
/// from a turn of the globe. Presses that begin over the interface are ignored.
/// Put it on the camera.
/// </summary>
[RequireComponent(typeof(Camera))]
public class GlobeCamera : MonoBehaviour
{
    [Header("The globe")]
    [SerializeField] private Transform globe;
    [Tooltip("The radius of the globe in world units (its transform's scale).")]
    [SerializeField, Min(0.1f)] private float radius = 5f;

    [Header("Place")]
    [Tooltip("The place on the Earth the camera is above.")]
    [SerializeField, Range(-80f, 80f)] private float latitude = 25f;
    [SerializeField, Range(-180f, 180f)] private float longitude = 20f;

    [Header("Zoom (the distance from the centre of the globe, in globe radii)")]
    [SerializeField, Min(1.05f)] private float startDistance = 3.3f;
    [SerializeField, Min(1.05f)] private float closestDistance = 1.35f;
    [SerializeField, Min(1.1f)] private float farthestDistance = 5.5f;
    [Tooltip("How much one notch of the wheel zooms (the distance is multiplied by e to the power of this).")]
    [SerializeField, Min(0.01f)] private float zoomPerNotch = 0.14f;
    [SerializeField, Min(1f)] private float zoomSmoothing = 12f;

    [Header("Turning")]
    [Tooltip("How quickly the globe slows down after the mouse is let go (higher = stops sooner).")]
    [SerializeField, Min(0.1f)] private float inertiaDamping = 3.5f;
    [Tooltip("Degrees per second the arrow keys turn the globe when zoomed out (less when close).")]
    [SerializeField, Min(1f)] private float keyboardSpeed = 55f;
    [Tooltip("The mouse has to move farther than this many pixels between press and release for it to count as a drag, not a click.")]
    [SerializeField, Min(1f)] private float clickTolerance = 6f;

    private Camera cam;
    private float distance;            // in globe radii
    private float targetDistance;
    private float velocityLat, velocityLon;
    private bool dragging;
    private float dragMoved;
    private Vector2 lastPointer;
    private float sinceLastMove;
    private bool clickPending;
    private Vector2 clickPosition;

    private bool gliding;
    private float glideTime, glideDuration;
    private float glideFromLat, glideFromLon, glideFromDistance;
    private float glideToLat, glideToLon, glideToDistance;

    /// <summary>The camera.</summary>
    public Camera Cam => cam;

    /// <summary>The radius of the globe in world units.</summary>
    public float Radius => radius;

    /// <summary>How far the camera is from the centre of the globe, in world units.</summary>
    public float Distance => distance * radius;

    public float Latitude => latitude;
    public float Longitude => longitude;

    /// <summary>True while the player holds the button and drags the globe.</summary>
    public bool IsDragging => dragging && dragMoved > clickTolerance;

    private void Awake()
    {
        cam = GetComponent<Camera>();
        distance = targetDistance = Mathf.Clamp(startDistance, closestDistance, farthestDistance);
        ApplyPose();
    }

    private void OnValidate()
    {
        if (farthestDistance < closestDistance)
            farthestDistance = closestDistance;
    }

    private void Update()
    {
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
        Vector2 pointer = PointerPosition();
        bool overUi = GameSession.ModalOpen || (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject());
        bool userInput = false;

        // ---- the mouse ----
        if (PrimaryPressed() && !overUi)
        {
            dragging = true;
            dragMoved = 0f;
            lastPointer = pointer;
            sinceLastMove = 0f;
            velocityLat = velocityLon = 0f;
        }

        if (dragging && PrimaryHeld())
        {
            Vector2 delta = pointer - lastPointer;
            lastPointer = pointer;
            dragMoved += delta.magnitude;
            if (delta.sqrMagnitude > 0f)
            {
                float k = DegreesPerPixel();
                longitude -= delta.x * k;                    // the globe follows the hand: a drag to the right turns the surface to the right
                latitude -= delta.y * k;
                if (dt > 0f)
                {
                    velocityLon = Mathf.Lerp(velocityLon, -delta.x * k / dt, 0.5f);
                    velocityLat = Mathf.Lerp(velocityLat, -delta.y * k / dt, 0.5f);
                }
                sinceLastMove = 0f;
                if (dragMoved > clickTolerance)
                    userInput = true;
            }
            else
            {
                sinceLastMove += dt;
            }
        }

        if (dragging && PrimaryReleased())
        {
            dragging = false;
            if (dragMoved <= clickTolerance)
            {
                clickPending = true;
                clickPosition = pointer;
                velocityLat = velocityLon = 0f;
            }
            else if (sinceLastMove > 0.08f)
            {
                velocityLat = velocityLon = 0f;              // the hand stopped before it let go: no coasting
            }
        }

        // ---- the wheel ----
        float scroll = ScrollNotches();
        if (Mathf.Abs(scroll) > 0.001f && !overUi)
        {
            targetDistance = Mathf.Clamp(targetDistance * Mathf.Exp(-scroll * zoomPerNotch), closestDistance, farthestDistance);
            userInput = true;
        }

        // ---- the keys ----
        Vector2 keys = GameSession.ModalOpen ? Vector2.zero : KeyboardTurn();
        if (keys.sqrMagnitude > 0f)
        {
            float k = keyboardSpeed * dt * Mathf.Lerp(0.3f, 1f, Mathf.InverseLerp(closestDistance, farthestDistance, distance));      // slower when close
            longitude += keys.x * k;
            latitude += keys.y * k;
            velocityLat = velocityLon = 0f;
            userInput = true;
        }

        if (userInput)
            gliding = false;

        // ---- gliding to a place ----
        if (gliding)
        {
            glideTime += dt;
            float t = glideDuration > 0f ? Mathf.Clamp01(glideTime / glideDuration) : 1f;
            float e = t * t * (3f - 2f * t);
            latitude = Mathf.Lerp(glideFromLat, glideToLat, e);
            longitude = Mathf.LerpAngle(glideFromLon, glideToLon, e);
            distance = targetDistance = Mathf.Lerp(glideFromDistance, glideToDistance, e);
            if (t >= 1f)
                gliding = false;
        }
        else
        {
            // ---- coasting after a drag ----
            if (!dragging)
            {
                longitude += velocityLon * dt;
                latitude += velocityLat * dt;
                float damp = Mathf.Exp(-inertiaDamping * dt);
                velocityLon *= damp;
                velocityLat *= damp;
                if (Mathf.Abs(velocityLon) < 0.01f) velocityLon = 0f;
                if (Mathf.Abs(velocityLat) < 0.01f) velocityLat = 0f;
            }

            distance = Mathf.Lerp(distance, targetDistance, 1f - Mathf.Exp(-zoomSmoothing * dt));
        }

        latitude = Mathf.Clamp(latitude, -80f, 80f);
        longitude = Mathf.Repeat(longitude + 180f, 360f) - 180f;

        ApplyPose();
    }

    /// <summary>
    /// True once, in the frame the player let go of the button without having dragged: a click. 'position' is where on the screen.
    /// (Reading it takes the click away.)
    /// </summary>
    public bool TryGetClick(out Vector2 position)
    {
        position = clickPosition;
        bool result = clickPending;
        clickPending = false;
        return result;
    }

    /// <summary>
    /// Takes the camera smoothly above a place. With a distance (in globe radii) it also zooms there; otherwise the zoom stays
    /// (but not farther out than the 'comfortable' distance, so that the place is seen well).
    /// </summary>
    public void FocusOn(float lat, float lon, float seconds, float zoomDistance = -1f)
    {
        glideFromLat = latitude;
        glideFromLon = longitude;
        glideFromDistance = distance;
        glideToLat = Mathf.Clamp(lat, -80f, 80f);
        glideToLon = lon;
        glideToDistance = zoomDistance > 0f
            ? Mathf.Clamp(zoomDistance, closestDistance, farthestDistance)
            : Mathf.Min(distance, Mathf.Lerp(closestDistance, farthestDistance, 0.45f));
        glideDuration = Mathf.Max(0.01f, seconds);
        glideTime = 0f;
        gliding = true;
        velocityLat = velocityLon = 0f;
    }

    /// <summary>Puts the camera above a place at once.</summary>
    public void JumpTo(float lat, float lon)
    {
        latitude = Mathf.Clamp(lat, -80f, 80f);
        longitude = lon;
        gliding = false;
        velocityLat = velocityLon = 0f;
        ApplyPose();
    }

    // How many degrees of the Earth's surface one pixel of the mouse is worth, so that the point under the cursor stays under it.
    private float DegreesPerPixel()
    {
        float height = Mathf.Max(1f, Screen.height);
        float surfaceDistance = Mathf.Max(0.05f, (distance - 1f) * radius);                 // from the camera to the near side of the globe
        float worldPerPixel = 2f * surfaceDistance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / height;
        return worldPerPixel / radius * Mathf.Rad2Deg;
    }

    private void ApplyPose()
    {
        if (cam == null)
            cam = GetComponent<Camera>();
        Vector3 centre = globe != null ? globe.position : Vector3.zero;
        Vector3 dir = Geo.Direction(latitude, longitude);
        if (globe != null)
            dir = globe.rotation * dir;
        transform.position = centre + dir * (distance * radius);
        transform.rotation = Quaternion.LookRotation(-dir, Vector3.up);
    }

    // ---- input (the new input system, or the old one if that is what the project uses) ----

    private static Vector2 PointerPosition()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null)
            return Mouse.current.position.ReadValue();
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.mousePosition;
#else
        return Vector2.zero;
#endif
    }

    private static bool PrimaryPressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetMouseButtonDown(0))
            return true;
#endif
        return false;
    }

    private static bool PrimaryHeld()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null && Mouse.current.leftButton.isPressed)
            return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetMouseButton(0))
            return true;
#endif
        return false;
    }

    private static bool PrimaryReleased()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame)
            return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetMouseButtonUp(0))
            return true;
#endif
        return false;
    }

    private static float ScrollNotches()
    {
        float s = 0f;
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null)
            s = Mouse.current.scroll.ReadValue().y;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Mathf.Approximately(s, 0f))
            s = Input.mouseScrollDelta.y;
#endif
        if (Mathf.Abs(s) > 5f)
            s /= 120f;                                  // some systems report 120 per notch
        return Mathf.Clamp(s, -3f, 3f);
    }

    private static Vector2 KeyboardTurn()
    {
        Vector2 v = Vector2.zero;
#if ENABLE_INPUT_SYSTEM
        Keyboard k = Keyboard.current;
        if (k != null)
        {
            if (k.leftArrowKey.isPressed || k.aKey.isPressed) v.x -= 1f;
            if (k.rightArrowKey.isPressed || k.dKey.isPressed) v.x += 1f;
            if (k.downArrowKey.isPressed || k.sKey.isPressed) v.y -= 1f;
            if (k.upArrowKey.isPressed || k.wKey.isPressed) v.y += 1f;
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (v == Vector2.zero)
        {
            if (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A)) v.x -= 1f;
            if (Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D)) v.x += 1f;
            if (Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.S)) v.y -= 1f;
            if (Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.W)) v.y += 1f;
        }
#endif
        return v;
    }
}
