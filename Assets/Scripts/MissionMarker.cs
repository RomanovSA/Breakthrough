using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The mark of a mission on the globe, made of what it needs by itself (no prefab): a red diamond with an exclamation mark floating above
/// the place on a thin stalk, and two rings of a signal that spread out over the surface one after the other. It always faces the camera
/// and keeps the same size on the screen however far the camera is. It pops up when it is made, grows a little when the pointer is on it
/// (SetHovered) and has a sphere collider on the diamond, so that a ray from the mouse can hit it.
/// Create it with MissionMarker.Create, as a child of the globe.
/// </summary>
public class MissionMarker : MonoBehaviour
{
    private const int SpritePixels = 128;

    private StrategicMission mission;
    private GlobeCamera globeCamera;
    private Vector3 up;                       // the direction from the centre of the globe to the place
    private float surfaceRadius;              // the radius of the globe in world units
    private float age;
    private bool hovered;
    private float hoverBlend;

    private Transform icon;
    private SpriteRenderer iconRenderer;
    private LineRenderer stalk;
    private SpriteRenderer[] rings;
    private SphereCollider hitSphere;

    private static Sprite diamondSprite;
    private static Sprite ringSprite;
    private static Material spriteMaterial;
    private static Material lineMaterial;

    [SerializeField] private Color color = new Color(1f, 0.28f, 0.2f, 1f);
    [Tooltip("The size of the diamond on the screen, as a fraction of the distance from the camera.")]
    [SerializeField, Min(0.001f)] private float screenSize = 0.045f;

    /// <summary>The mission this mark stands for.</summary>
    public StrategicMission Mission => mission;

    /// <summary>The place on the surface of the globe, in world units.</summary>
    public Vector3 SurfacePoint => transform.position;

    /// <summary>Makes the mark of a mission on the globe (a child of 'globe', whose centre is its origin).</summary>
    public static MissionMarker Create(StrategicMission mission, Transform globe, float globeRadius, GlobeCamera cam)
    {
        GameObject go = new GameObject("Mission " + mission.number + ": " + mission.title);
        go.transform.SetParent(globe, false);
        MissionMarker m = go.AddComponent<MissionMarker>();
        m.Build(mission, globe, globeRadius, cam);
        return m;
    }

    private void Build(StrategicMission m, Transform globe, float globeRadius, GlobeCamera cam)
    {
        mission = m;
        globeCamera = cam;
        surfaceRadius = globeRadius;
        EnsureArt();

        up = (globe != null ? globe.rotation : Quaternion.identity) * Geo.Direction(m.latitude, m.longitude);
        transform.position = (globe != null ? globe.position : Vector3.zero) + up * globeRadius;

        // the diamond
        GameObject i = new GameObject("Icon");
        i.transform.SetParent(transform, false);
        icon = i.transform;
        iconRenderer = i.AddComponent<SpriteRenderer>();
        iconRenderer.sprite = diamondSprite;
        iconRenderer.sharedMaterial = spriteMaterial;
        iconRenderer.color = Color.white;
        iconRenderer.sortingOrder = 5;
        iconRenderer.shadowCastingMode = ShadowCastingMode.Off;
        iconRenderer.receiveShadows = false;
        hitSphere = i.AddComponent<SphereCollider>();
        hitSphere.radius = 0.6f;

        // the stalk from the surface to the diamond
        GameObject s = new GameObject("Stalk");
        s.transform.SetParent(transform, false);
        stalk = s.AddComponent<LineRenderer>();
        stalk.sharedMaterial = lineMaterial;
        stalk.positionCount = 2;
        stalk.useWorldSpace = true;
        stalk.startColor = stalk.endColor = new Color(color.r, color.g, color.b, 0.9f);
        stalk.shadowCastingMode = ShadowCastingMode.Off;
        stalk.receiveShadows = false;
        stalk.numCapVertices = 0;

        // two rings of signal on the surface
        rings = new SpriteRenderer[2];
        for (int r = 0; r < rings.Length; r++)
        {
            GameObject g = new GameObject("Ring " + r);
            g.transform.SetParent(transform, false);
            SpriteRenderer sr = g.AddComponent<SpriteRenderer>();
            sr.sprite = ringSprite;
            sr.sharedMaterial = spriteMaterial;
            sr.shadowCastingMode = ShadowCastingMode.Off;
            sr.receiveShadows = false;
            sr.sortingOrder = 1;
            rings[r] = sr;
        }

        ApplyLook(0f);
    }

    /// <summary>The pointer is on the mark (it grows a little) or is not.</summary>
    public void SetHovered(bool value)
    {
        hovered = value;
    }

    private void LateUpdate()
    {
        age += Time.unscaledDeltaTime;
        hoverBlend = Mathf.MoveTowards(hoverBlend, hovered ? 1f : 0f, 8f * Time.unscaledDeltaTime);
        ApplyLook(age);
    }

    private void ApplyLook(float time)
    {
        if (icon == null)
            return;

        Camera cam = globeCamera != null ? globeCamera.Cam : Camera.main;
        if (cam == null)
            return;

        // the size that looks the same from anywhere
        float distance = Vector3.Distance(cam.transform.position, transform.position);
        float unit = Mathf.Max(0.02f, distance * screenSize);

        // popping up: overshoots a little, then settles
        float appear = Mathf.Clamp01(time / 0.55f);
        float pop = appear < 1f ? 1f + 2.2f * Mathf.Pow(appear - 1f, 3f) + 1.2f * Mathf.Pow(appear - 1f, 2f) : 1f;
        float scale = unit * pop * (1f + 0.3f * hoverBlend) * (1f + 0.04f * Mathf.Sin(time * 4f));

        // the diamond floats above the place
        float height = unit * 1.5f;
        icon.position = transform.position + up * height;
        icon.rotation = cam.transform.rotation;
        icon.localScale = Vector3.one * scale;
        iconRenderer.color = Color.Lerp(Color.white, new Color(1f, 0.95f, 0.7f), hoverBlend);
        hitSphere.radius = 0.62f;

        // the stalk
        float width = unit * 0.05f;
        stalk.startWidth = width;
        stalk.endWidth = width;
        stalk.SetPosition(0, transform.position + up * (unit * 0.02f));
        stalk.SetPosition(1, icon.position - up * (scale * 0.45f));
        stalk.enabled = appear > 0.05f;

        // the rings spread over the surface, flat on it
        Quaternion flat = Quaternion.LookRotation(up, Mathf.Abs(Vector3.Dot(up, Vector3.up)) > 0.95f ? Vector3.right : Vector3.up);
        for (int r = 0; r < rings.Length; r++)
        {
            float phase = Mathf.Repeat(time * 0.55f + r * 0.5f, 1f);
            float size = unit * Mathf.Lerp(0.5f, 5.5f, phase);
            float alpha = Mathf.Sin(phase * Mathf.PI) * Mathf.Lerp(0.95f, 0.5f, phase) * Mathf.Clamp01(time / 0.3f);
            rings[r].transform.position = transform.position + up * (unit * 0.03f);
            rings[r].transform.rotation = flat;
            rings[r].transform.localScale = Vector3.one * size;
            rings[r].color = new Color(color.r, color.g, color.b, alpha);
        }
    }

    // ---- the pictures, drawn by code ----

    private static void EnsureArt()
    {
        if (spriteMaterial == null)
        {
            Shader sh = Shader.Find("Sprites/Default");
            spriteMaterial = new Material(sh) { name = "MissionMarkerSprite", hideFlags = HideFlags.HideAndDontSave };
        }
        if (lineMaterial == null)
        {
            Shader sh = Shader.Find("Sprites/Default");
            lineMaterial = new Material(sh) { name = "MissionMarkerLine", hideFlags = HideFlags.HideAndDontSave };
        }
        if (diamondSprite == null)
            diamondSprite = MakeDiamond();
        if (ringSprite == null)
            ringSprite = MakeRing();
    }

    // A red diamond with a white edge and a white exclamation mark.
    private static Sprite MakeDiamond()
    {
        const int S = 2;
        Texture2D tex = NewTexture();
        Color red = new Color(1f, 0.27f, 0.2f);
        Color dark = new Color(0.25f, 0.02f, 0.02f);
        Color white = Color.white;
        Color[] px = new Color[SpritePixels * SpritePixels];

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
                        float d = Mathf.Abs(u) + Mathf.Abs(v);          // the diamond: d < 1

                        Color c = Color.clear;
                        if (d < 0.94f)
                        {
                            c = d > 0.80f ? white : red;
                            if (d <= 0.80f)
                            {
                                bool bar = Mathf.Abs(u) < 0.07f && v > -0.12f && v < 0.46f;
                                bool dot = (u * u + (v + 0.30f) * (v + 0.30f)) < 0.085f * 0.085f;
                                if (bar || dot)
                                    c = white;
                            }
                        }
                        else if (d < 1f)
                        {
                            c = dark;
                        }

                        r += c.r * c.a; g += c.g * c.a; b += c.b * c.a; a += c.a;
                    }
                }
                float n = S * S;
                px[y * SpritePixels + x] = a > 0f ? new Color(r / a, g / a, b / a, a / n) : Color.clear;
            }
        }

        return Finish(tex, px);
    }

    // A thin soft ring, white (the colour is set on the renderer).
    private static Sprite MakeRing()
    {
        Texture2D tex = NewTexture();
        Color[] px = new Color[SpritePixels * SpritePixels];
        for (int y = 0; y < SpritePixels; y++)
        {
            for (int x = 0; x < SpritePixels; x++)
            {
                float u = ((x + 0.5f) / SpritePixels) * 2f - 1f;
                float v = ((y + 0.5f) / SpritePixels) * 2f - 1f;
                float d = Mathf.Sqrt(u * u + v * v);
                float edge = Mathf.Abs(d - 0.86f);
                float a = Mathf.Clamp01(1f - edge / 0.07f);          // a line with soft sides
                a *= a;
                px[y * SpritePixels + x] = new Color(1f, 1f, 1f, a);
            }
        }
        return Finish(tex, px);
    }

    private static Texture2D NewTexture()
    {
        Texture2D tex = new Texture2D(SpritePixels, SpritePixels, TextureFormat.RGBA32, true);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Trilinear;
        tex.hideFlags = HideFlags.HideAndDontSave;
        return tex;
    }

    private static Sprite Finish(Texture2D tex, Color[] pixels)
    {
        tex.SetPixels(pixels);
        tex.Apply(true);
        Sprite sprite = Sprite.Create(tex, new Rect(0, 0, SpritePixels, SpritePixels), new Vector2(0.5f, 0.5f), SpritePixels);   // 1 x 1 world unit
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }
}
