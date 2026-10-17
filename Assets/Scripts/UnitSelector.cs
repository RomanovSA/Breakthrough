using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Who is selected, and how the selection changes:
///  - when the units appear, the first unit of the player's faction that can act is selected;
///  - click a unit of your own faction to select it, click it again to deselect it;
///  - Tab selects the next of your units that still has action points;
///  - with a unit selected, click a tile to send the unit there;
///  - right click: the selected unit turns its field of view towards the cursor.
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
    private UnitSpawner spawner;

    /// <summary>The unit that is currently selected (null if none).</summary>
    public Unit SelectedUnit => selectedUnit;

    /// <summary>The faction the player is allowed to control.</summary>
    public Faction ControlledFaction => controlledFaction;

    private void OnEnable()
    {
        spawner = FindFirstObjectByType<UnitSpawner>();
        if (spawner != null)
            spawner.UnitsSpawned += OnUnitsSpawned;
    }

    private void OnDisable()
    {
        if (spawner != null)
            spawner.UnitsSpawned -= OnUnitsSpawned;
    }

    private void Start()
    {
        // In case the units were spawned before we started listening.
        if (selectedUnit == null)
            SelectFirstAvailable();
    }

    private void OnUnitsSpawned()
    {
        // The units are new, whatever was selected before is gone.
        SelectFirstAvailable();
    }

    private void Update()
    {
        if (TabPressed())
            SelectNextAvailable();

        if (TryGetRightClick(out Vector2 rightClickPos))
            HandleRightClick(rightClickPos);

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

    /// <summary>Selects the unit (and deselects the previous one). Only units of the controlled faction can be selected.</summary>
    public bool Select(Unit unit)
    {
        if (unit == null || unit.Faction != controlledFaction)
            return false;

        if (unit == selectedUnit)
            return true;

        Deselect();
        selectedUnit = unit;
        unit.SetSelected(true);
        return true;
    }

    public void Deselect()
    {
        if (selectedUnit != null)
            selectedUnit.SetSelected(false);

        selectedUnit = null;
    }

    /// <summary>
    /// Selects the next unit (in spawn order, wrapping around) that still has action points.
    /// With nothing selected it picks the first such unit. Returns false if no unit can act.
    /// </summary>
    public bool SelectNextAvailable()
    {
        List<Unit> mine = CollectControlledUnits();
        if (mine.Count == 0)
            return false;

        int start = selectedUnit != null ? mine.IndexOf(selectedUnit) : -1;

        for (int i = 1; i <= mine.Count; i++)
        {
            Unit candidate = mine[(start + i) % mine.Count];
            if (candidate.ActionPoints > 0)
                return Select(candidate);
        }

        return false;
    }

    /// <summary>Drops the current selection and selects the first unit that can act (start of a turn).</summary>
    public bool SelectFirstAvailable()
    {
        Deselect();
        return SelectNextAvailable();
    }

    private List<Unit> CollectControlledUnits()
    {
        List<Unit> result = new List<Unit>();
        foreach (Unit u in Unit.All)
        {
            if (u != null && u.Faction == controlledFaction)
                result.Add(u);
        }

        return result;
    }

    private void HandleUnitClick(Unit unit)
    {
        // Not ours: it can neither be selected nor change the current selection.
        if (unit.Faction != controlledFaction)
            return;

        if (unit == selectedUnit)
            Deselect();
        else
            Select(unit);
    }

    // Right click: the selected unit turns its field of view towards the cursor.
    private void HandleRightClick(Vector2 screenPos)
    {
        if (selectedUnit == null)
            return;

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        if (targetCamera == null)
            targetCamera = Camera.main;
        if (targetCamera == null)
            return;

        // where the cursor points on the ground the unit stands on
        Ray ray = targetCamera.ScreenPointToRay(screenPos);
        Plane ground = new Plane(Vector3.up, selectedUnit.transform.position);
        if (!ground.Raycast(ray, out float distance))
            return;

        selectedUnit.LookAt(ray.GetPoint(distance));
    }

    private static bool TryGetRightClick(out Vector2 screenPos)
    {
        screenPos = default;

#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)
        {
            screenPos = Mouse.current.position.ReadValue();
            return true;
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetMouseButtonDown(1))
        {
            screenPos = Input.mousePosition;
            return true;
        }
#endif
        return false;
    }

    private static bool TabPressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame)
            return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.Tab))
            return true;
#endif
        return false;
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
