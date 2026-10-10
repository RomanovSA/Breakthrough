using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Isometric camera for the tile field:
/// fits the whole GridManager into the frame on Start,
/// orbits around the field in 45 degree steps (Q = left, E = right)
/// and pans over the field with W / A / S / D.
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

    [Header("Rotation (Q / E)")]
    [SerializeField] private float rotationStep = 45f;
    [SerializeField] private float rotationSpeed = 270f; // degrees per second

    [Header("Movement (W A S D)")]
    [SerializeField] private float panSpeed = 8f; // world units per second
    [SerializeField] private bool clampToField = true;
    [SerializeField] private float panMargin = 2f; // how far past the field edge the camera may look

    private Vector3 pivot;
    private Bounds fieldBounds;
    private float currentYaw;
    private float targetYaw;

    private void OnEnable()
    {
        if (grid == null)
            grid = FindFirstObjectByType<GridManager>();
        if (grid != null)
            grid.GridBuilt += Frame; // re-frame whenever the field is rebuilt
    }

    private void OnDisable()
    {
        if (grid != null)
            grid.GridBuilt -= Frame;
    }

    private void Start()
    {
        Frame();
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
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = distance + bounds.extents.magnitude + 10f;

        ApplyTransform();
    }

    // Moves the point the camera looks at. Input is relative to the camera:
    // up = away from the camera, right = to the right on screen.
    private void Pan(Vector2 input, float deltaTime)
    {
        Vector3 dir = Quaternion.Euler(0f, currentYaw, 0f) * new Vector3(input.x, 0f, input.y);
        if (dir.sqrMagnitude > 1f)
            dir.Normalize();

        pivot += dir * (panSpeed * deltaTime);

        if (clampToField)
        {
            pivot.x = Mathf.Clamp(pivot.x, fieldBounds.min.x - panMargin, fieldBounds.max.x + panMargin);
            pivot.z = Mathf.Clamp(pivot.z, fieldBounds.min.z - panMargin, fieldBounds.max.z + panMargin);
        }
    }

    private void ApplyTransform()
    {
        transform.rotation = Quaternion.Euler(pitch, currentYaw, 0f);
        transform.position = pivot - transform.forward * distance;
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
