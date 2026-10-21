using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// TEMPORARY: draws the area a unit can see, flat on the ground, as a translucent fan in the faction's colour
/// (blue for the player, red for enemies). The fan follows the unit's facing, is as wide as the unit's view angle
/// and as long as its view range, and is cut short where a wall blocks the view or the field ends.
/// Lives on a child of the unit. Switch it off with 'Visible', or remove the VisionCone object when the real
/// vision system no longer needs it.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class UnitVisionDisplay : MonoBehaviour
{
    [SerializeField] private bool visible = true;
    [SerializeField] private bool wallsBlockView = true;
    [SerializeField] private Color playerColor = new Color(0.25f, 0.85f, 1f, 0.35f);
    [SerializeField] private Color enemyColor = new Color(1f, 0.3f, 0.25f, 0.35f);

    [Tooltip("One ray per this many degrees. Smaller = smoother edge of the fan.")]
    [SerializeField, Range(0.25f, 5f)] private float rayStepDegrees = 1f;

    private Unit unit;
    private GridManager grid;
    private Mesh mesh;
    private MeshRenderer meshRenderer;

    private readonly List<Vector3> vertices = new List<Vector3>();
    private readonly List<Color> colors = new List<Color>();
    private readonly List<int> triangles = new List<int>();

    private void OnEnable()
    {
        meshRenderer = GetComponent<MeshRenderer>();

        mesh = new Mesh { name = "UnitVision" };
        mesh.hideFlags = HideFlags.HideAndDontSave;       // built at run time, never saved into the scene
        GetComponent<MeshFilter>().sharedMesh = mesh;
    }

    private void OnDisable()
    {
        if (mesh == null)
            return;

        if (Application.isPlaying)
            Destroy(mesh);
        else
            DestroyImmediate(mesh);
    }

    private void LateUpdate()
    {
        Rebuild();
    }

    /// <summary>Rebuilds the fan from the unit's current position, facing and settings.</summary>
    public void Rebuild()
    {
        if (mesh == null)
            OnEnable();

        meshRenderer.enabled = visible;
        if (!visible)
            return;

        if (unit == null)
            unit = GetComponentInParent<Unit>();
        if (unit == null)
            return;
        if (grid == null)
            grid = FindFirstObjectByType<GridManager>();

        float tileSize = grid != null ? grid.TileSize : 1f;
        float range = unit.ViewRange * tileSize;
        float angle = Mathf.Clamp(unit.ViewAngle, 1f, 360f);
        Vector3 forward = unit.FacingDirection;
        Vector3 origin = transform.position;
        origin.y = unit.transform.position.y + (grid != null ? grid.OverlayHeight : 0.02f) + 0.02f;   // above the tile marks

        int rays = Mathf.Max(2, Mathf.CeilToInt(angle / rayStepDegrees) + 1);
        Color baseColor = unit.Faction == Faction.Player ? playerColor : enemyColor;
        Color farColor = new Color(baseColor.r, baseColor.g, baseColor.b, baseColor.a * 0.4f);   // fades out with distance

        vertices.Clear();
        colors.Clear();
        triangles.Clear();

        vertices.Add(transform.InverseTransformPoint(origin));
        colors.Add(baseColor);

        for (int r = 0; r < rays; r++)
        {
            float a = -angle * 0.5f + angle * r / (rays - 1);
            Vector3 dir = Quaternion.AngleAxis(a, Vector3.up) * forward;
            float dist = (grid != null && wallsBlockView) ? grid.VisibleDistance(origin, dir, range) : range;

            vertices.Add(transform.InverseTransformPoint(origin + dir * dist));
            colors.Add(farColor);

            if (r > 0)
            {
                triangles.Add(0);
                triangles.Add(r);
                triangles.Add(r + 1);
            }
        }

        mesh.Clear();
        mesh.SetVertices(vertices);
        mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
    }
}
