using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The health panel in the bottom-left corner: shown while a unit is selected,
/// with a bar and the exact numbers (current / maximum).
/// This component sits on an object that is always active (the Canvas);
/// only the panel it controls is switched on and off.
/// </summary>
public class SelectedUnitHealthUI : MonoBehaviour
{
    [SerializeField] private UnitSelector selector;
    [SerializeField] private GameObject panel;
    [SerializeField] private RectTransform fill;
    [SerializeField] private Image fillImage;
    [SerializeField] private TextMeshProUGUI valueText;

    private Unit shownUnit;
    private int shownHealth = -1;
    private int shownMax = -1;

    private void Update()
    {
        if (selector == null)
            selector = FindFirstObjectByType<UnitSelector>();

        Unit unit = selector != null ? selector.SelectedUnit : null;
        if (unit != null && !unit.IsSelected)
            unit = null;

        if (unit == null)
        {
            if (panel != null && panel.activeSelf)
                panel.SetActive(false);
            shownUnit = null;
            return;
        }

        if (panel != null && !panel.activeSelf)
            panel.SetActive(true);

        if (unit == shownUnit && unit.Health == shownHealth && unit.MaxHealth == shownMax)
            return;

        shownUnit = unit;
        shownHealth = unit.Health;
        shownMax = unit.MaxHealth;
        Refresh();
    }

    private void Refresh()
    {
        float ratio = shownMax > 0 ? Mathf.Clamp01((float)shownHealth / shownMax) : 0f;

        if (fill != null)
            fill.anchorMax = new Vector2(ratio, 1f);
        if (fillImage != null)
            fillImage.color = HealthColors.FromRatio(ratio);
        if (valueText != null)
            valueText.text = shownHealth + " / " + shownMax;
    }
}
