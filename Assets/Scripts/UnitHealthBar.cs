using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The health bar that floats above a unit. It lives under the unit (world-space canvas),
/// always faces the camera and follows the unit's health.
/// </summary>
public class UnitHealthBar : MonoBehaviour
{
    [SerializeField] private RectTransform fill;
    [SerializeField] private Image fillImage;

    private Unit unit;
    private Camera cam;

    private void OnEnable()
    {
        if (unit == null)
            unit = GetComponentInParent<Unit>();

        if (unit != null)
        {
            unit.HealthChanged += OnHealthChanged;
            Refresh();
        }
    }

    private void OnDisable()
    {
        if (unit != null)
            unit.HealthChanged -= OnHealthChanged;
    }

    // The camera can be turned with Q / E, so keep the bar parallel to the screen every frame.
    private void LateUpdate()
    {
        if (cam == null)
            cam = Camera.main;
        if (cam != null)
            transform.rotation = cam.transform.rotation;
    }

    private void OnHealthChanged(Unit changed)
    {
        Refresh();
    }

    private void Refresh()
    {
        if (unit == null || fill == null)
            return;

        float ratio = unit.MaxHealth > 0 ? (float)unit.Health / unit.MaxHealth : 0f;
        ratio = Mathf.Clamp01(ratio);

        fill.anchorMax = new Vector2(ratio, 1f);
        if (fillImage != null)
            fillImage.color = HealthColors.FromRatio(ratio);
    }
}
