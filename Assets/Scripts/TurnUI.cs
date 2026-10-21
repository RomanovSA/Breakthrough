using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Keeps the End Turn button in sync with the game: it is greyed out while any unit is moving, while the shot view is on, and
/// during the enemy's turn, when it says so instead of 'End turn'.
/// The button's click is wired to TurnManager.EndTurn in the inspector (On Click).
/// </summary>
public class TurnUI : MonoBehaviour
{
    [SerializeField] private Button endTurnButton;
    [SerializeField] private string enemyTurnText = "Ход противника...";

    private TMP_Text label;
    private string playerText;

    private void Update()
    {
        if (endTurnButton == null)
            return;

        bool playersTurn = TurnManager.IsPlayersTurnNow;
        endTurnButton.interactable = playersTurn && !Unit.AnyMoving && !ShotCamera.Active;

        if (label == null)
        {
            label = endTurnButton.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
                playerText = label.text;
        }
        if (label != null)
        {
            string wanted = playersTurn ? playerText : enemyTurnText;
            if (label.text != wanted)
                label.text = wanted;
        }
    }
}
