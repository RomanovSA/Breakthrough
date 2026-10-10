using TMPro;
using UnityEngine;

/// <summary>
/// A single cell of the field. Stores its grid coordinates, shows them on the top face,
/// can be highlighted (e.g. while the mouse hovers over it) and remembers which of its
/// edges have a wall. Only the east (+x) and north (+y) edges are stored here;
/// the west and south edges belong to the neighbouring tiles.
/// </summary>
public class Tile : MonoBehaviour
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"); // URP / HDRP
    private static readonly int ColorId = Shader.PropertyToID("_Color");         // Built-in

    [SerializeField] private Vector2Int coords;
    [SerializeField] private TextMeshPro label;
    [SerializeField] private Color highlightColor = new Color(0.45f, 0.85f, 1f, 1f);
    [SerializeField] private bool wallEast;
    [SerializeField] private bool wallNorth;

    private Renderer tileRenderer;
    private MaterialPropertyBlock block;

    public Vector2Int Coords => coords;

    /// <summary>A wall stands on the edge between this tile and the tile at (x + 1, y).</summary>
    public bool HasWallEast => wallEast;

    /// <summary>A wall stands on the edge between this tile and the tile at (x, y + 1).</summary>
    public bool HasWallNorth => wallNorth;

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

    public void SetHighlighted(bool highlighted)
    {
        if (tileRenderer == null)
            tileRenderer = GetComponent<Renderer>();
        if (tileRenderer == null)
            return;

        if (highlighted)
        {
            if (block == null)
                block = new MaterialPropertyBlock();

            tileRenderer.GetPropertyBlock(block);
            block.SetColor(BaseColorId, highlightColor);
            block.SetColor(ColorId, highlightColor);
            tileRenderer.SetPropertyBlock(block);
        }
        else
        {
            // Drop the override so the material's own color is used again.
            tileRenderer.SetPropertyBlock(null);
        }
    }
}
