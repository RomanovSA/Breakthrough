using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Isometric camera for the tile field:
/// starts close to the player's unit (the whole field is one Frame() away: see the context menu),
/// follows the selected unit while it is on the move,
/// orbits around the field in 45 degree steps (Q = left, E = right)
/// pans over the field with W / A / S / D and zooms with the mouse wheel (towards the cursor).
/// </summary>
[RequireComponent(typeof(Camera))]
public class IsometricCamera : MonoBehaviour
{
    [Header("View")]
    [SerializeField] private GridManager grid;
    [SerializeField] private float pitch = 35.264f; // true isometric; use 30 for the classic game look
    [SerializeField] private float yaw = 45f;
    [SerializeField] private float distance = 50f;
    [SerializeField] private float padding = 1f;

    [Header("Start and follow")]
    [Tooltip("On start (and whenever the units appear) the camera is put close to the player's unit instead of showing the whole field.")]
    [SerializeField] private bool startOnPlayerUnit = true;
    [Tooltip("The camera size at the start: half the height of the picture in world units. Smaller = closer.")]
    [SerializeField, Min(0.5f)] private float startSize = 5.5f;
    [Tooltip("While the selected unit moves, the camera follows it. Panning with W A S D takes the camera away from it until the next move.")]
    [SerializeField] private bool followMovingUnit = true;
    [Tooltip("How quickly the camera catches up with the moving unit. Higher = tighter (the camera lags by about speed / this).")]
    [SerializeField, Min(0.5f)] private float followSharpness = 6f;

    [Header("Rotation (Q / E)")]
    [SerializeField] private float rotationStep = 45f;
    [SerializeField] private float rotationSpeed = 270f; // degrees per second

    [Header("Movement (W A S D)")]
    [SerializeField] private float panSpeed = 8f; // world units per second
    [Tooltip("The camera size at which Pan Speed applies as it is. Zoomed out the camera pans faster, zoomed in slower.")]
    [SerializeField, Min(0.1f)] private float panReferenceSize = 5.5f;
    [SerializeField] private bool clampToField = true;
    [SerializeField] private float panMargin = 2f; // how far past the field edge the camera may look

    [Header("Zoom (mouse wheel)")]
    [Tooltip("How much one notch of the wheel zooms: 0.15 = about 16% per notch.")]
    [SerializeField, Min(0.01f)] private float zoomSpeed = 0.15f;
    [Tooltip("Closest zoom: the smallest camera size (half the height of the picture, in world units).")]
    [SerializeField, Min(0.1f)] private float closestSize = 1.4f;
    [Tooltip("Farthest zoom, as a share of the size that fits the whole field (1.5 = a bit farther than 'whole field').")]
    [SerializeField, Range(1f, 3f)] private float farthestZoom = 1.5f;
    [Tooltip("How quickly the picture catches up with the wheel. Higher = snappier.")]
    [SerializeField, Min(1f)] private float zoomSmoothing = 12f;

    private Vector3 pivot;
    private Bounds fieldBounds;
    private float currentYaw;
    private float targetYaw;
    private float viewDistance;   // how far the camera sits from what it looks at (grows with the size of the field)
    private Camera zoomCamera;
    private float fitSize;        // the size that shows the whole field (set by Frame)
    private float targetZoomSize; // where the wheel wants the size to go
    private Vector2 zoomAnchor;   // the screen spot that stays put while zooming (the cursor)
    private UnitSelector selector;
    private UnitSpawner spawner;
    private bool followActive;
    private bool wasMoving;
    private Unit followedUnit;
    private bool glideActive;
    private Vector3 glideTarget;

    private void OnEnable()
    {
        if (grid == null)
            grid = FindFirstObjectByType<GridManager>();
        if (grid != null)
            grid.GridBuilt += FrameAndFocus; // re-frame whenever the field is rebuilt

        spawner = FindFirstObjectByType<UnitSpawner>();
        if (spawner != null)
            spawner.UnitsSpawned += OnUnitsSpawned;     // new units: go to the player's
    }

    private void OnDisable()
    {
        if (grid != null)
            grid.GridBuilt -= FrameAndFocus;
        if (spawner != null)
            spawner.UnitsSpawned -= OnUnitsSpawned;
    }

    private void Start()
    {
        FrameAndFocus();
    }

    private void Update()
    {
        bool changed = false;

        if (LeftPressed())
            targetYaw -= rotationStep;
        if (RightPressed())
            targetYaw += rotationStep;

        if (!Mathf.Approximately(currentYaw, targetYaw))
        {
            currentYaw = Mathf.MoveTowards(currentYaw, targetYaw, rotationSpeed * Time.deltaTime);
            changed = true;
        }

        Vector2 move = ReadMove();
        if (move.sqrMagnitude > 0f)
        {
            Pan(move, Time.deltaTime);
            changed = true;
        }

        if (changed)
            ApplyTransform();

        UpdateFollow(Time.deltaTime, move.sqrMagnitude > 0f);

        HandleZoom(Time.deltaTime);
    }

    // The whole field, and then (if wanted) close to the player's unit.
    private void FrameAndFocus()
    {
        Frame();
        if (startOnPlayerUnit)
            FocusOnPlayerUnit();
    }

    /// <summary>
    /// Puts the camera close to the selected unit (or, if none is selected, to the first living unit of the player's faction), at the
    /// start size, at once. Returns false if there is no such unit yet.
    /// </summary>
    public bool FocusOnPlayerUnit()
    {
        if (!startOnPlayerUnit || fitSize <= 0f)
            return false;

        Unit unit = FindUnitToFocus();
        if (unit == null)
            return false;

        if (zoomCamera == null)
            zoomCamera = GetComponent<Camera>();

        float size = Mathf.Clamp(startSize, Mathf.Min(closestSize, fitSize), fitSize * farthestZoom);
        zoomCamera.orthographicSize = size;
        targetZoomSize = size;
        zoomAnchor = new Vector2(zoomCamera.pixelWidth * 0.5f, zoomCamera.pixelHeight * 0.5f);

        Vector3 p = unit.transform.position;
        pivot = new Vector3(p.x, pivot.y, p.z);
        ClampPivot();
        ApplyTransform();
        return true;
    }

    private void OnUnitsSpawned()
    {
        FocusOnPlayerUnit();
    }

    private Unit FindUnitToFocus()
    {
        if (selector == null)
            selector = FindFirstObjectByType<UnitSelector>();

        if (selector != null && selector.SelectedUnit != null && selector.SelectedUnit.IsAlive)
            return selector.SelectedUnit;

        Faction mine = selector != null ? selector.ControlledFaction : Faction.Player;
        foreach (Unit u in Unit.All)
        {
            if (u != null && u.IsAlive && u.Faction == mine)
                return u;
        }
        return null;
    }

    /// <summary>
    /// The camera glides to look at this point (the enemy phase uses it to show what happens, the new turn to go back to the player's
    /// unit). Panning with W A S D takes the camera away from the point.
    /// </summary>
    public void GlideTo(Vector3 worldPoint)
    {
        glideTarget = new Vector3(worldPoint.x, pivot.y, worldPoint.z);
        glideActive = true;
    }

    // While the selected unit is on the move the camera glides after it. A move that starts later is followed again.
    private void UpdateFollow(float deltaTime, bool userPanning)
    {
        if (userPanning)
            glideActive = false;
        if (glideActive)
        {
            float g = 1f - Mathf.Exp(-4f * Mathf.Max(0f, deltaTime));
            pivot.x = Mathf.Lerp(pivot.x, glideTarget.x, g);
            pivot.z = Mathf.Lerp(pivot.z, glideTarget.z, g);
            ClampPivot();
            ApplyTransform();
            if (new Vector2(pivot.x - glideTarget.x, pivot.z - glideTarget.z).sqrMagnitude < 0.0009f)
                glideActive = false;
        }

        if (!followMovingUnit)
            return;

        if (selector == null)
            selector = FindFirstObjectByType<UnitSelector>();
        Unit unit = selector != null ? selector.SelectedUnit : null;

        bool moving = unit != null && unit.IsMoving;
        if (userPanning)
            followActive = false;                                   // the player takes the camera away
        else if (moving && (!wasMoving || unit != followedUnit))
            followActive = true;                                    // a move has just started
        if (!moving)
            followActive = false;
        wasMoving = moving;
        followedUnit = unit;

        if (!followActive)
            return;

        Vector3 p = unit.transform.position;
        float k = 1f - Mathf.Exp(-followSharpness * Mathf.Max(0f, deltaTime));
        pivot.x = Mathf.Lerp(pivot.x, p.x, k);
        pivot.z = Mathf.Lerp(pivot.z, p.z, k);
        ClampPivot();
        ApplyTransform();
    }

    [ContextMenu("Frame")]
    public void Frame()
    {
        Camera cam = GetComponent<Camera>();

        if (grid == null)
            grid = FindFirstObjectByType<GridManager>();
        if (grid == null)
            return;

        // Frame the tiles only (walls and labels must not change the framing).
        Tile[] tiles = grid.GetComponentsInChildren<Tile>();
        if (tiles.Length == 0)
            return;

        Bounds bounds = tiles[0].GetComponent<Renderer>().bounds;
        for (int i = 1; i < tiles.Length; i++)
            bounds.Encapsulate(tiles[i].GetComponent<Renderer>().bounds);

        fieldBounds = bounds;
        pivot = bounds.center;
        currentYaw = yaw;
        targetYaw = yaw;

        cam.orthographic = true;
        cam.orthographicSize = ComputeFitSize(bounds, cam.aspect);

        // zoom starts from 'the whole field fits', and (re)framing resets it
        fitSize = cam.orthographicSize;
        targetZoomSize = fitSize;
        zoomAnchor = new Vector2(cam.pixelWidth * 0.5f, cam.pixelHeight * 0.5f);
        cam.nearClipPlane = 0.1f;
        viewDistance = Mathf.Max(distance, bounds.extents.magnitude * 2f + 30f);
        cam.farClipPlane = viewDistance + bounds.extents.magnitude + 80f;

        ApplyTransform();
    }

    // Moves the point the camera looks at. Input is relative to the camera:
    // up = away from the camera, right = to the right on screen.
    private void Pan(Vector2 input, float deltaTime)
    {
        Vector3 dir = Quaternion.Euler(0f, currentYaw, 0f) * new Vector3(input.x, 0f, input.y);
        if (dir.sqrMagnitude > 1f)
            dir.Normalize();

        float zoomFactor = zoomCamera != null ? Mathf.Max(0.1f, zoomCamera.orthographicSize / panReferenceSize) : 1f;
        pivot += dir * (panSpeed * zoomFactor * deltaTime);

        if (clampToField)
        {
            pivot.x = Mathf.Clamp(pivot.x, fieldBounds.min.x - panMargin, fieldBounds.max.x + panMargin);
            pivot.z = Mathf.Clamp(pivot.z, fieldBounds.min.z - panMargin, fieldBounds.max.z + panMargin);
        }
    }

    // ---- zoom ----

    private void HandleZoom(float dt)
    {
        if (fitSize <= 0f)
            return; // not framed yet

        float scroll = ReadScroll();
        if (Mathf.Abs(scroll) > 0.0001f && !PointerOverUi())
            ApplyScroll(scroll, PointerPosition());

        StepZoom(dt);
    }

    // The wheel moves the target size; scrolling up (positive) zooms in. The cursor becomes the zoom anchor.
    private void ApplyScroll(float notches, Vector2 screenPos)
    {
        if (fitSize <= 0f)
            return;

        targetZoomSize = Mathf.Clamp(targetZoomSize * Mathf.Exp(-notches * zoomSpeed), Mathf.Min(closestSize, fitSize), fitSize * farthestZoom);
        zoomAnchor = screenPos;
    }

    // Moves the real size towards the target, keeping the point under the anchor where it is.
    private void StepZoom(float dt)
    {
        if (zoomCamera == null)
            zoomCamera = GetComponent<Camera>();

        float size = zoomCamera.orthographicSize;
        if (Mathf.Approximately(size, targetZoomSize))
            return;

        float next = Mathf.Lerp(size, targetZoomSize, 1f - Mathf.Exp(-zoomSmoothing * Mathf.Max(0f, dt)));
        if (Mathf.Abs(next - targetZoomSize) < fitSize * 0.0005f)
            next = targetZoomSize;

        Vector3 before = GroundPoint(zoomAnchor);
        zoomCamera.orthographicSize = next;
        Vector3 after = GroundPoint(zoomAnchor);

        // slide the camera so the ground point that was under the cursor is under it again
        Vector3 shift = before - after;
        shift.y = 0f;
        pivot += shift;
        ClampPivot();
        ApplyTransform();
    }

    private Vector3 GroundPoint(Vector2 screenPos)
    {
        Ray ray = zoomCamera.ScreenPointToRay(screenPos);
        Plane ground = new Plane(Vector3.up, new Vector3(0f, pivot.y, 0f));
        return ground.Raycast(ray, out float distance) ? ray.GetPoint(distance) : pivot;
    }

    private void ClampPivot()
    {
        if (!clampToField)
            return;

        pivot.x = Mathf.Clamp(pivot.x, fieldBounds.min.x - panMargin, fieldBounds.max.x + panMargin);
        pivot.z = Mathf.Clamp(pivot.z, fieldBounds.min.z - panMargin, fieldBounds.max.z + panMargin);
    }

    private void ApplyTransform()
    {
        transform.rotation = Quaternion.Euler(pitch, currentYaw, 0f);
        transform.position = pivot - transform.forward * (viewDistance > 0f ? viewDistance : distance);
    }

    // Orthographic size that keeps the field fully visible from every yaw angle,
    // so the zoom stays constant while the camera orbits.
    private float ComputeFitSize(Bounds bounds, float aspect)
    {
        Vector3 ext = bounds.extents;
        float best = 0f;

        for (float angle = 0f; angle < 360f; angle += 5f)
        {
            Quaternion rot = Quaternion.Euler(pitch, angle, 0f);
            Vector3 right = rot * Vector3.right;
            Vector3 up = rot * Vector3.up;

            float halfWidth = 0f;
            float halfHeight = 0f;
            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sy = -1; sy <= 1; sy += 2)
                {
                    for (int sz = -1; sz <= 1; sz += 2)
                    {
                        Vector3 corner = new Vector3(sx * ext.x, sy * ext.y, sz * ext.z);
                        halfWidth = Mathf.Max(halfWidth, Mathf.Abs(Vector3.Dot(corner, right)));
                        halfHeight = Mathf.Max(halfHeight, Mathf.Abs(Vector3.Dot(corner, up)));
                    }
                }
            }

            float size = Mathf.Max(halfHeight + padding, (halfWidth + padding) / aspect);
            best = Mathf.Max(best, size);
        }

        return best;
    }

    private static Vector2 ReadMove()
    {
        float x = 0f;
        float y = 0f;

#if ENABLE_INPUT_SYSTEM
        Keyboard kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.wKey.isPressed) y += 1f;
            if (kb.sKey.isPressed) y -= 1f;
            if (kb.dKey.isPressed) x += 1f;
            if (kb.aKey.isPressed) x -= 1f;
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKey(KeyCode.W)) y += 1f;
        if (Input.GetKey(KeyCode.S)) y -= 1f;
        if (Input.GetKey(KeyCode.D)) x += 1f;
        if (Input.GetKey(KeyCode.A)) x -= 1f;
#endif

        return new Vector2(Mathf.Clamp(x, -1f, 1f), Mathf.Clamp(y, -1f, 1f));
    }

    // Wheel notches since the last frame (positive = scrolled up / away from you).
    private static float ReadScroll()
    {
        float scroll = 0f;

#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null)
            scroll += Mouse.current.scroll.ReadValue().y / 120f;   // Windows reports 120 per notch
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        scroll += Input.mouseScrollDelta.y;
#endif
        return scroll;
    }

    private static Vector2 PointerPosition()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null)
            return Mouse.current.position.ReadValue();
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.mousePosition;
#else
        return new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
#endif
    }

    private static bool PointerOverUi()
    {
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }

    private static bool LeftPressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.qKey.wasPressedThisFrame)
            return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.Q))
            return true;
#endif
        return false;
    }

    private static bool RightPressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
            return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.E))
            return true;
#endif
        return false;
    }
}
