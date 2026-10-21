using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Works out the chance to hit: how much of the inside of the spread ring is covered by the enemy.
/// The bullets land anywhere inside the ring with the same likelihood, so the share of the ring's area that the enemy covers
/// (as the shooter sees it) is the chance that a bullet hits.
///
/// How: a small hidden camera is put where the game camera is and aimed at the middle of the ring, zoomed so that the ring
/// fills the picture. It draws the scene once into a small texture with the enemy painted in one flat, unlit green
/// (anything standing in front of the enemy - a car, a bin, a palm - hides it, exactly as on the screen; the shooter himself is
/// left out, the bullets do not start in his head). Then the pixels inside the circle of the ring are counted: green ones /
/// all ones. Takes about a millisecond; the picture and the camera are made once and reused.
/// </summary>
public class HitChanceMeter : MonoBehaviour
{
    [Tooltip("Size of the square picture the enemy is counted in (pixels). More = a finer count."), SerializeField, Range(32, 512)]
    private int resolution = 128;

    [Tooltip("The ring fills 1 / this much of the picture's width; a bit of room around it is a safety margin."), SerializeField, Range(1.02f, 2f)]
    private float margin = 1.12f;

    private RenderTexture target;
    private Texture2D readback;
    private Camera measureCamera;
    private Material maskMaterial;
    private Texture2D maskTexture;

    /// <summary>How many pixels were inside the ring in the last measurement (for checking).</summary>
    public int LastRingPixels { get; private set; }

    /// <summary>How many of them were the enemy.</summary>
    public int LastEnemyPixels { get; private set; }

    /// <summary>
    /// The share (0 to 1) of the ring's area covered by the target, seen from viewPoint. ringCenter is the middle of the
    /// ring, ringRadius its radius in world units (the ring is flat and faces viewPoint). alsoHide are renderers that must not
    /// be in the picture (the ring itself and its label).
    /// </summary>
    public float Measure(Unit shooter, Unit enemy, Vector3 viewPoint, Vector3 ringCenter, float ringRadius, params Renderer[] alsoHide)
    {
        if (enemy == null || ringRadius <= 0f)
            return 0f;

        Vector3 toRing = ringCenter - viewPoint;
        float distance = toRing.magnitude;
        if (distance < 0.05f)
            return 0f;

        EnsureObjects();

        // the camera: where the game camera is, looking at the ring, zoomed on it
        Transform t = measureCamera.transform;
        t.SetPositionAndRotation(viewPoint, Quaternion.LookRotation(toRing, Vector3.up));
        float halfAngle = Mathf.Atan(ringRadius * margin / distance) * Mathf.Rad2Deg;
        measureCamera.fieldOfView = Mathf.Clamp(2f * halfAngle, 0.3f, 120f);
        measureCamera.aspect = 1f;
        measureCamera.nearClipPlane = 0.03f;
        measureCamera.farClipPlane = distance + 100f;

        // the enemy flat green, the shooter and the extras out of the picture
        var swapped = new System.Collections.Generic.List<(Renderer renderer, Material[] materials)>();
        foreach (Renderer r in enemy.GetComponentsInChildren<Renderer>())
        {
            if (!IsBody(r))
                continue;

            Material[] original = r.sharedMaterials;
            Material[] mask = new Material[original.Length];
            for (int i = 0; i < mask.Length; i++)
                mask[i] = maskMaterial;
            swapped.Add((r, original));
            r.sharedMaterials = mask;
        }

        var hidden = new System.Collections.Generic.List<Renderer>();
        if (shooter != null)
            foreach (Renderer r in shooter.GetComponentsInChildren<Renderer>())
                if (r != null && r.enabled && IsBody(r)) { r.enabled = false; hidden.Add(r); }
        if (alsoHide != null)
            foreach (Renderer r in alsoHide)
                if (r != null && r.enabled) { r.enabled = false; hidden.Add(r); }

        measureCamera.Render();

        foreach (var s in swapped)
            if (s.renderer != null)
                s.renderer.sharedMaterials = s.materials;
        foreach (Renderer r in hidden)
            if (r != null)
                r.enabled = true;

        // count: the pixels inside the circle of the ring, and the green ones among them
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = target;
        readback.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0, false);
        RenderTexture.active = previous;
        Color32[] pixels = readback.GetPixels32();

        float centre = (resolution - 1) * 0.5f;
        float radius = resolution / (2f * margin);              // the ring's radius in pixels (see the zoom above)
        float r2 = radius * radius;
        int inside = 0, enemyPixels = 0;
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float dx = x - centre, dy = y - centre;
                if (dx * dx + dy * dy > r2)
                    continue;

                inside++;
                Color32 p = pixels[y * resolution + x];
                if (p.g > 170 && p.r < 110 && p.b < 110)
                    enemyPixels++;
            }
        }

        LastRingPixels = inside;
        LastEnemyPixels = enemyPixels;
        return inside > 0 ? (float)enemyPixels / inside : 0f;
    }

    private static bool IsBody(Renderer r)
    {
        // the body and the weapon: not the vision fan on the ground
        return r != null && (r is MeshRenderer || r is SkinnedMeshRenderer) && r.GetComponent<UnitVisionDisplay>() == null;
    }

    private void EnsureObjects()
    {
        if (measureCamera != null && target != null && target.width == resolution)
            return;

        Cleanup();

        target = new RenderTexture(resolution, resolution, 24, RenderTextureFormat.ARGB32);
        target.antiAliasing = 1;
        target.filterMode = FilterMode.Point;
        target.hideFlags = HideFlags.HideAndDontSave;
        readback = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);
        readback.hideFlags = HideFlags.HideAndDontSave;

        maskTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        maskTexture.SetPixels(new[] { Color.green, Color.green, Color.green, Color.green });
        maskTexture.Apply();
        maskTexture.hideFlags = HideFlags.HideAndDontSave;
        // unlit and reading its colour from a texture: nothing about the unit (tint, glow, light) can change it
        maskMaterial = new Material(Shader.Find("Unlit/Texture"));
        maskMaterial.mainTexture = maskTexture;
        maskMaterial.hideFlags = HideFlags.HideAndDontSave;

        GameObject go = new GameObject("HitChanceCamera");
        go.hideFlags = HideFlags.DontSave;
        go.transform.SetParent(transform, false);
        measureCamera = go.AddComponent<Camera>();
        measureCamera.enabled = false;                          // it only draws when asked to
        measureCamera.clearFlags = CameraClearFlags.SolidColor;
        measureCamera.backgroundColor = Color.black;
        measureCamera.cullingMask = ~0;
        measureCamera.allowMSAA = false;
        measureCamera.allowHDR = false;
        measureCamera.targetTexture = target;
        UniversalAdditionalCameraData data = go.AddComponent<UniversalAdditionalCameraData>();
        data.renderType = CameraRenderType.Base;
        data.renderPostProcessing = false;
        data.renderShadows = false;
        data.antialiasing = AntialiasingMode.None;
    }

    private void Cleanup()
    {
        foreach (Object o in new Object[] { measureCamera != null ? measureCamera.gameObject : null, target, readback, maskMaterial, maskTexture })
        {
            if (o == null)
                continue;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }
        measureCamera = null; target = null; readback = null; maskMaterial = null; maskTexture = null;
    }

    private void OnDestroy()
    {
        Cleanup();
    }

    /// <summary>Throws away the camera, the picture and the material (what OnDestroy does, also in edit mode).</summary>
    public void Dispose()
    {
        Cleanup();
    }
}
