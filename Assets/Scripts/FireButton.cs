using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The Fire button. It can be pressed only while the selected unit sees an enemy (inside its field of view, no building in
/// the way) and is not busy moving. Pressing it sends the camera to the shot view: over the shoulder of the shooter, looking at
/// the nearest enemy it sees (ShotCamera). While that view is on, the same button says BACK and sends the camera home
/// (so do Esc and the right mouse button).
/// Going to the shot view costs nothing; every shot fired from there (ShootButton) costs action points, so the button
/// stays grey when the unit cannot pay for even one.
/// </summary>
[RequireComponent(typeof(Button))]
public class FireButton : MonoBehaviour
{
    [SerializeField] private UnitSelector selector;
    [SerializeField] private GridManager grid;
    [SerializeField] private ShotCamera shotCamera;
    [SerializeField] private TMP_Text label;
    [SerializeField] private string fireText = "FIRE";
    [SerializeField] private string backText = "BACK";

    private Button button;
    private Unit target;

    /// <summary>The enemy the shot would be at right now (the nearest one in view), or null.</summary>
    public Unit Target => target;

    private void OnEnable()
    {
        button = GetComponent<Button>();
        button.onClick.AddListener(OnClick);
    }

    private void OnDisable()
    {
        if (button != null)
            button.onClick.RemoveListener(OnClick);
    }

    private void Update()
    {
        Refresh();
    }

    /// <summary>Works out whether there is somebody to shoot at and sets the button accordingly.</summary>
    public void Refresh()
    {
        if (button == null)
            button = GetComponent<Button>();
        if (selector == null)
            selector = FindFirstObjectByType<UnitSelector>();
        if (grid == null)
            grid = FindFirstObjectByType<GridManager>();
        if (shotCamera == null)
            shotCamera = FindFirstObjectByType<ShotCamera>();

        // in the shot view the button takes the camera back
        if (shotCamera != null && (shotCamera.IsOn || ShotCamera.Active))
        {
            SetLabel(backText);
            button.interactable = shotCamera.IsHolding;
            return;
        }

        SetLabel(fireText);
        Unit shooter = selector != null ? selector.SelectedUnit : null;
        target = FindTarget(shooter);
        // going to the shot view needs the points for at least one shot
        button.interactable = target != null && !Unit.AnyMoving && shooter != null && shooter.CanAffordShot && TurnManager.IsPlayersTurnNow;
    }

    // The nearest enemy the unit can see.
    private Unit FindTarget(Unit shooter)
    {
        if (shooter == null || grid == null || !shooter.IsAlive || !shooter.IsSelected)
            return null;

        Unit best = null;
        float bestDistance = float.PositiveInfinity;
        for (int i = 0; i < Unit.All.Count; i++)
        {
            Unit other = Unit.All[i];
            if (other == null || other.Faction == shooter.Faction || !other.IsAlive || !grid.CanSee(shooter, other))
                continue;

            float distance = (other.transform.position - shooter.transform.position).sqrMagnitude;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = other;
            }
        }
        return best;
    }

    private void OnClick()
    {
        if (shotCamera == null)
            return;

        if (shotCamera.IsOn)
        {
            shotCamera.End();
            return;
        }

        Unit shooter = selector != null ? selector.SelectedUnit : null;
        if (shooter != null && target != null)
            shotCamera.Begin(shooter, target);
    }

    private void SetLabel(string text)
    {
        if (label != null && label.text != text)
            label.text = text;
    }
}
