using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The row of icons at the bottom of the screen (XCOM style) that shows which enemies the player's units can see.
///
/// Every frame it works out what each unit of the viewing faction (the player) sees: the same field of view that is
/// drawn as the vision fan and that darkens the map (position, facing, view angle, range, walls and buildings in the way).
/// For every enemy that is inside it an icon appears: a red frame with the silhouette of a soldier and a small health bar
/// underneath. The icon pops in when the enemy comes into view and fades out when it leaves the view or dies.
///
/// The icons can be clicked: with the selected unit able to see that enemy, a click goes to the shot view at it, or, when the shot view is
/// on, switches the shot to it. The icon of the enemy that is shot at gets a yellow glow.
///
/// This object is the row: put it on a UI object anchored where the row should be, with a Horizontal Layout Group
/// (the icons are created under it as needed). Nothing is drawn while no enemy is in view.
/// </summary>
public class VisibleEnemiesUI : MonoBehaviour
{
    [SerializeField] private GridManager grid;
    [Tooltip("Whose eyes count: the units of this faction see.")]
    [SerializeField] private Faction viewerFaction = Faction.Player;
    [Tooltip("Whose units get an icon when they are seen.")]
    [SerializeField] private Faction watchedFaction = Faction.Enemy;

    [Header("Icons")]
    [SerializeField, Min(16f)] private float iconSize = 76f;
    [SerializeField] private bool showHealth = true;
    [Tooltip("How fast an icon fades in and out (1 = a second, 8 = an eighth of a second).")]
    [SerializeField, Min(0.5f)] private float fadeSpeed = 8f;
    [SerializeField] private Color frameColor = new Color(0.95f, 0.2f, 0.14f, 1f);

    private class Entry
    {
        public Unit enemy;
        public RectTransform root;
        public CanvasGroup group;
        public Image healthFill;
        public Outline glow;
        public Button button;
        public float alpha;
        public bool inView;
    }

    private readonly Dictionary<Unit, Entry> entries = new Dictionary<Unit, Entry>();
    private readonly List<Unit> toRemove = new List<Unit>();
    private readonly HashSet<Vector2Int> viewTiles = new HashSet<Vector2Int>();
    private readonly HashSet<Unit> seen = new HashSet<Unit>();
    private readonly HashSet<Unit> seenBySelected = new HashSet<Unit>();
    private UnitSelector selector;
    private ShotCamera shotCamera;
    private Sprite iconSprite;
    private Sprite barSprite;

    /// <summary>How many enemies are in view right now (for checking).</summary>
    public int VisibleCount => seen.Count;

    /// <summary>How many icons are on screen right now, including the ones still fading out (for checking).</summary>
    public int IconCount => entries.Count;

    private void OnEnable()
    {
        if (grid == null)
            grid = FindFirstObjectByType<GridManager>();
        if (grid != null)
            grid.GridBuilt += ResetIcons;
    }

    private void OnDisable()
    {
        if (grid != null)
            grid.GridBuilt -= ResetIcons;
        DestroyEverything();
    }

    private void Update()
    {
        Refresh(Time.unscaledDeltaTime);
    }

    /// <summary>Works out who is seen and updates the icons (done every frame by itself).</summary>
    public void Refresh(float deltaTime)
    {
        if (grid == null)
            grid = FindFirstObjectByType<GridManager>();
        if (grid == null)
            return;

        if (selector == null)
            selector = FindFirstObjectByType<UnitSelector>();
        if (shotCamera == null)
            shotCamera = FindFirstObjectByType<ShotCamera>();
        Unit selected = selector != null ? selector.SelectedUnit : null;

        // who is in view of any unit of the viewing faction?
        seen.Clear();
        seenBySelected.Clear();
        IReadOnlyList<Unit> units = Unit.All;
        for (int i = 0; i < units.Count; i++)
        {
            Unit viewer = units[i];
            if (viewer == null || viewer.Faction != viewerFaction || !viewer.IsAlive)
                continue;

            grid.ComputeVisibleTiles(viewer.transform.position, viewer.FacingDirection, viewer.ViewAngle, viewer.ViewRange, viewTiles);
            for (int j = 0; j < units.Count; j++)
            {
                Unit other = units[j];
                if (other != null && other.Faction == watchedFaction && other.IsAlive && viewTiles.Contains(other.Coords))
                {
                    seen.Add(other);
                    if (viewer == selected)
                        seenBySelected.Add(other);          // these can be clicked to shoot at
                }
            }
        }

        foreach (Unit enemy in seen)
        {
            if (!entries.ContainsKey(enemy))
                entries[enemy] = CreateIcon(enemy);
        }

        // fade in / out
        toRemove.Clear();
        foreach (KeyValuePair<Unit, Entry> pair in entries)
        {
            Entry e = pair.Value;
            e.inView = pair.Key != null && seen.Contains(pair.Key);
            e.alpha = Mathf.MoveTowards(e.alpha, e.inView ? 1f : 0f, fadeSpeed * Mathf.Max(0f, deltaTime));

            if (e.root == null || (!e.inView && e.alpha <= 0f))
            {
                toRemove.Add(pair.Key);
                continue;
            }

            e.group.alpha = e.alpha;
            e.group.blocksRaycasts = e.inView && e.alpha > 0.6f;

            // clickable when the shooter could really go and shoot at it; the one that is shot at glows yellow
            bool inShot = shotCamera != null && shotCamera.IsOn;
            bool targeted = inShot && shotCamera.Target == pair.Key;
            bool usable = inShot
                ? shotCamera.IsTarget(pair.Key)
                : selected != null && selected.CanAffordShot && !Unit.AnyMoving && seenBySelected.Contains(pair.Key);
            if (e.button != null)
                e.button.interactable = usable;
            if (e.glow != null)
            {
                e.glow.effectColor = targeted ? new Color(1f, 0.85f, 0.2f, 1f) : new Color(frameColor.r, frameColor.g, frameColor.b, 0.55f);
                e.glow.effectDistance = targeted ? new Vector2(4f, -4f) : new Vector2(2f, -2f);
            }
            e.root.localScale = Vector3.one * Mathf.Lerp(1.35f, 1f, e.alpha);       // pops in from a bit bigger

            if (e.healthFill != null && pair.Key != null)
            {
                float ratio = pair.Key.MaxHealth > 0 ? (float)pair.Key.Health / pair.Key.MaxHealth : 0f;
                e.healthFill.fillAmount = Mathf.Clamp01(ratio);
                e.healthFill.color = HealthColors.FromRatio(ratio);
            }
        }

        foreach (Unit key in toRemove)
        {
            Entry e = entries[key];
            if (e.root != null)
                DestroyIt(e.root.gameObject);
            entries.Remove(key);
        }
    }

    // ---- building an icon ----

    private Entry CreateIcon(Unit enemy)
    {
        EnsureSprites();

        GameObject root = new GameObject("EnemyIcon_" + enemy.name, typeof(RectTransform));
        root.hideFlags = HideFlags.DontSave;
        root.transform.SetParent(transform, false);

        float height = iconSize + (showHealth ? 10f : 0f);
        RectTransform rt = (RectTransform)root.transform;
        rt.sizeDelta = new Vector2(iconSize, height);
        LayoutElement layout = root.AddComponent<LayoutElement>();
        layout.preferredWidth = iconSize;
        layout.preferredHeight = height;
        CanvasGroup group = root.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;      // switched on by Refresh once the icon is there
        group.interactable = true;

        // the picture: frame + soldier silhouette (drawn in code)
        Image picture = NewImage(root.transform, "Picture", iconSprite);
        picture.color = Color.white;
        picture.raycastTarget = true;
        RectTransform pr = picture.rectTransform;
        pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 1f);
        pr.pivot = new Vector2(0.5f, 1f);
        pr.sizeDelta = new Vector2(iconSize, iconSize);
        pr.anchoredPosition = Vector2.zero;
        Outline glow = picture.gameObject.AddComponent<Outline>();
        glow.effectColor = new Color(frameColor.r, frameColor.g, frameColor.b, 0.55f);
        glow.effectDistance = new Vector2(2f, -2f);

        Image fill = null;
        if (showHealth)
        {
            Image back = NewImage(root.transform, "HealthBack", barSprite);
            back.color = new Color(0f, 0f, 0f, 0.75f);
            RectTransform br = back.rectTransform;
            br.anchorMin = br.anchorMax = new Vector2(0.5f, 0f);
            br.pivot = new Vector2(0.5f, 0f);
            br.sizeDelta = new Vector2(iconSize, 7f);
            br.anchoredPosition = Vector2.zero;

            fill = NewImage(back.transform, "HealthFill", barSprite);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = 0;
            RectTransform fr = fill.rectTransform;
            fr.anchorMin = Vector2.zero;
            fr.anchorMax = Vector2.one;
            fr.offsetMin = new Vector2(1f, 1f);
            fr.offsetMax = new Vector2(-1f, -1f);
        }

        // the icon is a button
        Button button = root.AddComponent<Button>();
        button.targetGraphic = picture;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        ColorBlock colours = button.colors;
        colours.normalColor = Color.white;
        colours.highlightedColor = new Color(1f, 0.86f, 0.7f, 1f);
        colours.pressedColor = new Color(0.8f, 0.62f, 0.52f, 1f);
        colours.selectedColor = Color.white;
        colours.disabledColor = Color.white;       // not clickable, but looks the same
        colours.fadeDuration = 0.08f;
        button.colors = colours;
        Unit clicked = enemy;
        button.onClick.AddListener(() => OnIconClicked(clicked));

        return new Entry { enemy = enemy, root = rt, group = group, healthFill = fill, glow = glow, button = button, alpha = 0f };
    }

    private void OnIconClicked(Unit enemy)
    {
        if (enemy == null || !enemy.IsAlive)
            return;
        if (shotCamera == null)
            shotCamera = FindFirstObjectByType<ShotCamera>();
        if (selector == null)
            selector = FindFirstObjectByType<UnitSelector>();
        if (shotCamera == null)
            return;

        if (shotCamera.IsOn)
            shotCamera.SwitchTarget(enemy);
        else if (selector != null)
            shotCamera.TryBegin(selector.SelectedUnit, enemy);
    }

    private static Image NewImage(Transform parent, string name, Sprite sprite)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.hideFlags = HideFlags.DontSave;
        go.transform.SetParent(parent, false);
        Image image = go.GetComponent<Image>();
        image.sprite = sprite;
        image.raycastTarget = false;
        return image;
    }

    private void EnsureSprites()
    {
        if (iconSprite == null)
            iconSprite = MakeIcon(frameColor);

        if (barSprite == null)
        {
            Texture2D white = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            white.hideFlags = HideFlags.HideAndDontSave;
            Color[] px = new Color[16];
            for (int i = 0; i < px.Length; i++) px[i] = Color.white;
            white.SetPixels(px);
            white.Apply();
            barSprite = Sprite.Create(white, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f);
            barSprite.hideFlags = HideFlags.HideAndDontSave;
        }
    }

    // A rounded dark square with a red frame and the silhouette of a soldier in a helmet, drawn pixel by pixel.
    private static Sprite MakeIcon(Color frame)
    {
        const int N = 96;
        const int S = 3;
        Texture2D tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.hideFlags = HideFlags.HideAndDontSave;

        Color back = new Color(0.10f, 0.04f, 0.04f, 0.88f);
        Color figure = new Color(0.95f, 0.30f, 0.22f, 1f);
        Color highlight = new Color(1f, 0.62f, 0.5f, 1f);
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
                        float u = ((x + (sx + 0.5f) / S) / N) * 2f - 1f;      // -1 .. 1
                        float v = ((y + (sy + 0.5f) / S) / N) * 2f - 1f;
                        if (!InsideRoundedSquare(u, v, 0f))
                            continue;

                        Color c;
                        if (!InsideRoundedSquare(u, v, 0.10f)) c = frame;                       // the frame
                        else if (InsideHelmet(u, v)) c = (v > 0.38f && u < -0.05f) ? highlight : figure;
                        else if (InsideHead(u, v)) c = figure;
                        else if (InsideShoulders(u, v)) c = figure;
                        else c = back;

                        r += c.r * c.a; g += c.g * c.a; b += c.b * c.a; a += c.a;
                    }
                }

                float samples = S * S;
                pixels[y * N + x] = a > 0f ? new Color(r / a, g / a, b / a, a / samples) : Color.clear;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        Sprite sprite = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }

    private static bool InsideRoundedSquare(float u, float v, float shrink)
    {
        float half = 1f - shrink;
        float corner = 0.28f;
        float cx = Mathf.Max(0f, Mathf.Abs(u) - (half - corner));
        float cy = Mathf.Max(0f, Mathf.Abs(v) - (half - corner));
        return Mathf.Abs(u) <= half && Mathf.Abs(v) <= half && cx * cx + cy * cy <= corner * corner;
    }

    private static bool InsideHelmet(float u, float v)
    {
        // a dome over the head
        float du = u / 0.40f, dv = (v - 0.28f) / 0.34f;
        return v > 0.22f && du * du + dv * dv <= 1f;
    }

    private static bool InsideHead(float u, float v)
    {
        float du = u / 0.30f, dv = (v - 0.18f) / 0.34f;
        return v <= 0.28f && du * du + dv * dv <= 1f;
    }

    private static bool InsideShoulders(float u, float v)
    {
        float du = u / 0.62f, dv = (v + 0.78f) / 0.50f;
        return v < -0.18f && du * du + dv * dv <= 1f;
    }

    // ---- housekeeping ----

    private void ResetIcons()
    {
        // the field was rebuilt: the units are new, so are the icons
        foreach (Entry e in entries.Values)
            if (e.root != null)
                DestroyIt(e.root.gameObject);
        entries.Clear();
        seen.Clear();
    }

    private void DestroyEverything()
    {
        ResetIcons();
        foreach (Object o in new Object[] { iconSprite != null ? iconSprite.texture : null, iconSprite, barSprite != null ? barSprite.texture : null, barSprite })
            if (o != null)
                DestroyIt(o);
        iconSprite = null;
        barSprite = null;
    }

    private static void DestroyIt(Object o)
    {
        if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
    }
}
