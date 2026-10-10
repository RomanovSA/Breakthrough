using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Click a unit to select it, click it again to deselect it.
/// With a unit selected, click a tile to send the unit there.
/// </summary>
public class UnitSelector : MonoBehaviour
{
    [SerializeField] private Camera targetCamera;
    [SerializeField] private LayerMask raycastMask = ~0;

    private Unit selectedUnit;

    private void Update()
    {
        if (!TryGetClick(out Vector2 screenPos))
            return;

        if (targetCamera == null)
            targetCamera = Camera.main;
        if (targetCamera == null)
            return;

        Ray ray = targetCamera.ScreenPointToRay(screenPos);
        if (!Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, raycastMask))
            return;

        Unit unit = hit.collider.GetComponentInParent<Unit>();
        if (unit != null)
        {
            HandleUnitClick(unit);
            return;
        }

        Tile tile = hit.collider.GetComponentInParent<Tile>();
        if (tile != null && selectedUnit != null)
            selectedUnit.MoveTo(tile);
    }

    private void HandleUnitClick(Unit unit)
    {
        if (unit == selectedUnit)
        {
            unit.SetSelected(false);
            selectedUnit = null;
            return;
        }

        if (selectedUnit != null)
            selectedUnit.SetSelected(false);

        selectedUnit = unit;
        unit.SetSelected(true);
    }

    private static bool TryGetClick(out Vector2 screenPos)
    {
        screenPos = default;

#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            screenPos = Mouse.current.position.ReadValue();
            return true;
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetMouseButtonDown(0))
        {
            screenPos = Input.mousePosition;
            return true;
        }
#endif
        return false;
    }
}
