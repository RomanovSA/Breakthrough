using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Turn flow. A turn of the player, then a turn of the enemy (EnemyAI), then the player again.
///
/// The player's turn:
///  - When the selected unit runs out of action points (and has stopped moving), the next unit that can
///    still act is selected automatically.
///  - When none of the player's units can act any more (and nothing is still moving), the turn ends by itself.
///  - EndTurn (the button) ends it by hand.
/// The enemy's turn: every enemy unit acts in turn (see EnemyAI); meanwhile the player cannot give orders (IsPlayerTurn is false;
/// the selector, the buttons and the shot view check it).
/// A new turn of the player gives every unit its action points back and selects the first unit of the player.
/// EndTurn is public and parameterless so the End Turn button can call it directly. Without an EnemyAI in the scene the enemy simply
/// does not act and the player's next turn begins at once.
/// </summary>
public class TurnManager : MonoBehaviour
{
    [SerializeField] private UnitSelector selector;
    [SerializeField] private EnemyAI enemyAI;

    // Units that were already seen without action points. A unit is handled once, when it becomes exhausted,
    // so a unit the player selects by hand while it is exhausted is not switched away again.
    private readonly HashSet<Unit> exhausted = new HashSet<Unit>();
    private readonly List<Unit> newlyExhausted = new List<Unit>();

    /// <summary>The turn manager of the scene (null if there is none).</summary>
    public static TurnManager Instance { get; private set; }

    /// <summary>True when the player may give orders: there is no turn manager, or it is the player's turn.</summary>
    public static bool IsPlayersTurnNow => Instance == null || Instance.IsPlayerTurn;

    /// <summary>Whose turn it is.</summary>
    public Faction ActiveFaction { get; private set; } = Faction.Player;

    /// <summary>True during the player's turn.</summary>
    public bool IsPlayerTurn => ActiveFaction == ControlledFaction;

    /// <summary>The number of the turn of the player (starts at 1).</summary>
    public int Round { get; private set; } = 1;

    /// <summary>Raised when a turn begins (the argument is whose).</summary>
    public event System.Action<Faction> TurnStarted;

    private Faction ControlledFaction => selector != null ? selector.ControlledFaction : Faction.Player;

    private void OnEnable()
    {
        Instance = this;
        if (enemyAI == null)
            enemyAI = FindFirstObjectByType<EnemyAI>();
        if (enemyAI != null)
            enemyAI.PhaseFinished += StartPlayerTurn;
    }

    private void OnDisable()
    {
        if (enemyAI != null)
            enemyAI.PhaseFinished -= StartPlayerTurn;
        if (Instance == this)
            Instance = null;
    }

    private void Update()
    {
        if (selector == null)
            selector = FindFirstObjectByType<UnitSelector>();
        if (selector == null)
            return;

        // the enemy is acting: nothing to do for the player's units
        if (!IsPlayerTurn)
            return;

        // the shot view is on: whoever has run out of points is dealt with after it
        if (ShotCamera.Active)
            return;

        exhausted.RemoveWhere(u => u == null);
        newlyExhausted.Clear();

        Faction mine = selector.ControlledFaction;
        bool anyCanAct = false;

        foreach (Unit unit in Unit.All)
        {
            if (unit == null || unit.Faction != mine || !unit.IsAlive)
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
    /// Ends the player's turn: the enemy's turn begins. (Without an enemy AI the player's next turn begins at once.)
    /// Ignored while a unit is still on the move, so a half-finished route is never paid for twice, while the shot view is on,
    /// and while it is not the player's turn.
    /// </summary>
    public void EndTurn()
    {
        if (!IsPlayerTurn || ShotCamera.Active)
            return;

        Unit[] units = FindObjectsByType<Unit>(FindObjectsSortMode.None);
        foreach (Unit unit in units)
        {
            if (unit.IsMoving)
                return;
        }

        if (enemyAI == null)
        {
            StartPlayerTurn();
            return;
        }

        ActiveFaction = enemyAI.AiFaction;
        TurnStarted?.Invoke(ActiveFaction);
        enemyAI.BeginPhase();
    }

    // The enemy has finished: everybody gets the points back, the player's next turn begins.
    private void StartPlayerTurn()
    {
        foreach (Unit unit in FindObjectsByType<Unit>(FindObjectsSortMode.None))
            unit.RestoreActionPoints();

        exhausted.Clear();
        ActiveFaction = ControlledFaction;
        Round++;

        if (selector == null)
            selector = FindFirstObjectByType<UnitSelector>();
        if (selector != null)
        {
            selector.SelectFirstAvailable();

            IsometricCamera iso = FindFirstObjectByType<IsometricCamera>();
            if (iso != null && selector.SelectedUnit != null)
                iso.GlideTo(selector.SelectedUnit.transform.position);
        }

        TurnStarted?.Invoke(ActiveFaction);
    }
}
