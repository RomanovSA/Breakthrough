using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Makes a unit glow: its materials are swapped for copies with emission switched on, in the colour of the material mixed with
/// a warm red, pulsing gently. Used to make the target stand out in the shot view. Show() starts it, Hide() puts the
/// original materials back (and the glow goes away); ShotCamera adds and removes this component.
/// The unit's own colour changes (selected, in the dark) keep working: they use property blocks, the glow uses the materials.
/// </summary>
public class UnitHighlight : MonoBehaviour
{
    [SerializeField] private Color glowColor = new Color(1f, 0.30f, 0.18f, 1f);
    [Tooltip("How much of the glow colour is mixed into the colour of each material.")]
    [SerializeField, Range(0f, 1f)] private float tint = 0.35f;
    [Tooltip("Brightness of the glow at its dimmest and at its brightest (above 1 is brighter than the colour itself).")]
    [SerializeField, Min(0f)] private float minBrightness = 0.55f;
    [SerializeField, Min(0f)] private float maxBrightness = 1.0f;
    [SerializeField, Min(0.1f)] private float pulseSpeed = 4f;

    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    private class Slot
    {
        public Renderer renderer;
        public Material[] originals;
        public Material[] glowing;
        public Color[] colours;
    }

    private readonly List<Slot> slots = new List<Slot>();

    /// <summary>True while the unit's materials are the glowing copies.</summary>
    public bool IsOn { get; private set; }

    /// <summary>How many glowing material copies are in use (for checking).</summary>
    public int GlowingMaterials
    {
        get
        {
            int n = 0;
            foreach (Slot s in slots)
                for (int i = 0; i < s.glowing.Length; i++)
                    if (s.glowing[i] != null && s.glowing[i] != s.originals[i]) n++;
            return n;
        }
    }

    public void Show()
    {
        if (IsOn)
            return;

        slots.Clear();
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            // the body and the weapon, not the vision fan on the ground
            if (r == null || !(r is MeshRenderer || r is SkinnedMeshRenderer) || r.GetComponent<UnitVisionDisplay>() != null)
                continue;

            Material[] originals = r.sharedMaterials;
            Material[] glowing = new Material[originals.Length];
            Color[] colours = new Color[originals.Length];
            for (int i = 0; i < originals.Length; i++)
            {
                Material o = originals[i];
                if (o == null || !o.HasProperty(BaseColorId))
                {
                    glowing[i] = o;
                    continue;
                }

                Material copy = new Material(o);
                copy.name = o.name + " (glow)";
                copy.hideFlags = HideFlags.HideAndDontSave;
                copy.EnableKeyword("_EMISSION");
                copy.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                glowing[i] = copy;
                colours[i] = o.GetColor(BaseColorId);
            }

            r.sharedMaterials = glowing;
            slots.Add(new Slot { renderer = r, originals = originals, glowing = glowing, colours = colours });
        }

        IsOn = true;
        Pulse(Time.unscaledTime, 1f);
    }

    /// <summary>Brings the original materials back and throws the copies away.</summary>
    public void Hide()
    {
        foreach (Slot s in slots)
        {
            if (s.renderer != null)
                s.renderer.sharedMaterials = s.originals;

            for (int i = 0; i < s.glowing.Length; i++)
            {
                Material m = s.glowing[i];
                if (m != null && m != s.originals[i])
                {
                    if (Application.isPlaying) Destroy(m); else DestroyImmediate(m);
                }
            }
        }

        slots.Clear();
        IsOn = false;
    }

    /// <summary>Sets the glow for this moment: time drives the pulse, strength 0..1 fades the whole glow in or out.</summary>
    public void Pulse(float time, float strength)
    {
        if (!IsOn)
            return;

        float wave = 0.5f + 0.5f * Mathf.Sin(time * pulseSpeed);
        float brightness = Mathf.Lerp(minBrightness, maxBrightness, wave) * Mathf.Clamp01(strength);

        foreach (Slot s in slots)
        {
            for (int i = 0; i < s.glowing.Length; i++)
            {
                Material m = s.glowing[i];
                if (m == null || m == s.originals[i])
                    continue;

                Color c = Color.Lerp(s.colours[i], glowColor, tint) * brightness;
                c.a = 1f;
                m.SetColor(EmissionColorId, c);
            }
        }
    }

    private void OnDestroy()
    {
        if (IsOn)
            Hide();
    }
}
