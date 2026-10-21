using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Marks on the map for what the player has heard (the noise comes from NoiseSystem: enemy footsteps within 10 tiles, shots within 20).
/// Where a unit of the player hears an enemy that he cannot see, a mark appears on that place: amber waves for footsteps, red waves with a
/// star for a shot. It pulses, always shows above everything (the enemy himself stays hidden), follows the enemy while he keeps walking and
/// is not given away more exactly than sound does: for footsteps the mark is up to Footstep Uncertainty tiles off the true place (the same
/// error all the time, so it does not jump around); a shot is marked exactly.
/// The mark goes away after Lifetime Turns turns of the player, when the enemy that made it is seen, or when he dies.
/// There is one mark for each enemy that was heard. Put this on any object in the scene (one is enough).
/// </summary>
public class NoiseMarkers : MonoBehaviour
{
    [SerializeField] private GridManager grid;
    [Tooltip("Whose ears count: noises heard by units of this faction are marked.")]
    [SerializeField] private Faction listenerFaction = Faction.Player;
    [Tooltip("A mark stays for this many turns of the player (counted from the turn it was last updated).")]
    [SerializeField, Min(1)] private int lifetimeTurns = 2;
    [Tooltip("The mark of footsteps is off the true place by up to this many tiles.")]
    [SerializeField, Min(0)] private int footstepUncertainty = 1;
    [Tooltip("The mark of a shot is off the true place by up to this many tiles.")]
    [SerializeField, Min(0)] private int shotUncertainty = 0;
    [Tooltip("How high above the ground the mark floats.")]
    [SerializeField, Min(0f)] private float height = 1.5f;
    [Tooltip("The smallest size of a mark (world units); when the camera is zoomed far out it grows so that it can still be seen.")]
    [SerializeField, Min(0.1f)] private float minSize = 1.2f;
    [SerializeField, Min(0f)] private float sizePerCameraUnit = 0.09f;

    private class Marker
    {
        public Unit source;
        public NoiseKind kind;
        public Vector2Int tile;          // where it is drawn
        public Vector2Int offset;        // how far off the true place the listener thinks it is (kept, so the mark does not jump)
        public int round;                // the player's turn it was last heard in
        public GameObject go;
        public SpriteRenderer renderer;
        public float age;
        public float fade = 1f;
        public bool dying;
    }

    private const int SpritePixels = 128;

    private readonly Dictionary<Unit, Marker> markers = new Dictionary<Unit, Marker>();
    private readonly List<Unit> toDrop = new List<Unit>();
    private Sprite footstepSprite;
    private Sprite shotSprite;
    private Material material;
    private float groundTop = float.NaN;

    /// <summary>How many marks there are (the ones that are fading out included).</summary>
    public int Count => markers.Count;

    /// <summary>Where the mark of this enemy is (false if there is none).</summary>
    public bool TryGetMark(Unit source, out Vector2Int tile, out NoiseKind kind)
    {
        Marker m;
        if (source != null && markers.TryGetValue(source, out m) && !m.dying)
        {
            tile = m.tile;
            kind = m.kind;
            return true;
        }
        tile = default;
        kind = default;
        return false;
    }

    private void OnEnable()
    {
        if (grid == null)
            grid = FindFirstObjectByType<GridManager>();
        NoiseSystem.Heard += OnHeard;
    }

    private void OnDisable()
    {
        NoiseSystem.Heard -= OnHeard;
        Clear();
    }

    /// <summary>Takes all the marks away.</summary>
    public void Clear()
    {
        foreach (Marker m in markers.Values)
            DestroyMarker(m);
        markers.Clear();
    }

    private int CurrentRound()
    {
        return TurnManager.Instance != null ? TurnManager.Instance.Round : 0;
    }

    private void OnHeard(NoiseEvent e, IReadOnlyList<Unit> listeners)
    {
        bool mine = false;
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners[i] != null && listeners[i].Faction == listenerFaction)
            {
                mine = true;
                break;
            }
        }
        if (!mine || e.source == null)
            return;

        // an enemy that can be seen needs no mark
        if (!e.source.IsConcealed)
        {
            Marker seen;
            if (markers.TryGetValue(e.source, out seen))
                seen.dying = true;
            return;
        }

        if (grid == null)
            grid = FindFirstObjectByType<GridManager>();
        if (grid == null)
            return;

        Marker m;
        if (!markers.TryGetValue(e.source, out m))
        {
            m = new Marker { source = e.source };
            markers[e.source] = m;
            CreateObject(m);
            m.offset = Vector2Int.zero;
        }

        // footsteps: a fixed error is picked once and kept; a shot is exact
        if (e.kind == NoiseKind.Shot)
        {
            m.offset = shotUncertainty > 0 ? NoiseSystem.FuzzyTile(grid, e.tile, shotUncertainty) - e.tile : Vector2Int.zero;
        }
        else if (m.age <= 0f || m.dying || m.offset == Vector2Int.zero && footstepUncertainty > 0 && m.round != CurrentRound())
        {
            m.offset = NoiseSystem.FuzzyTile(grid, e.tile, footstepUncertainty) - e.tile;
        }

        Vector2Int drawn = e.tile + m.offset;
        Tile t = grid.GetTile(drawn);
        m.tile = t != null && !t.IsBlocked ? drawn : e.tile;

        // a shot is not downgraded to footsteps within the same turn
        if (e.kind == NoiseKind.Shot || m.round != CurrentRound() || m.dying)
            m.kind = e.kind;
        m.round = CurrentRound();
        m.dying = false;
        m.fade = Mathf.Max(m.fade, 0.4f);
        ApplyKind(m);
    }

    private void Update()
    {
        int round = CurrentRound();
        toDrop.Clear();

        foreach (KeyValuePair<Unit, Marker> pair in markers)
        {
            Marker m = pair.Value;
            m.age += Time.unscaledDeltaTime;

            if (!m.dying)
            {
                if (m.source == null || !m.source.IsAlive || !m.source.IsConcealed || round - m.round >= lifetimeTurns)
                    m.dying = true;
            }

            m.fade = m.dying ? m.fade - Time.unscaledDeltaTime / 0.5f : Mathf.Min(1f, m.fade + Time.unscaledDeltaTime / 0.25f);
            if (m.dying && m.fade <= 0f)
                toDrop.Add(pair.Key);
        }

        foreach (Unit u in toDrop)
        {
            DestroyMarker(markers[u]);
            markers.Remove(u);
        }
    }

    private void LateUpdate()
    {
        Camera cam = Camera.main;
        if (cam == null)
            return;

        bool hidden = ShotCamera.Active;            // the shot view has its own things to look at
        float size = Mathf.Max(minSize, cam.orthographic ? cam.orthographicSize * sizePerCameraUnit : minSize);

        foreach (Marker m in markers.Values)
        {
            if (m.go == null)
                continue;

            m.renderer.enabled = !hidden;
            Tile t = grid != null ? grid.GetTile(m.tile) : null;
            if (t != null)
                m.go.transform.position = TopOf(t) + Vector3.up * height;

            float pulse = 1f + 0.1f * Mathf.Sin(m.age * 5f);
            m.go.transform.rotation = cam.transform.rotation;                       // always faces the camera
            m.go.transform.localScale = Vector3.one * (size * pulse);
            m.renderer.color = new Color(1f, 1f, 1f, Mathf.Clamp01(m.fade) * (0.8f + 0.2f * Mathf.Sin(m.age * 5f + 1f)));
        }
    }

    private Vector3 TopOf(Tile t)
    {
        if (float.IsNaN(groundTop))
        {
            Tile origin = grid.GetTile(Vector2Int.zero);
            Renderer r = origin != null ? origin.GetComponent<Renderer>() : null;
            groundTop = r != null ? r.bounds.max.y : 0.5f;
        }
        Vector3 p = t.transform.position;
        return new Vector3(p.x, t.HasRoof ? t.RoofY : groundTop, p.z);
    }

    private void CreateObject(Marker m)
    {
        EnsureArt();
        m.go = new GameObject("NoiseMark " + (m.source != null ? m.source.name : ""));
        m.go.transform.SetParent(transform, false);
        m.renderer = m.go.AddComponent<SpriteRenderer>();
        m.renderer.sharedMaterial = material;
        m.renderer.shadowCastingMode = ShadowCastingMode.Off;
        m.renderer.receiveShadows = false;
        m.renderer.color = new Color(1f, 1f, 1f, 0f);
        ApplyKind(m);
    }

    private void ApplyKind(Marker m)
    {
        if (m.renderer != null)
            m.renderer.sprite = m.kind == NoiseKind.Shot ? shotSprite : footstepSprite;
    }

    private void DestroyMarker(Marker m)
    {
        if (m.go == null)
            return;
        if (Application.isPlaying) Destroy(m.go); else DestroyImmediate(m.go);
    }

    private void EnsureArt()
    {
        if (material == null)
        {
            // a UI shader whose depth test can be switched off: the mark is drawn over everything
            material = new Material(Shader.Find("UI/Default"));
            material.hideFlags = HideFlags.HideAndDontSave;
            material.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
            material.renderQueue = 4000;
        }
        if (footstepSprite == null)
            footstepSprite = MakeSprite(false);
        if (shotSprite == null)
            shotSprite = MakeSprite(true);
    }

    private void OnDestroy()
    {
        foreach (Sprite s in new[] { footstepSprite, shotSprite })
        {
            if (s == null)
                continue;
            Object tex = s.texture;
            if (Application.isPlaying) { Destroy(s); Destroy(tex); } else { DestroyImmediate(s); DestroyImmediate(tex); }
        }
        if (material != null)
        {
            if (Application.isPlaying) Destroy(material); else DestroyImmediate(material);
        }
    }

    // Waves going out from a dot: amber for footsteps, red with a star at the centre for a shot. A dark edge keeps it readable on any ground.
    private static Sprite MakeSprite(bool shot)
    {
        const int S = 2;
        Texture2D tex = new Texture2D(SpritePixels, SpritePixels, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.hideFlags = HideFlags.HideAndDontSave;

        Color colour = shot ? new Color(1f, 0.36f, 0.22f) : new Color(1f, 0.76f, 0.22f);
        Color edge = new Color(0f, 0f, 0f, 0.6f);
        float[] radii = { 0.34f, 0.60f, 0.86f };
        float[] alphas = { 1f, 0.8f, 0.6f };
        const float Width = 0.05f;
        Color[] pixels = new Color[SpritePixels * SpritePixels];

        for (int y = 0; y < SpritePixels; y++)
        {
            for (int x = 0; x < SpritePixels; x++)
            {
                float r = 0f, g = 0f, b = 0f, a = 0f;
                for (int sy = 0; sy < S; sy++)
                {
                    for (int sx = 0; sx < S; sx++)
                    {
                        float u = ((x + (sx + 0.5f) / S) / SpritePixels) * 2f - 1f;
                        float v = ((y + (sy + 0.5f) / S) / SpritePixels) * 2f - 1f;
                        float d = Mathf.Sqrt(u * u + v * v);
                        float theta = Mathf.Atan2(v, u);

                        Color c = Color.clear;
                        bool dot = d < 0.11f;
                        bool star = shot && d >= 0.11f && d < 0.27f && Mathf.Abs(Mathf.Sin(4f * theta)) < 0.14f;
                        if (dot || star)
                        {
                            c = colour;
                        }
                        else if ((d < 0.15f) || (shot && d < 0.30f && Mathf.Abs(Mathf.Sin(4f * theta)) < 0.22f && d >= 0.11f))
                        {
                            c = edge;
                        }
                        else
                        {
                            for (int k = 0; k < radii.Length; k++)
                            {
                                float off = Mathf.Abs(d - radii[k]);
                                if (off <= Width)
                                {
                                    c = new Color(colour.r, colour.g, colour.b, alphas[k]);
                                    break;
                                }
                                if (off <= Width + 0.035f)
                                    c = edge;
                            }
                        }

                        r += c.r * c.a; g += c.g * c.a; b += c.b * c.a; a += c.a;
                    }
                }

                float samples = S * S;
                pixels[y * SpritePixels + x] = a > 0f ? new Color(r / a, g / a, b / a, a / samples) : Color.clear;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        Sprite sprite = Sprite.Create(tex, new Rect(0, 0, SpritePixels, SpritePixels), new Vector2(0.5f, 0.5f), SpritePixels);       // 1 x 1 world unit
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }
}
