using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// The Crouch / Stand up button (and the C key). It changes the stance of the selected unit; either change costs the unit
/// Unit.StanceCost action points. It is grey when the unit cannot do it (moving, not enough points), and it is not there at all
/// while the shot view is on. The label says what the press will do and what it costs.
/// It lives on the button; the panel around it fades with a CanvasGroup so that this object can stay active.
/// </summary>
[RequireComponent(typeof(Button))]
public class StanceButton : MonoBehaviour
{
    [SerializeField] private UnitSelector selector;
    [SerializeField] private CanvasGroup panelGroup;
    [SerializeField] private string crouchText = "ПРИСЕСТЬ";
    [SerializeField] private string standText = "ВСТАТЬ";

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
        bool visible = unit != null && unit.IsAlive && !ShotCamera.Active && TurnManager.IsPlayersTurnNow;
        if (panelGroup != null)
        {
            panelGroup.alpha = Mathf.MoveTowards(panelGroup.alpha, visible ? 1f : 0f, 6f * Mathf.Max(0f, deltaTime));
            panelGroup.blocksRaycasts = visible;
            panelGroup.interactable = visible;
        }

        button.interactable = visible && unit.CanChangeStance;

        if (unit != null && label != null)
        {
            string text = (unit.IsCrouched ? standText : crouchText) + " (" + unit.StanceCost + " ОД)";
            if (text != shownText)
            {
                shownText = text;
                label.text = text;
            }
        }
    }

    private void OnClick()
    {
        Unit unit = selector != null ? selector.SelectedUnit : null;
        if (unit != null && !ShotCamera.Active)
            unit.ToggleStance();
    }

    private static bool KeyPressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame)
            return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.C))
            return true;
#endif
        return false;
    }
}
