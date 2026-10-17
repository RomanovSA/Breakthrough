using UnityEngine;

/// <summary>
/// A wall standing on the edge between two tiles. It knows which tiles those are and can be darkened
/// when the selected unit cannot see it.
/// </summary>
public class Wall : MonoBehaviour
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"); // URP / HDRP
    private static readonly int ColorId = Shader.PropertyToID("_Color");         // Built-in

    [SerializeField] private Vector2Int a;
    [SerializeField] private Vector2Int b;

    private Renderer[] renderers;
    private MaterialPropertyBlock block;
    private bool isShaded;
    private float shadeFactor = 1f;

    /// <summary>One of the two tiles the wall stands between.</summary>
    public Vector2Int A => a;

    /// <summary>The other tile the wall stands between.</summary>
    public Vector2Int B => b;

    public bool IsShaded => isShaded;

    public void Init(Vector2Int tileA, Vector2Int tileB)
    {
        a = tileA;
        b = tileB;
    }

    public void SetShaded(bool shaded, float factor)
    {
        if (isShaded == shaded && (!shaded || Mathf.Approximately(shadeFactor, factor)))
            return;

        isShaded = shaded;
        shadeFactor = factor;

        if (renderers == null)
            renderers = GetComponentsInChildren<Renderer>();
        if (block == null)
            block = new MaterialPropertyBlock();

        foreach (Renderer r in renderers)
        {
            if (r == null)
                continue;

            Material[] mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (!shaded || mats[i] == null)
                {
                    r.SetPropertyBlock(null, i);
                    continue;
                }

                Color c = mats[i].HasProperty("_BaseColor") ? mats[i].GetColor("_BaseColor")
                        : mats[i].HasProperty("_Color") ? mats[i].GetColor("_Color") : Color.white;
                c = new Color(c.r * factor, c.g * factor, c.b * factor, c.a);

                r.GetPropertyBlock(block, i);
                block.SetColor(BaseColorId, c);
                block.SetColor(ColorId, c);
                r.SetPropertyBlock(block, i);
            }
        }
    }
}
