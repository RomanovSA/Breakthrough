using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Highlights the tile under the mouse cursor and removes the highlight
/// as soon as the cursor leaves the tile. Nothing is highlighted while the cursor is over UI.
/// </summary>
public class TileHover : MonoBehaviour
{
    [SerializeField] private Camera targetCamera;
    [SerializeField] private LayerMask raycastMask = Physics.DefaultRaycastLayers;   // everything except 'Ignore Raycast'

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

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return null;

        // Ignore the cursor while it is outside of the camera's view.
        if (!targetCamera.pixelRect.Contains(screenPos))
            return null;

        Ray ray = targetCamera.ScreenPointToRay(screenPos);
        if (!Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, raycastMask))
            return null;

        // If the first thing under the cursor is a unit or a wall (not a tile), nothing is highlighted.
        // a tile under a building, car, palm... is not highlighted; the roof of a building (with a ladder) is
        return Tile.FromCollider(hit.collider);
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
