using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shows the unit's action points and keeps the End Turn button in sync with the game:
/// the button is greyed out while the unit is moving.
/// The button's click is wired to TurnManager.EndTurn in the inspector (On Click).
/// </summary>
public class TurnUI : MonoBehaviour
{
    [SerializeField] private Button endTurnButton;
    [SerializeField] private TextMeshProUGUI actionPointsText;

    private Unit unit;
    private int shownPoints = -1;
    private int shownMax = -1;

    private void Update()
    {
        // The unit is respawned when the field is rebuilt, so look for it again when it is gone.
        if (unit == null)
            unit = FindFirstObjectByType<Unit>();

        if (unit == null)
        {
            if (shownPoints != -1)
            {
                shownPoints = -1;
                shownMax = -1;
                if (actionPointsText != null)
                    actionPointsText.text = string.Empty;
            }
            return;
        }

        if (unit.ActionPoints != shownPoints || unit.MaxActionPoints != shownMax)
        {
            shownPoints = unit.ActionPoints;
            shownMax = unit.MaxActionPoints;
            if (actionPointsText != null)
                actionPointsText.text = "Очки действия: " + shownPoints + "/" + shownMax;
        }

        if (endTurnButton != null)
            endTurnButton.interactable = !unit.IsMoving;
    }
}
