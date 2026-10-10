using UnityEngine;

/// <summary>
/// One place that decides what colour a health bar has: red when empty, green when full.
/// </summary>
public static class HealthColors
{
    private static readonly Color Empty = new Color(0.85f, 0.15f, 0.15f, 1f);
    private static readonly Color Full = new Color(0.25f, 0.80f, 0.30f, 1f);

    public static Color FromRatio(float ratio)
    {
        return Color.Lerp(Empty, Full, Mathf.Clamp01(ratio));
    }
}
