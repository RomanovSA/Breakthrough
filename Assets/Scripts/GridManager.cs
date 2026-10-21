using System.Collections.Generic;
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
    private const string ObjectsRootName = "Objects";
    private const string OverlayRootName = "Overlay";
    private const int IgnoreRaycastLayer = 2;     // Unity's built-in 'Ignore Raycast' layer

    [Header("Prefabs")]
    [SerializeField] private Tile tilePrefab;
    [SerializeField] private GameObject wallPrefab;
    [Tooltip("Material of the tile marks (hover, reachable, in the dark). Needs a transparent shader that uses vertex colours, e.g. Sprites/Default.")]
    [SerializeField] private Material overlayMaterial;

    [Header("Cover")]
    [Tooltip("A solid thing at least this tall (above the ground, world units) is half cover: a unit behind it is covered when crouching.")]
    [SerializeField, Min(0f)] private float halfCoverMinHeight = 0.4f;
    [Tooltip("A solid thing at least this tall is full cover (units are 1 tall; a van is about 1.4, a sedan 0.8, a bus stop 1.2).")]
    [SerializeField, Min(0f)] private float fullCoverMinHeight = 1.3f;

    [Header("Ladders")]
    [Tooltip("Action points a unit spends to climb a ladder, up to a roof or down to the ground.")]
    [SerializeField, Min(1)] private int ladderCost = 1;

    [Header("Field")]
    [Tooltip("Used when no Settings Provider is assigned.")]
    [SerializeField] private GridSettings defaultSettings = new GridSettings();
    [Tooltip("Optional. When assigned, it decides the size and the walls of the field.")]
    [SerializeField] private GridSettingsProvider settingsProvider;
    [SerializeField] private bool buildOnStart = true;

    private Tile[,] map;
    private readonly Dictionary<Tile, Ladder> ladderByTile = new Dictionary<Tile, Ladder>();   // a ladder is stored under both of its ends
    private List<Tile> tileList;
    private List<Wall> wallList;

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
        Transform objectsRoot = CreateRoot(ObjectsRootName);
        Transform overlayRoot = CreateRoot(OverlayRootName);

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Tile tile = SpawnTile(tilesRoot);
                tile.transform.localPosition = new Vector3(x * tileSize, 0f, y * tileSize);
                tile.Init(new Vector2Int(x, y));
                tile.SetLabelVisible(settings.showCoordinates);
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

        SpawnObjects(settings.objects, objectsRoot, tileSize);
        BuildOverlay(overlayRoot, settings.overlayHeight);
        wallList = null;
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
        tileList = null;
        wallList = null;
        CurrentSettings = null;
        ladderByTile.Clear();
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
        Tile fromTile = GetTile(from);
        Tile toTile = GetTile(to);
        if (fromTile == null || toTile == null)
            return false;

        Vector2Int d = to - from;
        bool sideways = Mathf.Abs(d.x) + Mathf.Abs(d.y) == 1;

        if (fromTile.HasRoof || toTile.HasRoof)
        {
            // On a roof a unit walks freely over the roof tiles. Between a roof and the ground there is only a ladder.
            if (!(fromTile.HasRoof && toTile.HasRoof))
                return sideways && GetLadder(fromTile, toTile) != null;

            if (sideways)
                return true;
            if (Mathf.Abs(d.x) == 1 && Mathf.Abs(d.y) == 1)
            {
                Vector2Int roofViaX = new Vector2Int(to.x, from.y);
                Vector2Int roofViaY = new Vector2Int(from.x, to.y);
                return CanStep(from, roofViaX) && CanStep(roofViaX, to) && CanStep(from, roofViaY) && CanStep(roofViaY, to);
            }
            return false;
        }

        if (fromTile.IsBlocked || toTile.IsBlocked)
            return false;

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

    /// <summary>Action points a unit spends to climb a ladder (up or down).</summary>
    public int LadderCost => ladderCost;

    /// <summary>The ladder that links these two tiles (a ground tile and the roof tile next to it), or null.</summary>
    public Ladder GetLadder(Tile a, Tile b)
    {
        Ladder ladder;
        if (a != null && b != null && ladderByTile.TryGetValue(a, out ladder)
            && ((ladder.foot == a && ladder.roof == b) || (ladder.roof == a && ladder.foot == b)))
            return ladder;
        return null;
    }

    /// <summary>All the ladders of the field.</summary>
    public IEnumerable<Ladder> Ladders
    {
        get
        {
            HashSet<Ladder> seen = new HashSet<Ladder>();
            foreach (Ladder l in ladderByTile.Values)
                if (seen.Add(l))
                    yield return l;
        }
    }

    /// <summary>
    /// What one hop between two neighbouring route tiles costs: climbing a ladder costs LadderCost, a straight step
    /// 1, a diagonal hop 2 (the two straight steps it replaces).
    /// </summary>
    public int StepCost(Vector2Int a, Vector2Int b)
    {
        Tile ta = GetTile(a);
        Tile tb = GetTile(b);
        if (ta != null && tb != null && GetLadder(ta, tb) != null)
            return ladderCost;

        Vector2Int d = b - a;
        return Mathf.Abs(d.x) + Mathf.Abs(d.y);
    }

    /// <summary>What a whole route costs in action points.</summary>
    public int PathCost(List<Tile> route)
    {
        int cost = 0;
        for (int i = 1; i < route.Count; i++)
            cost += StepCost(route[i - 1].Coords, route[i].Coords);
        return cost;
    }

    /// <summary>Distance between the centres of two neighbouring tiles, in world units.</summary>
    public float TileSize
    {
        get
        {
            Tile a = GetTile(Vector2Int.zero);
            Tile b = GetTile(Vector2Int.right);
            if (a != null && b != null)
                return Vector3.Distance(a.transform.position, b.transform.position);

            return CurrentSettings != null ? CurrentSettings.tileSize : 1f;
        }
    }

    /// <summary>
    /// How far one can see from a world position along a flat direction, in world units: up to maxDistance,
    /// but not through a wall and not beyond the edge of the field.
    /// The field is assumed to be lined up with the world axes (it is built that way).
    /// </summary>
    public float VisibleDistance(Vector3 origin, Vector3 direction, float maxDistance)
    {
        Tile first = GetTile(Vector2Int.zero);
        Vector2Int size = Size;
        Vector2 dir = new Vector2(direction.x, direction.z);
        if (first == null || dir.sqrMagnitude < 1e-8f || maxDistance <= 0f)
            return maxDistance;
        dir.Normalize();

        // work in tile units: tile (i, j) is the square from i - 0.5 to i + 0.5 and j - 0.5 to j + 0.5
        float s = TileSize;
        Vector3 p0 = first.transform.position;
        Vector2 o = new Vector2((origin.x - p0.x) / s, (origin.z - p0.z) / s);
        float maxT = maxDistance / s;

        int i = Mathf.RoundToInt(o.x);
        int j = Mathf.RoundToInt(o.y);
        if (i < 0 || j < 0 || i >= size.x || j >= size.y)
            return maxDistance;

        int stepX = dir.x > 0f ? 1 : -1;
        int stepY = dir.y > 0f ? 1 : -1;
        float tDeltaX = Mathf.Abs(dir.x) > 1e-6f ? 1f / Mathf.Abs(dir.x) : float.PositiveInfinity;
        float tDeltaY = Mathf.Abs(dir.y) > 1e-6f ? 1f / Mathf.Abs(dir.y) : float.PositiveInfinity;
        float tMaxX = Mathf.Abs(dir.x) > 1e-6f ? ((i + stepX * 0.5f) - o.x) / dir.x : float.PositiveInfinity;
        float tMaxY = Mathf.Abs(dir.y) > 1e-6f ? ((j + stepY * 0.5f) - o.y) / dir.y : float.PositiveInfinity;

        // a ray can cross at most this many tile borders before it has left the field
        int maxSteps = size.x + size.y + 4;
        for (int step = 0; step < maxSteps; step++)
        {
            bool crossX = tMaxX < tMaxY;
            float t = crossX ? tMaxX : tMaxY;
            if (t >= maxT)
                return maxDistance;

            Vector2Int current = new Vector2Int(i, j);
            Vector2Int next = crossX ? new Vector2Int(i + stepX, j) : new Vector2Int(i, j + stepY);

            if (next.x < 0 || next.y < 0 || next.x >= size.x || next.y >= size.y)
                return t * s;                       // the edge of the field
            if (IsEdgeBlocked(current, next))
                return t * s;                       // a wall

            Tile nextTile = GetTile(next);
            if (nextTile != null && nextTile.BlocksSight && nextTile.BlockTopY > origin.y + 0.3f)
                return t * s;                       // a building (but not one lower than the viewer: from a roof you see over it)

            i = next.x;
            j = next.y;
            if (crossX) tMaxX += tDeltaX; else tMaxY += tDeltaY;
        }

        return maxDistance;
    }

    /// <summary>
    /// The biggest set of free tiles that are connected to each other (walls, buildings and cars respected).
    /// Anything outside it is a pocket that can not be reached from the main part of the field.
    /// </summary>
    public HashSet<Vector2Int> GetLargestFreeRegion()
    {
        Vector2Int size = Size;
        HashSet<Vector2Int> best = new HashSet<Vector2Int>();
        bool[,] seen = new bool[size.x, size.y];
        Vector2Int[] steps = { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };

        for (int x = 0; x < size.x; x++)
        {
            for (int y = 0; y < size.y; y++)
            {
                Tile start = GetTile(new Vector2Int(x, y));
                if (seen[x, y] || start == null || start.IsBlocked)
                    continue;

                HashSet<Vector2Int> region = new HashSet<Vector2Int>();
                Queue<Vector2Int> queue = new Queue<Vector2Int>();
                queue.Enqueue(start.Coords);
                seen[x, y] = true;

                while (queue.Count > 0)
                {
                    Vector2Int c = queue.Dequeue();
                    region.Add(c);
                    foreach (Vector2Int d in steps)
                    {
                        Vector2Int n = c + d;
                        if (n.x < 0 || n.y < 0 || n.x >= size.x || n.y >= size.y || seen[n.x, n.y] || !CanStep(c, n))
                            continue;

                        seen[n.x, n.y] = true;
                        queue.Enqueue(n);
                    }
                }

                if (region.Count > best.Count)
                    best = region;
            }
        }

        return best;
    }

    /// <summary>The four directions, in the order north (+y), east (+x), south, west. Used for cover.</summary>
    public static readonly Vector2Int[] Directions = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };

    /// <summary>
    /// The cover a unit standing on this tile has against the given direction (0 north, 1 east, 2 south, 3 west): a wall
    /// on the edge is full cover, and so is whatever stands on the next tile if it is tall enough (half cover if it is low).
    /// </summary>
    public CoverType GetCover(Vector2Int tile, int direction)
    {
        Vector2Int next = tile + Directions[direction];
        Tile from = GetTile(tile);
        Tile to = GetTile(next);
        CoverType best = CoverType.None;

        if (from != null && from.IsBlocked)
            return CoverType.None;                  // nobody stands on a blocked ground tile; on a roof there is no cover

        if (from != null && to != null && IsEdgeBlocked(tile, next))
            best = CoverType.Full;

        if (to != null && to.IsBlocked && to.ProvidesCover > best)
            best = to.ProvidesCover;

        return best;
    }

    /// <summary>The best cover the tile has in any direction.</summary>
    public CoverType GetBestCover(Vector2Int tile)
    {
        CoverType best = CoverType.None;
        for (int d = 0; d < 4; d++)
            best = (CoverType)Mathf.Max((int)best, (int)GetCover(tile, d));
        return best;
    }

    private readonly HashSet<Vector2Int> seeScratch = new HashSet<Vector2Int>();

    /// <summary>
    /// True when the target stands on a tile inside the viewer's field of view (its position, facing, view angle and range,
    /// with buildings and walls blocking the view): the same field that is drawn as the vision fan.
    /// </summary>
    public bool CanSee(Unit viewer, Unit target)
    {
        if (viewer == null || target == null)
            return false;

        ComputeVisibleTiles(viewer.transform.position, viewer.FacingDirection, viewer.ViewAngle, viewer.ViewRange, seeScratch);
        return seeScratch.Contains(target.Coords);
    }

    private TileOverlay overlay;

    /// <summary>The layer above the ground where tile marks (hover, reachable, in the dark) are drawn.</summary>
    public TileOverlay Overlay
    {
        get
        {
            if (overlay == null)
                overlay = GetComponentInChildren<TileOverlay>();
            return overlay;
        }
    }

    /// <summary>How far above the top of the tiles the tile marks are drawn (world units).</summary>
    public float OverlayHeight => Overlay != null ? Overlay.Height : 0.02f;

    private void BuildOverlay(Transform root, float height)
    {
        overlay = root.gameObject.AddComponent<TileOverlay>();
        overlay.Configure(overlayMaterial, height);
        overlay.Rebuild();
    }

    /// <summary>All the tiles of the field.</summary>
    public IReadOnlyList<Tile> Tiles
    {
        get
        {
            EnsureMap();
            return tileList != null ? (IReadOnlyList<Tile>)tileList : new List<Tile>();
        }
    }

    /// <summary>All the walls of the field.</summary>
    public IReadOnlyList<Wall> Walls
    {
        get
        {
            if (wallList == null)
                wallList = new List<Wall>(GetComponentsInChildren<Wall>());
            return wallList;
        }
    }

    /// <summary>
    /// Works out which tiles are in view of someone standing at 'origin' and looking along 'facing' with the given
    /// view angle (degrees, the whole cone) and range (tiles). A tile counts as seen if any of five points in it
    /// (the middle and four spots near its corners) lies inside the cone with a clear line of sight, i.e. the same
    /// area the vision fan is drawn for. The tile the viewer stands on is always seen.
    /// </summary>
    public void ComputeVisibleTiles(Vector3 origin, Vector3 facing, float viewAngle, float rangeTiles, HashSet<Vector2Int> result)
    {
        result.Clear();
        EnsureMap();
        if (tileList == null)
            return;

        float s = TileSize;
        float range = rangeTiles * s;
        float halfAngle = Mathf.Clamp(viewAngle, 0f, 360f) * 0.5f;
        facing.y = 0f;
        facing = facing.sqrMagnitude > 1e-6f ? facing.normalized : Vector3.forward;

        float spot = 0.4f * s;
        Vector3[] offsets =
        {
            Vector3.zero,
            new Vector3(spot, 0f, spot), new Vector3(-spot, 0f, spot), new Vector3(spot, 0f, -spot), new Vector3(-spot, 0f, -spot)
        };

        // only the tiles within reach of the viewer (a square around it) are looked at, not the whole field
        Tile first = GetTile(Vector2Int.zero);
        if (first == null)
            return;
        Vector3 p0 = first.transform.position;
        Vector2Int fieldSize = Size;
        int reach = Mathf.CeilToInt(rangeTiles) + 2;
        int ci = Mathf.RoundToInt((origin.x - p0.x) / s), cj = Mathf.RoundToInt((origin.z - p0.z) / s);
        int xMin = Mathf.Max(0, ci - reach), xMax = Mathf.Min(fieldSize.x - 1, ci + reach);
        int yMin = Mathf.Max(0, cj - reach), yMax = Mathf.Min(fieldSize.y - 1, cj + reach);

        for (int ix = xMin; ix <= xMax; ix++)
        for (int iy = yMin; iy <= yMax; iy++)
        {
            Tile tile = GetTile(new Vector2Int(ix, iy));
            if (tile == null)
                continue;

            Vector3 centre = tile.transform.position;
            Vector3 toCentre = centre - origin; toCentre.y = 0f;
            if (toCentre.magnitude > range + 0.8f * s)
                continue; // clearly out of range

            for (int i = 0; i < offsets.Length; i++)
            {
                if (PointInView(origin, facing, halfAngle, range, centre + offsets[i]))
                {
                    result.Add(tile.Coords);
                    break;
                }
            }
        }
    }

    private bool PointInView(Vector3 origin, Vector3 facing, float halfAngle, float range, Vector3 point)
    {
        Vector3 d = point - origin;
        d.y = 0f;
        float dist = d.magnitude;
        if (dist < 0.05f)
            return true;                      // right under the viewer's feet
        if (dist > range)
            return false;
        if (Vector3.Angle(facing, d) > halfAngle + 0.01f)
            return false;

        return VisibleDistance(origin, d, dist) >= dist - 0.001f;   // nothing in the way
    }

    // Puts the objects of the settings on the field (buildings, props, vehicles...).
    private void SpawnObjects(List<GridObject> objects, Transform parent, float tileSize)
    {
        if (objects == null || objects.Count == 0)
            return;

        Tile origin = GetTile(Vector2Int.zero);
        if (origin == null)
            return;

        Renderer originRenderer = origin.GetComponent<Renderer>();
        float top = originRenderer != null ? originRenderer.bounds.max.y : origin.transform.position.y;

        List<PendingLadder> pendingLadders = new List<PendingLadder>();

        foreach (GridObject o in objects)
        {
            if (o.prefab == null)
                continue;

            GameObject go = InstantiateObject(o.prefab, parent);
            Vector3 position = origin.transform.position + new Vector3(o.position.x * tileSize, 0f, o.position.y * tileSize);
            position.y = top + o.height;

            go.name = o.prefab.name;
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, o.yaw, 0f));
            float objectScale = o.scale > 0f ? o.scale : 1f;
            go.transform.localScale = new Vector3(objectScale * (o.stretchX > 0f ? o.stretchX : 1f), objectScale, objectScale * (o.stretchZ > 0f ? o.stretchZ : 1f));

            // solid things (buildings) block the tiles under them; everything else is ignored by mouse clicks,
            // so a click goes through a car or a road piece to the tile below
            if (o.solid)
            {
                List<Tile> covered = BlockTilesUnder(go, tileSize, o.blocksSight, o.singleTile, o.providesCover ? MeasureCover(go, top) : CoverType.None);
                MakeShootable(go, covered, o);
                if (covered != null && FindMarker(go, "LadderFoot") != null)
                    pendingLadders.Add(new PendingLadder { go = go, tiles = covered });
                if (o.singleTile)
                    SetLayerRecursively(go, IgnoreRaycastLayer);      // a click goes through it to the (blocked) tile under it
            }
            else
                SetLayerRecursively(go, IgnoreRaycastLayer);
        }

        FinalizeLadders(pendingLadders, tileSize, top);
    }

    // Marks as blocked every tile that the object covers noticeably (seen from above: at least a third of the tile is
    // under it). The tile in the middle of the object is always blocked, so even a small thing blocks something.
    // The tiles also block the view when the object is tall.
    // How good the cover is that an object gives, from how far it sticks out above the ground.
    private CoverType MeasureCover(GameObject go, float groundTop)
    {
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return CoverType.None;

        float topY = renderers[0].bounds.max.y;
        foreach (Renderer r in renderers)
            topY = Mathf.Max(topY, r.bounds.max.y);

        float h = topY - groundTop;
        return h >= fullCoverMinHeight ? CoverType.Full : h >= halfCoverMinHeight ? CoverType.Half : CoverType.None;
    }

    // A solid thing can be shot: it gets colliders (a bullet is a real body) and health. Things that came without a collider
    // get one in the shape of their mesh.
    private static void MakeShootable(GameObject go, List<Tile> blocked, GridObject o)
    {
        foreach (MeshFilter filter in go.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null || filter.GetComponent<Collider>() != null)
                continue;

            MeshCollider collider = filter.gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = filter.sharedMesh;
            // on the 'Ignore Raycast' layer: bullets hit it, the mouse (a Physics.Raycast) passes through it like before,
            // so clicking a tile behind or on top of a building still works
            filter.gameObject.layer = IgnoreRaycastLayer;
        }

        Destructible thing = go.GetComponent<Destructible>();
        if (thing == null)
            thing = go.AddComponent<Destructible>();
        thing.Setup(o.health > 0 ? o.health : 100, o.breakable, blocked);
    }

    private class PendingLadder
    {
        public GameObject go;
        public List<Tile> tiles;
    }

    private static Transform FindMarker(GameObject go, string namePrefix)
    {
        foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
            if (t.name.StartsWith(namePrefix))
                return t;
        return null;
    }

    private List<Tile> BlockTilesUnder(GameObject go, float tileSize, bool blocksSight, bool ownTileOnly, CoverType cover)
    {
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
        Tile origin = GetTile(Vector2Int.zero);
        if (renderers.Length == 0 || origin == null)
            return null;

        Bounds b = renderers[0].bounds;
        foreach (Renderer r in renderers)
            b.Encapsulate(r.bounds);

        // a small thing (palm, bin, pole): only the tile it stands on, however wide its leaves are
        if (ownTileOnly)
        {
            Vector3 offset = go.transform.position - origin.transform.position;
            Tile own = GetTile(new Vector2Int(Mathf.RoundToInt(offset.x / tileSize), Mathf.RoundToInt(offset.z / tileSize)));
            if (own != null)
            {
                own.SetBlocked(true);
                own.SetProvidesCover(cover);
                if (blocksSight)
                {
                    own.SetBlocksSight(true);
                    own.SetBlockTop(b.max.y);
                }
            }
            return own != null ? new List<Tile> { own } : null;
        }

        List<Tile> blocked = new List<Tile>();
        Vector3 o = origin.transform.position;
        float half = 0.5f * tileSize;
        int x0 = Mathf.FloorToInt((b.min.x - o.x) / tileSize);
        int x1 = Mathf.CeilToInt((b.max.x - o.x) / tileSize);
        int y0 = Mathf.FloorToInt((b.min.z - o.z) / tileSize);
        int y1 = Mathf.CeilToInt((b.max.z - o.z) / tileSize);

        Vector2Int middle = new Vector2Int(Mathf.RoundToInt((b.center.x - o.x) / tileSize), Mathf.RoundToInt((b.center.z - o.z) / tileSize));

        for (int x = x0; x <= x1; x++)
        {
            for (int y = y0; y <= y1; y++)
            {
                Tile tile = GetTile(new Vector2Int(x, y));
                if (tile == null)
                    continue;

                float cx = o.x + x * tileSize, cz = o.z + y * tileSize;
                float overlapX = Mathf.Min(b.max.x, cx + half) - Mathf.Max(b.min.x, cx - half);
                float overlapZ = Mathf.Min(b.max.z, cz + half) - Mathf.Max(b.min.z, cz - half);
                float covered = overlapX > 0f && overlapZ > 0f ? overlapX * overlapZ / (tileSize * tileSize) : 0f;

                if (covered >= 0.33f || tile.Coords == middle)
                {
                    tile.SetBlocked(true);
                    tile.SetProvidesCover(cover);
                    if (blocksSight)
                    {
                        tile.SetBlocksSight(true);
                        tile.SetBlockTop(b.max.y);
                    }
                    blocked.Add(tile);
                }
            }
        }

        return blocked;
    }

    // A building with a ladder (LadderFoot / LadderBase / RoofLevel markers in the model) gets a walkable roof, but only when
    // the tile in front of the ladder is a free ground tile. The roof is all the tiles under the building.
    private void FinalizeLadders(List<PendingLadder> pending, float tileSize, float groundTop)
    {
        if (pending.Count == 0)
            return;

        Tile origin = GetTile(Vector2Int.zero);
        if (origin == null)
            return;
        Vector3 o = origin.transform.position;

        foreach (PendingLadder p in pending)
        {
            Transform footMark = FindMarker(p.go, "LadderFoot");
            Transform baseMark = FindMarker(p.go, "LadderBase");
            Transform topMark = FindMarker(p.go, "LadderTop");
            Transform roofMark = FindMarker(p.go, "RoofLevel");
            if (footMark == null || roofMark == null)
                continue;

            Vector2Int footCoords = new Vector2Int(Mathf.RoundToInt((footMark.position.x - o.x) / tileSize), Mathf.RoundToInt((footMark.position.z - o.z) / tileSize));
            Tile footTile = GetTile(footCoords);
            if (footTile == null || footTile.IsBlocked || footTile.HasRoof || ladderByTile.ContainsKey(footTile))
                continue;

            // the roof tile: the tile of this building that touches the foot tile (the one nearest to the top of the ladder)
            Tile roofTile = null;
            float bestDistance = float.PositiveInfinity;
            Vector3 reference = topMark != null ? topMark.position : roofMark.position;
            foreach (Vector2Int dir in Directions)
            {
                Tile neighbour = GetTile(footCoords + dir);
                if (neighbour == null || !p.tiles.Contains(neighbour) || neighbour.HasRoof)
                    continue;

                Vector3 delta = neighbour.transform.position - reference;
                delta.y = 0f;
                if (delta.magnitude < bestDistance)
                {
                    bestDistance = delta.magnitude;
                    roofTile = neighbour;
                }
            }
            if (roofTile == null)
                continue;

            float roofHeight = roofMark.position.y;
            foreach (Tile t in p.tiles)
            {
                t.SetRoof(roofHeight);
                if (t.BlocksSight)
                    t.SetBlockTop(roofHeight);
                AddRoofPick(t, roofHeight, tileSize);
            }

            Vector3 toRoof = roofTile.transform.position - footTile.transform.position;
            toRoof.y = 0f;
            Vector3 bottom = baseMark != null
                ? new Vector3(baseMark.position.x, groundTop, baseMark.position.z)
                : new Vector3(footTile.transform.position.x, groundTop, footTile.transform.position.z) + toRoof.normalized * (0.38f * tileSize);

            Ladder ladder = new Ladder
            {
                foot = footTile,
                roof = roofTile,
                climbBottom = bottom,
                climbTop = new Vector3(bottom.x, roofHeight, bottom.z)
            };
            ladderByTile[footTile] = ladder;
            ladderByTile[roofTile] = ladder;
        }
    }

    // A thin collider lying on the roof of a tile, so that a mouse click on the roof finds the roof tile.
    private static void AddRoofPick(Tile tile, float roofHeight, float tileSize)
    {
        GameObject pick = new GameObject("RoofPick");
        pick.transform.SetParent(tile.transform, false);
        pick.transform.position = new Vector3(tile.transform.position.x, roofHeight - 0.03f, tile.transform.position.z);

        Vector3 s = pick.transform.lossyScale;
        BoxCollider box = pick.AddComponent<BoxCollider>();
        box.size = new Vector3(tileSize * 0.98f / Mathf.Max(0.0001f, s.x), 0.06f / Mathf.Max(0.0001f, s.y), tileSize * 0.98f / Mathf.Max(0.0001f, s.z));
        pick.AddComponent<RoofPick>();
    }

    private static void SetLayerRecursively(GameObject go, int layer)
    {
        foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
            t.gameObject.layer = layer;
    }

    private GameObject InstantiateObject(GameObject prefab, Transform parent)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
            return (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
#endif
        return Instantiate(prefab, parent);
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
        tileList = new List<Tile>(tiles);
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

        Wall wallInfo = wall.GetComponent<Wall>();
        if (wallInfo == null)
            wallInfo = wall.AddComponent<Wall>();
        wallInfo.Init(a, b);

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
