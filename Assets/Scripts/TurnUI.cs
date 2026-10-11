using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shows the action points of the selected unit and keeps the End Turn button in sync with the game:
/// the button is greyed out while any unit is moving.
/// The button's click is wired to TurnManager.EndTurn in the inspector (On Click).
/// </summary>
public class TurnUI : MonoBehaviour
{
    [SerializeField] private UnitSelector selector;
    [SerializeField] private Button endTurnButton;
    [SerializeField] private TextMeshProUGUI actionPointsText;

    private Unit shownUnit;
    private int shownPoints = -2; // -2: nothing shown yet, so the first frame clears the placeholder text
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
            if (shownPoints != -1)
            {
                shownUnit = null;
                shownPoints = -1;
                shownMax = -1;
                if (actionPointsText != null)
                    actionPointsText.text = string.Empty;
            }
        }
        else if (unit != shownUnit || unit.ActionPoints != shownPoints || unit.MaxActionPoints != shownMax)
        {
            shownUnit = unit;
            shownPoints = unit.ActionPoints;
            shownMax = unit.MaxActionPoints;
            if (actionPointsText != null)
                actionPointsText.text = "Очки действия: " + shownPoints + "/" + shownMax;
        }

        if (endTurnButton != null)
            endTurnButton.interactable = !Unit.AnyMoving;
    }
}
