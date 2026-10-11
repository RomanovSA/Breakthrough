using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A unit standing on a tile of the field. Can be selected / deselected and driven to another tile
/// along a route that goes around walls.
///
/// Action points: for cost purposes the unit cannot move diagonally, so every tile of the route costs
/// 1 point. While running it still cuts corners diagonally when that is possible; such a diagonal hop
/// is charged as the 2 steps it replaces. With 0 action points the unit cannot move.
/// </summary>
public class Unit : MonoBehaviour
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"); // URP / HDRP
    private static readonly int ColorId = Shader.PropertyToID("_Color");         // Built-in

    [SerializeField] private Vector2Int coords;
    [SerializeField] private Color selectedColor = new Color(1f, 0.9f, 0.1f, 1f);
    [Tooltip("Parts that change colour when the unit is selected. Empty = the whole unit.")]
    [SerializeField] private Renderer[] selectionRenderers;
    [SerializeField, Min(0.1f)] private float moveSpeed = 4f; // world units per second

    [Header("Action points")]
    [SerializeField, Min(0)] private int maxActionPoints = 4;
    [SerializeField, Min(0)] private int actionPoints = 4;

    [Header("Health")]
    [SerializeField, Min(1)] private int maxHealth = 100;
    [SerializeField, Min(0)] private int health = 100;

    private Renderer[] tintTargets;
    private MaterialPropertyBlock block;
    private bool isSelected;

    private Tile currentTile;   // tile the unit last arrived on
    private Tile stepTarget;    // tile the unit is running to right now (null when idle)
    private Vector3 stepPosition;
    private readonly Queue<Tile> waypoints = new Queue<Tile>();

    /// <summary>Action points left in the current turn.</summary>
    public int ActionPoints => actionPoints;

    /// <summary>Action points the unit gets back at the start of every turn.</summary>
    public int MaxActionPoints => maxActionPoints;

    /// <summary>Current health. 0 means the unit has no health left.</summary>
    public int Health => health;

    /// <summary>The most health the unit can have.</summary>
    public int MaxHealth => maxHealth;

    public bool IsAlive => health > 0;

    /// <summary>Raised whenever the unit's health changes (damage or healing).</summary>
    public event System.Action<Unit> HealthChanged;

    public Vector2Int Coords => coords;
    public bool IsSelected => isSelected;
    public bool IsMoving => stepTarget != null;

    private void Awake()
    {
        actionPoints = maxActionPoints;
        health = maxHealth;
    }

    private void Update()
    {
        Tick(Time.deltaTime);
    }

    /// <summary>Reduces the unit's health by the given amount (never below 0).</summary>
    public void TakeDamage(int amount)
    {
        if (amount <= 0)
            return;

        SetHealth(health - amount);
    }

    /// <summary>Restores the given amount of health (never above the maximum).</summary>
    public void Heal(int amount)
    {
        if (amount <= 0)
            return;

        SetHealth(health + amount);
    }

    private void SetHealth(int value)
    {
        value = Mathf.Clamp(value, 0, maxHealth);
        if (value == health)
            return;

        health = value;
        HealthChanged?.Invoke(this);
    }

    // Handy for trying the health bars out: right-click the Unit component header.
    [ContextMenu("Take 10 damage")]
    private void DebugTakeDamage()
    {
        TakeDamage(10);
    }

    [ContextMenu("Heal 10")]
    private void DebugHeal()
    {
        Heal(10);
    }

    /// <summary>Gives the unit all of its action points back (start of a new turn).</summary>
    public void RestoreActionPoints()
    {
        actionPoints = maxActionPoints;
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

    // Starts the next hop of the route and pays for it: a straight hop costs 1 point,
    // a diagonal hop costs 2 (the two orthogonal steps it replaces).
    private void AdvanceStep()
    {
        if (waypoints.Count == 0)
        {
            stepTarget = null;
            return;
        }

        stepTarget = waypoints.Dequeue();
        stepPosition = GetPositionOn(stepTarget);

        int cost = 1;
        if (currentTile != null)
        {
            Vector2Int delta = stepTarget.Coords - currentTile.Coords;
            cost = Mathf.Abs(delta.x) + Mathf.Abs(delta.y);
        }

        actionPoints = Mathf.Max(0, actionPoints - cost);
    }

    /// <summary>
    /// Instantly puts the unit onto the given tile so that its bottom touches the tile's top face.
    /// Costs no action points.
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
    /// Sends the unit towards the given tile, walking around walls. Every tile of the route costs 1 action
    /// point, so with too few points the unit runs as far as its points allow and stops.
    /// Returns false (and does nothing) if the unit has no action points or the tile cannot be reached.
    /// A new call while moving makes the unit finish its current hop and then follow the new route.
    /// </summary>
    public bool MoveTo(Tile destination)
    {
        if (destination == null || actionPoints <= 0)
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

        int cost = path.Count - 1;
        if (cost == 0)
        {
            // Clicked the tile we are already heading to: stop there.
            if (stepTarget == null)
                return false;
            waypoints.Clear();
            return true;
        }

        // Not enough points for the whole route: keep only the part we can pay for.
        int steps = Mathf.Min(cost, actionPoints);
        if (steps < cost)
            path.RemoveRange(steps + 1, path.Count - (steps + 1));

        // Cut corners where possible. The total cost stays the same.
        List<Tile> route = Pathfinder.SmoothPath(grid, path);

        waypoints.Clear();
        for (int i = 1; i < route.Count; i++)
            waypoints.Enqueue(route[i]);

        if (stepTarget == null)
            AdvanceStep();

        return true;
    }

    // The unit's pivot is at its feet, so it simply stands on the top face of the tile.
    private Vector3 GetPositionOn(Tile tile)
    {
        Renderer tileRenderer = tile.GetComponent<Renderer>();
        Vector3 tilePos = tile.transform.position;
        float tileTop = tileRenderer != null ? tileRenderer.bounds.max.y : tilePos.y;
        return new Vector3(tilePos.x, tileTop, tilePos.z);
    }

    // The parts that change colour when the unit is selected: the ones assigned in Selection Renderers,
    // or every renderer of the unit if none are assigned.
    private Renderer[] GetTintTargets()
    {
        if (tintTargets == null)
        {
            tintTargets = selectionRenderers != null && selectionRenderers.Length > 0
                ? selectionRenderers
                : GetComponentsInChildren<Renderer>();
        }

        return tintTargets;
    }

    public void SetSelected(bool selected)
    {
        isSelected = selected;

        if (block == null)
            block = new MaterialPropertyBlock();

        foreach (Renderer rend in GetTintTargets())
        {
            if (rend == null)
                continue;

            if (selected)
            {
                rend.GetPropertyBlock(block);
                block.SetColor(BaseColorId, selectedColor);
                block.SetColor(ColorId, selectedColor);
                rend.SetPropertyBlock(block);
            }
            else
            {
                // Drop the override so the material's own color is used again.
                rend.SetPropertyBlock(null);
            }
        }
    }

    public void ToggleSelected()
    {
        SetSelected(!isSelected);
    }
}
