using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What the selected unit can see. Only the tiles, walls and units inside its field of view are shown normally,
/// everything else is darkened (units in the dark also lose their health bar and vision fan).
/// With nothing selected everything is shown normally.
/// The field of view is the one of the selected unit: its position, facing, view angle and range, with walls
/// blocking the view (the same area UnitVisionDisplay draws).
/// </summary>
public class VisionShading : MonoBehaviour
{
    [SerializeField] private UnitSelector selector;
    [SerializeField] private GridManager grid;

    [Tooltip("How bright the things outside the field of view are: 1 = unchanged, 0 = black.")]
    [SerializeField, Range(0.05f, 1f)] private float shadeFactor = 0.35f;

    private readonly HashSet<Vector2Int> visible = new HashSet<Vector2Int>();
    private bool shading;

    private void LateUpdate()
    {
        Refresh();
    }

    private void OnDisable()
    {
        ClearAll();
    }

    /// <summary>Works out what the selected unit sees and shades everything else.</summary>
    public void Refresh()
    {
        if (selector == null)
            selector = FindFirstObjectByType<UnitSelector>();
        if (grid == null)
            grid = FindFirstObjectByType<GridManager>();
        if (selector == null || grid == null)
            return;

        Unit viewer = selector.SelectedUnit;
        if (viewer != null && !viewer.IsSelected)
            viewer = null;

        if (viewer == null)
        {
            if (shading)
                ClearAll();
            return;
        }

        shading = true;
        grid.ComputeVisibleTiles(viewer.transform.position, viewer.FacingDirection, viewer.ViewAngle, viewer.ViewRange, visible);

        foreach (Tile tile in grid.Tiles)
        {
            if (tile != null)
                tile.SetShaded(!visible.Contains(tile.Coords), shadeFactor);
        }

        // a wall is seen if either of the two tiles it stands between is seen
        foreach (Wall wall in grid.Walls)
        {
            if (wall != null)
                wall.SetShaded(!(visible.Contains(wall.A) || visible.Contains(wall.B)), shadeFactor);
        }

        foreach (Unit unit in Unit.All)
        {
            if (unit == null)
                continue;

            bool seen = unit == viewer || visible.Contains(unit.Coords);
            unit.SetFogged(!seen, shadeFactor);
        }
    }

    private void ClearAll()
    {
        shading = false;

        if (grid != null)
        {
            foreach (Tile tile in grid.Tiles)
            {
                if (tile != null)
                    tile.SetShaded(false, 1f);
            }

            foreach (Wall wall in grid.Walls)
            {
                if (wall != null)
                    wall.SetShaded(false, 1f);
            }
        }

        foreach (Unit unit in Unit.All)
        {
            if (unit != null)
                unit.SetFogged(false, 1f);
        }
    }
}
