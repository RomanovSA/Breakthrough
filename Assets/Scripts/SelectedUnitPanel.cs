using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The panel in the bottom-left corner: shown while a unit is selected.
/// It shows the unit's action points and, right below them, its health bar with the exact numbers.
/// This component sits on an object that is always active (the Canvas);
/// only the panel it controls is switched on and off.
/// </summary>
public class SelectedUnitPanel : MonoBehaviour
{
    [SerializeField] private UnitSelector selector;
    [SerializeField] private GameObject panel;
    [SerializeField] private TextMeshProUGUI actionPointsText;
    [SerializeField] private RectTransform fill;
    [SerializeField] private Image fillImage;
    [SerializeField] private TextMeshProUGUI valueText;

    private Unit shownUnit;
    private int shownHealth = -1;
    private int shownMaxHealth = -1;
    private int shownPoints = -1;
    private int shownMaxPoints = -1;

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

        if (unit == shownUnit
            && unit.Health == shownHealth && unit.MaxHealth == shownMaxHealth
            && unit.ActionPoints == shownPoints && unit.MaxActionPoints == shownMaxPoints)
            return;

        shownUnit = unit;
        shownHealth = unit.Health;
        shownMaxHealth = unit.MaxHealth;
        shownPoints = unit.ActionPoints;
        shownMaxPoints = unit.MaxActionPoints;
        Refresh();
    }

    private void Refresh()
    {
        float ratio = shownMaxHealth > 0 ? Mathf.Clamp01((float)shownHealth / shownMaxHealth) : 0f;

        if (fill != null)
            fill.anchorMax = new Vector2(ratio, 1f);
        if (fillImage != null)
            fillImage.color = HealthColors.FromRatio(ratio);
        if (valueText != null)
            valueText.text = shownHealth + " / " + shownMaxHealth;
        if (actionPointsText != null)
            actionPointsText.text = "Очки действия: " + shownPoints + "/" + shownMaxPoints;
    }
}
