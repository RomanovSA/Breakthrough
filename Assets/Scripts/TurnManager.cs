using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Turn flow.
///  - When the selected unit runs out of action points (and has stopped moving), the next unit that can
///    still act is selected automatically.
///  - When none of the player's units can act any more (and nothing is still moving), the turn ends by itself.
///  - Ending a turn gives every unit its action points back and selects the first unit of the player.
/// EndTurn is public and parameterless so the End Turn button can call it directly.
/// </summary>
public class TurnManager : MonoBehaviour
{
    [SerializeField] private UnitSelector selector;

    // Units that were already seen without action points. A unit is handled once, when it becomes exhausted,
    // so a unit the player selects by hand while it is exhausted is not switched away again.
    private readonly HashSet<Unit> exhausted = new HashSet<Unit>();
    private readonly List<Unit> newlyExhausted = new List<Unit>();

    private void Update()
    {
        if (selector == null)
            selector = FindFirstObjectByType<UnitSelector>();
        if (selector == null)
            return;

        exhausted.RemoveWhere(u => u == null);
        newlyExhausted.Clear();

        Faction mine = selector.ControlledFaction;
        bool anyCanAct = false;

        foreach (Unit unit in Unit.All)
        {
            if (unit == null || unit.Faction != mine)
                continue;

            if (unit.ActionPoints > 0)
            {
                anyCanAct = true;
                exhausted.Remove(unit);
            }
            else if (!unit.IsMoving && exhausted.Add(unit))
            {
                newlyExhausted.Add(unit); // out of points and standing still: it has really finished
            }
        }

        if (newlyExhausted.Count == 0)
            return;

        // The selected unit has finished its turn: pass the selection on (nothing happens if nobody can act).
        Unit selected = selector.SelectedUnit;
        if (selected != null && newlyExhausted.Contains(selected))
            selector.SelectNextAvailable();

        // Nobody can act any more and nobody is still running: the turn is over.
        if (!anyCanAct && !Unit.AnyMoving)
            EndTurn();
    }

    /// <summary>
    /// Ends the turn: every unit gets all of its action points back and the first unit of the player is selected.
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

        if (selector == null)
            selector = FindFirstObjectByType<UnitSelector>();
        if (selector != null)
            selector.SelectFirstAvailable();
    }
}
