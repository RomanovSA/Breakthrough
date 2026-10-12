using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Keeps the End Turn button in sync with the game: it is greyed out while any unit is moving.
/// The button's click is wired to TurnManager.EndTurn in the inspector (On Click).
/// </summary>
public class TurnUI : MonoBehaviour
{
    [SerializeField] private Button endTurnButton;

    private void Update()
    {
        if (endTurnButton != null)
            endTurnButton.interactable = !Unit.AnyMoving;
    }
}
