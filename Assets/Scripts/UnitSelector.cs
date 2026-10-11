using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Click a unit of your own faction to select it, click it again to deselect it, click another one to
/// switch. With a unit selected, click a tile to send the unit there.
/// Units of other factions cannot be selected or given orders, clicking them does nothing.
/// Clicks on UI (for example the End Turn button) are ignored.
/// </summary>
public class UnitSelector : MonoBehaviour
{
    [SerializeField] private Camera targetCamera;
    [SerializeField] private LayerMask raycastMask = ~0;

    [Tooltip("Only units of this faction can be selected and given orders.")]
    [SerializeField] private Faction controlledFaction = Faction.Player;

    private Unit selectedUnit;

    /// <summary>The unit that is currently selected (null if none).</summary>
    public Unit SelectedUnit => selectedUnit;

    /// <summary>The faction the player is allowed to control.</summary>
    public Faction ControlledFaction => controlledFaction;

    private void Update()
    {
        if (!TryGetClick(out Vector2 screenPos))
            return;

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
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
        // Not ours: it can neither be selected nor change the current selection.
        if (unit.Faction != controlledFaction)
            return;

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
