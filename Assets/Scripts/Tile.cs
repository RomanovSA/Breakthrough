using TMPro;
using UnityEngine;

/// <summary>
/// A single cell of the field. Stores its grid coordinates, shows them on the top face
/// and remembers which of its edges have a wall (only the east (+x) and north (+y) edges are stored
/// here; the west and south edges belong to the neighbouring tiles).
///
/// The top of the tile can be tinted: blue while the mouse hovers over it, green while it is
/// within reach of the selected unit. Hovering wins over the green tint.
/// </summary>
public class Tile : MonoBehaviour
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"); // URP / HDRP
    private static readonly int ColorId = Shader.PropertyToID("_Color");         // Built-in

    [SerializeField] private Vector2Int coords;
    [SerializeField] private TextMeshPro label;
    [SerializeField] private Color highlightColor = new Color(0.45f, 0.85f, 1f, 1f);
    [SerializeField] private Color reachableColor = new Color(0.55f, 0.9f, 0.55f, 1f);
    [SerializeField] private bool wallEast;
    [SerializeField] private bool wallNorth;

    private Renderer tileRenderer;
    private MaterialPropertyBlock block;
    private bool isHovered;
    private bool isReachable;

    public Vector2Int Coords => coords;

    /// <summary>A wall stands on the edge between this tile and the tile at (x + 1, y).</summary>
    public bool HasWallEast => wallEast;

    /// <summary>A wall stands on the edge between this tile and the tile at (x, y + 1).</summary>
    public bool HasWallNorth => wallNorth;

    /// <summary>True while the tile is marked as reachable for the selected unit.</summary>
    public bool IsReachable => isReachable;

    public void Init(Vector2Int gridCoords)
    {
        coords = gridCoords;
        wallEast = false;
        wallNorth = false;
        gameObject.name = $"Tile_{gridCoords.x}_{gridCoords.y}";

        if (label != null)
            label.text = $"{gridCoords.x},{gridCoords.y}";
    }

    public void SetWallEast(bool hasWall)
    {
        wallEast = hasWall;
    }

    public void SetWallNorth(bool hasWall)
    {
        wallNorth = hasWall;
    }

    /// <summary>Hover highlight (used while the mouse is over the tile).</summary>
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

    private void ApplyVisual()
    {
        if (tileRenderer == null)
            tileRenderer = GetComponent<Renderer>();
        if (tileRenderer == null)
            return;

        if (!isHovered && !isReachable)
        {
            // Drop the override so the material's own color is used again.
            tileRenderer.SetPropertyBlock(null);
            return;
        }

        Color color = isHovered ? highlightColor : reachableColor;

        if (block == null)
            block = new MaterialPropertyBlock();

        tileRenderer.GetPropertyBlock(block);
        block.SetColor(BaseColorId, color);
        block.SetColor(ColorId, color);
        tileRenderer.SetPropertyBlock(block);
    }
}
