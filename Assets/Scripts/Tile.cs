using TMPro;
using UnityEngine;

/// <summary>
/// A single cell of the field. Stores its grid coordinates, shows them on the top face, remembers which of its
/// edges have a wall and whether something solid (a building) stands on it.
///
/// A tile can be marked: blue while the mouse hovers over it, green while it is within reach of the selected unit,
/// dark while the selected unit cannot see it. The marks are not painted on the tile itself (the ground is covered by
/// models) but on the field's TileOverlay, a layer just above the ground. Hovering wins over reachable, reachable
/// wins over dark.
/// </summary>
public class Tile : MonoBehaviour
{
    [SerializeField] private Vector2Int coords;
    [SerializeField] private TextMeshPro label;
    [SerializeField] private Color highlightColor = new Color(0.45f, 0.85f, 1f, 1f);
    [SerializeField] private Color reachableColor = new Color(0.55f, 0.9f, 0.55f, 1f);
    [SerializeField] private bool wallEast;
    [SerializeField] private bool wallNorth;
    [SerializeField] private bool isBlocked;
    [SerializeField] private bool blocksSight;
    [SerializeField] private CoverType providesCover;
    [SerializeField] private bool hasRoof;
    [SerializeField] private float roofY;
    [SerializeField] private float blockTopY;

    private const float HoverAlpha = 0.55f;
    private const float ReachableAlpha = 0.5f;

    private GridManager grid;
    private bool isHovered;
    private bool isReachable;
    private bool isShaded;
    private CoverType coverHighlight;
    private float shadeFactor = 1f;

    public Vector2Int Coords => coords;

    /// <summary>A wall stands on the edge between this tile and the tile at (x + 1, y).</summary>
    public bool HasWallEast => wallEast;

    /// <summary>A wall stands on the edge between this tile and the tile at (x, y + 1).</summary>
    public bool HasWallNorth => wallNorth;

    /// <summary>Something solid (a building, a car) stands on this tile: units cannot walk onto it.</summary>
    public bool IsBlocked => isBlocked;

    /// <summary>Something tall (a building) stands on this tile: nobody can see through it.</summary>
    public bool BlocksSight => blocksSight;

    /// <summary>The cover this tile gives to its neighbours (a unit next to it is in cover): what stands on the tile decides.</summary>
    public CoverType ProvidesCover => providesCover;

    /// <summary>True while the mouse hovers over the tile.</summary>
    public bool IsHighlighted => isHovered;

    /// <summary>
    /// The top of this tile (the flat roof of the building that stands on it) can be walked on: it is reached by a ladder.
    /// Such a tile is still blocked on the ground; a unit on its roof is on the same tile coordinates, higher up.
    /// </summary>
    public bool HasRoof => hasRoof;

    /// <summary>Height of the roof of this tile in world units (only meaningful when HasRoof).</summary>
    public float RoofY => roofY;

    /// <summary>How high the thing that blocks the view on this tile reaches (world units). Someone standing higher sees over it.</summary>
    public float BlockTopY => blockTopY;

    /// <summary>True while the tile is marked as reachable for the selected unit.</summary>
    public bool IsReachable => isReachable;

    /// <summary>True while the tile is darkened because the selected unit cannot see it.</summary>
    public bool IsShaded => isShaded;

    public void Init(Vector2Int gridCoords)
    {
        coords = gridCoords;
        wallEast = false;
        wallNorth = false;
        isBlocked = false;
        blocksSight = false;
        providesCover = CoverType.None;
        hasRoof = false;
        roofY = 0f;
        blockTopY = 0f;
        gameObject.name = $"Tile_{gridCoords.x}_{gridCoords.y}";

        if (label != null)
            label.text = $"{gridCoords.x},{gridCoords.y}";
    }

    /// <summary>Shows or hides the x,y number on the tile.</summary>
    public void SetLabelVisible(bool visible)
    {
        if (label != null)
            label.gameObject.SetActive(visible);
    }

    public void SetWallEast(bool hasWall)
    {
        wallEast = hasWall;
    }

    public void SetWallNorth(bool hasWall)
    {
        wallNorth = hasWall;
    }

    public void SetBlocked(bool blocked)
    {
        isBlocked = blocked;
    }

    /// <summary>The thing that stood on this tile is gone: it is walkable again, gives no cover and does not block the view.</summary>
    public void ClearObstacle()
    {
        isBlocked = false;
        blocksSight = false;
        providesCover = CoverType.None;
        blockTopY = 0f;
    }

    public void SetBlocksSight(bool blocks)
    {
        blocksSight = blocks;
    }

    /// <summary>Makes the top of this tile a walkable roof at the given height.</summary>
    public void SetRoof(float height)
    {
        hasRoof = true;
        roofY = height;
    }

    /// <summary>How high what blocks the view on this tile reaches.</summary>
    public void SetBlockTop(float height)
    {
        blockTopY = height;
    }

    /// <summary>
    /// The tile a mouse ray hit, as far as the player is concerned: a free tile, or the roof of a building tile (when the ray
    /// hit the roof's own collider). A building tile hit anywhere else (its walls, the ground under it) is not a target.
    /// </summary>
    public static Tile FromCollider(Collider collider)
    {
        if (collider == null)
            return null;

        Tile t = collider.GetComponentInParent<Tile>();
        if (t == null)
            return null;

        if (t.IsBlocked)
            return t.HasRoof && collider.GetComponent<RoofPick>() != null ? t : null;

        return t;
    }

    /// <summary>Sets the cover this tile gives to its neighbours (the better one wins when several things stand here).</summary>
    public void SetProvidesCover(CoverType cover)
    {
        if (cover > providesCover)
            providesCover = cover;
    }

    /// <summary>Marks the tile as the cover a unit is about to use (None removes the mark).</summary>
    public void SetCoverHighlight(CoverType cover)
    {
        coverHighlight = cover;
        ApplyVisual();
    }

    /// <summary>Hover mark (used while the mouse is over the tile).</summary>
    public void SetHighlighted(bool highlighted)
    {
        isHovered = highlighted;
        ApplyVisual();
    }

    /// <summary>Marks the tile as one the selected unit can walk to this turn.</summary>
    public void SetReachable(bool reachable)
    {
        isReachable = reachable;
        ApplyVisual();
    }

    /// <summary>
    /// Darkens the tile (the selected unit cannot see it): factor is how bright it stays, 1 = not at all, 0 = black.
    /// The hover and reachable marks are shown at full strength even on a darkened tile.
    /// </summary>
    public void SetShaded(bool shaded, float factor)
    {
        if (isShaded == shaded && (!shaded || Mathf.Approximately(shadeFactor, factor)))
            return;

        isShaded = shaded;
        shadeFactor = factor;
        ApplyVisual();
    }

    // Works out the colour of the mark and hands it to the overlay.
    private void ApplyVisual()
    {
        if (grid == null)
            grid = GetComponentInParent<GridManager>();

        TileOverlay overlay = grid != null ? grid.Overlay : null;
        if (overlay == null)
            return;

        Color mark;
        if (isHovered)
            mark = new Color(highlightColor.r, highlightColor.g, highlightColor.b, HoverAlpha);
        else if (coverHighlight != CoverType.None)
            mark = coverHighlight == CoverType.Full ? new Color(0.15f, 0.45f, 1f, 0.6f) : new Color(0.55f, 0.85f, 1f, 0.5f);
        else if (isReachable)
            mark = new Color(reachableColor.r, reachableColor.g, reachableColor.b, ReachableAlpha);
        else if (isShaded)
            mark = new Color(0f, 0f, 0f, 1f - shadeFactor);
        else
            mark = Color.clear;

        overlay.SetTileColor(coords, mark);
    }
}
