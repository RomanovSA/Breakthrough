using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Highlights the tile under the mouse cursor and removes the highlight
/// as soon as the cursor leaves the tile.
/// </summary>
public class TileHover : MonoBehaviour
{
    [SerializeField] private Camera targetCamera;
    [SerializeField] private LayerMask raycastMask = ~0;

    private Tile hoveredTile;

    private void Update()
    {
        SetHovered(FindTileUnderCursor());
    }

    private void OnDisable()
    {
        SetHovered(null);
    }

    private Tile FindTileUnderCursor()
    {
        if (targetCamera == null)
            targetCamera = Camera.main;
        if (targetCamera == null)
            return null;

        if (!TryGetPointerPosition(out Vector2 screenPos))
            return null;

        // Ignore the cursor while it is outside of the camera's view.
        if (!targetCamera.pixelRect.Contains(screenPos))
            return null;

        Ray ray = targetCamera.ScreenPointToRay(screenPos);
        if (!Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, raycastMask))
            return null;

        // If the first thing under the cursor is a unit (not a tile), nothing is highlighted.
        return hit.collider.GetComponentInParent<Tile>();
    }

    private void SetHovered(Tile tile)
    {
        if (tile == hoveredTile)
            return;

        if (hoveredTile != null)
            hoveredTile.SetHighlighted(false);

        hoveredTile = tile;

        if (hoveredTile != null)
            hoveredTile.SetHighlighted(true);
    }

    private static bool TryGetPointerPosition(out Vector2 screenPos)
    {
        screenPos = default;

#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null)
        {
            screenPos = Mouse.current.position.ReadValue();
            return true;
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        screenPos = Input.mousePosition;
        return true;
#else
        return false;
#endif
    }
}
