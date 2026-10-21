using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The ring of bullet spread (as in Phoenix Point), shown around the target in the shot view: the bullets of the shooter land
/// somewhere inside it. Its radius is the distance to the target times the tangent of the shooter's Aim Spread (a cone of fire),
/// so it is wider the farther the enemy is and the worse the shooter aims.
/// When the shot view starts the ring is big and closes in on its true size while the shooter takes aim, then it breathes a little.
/// Next to the ring stands the chance to hit (see HitChanceMeter), as a percentage, coloured red / yellow / green.
/// (Only digits and the % sign: letters outside the font's own atlas would be drawn by a second, depth-tested mesh and could hide.)
/// Both are flat pictures that always face the camera and are drawn over everything. ShotCamera creates the ring and removes it again.
/// </summary>
public class SpreadRing : MonoBehaviour
{
    [Tooltip("How many seconds the ring takes to close in from its starting size to the true spread.")]
    [SerializeField, Min(0.1f)] private float settleTime = 1.1f;
    [Tooltip("The ring starts this many times wider than the true spread.")]
    [SerializeField, Min(1f)] private float startSize = 2.4f;
    [Tooltip("The ring is never smaller than this (world units), so a very accurate shooter still has a ring to look at.")]
    [SerializeField, Min(0.02f)] private float minRadius = 0.14f;
    [SerializeField, Range(0f, 0.15f)] private float breathing = 0.03f;

    [Header("Chance to hit")]
    [Tooltip("Below this chance the number is red.")]
    [SerializeField, Range(0f, 1f)] private float lowChance = 0.35f;
    [Tooltip("From this chance the number is green; in between it is yellow.")]
    [SerializeField, Range(0f, 1f)] private float highChance = 0.65f;
    [Tooltip("How big the number looks on the screen (it grows with the distance, so it always looks the same size).")]
    [SerializeField, Min(0.001f)] private float labelSize = 0.03f;

    private const int Pixels = 128;
    private const float RingRadius = 0.93f;      // radius of the drawn ring in the picture (the picture is -1 .. 1)

    private Unit shooter;
    private Unit target;
    private SpriteRenderer spriteRenderer;
    private Sprite sprite;
    private Material material;
    private TextMeshPro label;
    private float finalRadius;
    private float chance;
    private float shownChance;
    private bool hasChance;
    private int shownPercent = -1;
    private int shots = 1;
    private int shownShots = 1;

    /// <summary>
    /// The radius of the ring of spread for a shot of this shooter at this target (distance x tan of the aim spread, never smaller than
    /// minRadius): the same rule the ring in the shot view uses, so the enemies miss as much as the player does.
    /// </summary>
    public static float SpreadRadius(Unit shooter, Unit target, float minRadius = 0.14f)
    {
        Vector3 from = shooter.transform.position + Vector3.up * 0.9f;
        Vector3 to = target.transform.position + Vector3.up * (0.55f - target.StanceChestDrop);
        return Mathf.Max(minRadius, Vector3.Distance(from, to) * Mathf.Tan(Mathf.Deg2Rad * shooter.AimSpread));
    }

    /// <summary>The radius the ring settles at, in world units (distance to the target x tan of the spread).</summary>
    public float FinalRadius => finalRadius;

    /// <summary>The radius the ring has right now, in world units (with the little breathing).</summary>
    public float CurrentRadius { get; private set; }

    /// <summary>The radius the ring has right now without the breathing: the one the chance is worked out for.</summary>
    public float BaseRadius { get; private set; }

    /// <summary>The middle of the ring: the chest of the target.</summary>
    public Vector3 Center { get; private set; }

    /// <summary>The percentage on the screen (0 to 100), -1 until there is one.</summary>
    public int ShownPercent => shownPercent;

    /// <summary>The picture of the ring (the chance meter leaves it out of its picture).</summary>
    public Renderer RingRenderer => spriteRenderer;

    /// <summary>The number next to the ring (the chance meter leaves it out too).</summary>
    public Renderer LabelRenderer => label != null ? label.GetComponent<Renderer>() : null;

    /// <summary>The number next to the ring, for checking.</summary>
    public TextMeshPro Label => label;

    /// <summary>Starts showing the ring for this shot. Nothing is visible until Tick is called.</summary>
    public void Show(Unit shootingUnit, Unit targetUnit)
    {
        shooter = shootingUnit;
        target = targetUnit;
        EnsureRenderer();

        // the cone of fire starts about at the shooter's chest and ends at the chest of the target
        Vector3 from = shooter.transform.position + Vector3.up * 0.9f;
        Vector3 to = target.transform.position + Vector3.up * (0.55f - target.StanceChestDrop);
        float distance = Vector3.Distance(from, to);
        finalRadius = Mathf.Max(minRadius, distance * Mathf.Tan(Mathf.Deg2Rad * shooter.AimSpread));

        Center = to;
        BaseRadius = finalRadius * startSize;
        CurrentRadius = BaseRadius;
        hasChance = false;
        shownPercent = -1;

        spriteRenderer.enabled = true;
        spriteRenderer.color = new Color(1f, 1f, 1f, 0f);
        label.gameObject.SetActive(true);
        label.color = new Color(1f, 1f, 1f, 0f);
    }

    /// <summary>How many bullets one shot sends: more than one is written after the chance (x3), which stays the chance of one bullet.</summary>
    public void SetShots(int bullets)
    {
        shots = Mathf.Max(1, bullets);
    }

    /// <summary>The chance to hit (0 to 1) that goes next to the ring; the number on the screen follows it smoothly.</summary>
    public void SetChance(float value)
    {
        chance = Mathf.Clamp01(value);
        if (!hasChance)
            shownChance = chance;
        hasChance = true;
    }

    /// <summary>
    /// Places and sizes the ring. visibility 0..1 fades it in and out; settledFor is how long the camera has been
    /// in place (the ring closes in over that time).
    /// </summary>
    public void Tick(float visibility, float settledFor, Camera cam)
    {
        if (spriteRenderer == null || target == null || cam == null)
            return;

        float k = Mathf.Clamp01(settledFor / settleTime);
        float eased = 1f - (1f - k) * (1f - k) * (1f - k);                           // quick at first, then slowly
        float wave = k >= 1f ? 1f + breathing * Mathf.Sin(Time.unscaledTime * 3.2f) : 1f;
        BaseRadius = finalRadius * Mathf.Lerp(startSize, 1f, eased);
        CurrentRadius = BaseRadius * wave;

        Center = target.transform.position + Vector3.up * (0.55f - target.StanceChestDrop);        // lower on a crouching target
        transform.SetPositionAndRotation(Center, cam.transform.rotation);            // flat on the screen, facing the camera
        transform.localScale = Vector3.one * (CurrentRadius / (RingRadius * 0.5f));

        float alpha = Mathf.Clamp01(visibility);
        spriteRenderer.color = new Color(1f, 1f, 1f, alpha);
        UpdateLabel(cam, alpha);
    }

    /// <summary>
    /// A random point inside the ring (every point is equally likely): where a bullet fired now will aim. The ring is flat
    /// and faces the camera, so the point is on that plane, around the chest of the target.
    /// </summary>
    public Vector3 RandomPointInRing()
    {
        float radius = BaseRadius * Mathf.Sqrt(Random.value);
        float angle = Random.value * Mathf.PI * 2f;
        return Center + transform.right * (radius * Mathf.Cos(angle)) + transform.up * (radius * Mathf.Sin(angle));
    }

    public void Hide()
    {
        if (spriteRenderer != null)
            spriteRenderer.enabled = false;
        if (label != null)
            label.gameObject.SetActive(false);
    }

    // The percentage, to the right of the ring, always the same size on the screen.
    private void UpdateLabel(Camera cam, float alpha)
    {
        if (label == null)
            return;

        if (hasChance)
            shownChance = Mathf.Lerp(shownChance, chance, 1f - Mathf.Exp(-10f * Time.unscaledDeltaTime));

        int percent = Mathf.RoundToInt(shownChance * 100f);
        Color colour = shownChance < lowChance ? new Color(1f, 0.32f, 0.26f) : shownChance < highChance ? new Color(1f, 0.82f, 0.25f) : new Color(0.45f, 0.95f, 0.4f);

        if (hasChance && (percent != shownPercent || shots != shownShots))
        {
            shownPercent = percent;
            shownShots = shots;
            label.text = "<b>" + percent + "%</b>" + (shots > 1 ? "<size=55%> x" + shots + "</size>" : "");
        }
        else if (!hasChance)
        {
            label.text = "";
        }

        float distance = Vector3.Distance(cam.transform.position, Center);
        float size = Mathf.Max(0.0001f, distance * labelSize);
        Vector3 position = Center + cam.transform.right * (CurrentRadius * 1.25f + size * 1.2f);

        label.transform.SetPositionAndRotation(position, cam.transform.rotation);
        label.transform.localScale = Vector3.one * size;
        label.color = new Color(colour.r, colour.g, colour.b, alpha * (hasChance ? 1f : 0f));
    }

    private void OnDestroy()
    {
        Dispose();
    }

    /// <summary>Throws away the picture, the material and the number (the number lives next to the ring, not under it).</summary>
    public void Dispose()
    {
        GameObject labelObject = label != null ? label.gameObject : null;
        Material labelMaterial = label != null ? label.fontMaterial : null;
        Object[] things = { sprite != null ? sprite.texture : null, sprite, material, labelMaterial, labelObject };

        label = null;
        sprite = null;
        material = null;
        spriteRenderer = null;

        foreach (Object o in things)
        {
            if (o == null)
                continue;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }
    }

    private void EnsureRenderer()
    {
        if (spriteRenderer != null)
            return;

        sprite = MakeSprite();
        // a UI shader whose depth test can be switched off: the ring is drawn over everything
        material = new Material(Shader.Find("UI/Default"));
        material.hideFlags = HideFlags.HideAndDontSave;
        material.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
        material.renderQueue = 4000;

        spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
        spriteRenderer.sprite = sprite;
        spriteRenderer.sharedMaterial = material;
        spriteRenderer.shadowCastingMode = ShadowCastingMode.Off;
        spriteRenderer.receiveShadows = false;

        // the number: a TextMeshPro text in the world, with the 'overlay' version of its shader (drawn over everything)
        GameObject textObject = new GameObject("ChanceLabel");
        textObject.hideFlags = HideFlags.DontSave;
        textObject.transform.SetParent(transform.parent != null ? transform.parent : null, false);
        label = textObject.AddComponent<TextMeshPro>();
        label.fontSize = 10f;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.richText = true;
        label.fontStyle = FontStyles.Bold;
        label.rectTransform.pivot = new Vector2(0f, 0.5f);
        label.rectTransform.sizeDelta = new Vector2(12f, 4f);
        label.outlineWidth = 0.22f;
        label.outlineColor = new Color32(0, 0, 0, 230);
        Shader overlay = Shader.Find("TextMeshPro/Distance Field Overlay");
        if (overlay != null)
        {
            Material m = label.fontMaterial;              // an own copy of the font's material
            m.shader = overlay;
            m.renderQueue = 4001;
        }
        label.text = "";
        textObject.SetActive(false);
    }

    // The picture: a thin bright ring with a dark edge, four ticks pointing inwards, a dot in the middle.
    private static Sprite MakeSprite()
    {
        const int S = 3;
        Texture2D tex = new Texture2D(Pixels, Pixels, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.hideFlags = HideFlags.HideAndDontSave;

        Color ring = new Color(1f, 0.93f, 0.78f, 0.95f);
        Color centre = new Color(1f, 0.25f, 0.18f, 1f);
        Color edge = new Color(0f, 0f, 0f, 0.55f);
        Color[] pixels = new Color[Pixels * Pixels];

        for (int y = 0; y < Pixels; y++)
        {
            for (int x = 0; x < Pixels; x++)
            {
                float r = 0f, g = 0f, b = 0f, a = 0f;
                for (int sy = 0; sy < S; sy++)
                {
                    for (int sx = 0; sx < S; sx++)
                    {
                        float u = ((x + (sx + 0.5f) / S) / Pixels) * 2f - 1f;
                        float v = ((y + (sy + 0.5f) / S) / Pixels) * 2f - 1f;
                        float d = Mathf.Sqrt(u * u + v * v);

                        // a bright line with a thin dark edge, so it can be seen against light and dark backgrounds alike
                        bool onRing = Mathf.Abs(d - RingRadius) <= 0.04f;
                        bool onTick = d < RingRadius - 0.04f && d > RingRadius - 0.20f && (Mathf.Abs(u) < 0.032f || Mathf.Abs(v) < 0.032f);
                        bool nearRing = Mathf.Abs(d - RingRadius) <= 0.06f;
                        bool nearTick = d < RingRadius - 0.03f && d > RingRadius - 0.22f && (Mathf.Abs(u) < 0.05f || Mathf.Abs(v) < 0.05f);

                        Color c = Color.clear;
                        if (d < 0.06f) c = centre;                                         // the dot
                        else if (onRing || onTick) c = ring;                               // the ring and the four ticks
                        else if (nearRing || nearTick) c = edge;                           // their dark edge

                        r += c.r * c.a; g += c.g * c.a; b += c.b * c.a; a += c.a;
                    }
                }

                float samples = S * S;
                pixels[y * Pixels + x] = a > 0f ? new Color(r / a, g / a, b / a, a / samples) : Color.clear;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        Sprite result = Sprite.Create(tex, new Rect(0, 0, Pixels, Pixels), new Vector2(0.5f, 0.5f), Pixels);      // 1 x 1 world units
        result.hideFlags = HideFlags.HideAndDontSave;
        return result;
    }
}
