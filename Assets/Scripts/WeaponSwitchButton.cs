using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// The button (and the X key) that takes the next weapon the selected unit carries into its hands (primary, secondary, extra place in turn)
/// for the points that the inventory says (1 by default). It is only there for a unit that carries more than one weapon, in the player's turn,
/// and not in the shot view; it cannot be pressed while a unit is walking or when there are not enough points.
/// It lives on the button; the panel around it fades with a CanvasGroup so that this object can stay active.
/// </summary>
[RequireComponent(typeof(Button))]
public class WeaponSwitchButton : MonoBehaviour
{
    [SerializeField] private UnitSelector selector;
    [SerializeField] private CanvasGroup panelGroup;

    private Button button;
    private TMP_Text label;
    private string shownText;

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
        Refresh(Time.unscaledDeltaTime);
        if (KeyPressed() && button.interactable)
            OnClick();
    }

    /// <summary>Shows or hides the panel, sets the label and whether the button can be pressed (done every frame by itself).</summary>
    public void Refresh(float deltaTime)
    {
        if (button == null)
            button = GetComponent<Button>();
        if (selector == null)
            selector = FindFirstObjectByType<UnitSelector>();
        if (label == null)
            label = GetComponentInChildren<TMP_Text>();

        Unit unit = selector != null ? selector.SelectedUnit : null;
        if (unit != null && !unit.IsSelected)
            unit = null;

        UnitInventory inv = unit != null ? unit.Inventory : null;
        bool visible = inv != null && inv.HasOtherWeapon && TurnManager.IsPlayersTurnNow && !ShotCamera.Active;

        if (panelGroup != null)
        {
            panelGroup.alpha = Mathf.MoveTowards(panelGroup.alpha, visible ? 1f : 0f, 6f * Mathf.Max(0f, deltaTime));
            panelGroup.blocksRaycasts = visible;
            panelGroup.interactable = visible;
        }

        button.interactable = visible && !Unit.AnyMoving && inv.CanSwitchWeapon;

        if (visible && label != null)
        {
            InventorySlot? next = inv.NextWeaponSlot();
            ItemData weapon = next.HasValue ? inv.Get(next.Value) : null;
            string text = "ВЗЯТЬ: " + (weapon != null ? weapon.DisplayName : "") + " (" + inv.SwitchCost + " ОД)";
            if (text != shownText)
            {
                shownText = text;
                label.text = text;
            }
        }
    }

    private void OnClick()
    {
        if (selector == null)
            return;
        Unit unit = selector.SelectedUnit;
        if (unit != null && unit.IsSelected && !Unit.AnyMoving && TurnManager.IsPlayersTurnNow && !ShotCamera.Active)
            unit.Inventory.SwitchWeapon();
    }

    private static bool KeyPressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.xKey.wasPressedThisFrame)
            return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.X))
            return true;
#endif
        return false;
    }
}
