using UnityEngine;

/// <summary>
/// Keeps track of turns. For now ending a turn simply gives every unit its action points back.
/// EndTurn is public and parameterless so a UI button can call it directly.
/// </summary>
public class TurnManager : MonoBehaviour
{
    /// <summary>
    /// Ends the turn: every unit gets all of its action points back.
    /// Ignored while a unit is still on the move, so a half-finished route is never paid for twice.
    /// </summary>
    public void EndTurn()
    {
        Unit[] units = FindObjectsByType<Unit>(FindObjectsSortMode.None);

        foreach (Unit unit in units)
        {
            if (unit.IsMoving)
                return;
        }

        foreach (Unit unit in units)
            unit.RestoreActionPoints();
    }
}
