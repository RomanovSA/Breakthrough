using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Shows the cover (XCOM style) around the selected unit.
///
///  - Cover lines: on every tile the unit can walk to (and on the tile it stands on), the edge that faces a cover is
///    marked on the ground. A thick, dark blue line means FULL cover (a building, a high wall), a thin, light blue line
///    means HALF cover (a car, a bin, a bus stop). So walking up to cover, you see which kind it is before you get there.
///  - Shields: over the tile under the mouse cursor, and over the tile the unit stands on, a shield for every direction
///    that has cover, placed on the side of the tile that faces the cover. A full shield is FULL cover, a half-filled
///    shield is HALF cover. The shields are always drawn on top, so a building in front cannot hide them.
///
/// The cover itself comes from the field: GridManager.GetCover. Nothing is shown while the unit is moving or when no
/// unit is selected.
/// </summary>
public class CoverMarkers : MonoBehaviour
{
    [SerializeField] private UnitSelector selector;
    [SerializeField] private GridManager grid;
    [Tooltip("The game camera (the shields turn to face it). Empty = the main camera.")]
    [SerializeField] private Camera targetCamera;

    [Header("Cover lines on the ground (as a share of a tile)")]
    [SerializeField, Range(0.02f, 0.4f)] private float fullThickness = 0.16f;
    [SerializeField, Range(0.02f, 0.4f)] private float halfThickness = 0.07f;
    [SerializeField] private Color fullColor = new Color(0.1f, 0.4f, 1f, 0.95f);
    [SerializeField] private Color halfColor = new Color(0.6f, 0.88f, 1f, 0.9f);

    [Header("Shields")]
    [Tooltip("Width of a shield, in tiles.")]
    [SerializeField, Min(0.1f)] private float iconSize = 0.55f;
    [Tooltip("How high above the ground the shields float, in world units.")]
    [SerializeField, Min(0f)] private float iconHeight = 1.15f;
    [Tooltip("How far from the middle of the tile, towards the cover, a shield sits (in tiles; 0.5 = the edge).")]
    [SerializeField, Range(0f, 0.8f)] private float iconEdge = 0.4f;

    private const int ShieldPixels = 64;

    private readonly List<SpriteRenderer> bars = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer> icons = new List<SpriteRenderer>();
    private readonly List<Tile> area = new List<Tile>();

    private Sprite barSprite, fullShield, halfShield;
    private Material barMaterial, iconMaterial;
    private Transform barRoot, iconRoot;

    private Unit lastUnit;
    private Vector2Int lastCoords;
    private int lastActionPoints = -1;
    private int lastOccupancy = -1;
    private Tile shownOwnTile, shownHoveredTile;
    private bool visible;
    private float groundY;

    /// <summary>How many cover lines are drawn right now (for checking).</summary>
    public int BarCount { get { int n = 0; foreach (SpriteRenderer r in bars) if (r != null && r.gameObject.activeInHierarchy) n++; return n; } }

    /// <summary>How many shields are drawn right now (for checking).</summary>
    public int ShieldCount { get { int n = 0; foreach (SpriteRenderer r in icons) if (r != null && r.gameObject.activeInHierarchy) n++; return n; } }

    private void OnEnable()
    {
        if (grid == null)
            grid = FindFirstObjectByType<GridManager>();
        if (grid != null)
            grid.GridBuilt += ResetState;
    }

    private void OnDisable()
    {
        if (grid != null)
            grid.GridBuilt -= ResetState;
        DestroyEverything();
    }

    private void LateUpdate()
    {
        Refresh();
    }

    /// <summary>Brings the markers up to date (done every frame by itself).</summary>
    public void Refresh()
    {
        if (grid == null)
            grid = FindFirstObjectByType<GridManager>();

        Unit unit = selector != null ? selector.SelectedUnit : null;
        if (unit == null || grid == null || !unit.isActiveAndEnabled || Unit.AnyMoving)
        {
            Hide();
            return;
        }

        EnsureAssets();
        bool changed = !visible || unit != lastUnit || unit.Coords != lastCoords || unit.ActionPoints != lastActionPoints || Unit.OccupancyVersion != lastOccupancy;
        SetRootsActive(true);
        visible = true;

        if (changed)
        {
            lastUnit = unit;
            lastCoords = unit.Coords;
            lastActionPoints = unit.ActionPoints;
            lastOccupancy = Unit.OccupancyVersion;

            Tile origin = grid.GetTile(Vector2Int.zero);
            Renderer originRenderer = origin != null ? origin.GetComponent<Renderer>() : null;
            groundY = (originRenderer != null ? originRenderer.bounds.max.y : 0.5f) + grid.OverlayHeight;

            CollectArea(unit);
            BuildBars();
            shownOwnTile = null;
            shownHoveredTile = null;
        }

        // the shields: on the unit's own tile and on the tile under the cursor (if the unit can go there)
        Tile own = grid.GetTile(unit.Coords);
        Tile hovered = null;
        foreach (Tile t in area)
        {
            if (t != null && t.IsHighlighted)
            {
                hovered = t;
                break;
            }
        }
        if (own != shownOwnTile || hovered != shownHoveredTile)
        {
            shownOwnTile = own;
            shownHoveredTile = hovered;
            BuildShields(own, hovered);
        }

        // the shields always face the camera
        Camera cam = targetCamera != null ? targetCamera : Camera.main;
        if (cam != null)
            foreach (SpriteRenderer icon in icons)
                if (icon != null && icon.gameObject.activeSelf)
                    icon.transform.rotation = cam.transform.rotation;
    }

    // The tiles to mark: the one the unit stands on and all it can walk to this turn.
    private void CollectArea(Unit unit)
    {
        area.Clear();
        Tile own = grid.GetTile(unit.Coords);
        if (own == null)
            return;

        area.Add(own);
        area.AddRange(Pathfinder.GetReachableTiles(grid, own, unit.ActionPoints, unit));
    }

    // One line on the ground for every edge of those tiles that faces a cover.
    private void BuildBars()
    {
        float tileSize = grid.TileSize;
        int used = 0;

        foreach (Tile t in area)
        {
            if (t == null)
                continue;

            for (int d = 0; d < 4; d++)
            {
                CoverType cover = grid.GetCover(t.Coords, d);
                if (cover == CoverType.None)
                    continue;

                Vector2Int dir = GridManager.Directions[d];
                float thickness = (cover == CoverType.Full ? fullThickness : halfThickness) * tileSize;
                float length = 0.96f * tileSize;
                Vector3 centre = t.transform.position + new Vector3(dir.x, 0f, dir.y) * (0.5f * tileSize - 0.5f * thickness - 0.01f * tileSize);
                centre.y = groundY;

                SpriteRenderer bar = GetFromPool(bars, barRoot, barSprite, barMaterial, used++, "CoverLine");
                bar.color = cover == CoverType.Full ? fullColor : halfColor;
                bar.transform.SetPositionAndRotation(centre, Quaternion.Euler(90f, 0f, 0f));        // lying flat, facing up
                bar.transform.localScale = dir.x == 0 ? new Vector3(length, thickness, 1f) : new Vector3(thickness, length, 1f);
            }
        }

        for (int i = used; i < bars.Count; i++)
            if (bars[i] != null)
                bars[i].gameObject.SetActive(false);
    }

    // A shield for every covered direction of the unit's tile and of the tile under the cursor.
    private void BuildShields(Tile own, Tile hovered)
    {
        int used = 0;
        used = AddShields(own, used, 0.9f);
        if (hovered != null && hovered != own)
            used = AddShields(hovered, used, 1f);

        for (int i = used; i < icons.Count; i++)
            if (icons[i] != null)
                icons[i].gameObject.SetActive(false);
    }

    private int AddShields(Tile tile, int used, float alpha)
    {
        if (tile == null)
            return used;

        float tileSize = grid.TileSize;
        for (int d = 0; d < 4; d++)
        {
            CoverType cover = grid.GetCover(tile.Coords, d);
            if (cover == CoverType.None)
                continue;

            Vector2Int dir = GridManager.Directions[d];
            Vector3 position = tile.transform.position + new Vector3(dir.x, 0f, dir.y) * (iconEdge * tileSize);
            position.y = groundY + iconHeight;

            SpriteRenderer icon = GetFromPool(icons, iconRoot, cover == CoverType.Full ? fullShield : halfShield, iconMaterial, used++, "CoverShield");
            icon.color = new Color(1f, 1f, 1f, alpha);
            icon.transform.position = position;
            icon.transform.localScale = Vector3.one * (iconSize * tileSize);
        }
        return used;
    }

    private SpriteRenderer GetFromPool(List<SpriteRenderer> pool, Transform root, Sprite sprite, Material material, int index, string name)
    {
        while (pool.Count <= index)
        {
            GameObject go = new GameObject(name);
            go.hideFlags = HideFlags.DontSave;
            go.transform.SetParent(root, false);
            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sharedMaterial = material;
            sr.shadowCastingMode = ShadowCastingMode.Off;
            sr.receiveShadows = false;
            pool.Add(sr);
        }

        SpriteRenderer r = pool[index];
        if (r == null)
        {
            // destroyed from outside: make a new one in its place
            pool.RemoveAt(index);
            return GetFromPool(pool, root, sprite, material, index, name);
        }

        r.sprite = sprite;
        r.gameObject.SetActive(true);
        return r;
    }

    private void Hide()
    {
        if (!visible)
            return;

        visible = false;
        lastUnit = null;
        shownOwnTile = null;
        shownHoveredTile = null;
        SetRootsActive(false);
    }

    private void SetRootsActive(bool active)
    {
        if (barRoot != null && barRoot.gameObject.activeSelf != active) barRoot.gameObject.SetActive(active);
        if (iconRoot != null && iconRoot.gameObject.activeSelf != active) iconRoot.gameObject.SetActive(active);
    }

    // The field was rebuilt: the tiles the markers knew are gone.
    private void ResetState()
    {
        area.Clear();
        visible = false;
        lastUnit = null;
        shownOwnTile = null;
        shownHoveredTile = null;
        foreach (SpriteRenderer r in bars) if (r != null) r.gameObject.SetActive(false);
        foreach (SpriteRenderer r in icons) if (r != null) r.gameObject.SetActive(false);
    }

    // ---- what the markers are made of (made on the fly, nothing to set up) ----

    private void EnsureAssets()
    {
        if (barRoot == null)
        {
            barRoot = new GameObject("CoverLines").transform;
            barRoot.gameObject.hideFlags = HideFlags.DontSave;
            barRoot.SetParent(transform, false);
            bars.Clear();
        }
        if (iconRoot == null)
        {
            iconRoot = new GameObject("CoverShields").transform;
            iconRoot.gameObject.hideFlags = HideFlags.DontSave;
            iconRoot.SetParent(transform, false);
            icons.Clear();
        }

        if (barSprite == null)
        {
            Texture2D white = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            white.hideFlags = HideFlags.HideAndDontSave;
            Color[] px = new Color[16];
            for (int i = 0; i < px.Length; i++) px[i] = Color.white;
            white.SetPixels(px);
            white.Apply();
            barSprite = Sprite.Create(white, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);      // exactly 1 x 1 world units
            barSprite.hideFlags = HideFlags.HideAndDontSave;
        }
        if (fullShield == null) fullShield = MakeShield(true);
        if (halfShield == null) halfShield = MakeShield(false);

        if (barMaterial == null)
        {
            barMaterial = new Material(Shader.Find("Sprites/Default"));
            barMaterial.hideFlags = HideFlags.HideAndDontSave;
        }
        if (iconMaterial == null)
        {
            // a UI shader whose depth test can be switched off, so the shields are drawn over buildings
            iconMaterial = new Material(Shader.Find("UI/Default"));
            iconMaterial.hideFlags = HideFlags.HideAndDontSave;
            iconMaterial.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
            iconMaterial.renderQueue = 4000;
        }
    }

    private void DestroyEverything()
    {
        foreach (Object o in new Object[] { barRoot != null ? barRoot.gameObject : null, iconRoot != null ? iconRoot.gameObject : null, barSprite, fullShield, halfShield, barMaterial, iconMaterial })
        {
            if (o == null)
                continue;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }

        bars.Clear();
        icons.Clear();
        barRoot = iconRoot = null;
        barSprite = fullShield = halfShield = null;
        barMaterial = iconMaterial = null;
        visible = false;
        lastUnit = null;
    }

    // A shield sprite drawn in code: white outline, blue inside; for half cover only the lower half is filled.
    private static Sprite MakeShield(bool full)
    {
        const int N = ShieldPixels;
        const int S = 4;                                           // sub-samples per pixel side, for smooth edges
        Texture2D tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.hideFlags = HideFlags.HideAndDontSave;

        Color filled = new Color(0.2f, 0.55f, 1f, 1f);
        Color empty = new Color(0.04f, 0.08f, 0.18f, 0.6f);
        Color[] pixels = new Color[N * N];

        for (int y = 0; y < N; y++)
        {
            for (int x = 0; x < N; x++)
            {
                float r = 0f, g = 0f, b = 0f, a = 0f;
                for (int sy = 0; sy < S; sy++)
                {
                    for (int sx = 0; sx < S; sx++)
                    {
                        float u = ((x + (sx + 0.5f) / S) / N) * 2f - 1f;
                        float v = ((y + (sy + 0.5f) / S) / N) * 2f - 1f;
                        if (!InsideShield(u, v, 0f))
                            continue;

                        Color c;
                        if (!InsideShield(u, v, 0.14f)) c = Color.white;               // the outline
                        else if (full || v < 0.05f) c = filled;                       // filled part
                        else c = empty;                                               // the empty upper half of a half shield
                        r += c.r * c.a; g += c.g * c.a; b += c.b * c.a; a += c.a;
                    }
                }

                float samples = S * S;
                pixels[y * N + x] = a > 0f ? new Color(r / a, g / a, b / a, a / samples) : Color.clear;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        Sprite sprite = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), N);        // 1 x 1 world units
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }

    // The shape of a shield in -1..1 coordinates: flat top, straight sides, rounded point at the bottom.
    private static bool InsideShield(float u, float v, float shrink)
    {
        float top = 0.9f - shrink;
        float bottom = -0.95f + shrink * 1.2f;
        if (v > top || v < bottom)
            return false;

        float halfWidth = 0.78f - shrink;
        if (v < 0.1f)
        {
            float k = (0.1f - v) / 1.05f;                          // 0 at the start of the taper, 1 at the point
            halfWidth *= Mathf.Sqrt(Mathf.Max(0f, 1f - k * k));
        }
        return Mathf.Abs(u) <= halfWidth;
    }
}
