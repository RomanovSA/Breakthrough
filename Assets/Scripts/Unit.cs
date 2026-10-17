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

    [Header("Faction")]
    [SerializeField] private Faction faction = Faction.Player;

    [SerializeField] private Color selectedColor = new Color(1f, 0.9f, 0.1f, 1f);
    [Tooltip("Parts that change colour when the unit is selected. Empty = the whole unit.")]
    [SerializeField] private Renderer[] selectionRenderers;
    [Tooltip("Which materials of those renderers change colour (e.g. only the shirt). Empty = all of them.")]
    [SerializeField] private int[] selectionMaterialIndices;
    [SerializeField, Min(0.1f)] private float moveSpeed = 4f; // world units per second

    [Header("Vision")]
    [Tooltip("Width of the field of view in degrees (the whole cone, not half of it).")]
    [SerializeField, Range(1f, 360f)] private float viewAngle = 30f;
    [Tooltip("How far the unit sees, in tiles.")]
    [SerializeField, Min(0f)] private float viewRange = 5f;
    [Tooltip("The part of the unit that turns (the Model object). Its forward direction is where the unit looks.")]
    [SerializeField] private Transform facing;
    [Tooltip("Switched off while the selected unit cannot see this unit (its health bar and vision fan).")]
    [SerializeField] private GameObject[] hiddenWhileUnseen;

    [Header("Action points")]
    [SerializeField, Min(0)] private int maxActionPoints = 4;
    [SerializeField, Min(0)] private int actionPoints = 4;

    [Header("Health")]
    [SerializeField, Min(1)] private int maxHealth = 100;
    [SerializeField, Min(0)] private int health = 100;

    private Renderer[] visualRenderers;
    private bool isFogged;
    private float fogFactor = 1f;

    private static readonly List<Unit> allUnits = new List<Unit>();

    /// <summary>Every unit that is currently active in the scene.</summary>
    public static IReadOnlyList<Unit> All => allUnits;

    /// <summary>Goes up every time some unit appears, disappears, takes a tile or leaves one.</summary>
    public static int OccupancyVersion { get; private set; }

    /// <summary>Coordinates of every tile that is taken by a unit other than 'exclude'.</summary>
    public static HashSet<Vector2Int> GetOccupiedCoords(Unit exclude)
    {
        HashSet<Vector2Int> result = new HashSet<Vector2Int>();

        foreach (Unit u in allUnits)
        {
            if (u != null && u != exclude)
                u.AddOccupiedCoords(result);
        }

        return result;
    }

    /// <summary>True if a unit other than 'exclude' stands on the tile or is about to walk onto it.</summary>
    public static bool IsOccupied(Tile tile, Unit exclude)
    {
        return tile != null && GetOccupiedCoords(exclude).Contains(tile.Coords);
    }

    private static void NotifyOccupancyChanged()
    {
        OccupancyVersion++;
    }

    /// <summary>True while at least one unit is on the move.</summary>
    public static bool AnyMoving
    {
        get
        {
            foreach (Unit u in allUnits)
            {
                if (u != null && u.IsMoving)
                    return true;
            }

            return false;
        }
    }
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

    /// <summary>The side this unit fights for.</summary>
    public Faction Faction => faction;

    /// <summary>Width of the field of view in degrees.</summary>
    public float ViewAngle => viewAngle;

    /// <summary>How far the unit sees, in tiles.</summary>
    public float ViewRange => viewRange;

    /// <summary>The direction the unit looks in, flat on the ground (length 1).</summary>
    public Vector3 FacingDirection
    {
        get
        {
            Vector3 v = (facing != null ? facing : transform).forward;
            v.y = 0f;
            return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
        }
    }

    public Vector2Int Coords => coords;
    public bool IsSelected => isSelected;

    /// <summary>True while the unit is in the dark: the selected unit cannot see it.</summary>
    public bool IsFogged => isFogged;
    public bool IsMoving => stepTarget != null;

    private void Awake()
    {
        actionPoints = maxActionPoints;
        health = maxHealth;
    }

    private void OnEnable()
    {
        allUnits.Add(this);
        NotifyOccupancyChanged();
    }

    private void OnDisable()
    {
        allUnits.Remove(this);
        NotifyOccupancyChanged();
    }

    // A unit takes the tile it stands on, the tile it is running to and every tile still ahead on its route.
    // That way two units can never be on one tile, and never plan to be.
    private void AddOccupiedCoords(HashSet<Vector2Int> set)
    {
        set.Add(currentTile != null ? currentTile.Coords : coords);

        if (stepTarget != null)
            set.Add(stepTarget.Coords);

        foreach (Tile t in waypoints)
        {
            if (t != null)
                set.Add(t.Coords);
        }
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
            NotifyOccupancyChanged();
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
    /// Instantly puts the unit onto the given tile (its feet on the tile's top face). Costs no action points.
    /// Returns false (and does nothing) if another unit is on that tile.
    /// </summary>
    public bool PlaceOn(Tile tile)
    {
        if (tile == null || IsOccupied(tile, this))
            return false;

        waypoints.Clear();
        stepTarget = null;
        currentTile = tile;
        coords = tile.Coords;
        transform.position = GetPositionOn(tile);
        NotifyOccupancyChanged();
        return true;
    }

    /// <summary>
    /// Sends the unit towards the given tile, walking around walls and around other units.
    /// Every tile of the route costs 1 action point, so with too few points the unit runs as far as its
    /// points allow and stops.
    /// Returns false (and does nothing) if the unit is already moving, has no action points, is already
    /// standing on that tile, the tile is taken by another unit, or it cannot be reached.
    /// </summary>
    public bool MoveTo(Tile destination)
    {
        // A move that has been started is always finished before a new order is accepted.
        if (IsMoving)
            return false;

        if (destination == null || actionPoints <= 0)
            return false;

        GridManager grid = destination.GetComponentInParent<GridManager>();
        if (grid == null)
            return false;

        if (currentTile == null)
            currentTile = grid.GetTile(coords);
        if (currentTile == null)
            return false;

        // The route goes around other units, and a tile that another unit stands on
        // (or is on its way to) cannot be the destination.
        List<Tile> path = Pathfinder.FindPath(grid, currentTile, destination, this);
        if (path == null)
            return false;

        int cost = path.Count - 1;
        if (cost == 0)
            return false; // already standing there

        // Not enough points for the whole route: keep only the part we can pay for.
        int steps = Mathf.Min(cost, actionPoints);
        if (steps < cost)
            path.RemoveRange(steps + 1, path.Count - (steps + 1));

        // Cut corners where possible. The total cost stays the same.
        List<Tile> route = Pathfinder.SmoothPath(grid, path);

        waypoints.Clear();
        for (int i = 1; i < route.Count; i++)
            waypoints.Enqueue(route[i]);

        AdvanceStep();
        NotifyOccupancyChanged();
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

    // ---- how the unit looks: selected (shirt turns yellow) and / or in the dark (everything is darkened) ----
    // This is the only place that writes the colour overrides, so the two effects never overwrite each other.

    private Renderer[] GetVisualRenderers()
    {
        if (visualRenderers == null)
        {
            Transform body = facing != null ? facing : transform;
            var list = new List<Renderer>();
            foreach (Renderer r in body.GetComponentsInChildren<Renderer>(true))
            {
                if (r.GetComponent<UnitVisionDisplay>() == null)   // the vision fan keeps its own colours
                    list.Add(r);
            }

            visualRenderers = list.ToArray();
        }

        return visualRenderers;
    }

    // Is this material of this renderer one of the parts that turn yellow when the unit is selected?
    private bool IsSelectionPart(Renderer rend, int materialIndex)
    {
        if (!isSelected)
            return false;

        bool wholeUnit = selectionRenderers == null || selectionRenderers.Length == 0;
        if (wholeUnit)
            return true;

        if (System.Array.IndexOf(selectionRenderers, rend) < 0)
            return false;

        return selectionMaterialIndices == null || selectionMaterialIndices.Length == 0
            || System.Array.IndexOf(selectionMaterialIndices, materialIndex) >= 0;
    }

    private void RefreshVisuals()
    {
        if (block == null)
            block = new MaterialPropertyBlock();

        foreach (Renderer rend in GetVisualRenderers())
        {
            if (rend == null)
                continue;

            Material[] mats = rend.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                bool selectedPart = IsSelectionPart(rend, i);
                if ((!selectedPart && !isFogged) || mats[i] == null)
                {
                    rend.SetPropertyBlock(null, i);       // the material's own colour
                    continue;
                }

                Color c;
                if (selectedPart)
                    c = selectedColor;
                else if (mats[i].HasProperty("_BaseColor"))
                    c = mats[i].GetColor("_BaseColor");
                else if (mats[i].HasProperty("_Color"))
                    c = mats[i].GetColor("_Color");
                else
                    c = Color.white;

                if (isFogged)
                    c = new Color(c.r * fogFactor, c.g * fogFactor, c.b * fogFactor, c.a);

                rend.GetPropertyBlock(block, i);
                block.SetColor(BaseColorId, c);
                block.SetColor(ColorId, c);
                rend.SetPropertyBlock(block, i);
            }
        }
    }

    public void SetSelected(bool selected)
    {
        isSelected = selected;
        RefreshVisuals();
    }

    /// <summary>
    /// Puts the unit 'in the dark': everything about it is darkened by the factor and its health bar and vision
    /// fan are hidden (the selected unit cannot see it). SetFogged(false, ...) brings it back.
    /// </summary>
    public void SetFogged(bool fogged, float factor)
    {
        if (isFogged == fogged && (!fogged || Mathf.Approximately(fogFactor, factor)))
            return;

        isFogged = fogged;
        fogFactor = factor;
        RefreshVisuals();

        if (hiddenWhileUnseen != null)
        {
            foreach (GameObject go in hiddenWhileUnseen)
            {
                if (go != null)
                    go.SetActive(!fogged);
            }
        }
    }

    public void ToggleSelected()
    {
        SetSelected(!isSelected);
    }
}
