using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A unit standing on a tile of the field. Can be selected / deselected
/// and driven to another tile along a path that goes around walls.
/// </summary>
public class Unit : MonoBehaviour
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"); // URP / HDRP
    private static readonly int ColorId = Shader.PropertyToID("_Color");         // Built-in

    [SerializeField] private Vector2Int coords;
    [SerializeField] private Color selectedColor = new Color(1f, 0.9f, 0.1f, 1f);
    [SerializeField, Min(0.1f)] private float moveSpeed = 4f; // world units per second

    private Renderer unitRenderer;
    private MaterialPropertyBlock block;
    private bool isSelected;

    private Tile currentTile;   // tile the unit last arrived on
    private Tile stepTarget;    // tile the unit is driving to right now (null when idle)
    private Vector3 stepPosition;
    private readonly Queue<Tile> waypoints = new Queue<Tile>();

    public Vector2Int Coords => coords;
    public bool IsSelected => isSelected;
    public bool IsMoving => stepTarget != null;

    private void Update()
    {
        Tick(Time.deltaTime);
    }

    private void Tick(float deltaTime)
    {
        if (stepTarget == null)
            return;

        transform.position = Vector3.MoveTowards(transform.position, stepPosition, moveSpeed * deltaTime);

        if ((transform.position - stepPosition).sqrMagnitude < 0.000001f)
        {
            transform.position = stepPosition;
            currentTile = stepTarget;
            coords = currentTile.Coords;
            AdvanceStep();
        }
    }

    private void AdvanceStep()
    {
        if (waypoints.Count == 0)
        {
            stepTarget = null;
            return;
        }

        stepTarget = waypoints.Dequeue();
        stepPosition = GetPositionOn(stepTarget);
    }

    /// <summary>
    /// Instantly puts the unit onto the given tile so that its bottom touches the tile's top face.
    /// </summary>
    public void PlaceOn(Tile tile)
    {
        waypoints.Clear();
        stepTarget = null;
        currentTile = tile;
        coords = tile.Coords;
        transform.position = GetPositionOn(tile);
    }

    /// <summary>
    /// Drives the unit to the given tile, walking around walls.
    /// Returns false (and does nothing) if the tile cannot be reached.
    /// A new call while moving makes the unit finish its current step and then follow the new path.
    /// </summary>
    public bool MoveTo(Tile destination)
    {
        if (destination == null)
            return false;

        GridManager grid = destination.GetComponentInParent<GridManager>();
        if (grid == null)
            return false;

        if (currentTile == null)
            currentTile = grid.GetTile(coords);

        // When already moving, plan from the tile we are heading to, so we never cut through a wall.
        Tile from = stepTarget != null ? stepTarget : currentTile;
        if (from == null)
            return false;

        List<Tile> path = Pathfinder.FindPath(grid, from, destination);
        if (path == null)
            return false;

        waypoints.Clear();
        for (int i = 1; i < path.Count; i++)
            waypoints.Enqueue(path[i]);

        if (stepTarget == null)
            AdvanceStep();

        return true;
    }

    private Vector3 GetPositionOn(Tile tile)
    {
        Renderer tileRenderer = tile.GetComponent<Renderer>();
        Renderer rend = GetComponentInChildren<Renderer>();

        float tileTop = tileRenderer != null ? tileRenderer.bounds.max.y : tile.transform.position.y;
        float halfHeight = rend != null ? rend.bounds.extents.y : 0f;

        Vector3 tilePos = tile.transform.position;
        return new Vector3(tilePos.x, tileTop + halfHeight, tilePos.z);
    }

    public void SetSelected(bool selected)
    {
        isSelected = selected;

        if (unitRenderer == null)
            unitRenderer = GetComponentInChildren<Renderer>();
        if (unitRenderer == null)
            return;

        if (selected)
        {
            if (block == null)
                block = new MaterialPropertyBlock();

            unitRenderer.GetPropertyBlock(block);
            block.SetColor(BaseColorId, selectedColor);
            block.SetColor(ColorId, selectedColor);
            unitRenderer.SetPropertyBlock(block);
        }
        else
        {
            // Drop the override so the material's own color is used again.
            unitRenderer.SetPropertyBlock(null);
        }
    }

    public void ToggleSelected()
    {
        SetSelected(!isSelected);
    }
}
