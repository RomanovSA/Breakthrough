using UnityEngine;

/// <summary>
/// One flat, translucent layer just above the ground of the field, with a square for every tile. The tiles use it
/// to show their marks (hover, reachable, in the dark) on top of whatever models cover the ground.
/// It is a single mesh with one square per tile; only the colours change, so marking tiles is cheap.
/// Built by GridManager whenever the field is built.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class TileOverlay : MonoBehaviour
{
    [SerializeField] private float height = 0.02f;
    [Tooltip("How much of a tile the square covers (below 1 leaves a thin line between the squares).")]
    [SerializeField, Range(0.5f, 1f)] private float coverage = 0.94f;

    private GridManager grid;
    private Mesh mesh;
    private Color[] colors;
    private Vector2Int size;
    private bool dirty;

    /// <summary>How far above the top of the tiles the layer is, in world units.</summary>
    public float Height => height;

    private void OnEnable()
    {
        Rebuild();
    }

    private void OnDisable()
    {
        DestroyMesh();
    }

    private void LateUpdate()
    {
        Apply();
    }

    /// <summary>Sets the height of the layer and the material of the squares. Call Rebuild afterwards.</summary>
    public void Configure(Material material, float heightAboveTiles)
    {
        height = heightAboveTiles;
        GetComponent<MeshRenderer>().sharedMaterial = material;
    }

    /// <summary>Builds one square for every tile that exists now.</summary>
    public void Rebuild()
    {
        grid = GetComponentInParent<GridManager>();
        if (grid == null)
            return;

        size = grid.Size;
        if (size.x == 0 || size.y == 0)
            return;

        Tile origin = grid.GetTile(Vector2Int.zero);
        if (origin == null)
            return;

        Renderer originRenderer = origin.GetComponent<Renderer>();
        float top = originRenderer != null ? originRenderer.bounds.max.y : origin.transform.position.y;
        float tileSize = grid.TileSize;
        float half = 0.5f * tileSize * coverage;
        Vector3 start = origin.transform.position;

        int quads = size.x * size.y;
        Vector3[] vertices = new Vector3[quads * 4];
        int[] triangles = new int[quads * 6];
        colors = new Color[quads * 4];

        for (int x = 0; x < size.x; x++)
        {
            for (int y = 0; y < size.y; y++)
            {
                int q = x * size.y + y;
                Tile here = grid.GetTile(new Vector2Int(x, y));
                float baseY = here != null && here.HasRoof ? here.RoofY : top;          // a roof carries its own marks
                Vector3 c = new Vector3(start.x + x * tileSize, baseY + height, start.z + y * tileSize);
                int v = q * 4;
                vertices[v] = transform.InverseTransformPoint(c + new Vector3(-half, 0f, -half));
                vertices[v + 1] = transform.InverseTransformPoint(c + new Vector3(-half, 0f, half));
                vertices[v + 2] = transform.InverseTransformPoint(c + new Vector3(half, 0f, half));
                vertices[v + 3] = transform.InverseTransformPoint(c + new Vector3(half, 0f, -half));

                int t = q * 6;
                triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + 2;
                triangles[t + 3] = v; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3;
            }
        }

        DestroyMesh();
        mesh = new Mesh { name = "TileOverlay" };
        mesh.hideFlags = HideFlags.HideAndDontSave;                       // rebuilt on load, never saved in the scene
        mesh.indexFormat = quads * 4 > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.colors = colors;
        mesh.RecalculateBounds();
        GetComponent<MeshFilter>().sharedMesh = mesh;

        MeshRenderer meshRenderer = GetComponent<MeshRenderer>();
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        meshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        meshRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        dirty = false;
    }

    /// <summary>Gives the square of a tile its colour (clear = no mark).</summary>
    public void SetTileColor(Vector2Int tile, Color color)
    {
        if (colors == null || tile.x < 0 || tile.y < 0 || tile.x >= size.x || tile.y >= size.y)
            return;

        int v = (tile.x * size.y + tile.y) * 4;
        if (colors[v] == color)
            return;

        colors[v] = color; colors[v + 1] = color; colors[v + 2] = color; colors[v + 3] = color;
        dirty = true;
    }

    /// <summary>The colour a tile's square has now.</summary>
    public Color GetTileColor(Vector2Int tile)
    {
        if (colors == null || tile.x < 0 || tile.y < 0 || tile.x >= size.x || tile.y >= size.y)
            return Color.clear;

        return colors[(tile.x * size.y + tile.y) * 4];
    }

    /// <summary>Sends the changed colours to the mesh (done every frame by itself).</summary>
    public void Apply()
    {
        if (!dirty || mesh == null)
            return;

        mesh.colors = colors;
        dirty = false;
    }

    private void DestroyMesh()
    {
        if (mesh == null)
            return;

        if (Application.isPlaying)
            Destroy(mesh);
        else
            DestroyImmediate(mesh);

        mesh = null;
    }
}
