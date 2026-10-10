using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Creates the field and owns it: builds the tiles and the walls from a GridSettings object
/// and answers questions about the field (tile by coordinates, can a unit step from A to B).
///
/// Where the settings come from:
///  - Build(settings): any class can call this with its own GridSettings;
///  - Settings Provider: an optional component (see GridSettingsProvider) asked on Build From Source;
///  - Default Settings: used when there is no provider (10 x 10, no walls).
///
/// Right-click the component header and choose "Build From Source" or "Clear" to use it in the editor.
/// </summary>
[DefaultExecutionOrder(-100)]
public class GridManager : MonoBehaviour
{
    private const string TilesRootName = "Tiles";
    private const string WallsRootName = "Walls";

    [Header("Prefabs")]
    [SerializeField] private Tile tilePrefab;
    [SerializeField] private GameObject wallPrefab;

    [Header("Field")]
    [Tooltip("Used when no Settings Provider is assigned.")]
    [SerializeField] private GridSettings defaultSettings = new GridSettings();
    [Tooltip("Optional. When assigned, it decides the size and the walls of the field.")]
    [SerializeField] private GridSettingsProvider settingsProvider;
    [SerializeField] private bool buildOnStart = true;

    private Tile[,] map;

    /// <summary>Raised after the field has been (re)built. Subscribe to react to a new field.</summary>
    public event System.Action GridBuilt;

    /// <summary>The settings the current field was built from (null before the first build).</summary>
    public GridSettings CurrentSettings { get; private set; }

    /// <summary>Number of tiles along X (x) and Y (y) of the field as it currently exists.</summary>
    public Vector2Int Size
    {
        get
        {
            EnsureMap();
            return map == null ? Vector2Int.zero : new Vector2Int(map.GetLength(0), map.GetLength(1));
        }
    }

    private void Start()
    {
        if (buildOnStart)
            BuildFromSource();
    }

    /// <summary>Builds the field from the Settings Provider, or from Default Settings if there is none.</summary>
    [ContextMenu("Build From Source")]
    public void BuildFromSource()
    {
        GridSettings settings = settingsProvider != null ? settingsProvider.GetSettings() : defaultSettings;
        Build(settings);
    }

    /// <summary>Builds the field from the given settings, replacing the current one.</summary>
    public void Build(GridSettings settings)
    {
        if (tilePrefab == null)
        {
            Debug.LogWarning("GridManager: Tile Prefab is not assigned.", this);
            return;
        }

        if (settings == null)
        {
            Debug.LogWarning("GridManager: no settings given, using Default Settings.", this);
            settings = defaultSettings;
        }

        int width = Mathf.Max(1, settings.width);
        int height = Mathf.Max(1, settings.height);
        float tileSize = Mathf.Max(0.01f, settings.tileSize);

        Clear();

        Transform tilesRoot = CreateRoot(TilesRootName);
        Transform wallsRoot = CreateRoot(WallsRootName);

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Tile tile = SpawnTile(tilesRoot);
                tile.transform.localPosition = new Vector3(x * tileSize, 0f, y * tileSize);
                tile.Init(new Vector2Int(x, y));
            }
        }

        map = null;

        if (settings.walls != null)
        {
            foreach (WallEdge edge in settings.walls)
            {
                Vector2Int d = edge.b - edge.a;
                bool adjacent = Mathf.Abs(d.x) + Mathf.Abs(d.y) == 1;
                if (!adjacent || GetTile(edge.a) == null || GetTile(edge.b) == null)
                {
                    Debug.LogWarning($"GridManager: ignoring invalid wall edge {edge.a} - {edge.b}.", this);
                    continue;
                }

                if (IsEdgeBlocked(edge.a, edge.b))
                    continue; // duplicate

                SetEdgeBlocked(edge.a, edge.b, true);
                SpawnWall(edge.a, edge.b, wallsRoot);
            }
        }

        CurrentSettings = settings;
        GridBuilt?.Invoke();
    }

    /// <summary>Removes the field (tiles and walls).</summary>
    [ContextMenu("Clear")]
    public void Clear()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            GameObject child = transform.GetChild(i).gameObject;
            if (Application.isPlaying)
            {
                child.transform.SetParent(null); // so it is not found as part of the field until it is destroyed
                Destroy(child);
            }
            else
            {
                DestroyImmediate(child);
            }
        }

        map = null;
        CurrentSettings = null;
    }

    /// <summary>Returns the tile with the given coordinates, or null if there is none.</summary>
    public Tile GetTile(Vector2Int c)
    {
        EnsureMap();
        if (map == null || c.x < 0 || c.y < 0 || c.x >= map.GetLength(0) || c.y >= map.GetLength(1))
            return null;
        return map[c.x, c.y];
    }

    /// <summary>True if there is a wall on the edge between two side-adjacent tiles.</summary>
    public bool IsEdgeBlocked(Vector2Int a, Vector2Int b)
    {
        Vector2Int d = b - a;
        Tile t;

        if (d == Vector2Int.right)
        {
            t = GetTile(a);
            return t != null && t.HasWallEast;
        }
        if (d == Vector2Int.left)
        {
            t = GetTile(b);
            return t != null && t.HasWallEast;
        }
        if (d == Vector2Int.up)
        {
            t = GetTile(a);
            return t != null && t.HasWallNorth;
        }
        if (d == Vector2Int.down)
        {
            t = GetTile(b);
            return t != null && t.HasWallNorth;
        }

        return false;
    }

    /// <summary>
    /// True if a unit can walk from one tile straight to a neighbouring tile (8 directions).
    /// A diagonal step is only allowed when no wall touches the corner it passes through.
    /// </summary>
    public bool CanStep(Vector2Int from, Vector2Int to)
    {
        if (GetTile(from) == null || GetTile(to) == null)
            return false;

        Vector2Int d = to - from;

        if (Mathf.Abs(d.x) + Mathf.Abs(d.y) == 1)
            return !IsEdgeBlocked(from, to);

        if (Mathf.Abs(d.x) == 1 && Mathf.Abs(d.y) == 1)
        {
            Vector2Int viaX = new Vector2Int(to.x, from.y);
            Vector2Int viaY = new Vector2Int(from.x, to.y);
            return CanStep(from, viaX) && CanStep(viaX, to) && CanStep(from, viaY) && CanStep(viaY, to);
        }

        return false;
    }

    private void SetEdgeBlocked(Vector2Int a, Vector2Int b, bool blocked)
    {
        Vector2Int d = b - a;
        Tile t;

        if (d == Vector2Int.right)
        {
            t = GetTile(a);
            if (t != null) t.SetWallEast(blocked);
        }
        else if (d == Vector2Int.left)
        {
            t = GetTile(b);
            if (t != null) t.SetWallEast(blocked);
        }
        else if (d == Vector2Int.up)
        {
            t = GetTile(a);
            if (t != null) t.SetWallNorth(blocked);
        }
        else if (d == Vector2Int.down)
        {
            t = GetTile(b);
            if (t != null) t.SetWallNorth(blocked);
        }
    }

    private Transform CreateRoot(string rootName)
    {
        GameObject go = new GameObject(rootName);
        go.transform.SetParent(transform, false);
        return go.transform;
    }

    private void EnsureMap()
    {
        if (map != null && map.Length > 0 && map[0, 0] != null)
            return;

        Tile[] tiles = GetComponentsInChildren<Tile>();
        if (tiles.Length == 0)
        {
            map = null;
            return;
        }

        int maxX = 0;
        int maxY = 0;
        foreach (Tile t in tiles)
        {
            maxX = Mathf.Max(maxX, t.Coords.x);
            maxY = Mathf.Max(maxY, t.Coords.y);
        }

        map = new Tile[maxX + 1, maxY + 1];
        foreach (Tile t in tiles)
            map[t.Coords.x, t.Coords.y] = t;
    }

    private Tile SpawnTile(Transform parent)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
            return (Tile)PrefabUtility.InstantiatePrefab(tilePrefab, parent);
#endif
        return Instantiate(tilePrefab, parent);
    }

    // The wall prefab is a thin slab: long along X, thin along Z.
    // Edges between tiles that differ in X need the slab turned by 90 degrees.
    private void SpawnWall(Vector2Int a, Vector2Int b, Transform parent)
    {
        if (wallPrefab == null)
            return;

        Tile tileA = GetTile(a);
        Tile tileB = GetTile(b);

        GameObject wall = InstantiateWall(parent);
        wall.name = $"Wall_{a.x}_{a.y}_{b.x}_{b.y}";
        wall.transform.rotation = a.y == b.y ? Quaternion.Euler(0f, 90f, 0f) : Quaternion.identity;

        Renderer tileRenderer = tileA.GetComponent<Renderer>();
        Renderer wallRenderer = wall.GetComponentInChildren<Renderer>();

        float tileTop = tileRenderer != null ? tileRenderer.bounds.max.y : tileA.transform.position.y;
        float halfHeight = wallRenderer != null ? wallRenderer.bounds.extents.y : 0f;

        Vector3 mid = (tileA.transform.position + tileB.transform.position) * 0.5f;
        wall.transform.position = new Vector3(mid.x, tileTop + halfHeight, mid.z);
    }

    private GameObject InstantiateWall(Transform parent)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
            return (GameObject)PrefabUtility.InstantiatePrefab(wallPrefab, parent);
#endif
        return Instantiate(wallPrefab, parent);
    }
}
