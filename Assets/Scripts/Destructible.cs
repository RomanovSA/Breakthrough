using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Gives a thing on the field (a car, a crate, a palm, a house...) health. A bullet that hits it takes health away; a thing that
/// can break and runs out of health breaks: it flies apart, the tiles it blocked become free again (walkable, no cover,
/// nothing blocks the view) and the maps of reach and cover are told to look again.
/// A thing that cannot break (a house with a roof ladder) takes the hits and stays.
/// GridManager adds this to every solid thing it puts on the field, with the numbers the city generator chose.
/// </summary>
public class Destructible : MonoBehaviour
{
    [SerializeField, Min(1)] private int maxHealth = 100;
    [SerializeField, Min(0)] private int health = 100;
    [Tooltip("Off: the thing takes damage but is never destroyed (a house with a roof ladder).")]
    [SerializeField] private bool canBreak = true;
    [Tooltip("The tiles that this thing blocks; they are set free when it breaks.")]
    [SerializeField] private List<Tile> tiles = new List<Tile>();

    private Renderer[] renderers;
    private Color[][] baseColours;
    private float flash;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    public int Health => health;
    public int MaxHealth => maxHealth;
    public bool CanBreak => canBreak;
    public bool IsBroken { get; private set; }

    /// <summary>How many hits it has taken (for checking).</summary>
    public int Hits { get; private set; }

    public void Setup(int startHealth, bool breakable, List<Tile> blockedTiles)
    {
        maxHealth = Mathf.Max(1, startHealth);
        health = maxHealth;
        canBreak = breakable;
        tiles = blockedTiles != null ? new List<Tile>(blockedTiles) : new List<Tile>();
    }

    /// <summary>A bullet (or anything else) hits it at this point.</summary>
    public void Damage(int amount, Vector3 point)
    {
        if (IsBroken || amount <= 0)
            return;

        Hits++;
        health = Mathf.Max(0, health - amount);
        flash = 1f;
        ApplyFlash(1f);

        if (health <= 0 && canBreak)
            Break();
    }

    private void Update()
    {
        if (flash <= 0f)
            return;

        flash = Mathf.Max(0f, flash - Time.deltaTime * 8f);
        ApplyFlash(flash);
    }

    // A short white flash of the whole thing when it is hit.
    private void ApplyFlash(float k)
    {
        if (renderers == null)
        {
            renderers = GetComponentsInChildren<MeshRenderer>();
            baseColours = new Color[renderers.Length][];
            for (int r = 0; r < renderers.Length; r++)
            {
                Material[] mats = renderers[r].sharedMaterials;
                baseColours[r] = new Color[mats.Length];
                for (int m = 0; m < mats.Length; m++)
                    baseColours[r][m] = mats[m] != null && mats[m].HasProperty(BaseColorId) ? mats[m].GetColor(BaseColorId) : Color.white;
            }
        }

        MaterialPropertyBlock block = new MaterialPropertyBlock();
        for (int r = 0; r < renderers.Length; r++)
        {
            if (renderers[r] == null)
                continue;

            Material[] mats = renderers[r].sharedMaterials;
            for (int m = 0; m < mats.Length; m++)
            {
                if (mats[m] == null || !mats[m].HasProperty(BaseColorId))
                    continue;

                if (k <= 0f)
                {
                    renderers[r].SetPropertyBlock(null, m);
                    continue;
                }

                block.Clear();
                block.SetColor(BaseColorId, Color.Lerp(baseColours[r][m], Color.white, 0.65f * k));
                renderers[r].SetPropertyBlock(block, m);
            }
        }
    }

    private void Break()
    {
        IsBroken = true;

        // it is only removed at the end of the frame: until then bullets must already fly through
        foreach (Collider c in GetComponentsInChildren<Collider>())
            c.enabled = false;

        // pieces flying apart, in the colour of the thing
        Bounds bounds = new Bounds(transform.position, Vector3.one * 0.5f);
        Color colour = new Color(0.45f, 0.45f, 0.45f);
        MeshRenderer[] parts = GetComponentsInChildren<MeshRenderer>();
        if (parts.Length > 0)
        {
            bounds = parts[0].bounds;
            foreach (MeshRenderer p in parts)
                bounds.Encapsulate(p.bounds);

            Material first = parts[0].sharedMaterial;
            if (first != null && first.HasProperty(BaseColorId))
                colour = first.GetColor(BaseColorId);
        }
        ImpactEffect.Debris(bounds, colour, Mathf.Clamp(Mathf.RoundToInt(bounds.size.magnitude * 3f), 5, 14));

        // the ground it stood on is free again
        foreach (Tile tile in tiles)
            if (tile != null)
                tile.ClearObstacle();
        Unit.NotifyMapChanged();

        if (Application.isPlaying) Destroy(gameObject); else DestroyImmediate(gameObject);
    }
}
