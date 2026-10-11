using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// While a unit is selected, tints the tiles it can walk to during the current turn
/// (as many steps as it has action points, walls respected).
/// The tint is hidden while the unit is moving and is recalculated whenever the unit stops,
/// spends or regains action points, or the selection changes.
/// </summary>
public class MovementRangeDisplay : MonoBehaviour
{
    [SerializeField] private UnitSelector selector;
    [SerializeField] private GridManager grid;

    private readonly List<Tile> highlighted = new List<Tile>();

    private bool hasState;
    private Unit shownUnit;
    private Vector2Int shownCoords;
    private int shownPoints;
    private bool shownMoving;
    private int shownOccupancy;

    private void Update()
    {
        if (selector == null)
            selector = FindFirstObjectByType<UnitSelector>();
        if (selector == null)
            return;

        Unit unit = selector.SelectedUnit;
        if (unit != null && !unit.IsSelected)
            unit = null;

        if (unit == null)
        {
            if (hasState)
            {
                Clear();
                hasState = false;
                shownUnit = null;
            }
            return;
        }

        bool moving = unit.IsMoving;
        if (hasState && unit == shownUnit && unit.Coords == shownCoords
            && unit.ActionPoints == shownPoints && moving == shownMoving
            && Unit.OccupancyVersion == shownOccupancy)
            return;

        hasState = true;
        shownUnit = unit;
        shownCoords = unit.Coords;
        shownPoints = unit.ActionPoints;
        shownMoving = moving;
        shownOccupancy = Unit.OccupancyVersion;

        Refresh(unit, moving);
    }

    private void OnDisable()
    {
        Clear();
        hasState = false;
        shownUnit = null;
    }

    private void Refresh(Unit unit, bool moving)
    {
        Clear();

        if (moving || unit.ActionPoints <= 0)
            return;

        if (grid == null)
            grid = FindFirstObjectByType<GridManager>();
        if (grid == null)
            return;

        Tile start = grid.GetTile(unit.Coords);
        foreach (Tile tile in Pathfinder.GetReachableTiles(grid, start, unit.ActionPoints, unit))
        {
            tile.SetReachable(true);
            highlighted.Add(tile);
        }
    }

    private void Clear()
    {
        foreach (Tile tile in highlighted)
        {
            if (tile != null)
                tile.SetReachable(false);
        }

        highlighted.Clear();
    }
}
