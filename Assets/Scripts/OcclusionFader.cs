using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Makes the scenery see-through when it stands between the camera and a player unit, so the unit can always be seen.
///
/// Every frame it looks along the view direction from the unit (feet, middle and head) towards the camera. Every
/// building, car, palm... of the city whose box is in the way fades to a translucent look; when it is out of the way
/// it fades back and gets its normal (opaque) materials again. Only objects of the city (the children of the field's
/// 'Objects' container) are touched; flat things such as roads and sidewalks and thin things such as traffic poles are left alone.
///
/// Works with the URP Lit materials: a see-through twin of every material is made on the fly and the transparency is
/// set per object, so two objects that share a material can fade independently.
/// </summary>
public class OcclusionFader : MonoBehaviour
{
    [SerializeField] private GridManager grid;
    [Tooltip("The game camera. Empty = the main camera.")]
    [SerializeField] private Camera targetCamera;
    [Tooltip("Units of this faction are kept visible.")]
    [SerializeField] private Faction watchedFaction = Faction.Player;

    [Header("Look")]
    [Tooltip("How see-through an object in the way gets: 0 = invisible, 1 = not at all.")]
    [SerializeField, Range(0f, 1f)] private float fadedAlpha = 0.25f;
    [Tooltip("Seconds for a complete fade in or out.")]
    [SerializeField, Min(0.01f)] private float fadeTime = 0.15f;

    [Header("What counts as in the way")]
    [Tooltip("How far around the unit's line of sight an object still counts (world units).")]
    [SerializeField, Min(0f)] private float margin = 0.2f;
    [Tooltip("Objects flatter than this (roads, sidewalks) never hide anybody and are left alone.")]
    [SerializeField, Min(0f)] private float minObjectHeight = 0.3f;
    [Tooltip("Objects thinner than this (traffic poles) hide next to nothing and are left alone.")]
    [SerializeField, Min(0f)] private float minObjectWidth = 0.6f;
    [Tooltip("Height of a unit: its feet, middle and head are checked.")]
    [SerializeField, Min(0.1f)] private float unitHeight = 1f;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly float[] SampleHeights = { 0.1f, 0.5f, 0.95f };

    private class Entry
    {
        public Renderer[] renderers;
        public Bounds area;                 // the box of the object, grown by the margin
        public bool occluding;
        public float alpha = 1f;
        public bool swapped;                // currently showing the see-through materials
        public Material[][] originals;
        public Material[][] fades;
        public Color[][] colors;
    }

    private readonly List<Entry> entries = new List<Entry>();
    private readonly Dictionary<Material, Material> fadeMaterials = new Dictionary<Material, Material>();
    private MaterialPropertyBlock block;

    /// <summary>How many objects are see-through right now (for checking).</summary>
    public int FadedCount
    {
        get
        {
            int n = 0;
            foreach (Entry e in entries)
                if (e.swapped) n++;
            return n;
        }
    }

    private void OnEnable()
    {
        if (grid == null)
            grid = FindFirstObjectByType<GridManager>();

        if (grid != null)
        {
            grid.GridBuilt += Rebuild;
            Rebuild();
        }
    }

    private void OnDisable()
    {
        if (grid != null)
            grid.GridBuilt -= Rebuild;

        RestoreAll();
        entries.Clear();

        foreach (Material m in fadeMaterials.Values)
        {
            if (m == null)
                continue;
            if (Application.isPlaying)
                Destroy(m);
            else
                DestroyImmediate(m);
        }
        fadeMaterials.Clear();
    }

    private void LateUpdate()
    {
        Tick(Time.deltaTime);
    }

    /// <summary>Collects the objects of the city (call again when the field is rebuilt; it does that by itself).</summary>
    public void Rebuild()
    {
        RestoreAll();
        entries.Clear();

        if (grid == null)
            return;

        Transform root = grid.transform.Find("Objects");
        if (root == null)
            return;

        foreach (Transform child in root)
        {
            MeshRenderer[] renderers = child.GetComponentsInChildren<MeshRenderer>();
            if (renderers.Length == 0)
                continue;

            Bounds b = renderers[0].bounds;
            foreach (MeshRenderer r in renderers)
                b.Encapsulate(r.bounds);

            if (b.size.y < minObjectHeight)
                continue;                                   // road, sidewalk: never in the way

            if (Mathf.Max(b.size.x, b.size.z) < minObjectWidth)
                continue;                                   // a thin pole: not worth fading

            b.Expand(margin * 2f);
            entries.Add(new Entry { renderers = renderers, area = b });
        }
    }

    /// <summary>One step of the fading (done every frame by itself).</summary>
    public void Tick(float deltaTime)
    {
        if (entries.Count == 0)
            return;

        Camera cam = targetCamera != null ? targetCamera : Camera.main;
        if (cam == null)
            return;

        Vector3 towardsCamera = -cam.transform.forward;

        foreach (Entry e in entries)
            e.occluding = false;

        // which objects are between the camera and a unit?
        IReadOnlyList<Unit> units = Unit.All;
        for (int i = 0; i < units.Count; i++)
        {
            Unit unit = units[i];
            if (unit == null || unit.Faction != watchedFaction || !unit.IsAlive)
                continue;

            foreach (float h in SampleHeights)
            {
                Ray ray = new Ray(unit.transform.position + Vector3.up * (h * unitHeight), towardsCamera);
                foreach (Entry e in entries)
                {
                    if (e.occluding)
                        continue;

                    // standing on the roof of a building: that building is not in the way
                    if (e.area.Contains(ray.origin) && ray.origin.y >= e.area.max.y - 1.5f)
                        continue;

                    if (e.area.IntersectRay(ray))
                        e.occluding = true;
                }
            }
        }

        // fade towards the wanted look
        float speed = (1f - fadedAlpha) / fadeTime;
        foreach (Entry e in entries)
        {
            float target = e.occluding ? fadedAlpha : 1f;
            if (Mathf.Approximately(e.alpha, target) && (e.swapped == (e.alpha < 1f)))
                continue;

            e.alpha = Mathf.MoveTowards(e.alpha, target, speed * Mathf.Max(0f, deltaTime));

            if (e.alpha < 0.999f)
            {
                if (!e.swapped)
                    SwapToFade(e);
                ApplyAlpha(e);
            }
            else
            {
                e.alpha = 1f;
                if (e.swapped)
                    Restore(e);
            }
        }
    }

    // Gives the object see-through twins of its materials.
    private void SwapToFade(Entry e)
    {
        if (e.originals == null)
        {
            e.originals = new Material[e.renderers.Length][];
            e.fades = new Material[e.renderers.Length][];
            e.colors = new Color[e.renderers.Length][];

            for (int r = 0; r < e.renderers.Length; r++)
            {
                Material[] shared = e.renderers[r].sharedMaterials;
                e.originals[r] = shared;
                e.fades[r] = new Material[shared.Length];
                e.colors[r] = new Color[shared.Length];

                for (int m = 0; m < shared.Length; m++)
                {
                    Material original = shared[m];
                    bool canFade = original != null && original.HasProperty("_Surface") && original.HasProperty(BaseColorId);
                    e.fades[r][m] = canFade ? GetFadeMaterial(original) : original;
                    e.colors[r][m] = canFade ? original.GetColor(BaseColorId) : Color.white;
                }
            }
        }

        for (int r = 0; r < e.renderers.Length; r++)
            e.renderers[r].sharedMaterials = e.fades[r];

        e.swapped = true;
    }

    // Sets the transparency of every material of the object.
    private void ApplyAlpha(Entry e)
    {
        if (block == null)
            block = new MaterialPropertyBlock();

        for (int r = 0; r < e.renderers.Length; r++)
        {
            for (int m = 0; m < e.fades[r].Length; m++)
            {
                if (e.fades[r][m] == e.originals[r][m])
                    continue;                                // this material can not fade

                Color c = e.colors[r][m];
                block.Clear();
                block.SetColor(BaseColorId, new Color(c.r, c.g, c.b, c.a * e.alpha));
                e.renderers[r].SetPropertyBlock(block, m);
            }
        }
    }

    // Back to the normal, opaque materials.
    private void Restore(Entry e)
    {
        for (int r = 0; r < e.renderers.Length; r++)
        {
            if (e.renderers[r] == null)
                continue;

            e.renderers[r].sharedMaterials = e.originals[r];
            for (int m = 0; m < e.originals[r].Length; m++)
                e.renderers[r].SetPropertyBlock(null, m);
        }

        e.swapped = false;
        e.alpha = 1f;
    }

    private void RestoreAll()
    {
        foreach (Entry e in entries)
            if (e.swapped)
                Restore(e);
    }

    // The see-through twin of a URP Lit material (one per original, shared by all objects).
    private Material GetFadeMaterial(Material original)
    {
        if (fadeMaterials.TryGetValue(original, out Material twin) && twin != null)
            return twin;

        twin = new Material(original);
        twin.name = original.name + " (see-through)";
        twin.hideFlags = HideFlags.HideAndDontSave;
        twin.SetFloat("_Surface", 1f);                                                   // transparent
        twin.SetFloat("_Blend", 0f);                                                     // normal alpha blending
        twin.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        twin.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        twin.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        twin.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        twin.SetFloat("_ZWrite", 0f);
        twin.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        twin.SetOverrideTag("RenderType", "Transparent");
        twin.renderQueue = (int)RenderQueue.Transparent;

        fadeMaterials[original] = twin;
        return twin;
    }
}
